namespace Humen.Core;

/// <summary>星球着色口径。</summary>
public enum PlanetColorMode
{
    /// <summary>按生物群系着色（§7.7）。M2 的默认口径。</summary>
    Biome,

    /// <summary>按高程分层着色。看地形用。</summary>
    Elevation,

    /// <summary>只分陆海。看 INV-8 的「5 大陆互不连通」用。</summary>
    LandSea,
}

public sealed record RenderOptions
{
    public int Width { get; init; } = 1600;
    public int Height { get; init; } = 1600;

    /// <summary>超采样倍率（每轴）。2 = 每像素 4 条光线，海岸线明显更干净。</summary>
    public int SuperSample { get; init; } = 2;

    public double CameraLat { get; init; } = 22;
    public double CameraLon { get; init; } = -35;

    /// <summary>相机到球心的距离，单位 = 星球半径。</summary>
    public double CameraDistanceR { get; init; } = 3.1;

    public double FovDeg { get; init; } = 40;

    /// <summary>
    /// 太阳直射点。默认放在相机经度东侧约 40°。
    ///
    /// ⚠️ 这个值不是随便填的：若太阳放在与相机相隔 100° 以上的地方，
    /// 可见圆面大半落在晨昏线以西，整颗星球渲染出来就是一团黑
    /// （默认值曾取 SunLon = 110 而相机在 −35，相差 145°，渲染结果几乎全黑）。
    /// 行星视图要的是「看得清地表」，故让太阳大致在相机这一侧、偏一点以显出立体感。
    /// </summary>
    public double SunLat { get; init; } = 15;
    public double SunLon { get; init; } = -35 + 40;

    /// <summary>海平面（米，相对今日）。取 <see cref="GlacialCycle.SeaLevelOffsetM"/>。</summary>
    public double SeaLevelM { get; init; } = 0;

    /// <summary>全球温度偏移（℃）。取 <see cref="GlacialCycle.GlobalTempOffsetC"/>。</summary>
    public double GlobalTempOffsetC { get; init; } = 0;

    public PlanetColorMode Mode { get; init; } = PlanetColorMode.Biome;

    public bool Atmosphere { get; init; } = true;
    public bool Clouds { get; init; } = true;
    public bool Stars { get; init; } = true;

    /// <summary>
    /// M4 的气候场（§7.6）。给了它，上色走 <see cref="Climate.SampleAnnual"/> ——
    /// 洋流、大陆度、迎风坡雨影、季风全部计入；
    /// <c>null</c> 则退回 M1 的降维版（只按纬度分带），用于对照与无场时的兜底。
    ///
    /// ⚠️ <b>这张图必须与库里部落的群系同源</b>。§7.6 的注释里已经写过一次这个教训：
    /// 两处各写一份判据，迟早出现「图上是雨林、库里是草原」。
    /// </summary>
    public ClimateGrid? Climate { get; init; }

    /// <summary>可读的年代标签，只用于文件名/日志。</summary>
    public string? YearLabel { get; init; }
}

/// <summary>
/// 星球光线投射渲染器 —— design.md §10「星球主视图」。
///
/// <b>为什么不走光栅化</b>：M2 要画的是一个孤零零的球，没有场景、没有网格、
/// 没有 GPU。对「一个球」而言，逐像素解一次射线与球的交点，比搭一套光栅管线
/// 短得多，而且是<b>解析精确</b>的 —— 海岸线不会被网格分辨率限制，
/// 想画多大就画多大。
///
/// <b>刻意不做地形位移</b>：地表起伏最大不过 ±2 km，而星球半径 6371 km，
/// 位移量是半径的 0.03% —— 挪动交点在画面上不可见，只会白白引入自交与阴影失真。
/// 地形感改由<b>着色</b>表达（高程/群系分层），这也是 §10 选择「按技术水平着色」
/// 而非做真实位移的原因。
///
/// <b>确定性</b>：云、星空全部由 world seed 导出，同 seed 渲染出的 PNG 逐字节一致。
/// </summary>
public static class PlanetRenderer
{
    /// <summary>大气壳半径倍率（§7.4 表：R × 1.02）。</summary>
    private const double AtmosphereR = Climate.AtmosphereShellFactor;

    /// <summary>大气边缘辉光的角向宽度（单位 = 星球半径）。</summary>
    private const double AtmosphereSigma = 0.0075;

    private const double StarCount = 900;

    /// <summary>星空的哈希子域，与其它用途隔离。</summary>
    private const int StarDomain = 9101;

    public static byte[] Render(PlanetRaster raster, long seed, RenderOptions opt)
    {
        int ss = Math.Max(1, opt.SuperSample);
        int w = opt.Width, h = opt.Height;
        int sw = w * ss, sh = h * ss;

        var (camLat, camLon) = (opt.CameraLat, opt.CameraLon);
        var camDir = Unit(Sphere.ToXyz(camLat, camLon, 1.0));
        var eye = Scale(camDir, opt.CameraDistanceR);

        // 相机基底。z_cam 由球心指向相机，f = −z_cam 为视线方向。
        // 近极点时 worldUp 与 z_cam 近乎共线，叉积退化 —— 换个参考轴。
        var worldUp = Math.Abs(camDir.Y) > 0.999 ? (X: 0.0, Y: 0.0, Z: 1.0) : (X: 0.0, Y: 1.0, Z: 0.0);
        var xCam = Unit(Cross(worldUp, camDir));
        var yCam = Cross(camDir, xCam);

        double tanHalf = Math.Tan(opt.FovDeg * Math.PI / 360.0);
        double aspect = w / (double)h;

        var sunDir = Unit(Sphere.ToXyz(opt.SunLat, opt.SunLon, 1.0));

        var clouds = new Simplex3(unchecked((long)Hashing.Hash64(seed, HashDomain.Elevation, 31, 0, 0)));

        // ── 背景（星空）先在超采样分辨率上摊开，之后被星球/大气覆盖 ──
        var hi = new byte[sw * sh * 3];
        if (opt.Stars) SplatStars(hi, sw, sh, seed, camDir, xCam, yCam, tanHalf, aspect, ss);

        for (int py = 0; py < sh; py++)
        {
            // ndcY：+1 在画面顶部
            double ndcY = 1.0 - 2.0 * (py + 0.5) / sh;
            for (int px = 0; px < sw; px++)
            {
                double ndcX = 2.0 * (px + 0.5) / sw - 1.0;

                // 射线方向 = f + x_cam·sx + y_cam·sy，其中 f = −z_cam = −camDir
                var dir = Unit(Add(Scale(camDir, -1.0),
                                   Add(Scale(xCam, ndcX * tanHalf * aspect),
                                       Scale(yCam, ndcY * tanHalf))));

                var (r, g, b) = CastRay(raster, seed, clouds, eye, dir, sunDir, opt);

                int o = (py * sw + px) * 3;
                hi[o] = r; hi[o + 1] = g; hi[o + 2] = b;
            }
        }

        return ss == 1 ? hi : Downsample(hi, sw, sh, ss);
    }

    // ══════════════════════════════════════════════════════════════
    //  单条光线
    // ══════════════════════════════════════════════════════════════

    private static (byte R, byte G, byte B) CastRay(
        PlanetRaster raster, long seed, Simplex3 clouds,
        (double X, double Y, double Z) eye, (double X, double Y, double Z) dir,
        (double X, double Y, double Z) sunDir, RenderOptions opt)
    {
        double b = Dot(eye, dir);
        double c = Dot(eye, eye) - 1.0;
        double disc = b * b - c;

        // ── 大气辉光：按「射线到球心的垂距」算，晨昏线上亮、背光面暗 ──
        double atmAlpha = 0;
        (byte R, byte G, byte B) atmColor = (110, 165, 255);
        if (opt.Atmosphere)
        {
            double bImp = Math.Sqrt(Math.Max(0.0, Dot(eye, eye) - b * b));
            double excess = bImp - 1.0;
            double glow = Math.Exp(-(excess * excess) / (2 * AtmosphereSigma * AtmosphereSigma));

            // 壳外还要限制在大气壳之内，否则整片天空都会被染蓝
            if (bImp < AtmosphereR)
            {
                var pAtm = Add(eye, Scale(dir, -b));        // 最近点
                double sun = Smoothstep(-0.30, 0.45, Dot(Unit(pAtm), sunDir));
                atmAlpha = Math.Clamp(glow * (0.12 + 0.88 * sun), 0.0, 1.0);
            }
        }

        // 未命中球体（disc < 0，或交点在相机背后）：深空底 + 大气辉光
        if (disc < 0) return Blend(Background(), atmColor, atmAlpha * 0.85);

        double t = -b - Math.Sqrt(disc);
        if (t <= 0) return Blend(Background(), atmColor, atmAlpha * 0.85);

        var p = Add(eye, Scale(dir, t));
        var n = Unit(p);
        var (lat, lon) = Sphere.ToLatLon(n.X, n.Y, n.Z);

        double elev = raster.SampleElevationM(lat, lon);
        bool ocean = elev <= opt.SeaLevelM;

        var baseColor = SurfaceColor(raster, lat, lon, elev, ocean, opt);

        // ── 光照：柔化晨昏线，避免一条生硬的黑白分界 ──
        // 夜侧地板取 0.10 而非 0：留一点环境光，否则背光面糊成纯黑，
        // 大陆轮廓与海岸线全看不见 —— 行星视图毕竟是给「看清地表」用的。
        double ndl = Dot(n, sunDir);
        double shade = 0.10 + 0.90 * Smoothstep(-0.06, 0.32, ndl);
        var lit = Scale(baseColor, shade);

        // ── 海面镜面高光 ──
        //
        // 指数必须给得足够高。Blinn-Phong 的光斑角半径 ≈ acos(0.5^(1/n))：
        // n = 90 时约 9°，在 1000px 的图上是一坨直径近 200px 的白斑 ——
        // 看着像打了盏聚光灯，不像太阳在海面上的反光。n = 900 时约 3°，才是行星尺度该有的样子。
        // （真实海面还要粗糙得多，本就该是小小一点而非一大片。）
        if (ocean && ndl > 0)
        {
            var view = Scale(dir, -1.0);
            var half = Unit(Add(sunDir, view));
            double spec = Math.Pow(Math.Max(0.0, Dot(n, half)), 900.0) * 0.70;
            if (spec > 0) lit = Add(lit, Scale((255.0, 250.0, 235.0), spec));
        }

        // ── 云层 ──
        if (opt.Clouds)
        {
            double cover = CloudCover(clouds, n);
            if (cover > 0)
            {
                double cshade = 0.35 + 0.65 * Smoothstep(-0.20, 0.40, ndl);
                var cloudCol = Scale((252.0, 253.0, 255.0), cshade);
                lit = Blend(lit, cloudCol, cover);
            }
        }

        // 大气压在星球边缘（临边薄雾）
        return Blend(lit, atmColor, atmAlpha * 0.55);
    }

    /// <summary>
    /// 无光照的等距圆柱地图。用途有二：
    ///   ① 在球面视图上看不真切的地方（极区、背面）用它核对；
    ///   ② 后续可直接当作 Unity 地表贴图，省掉一次重算。
    /// 地图<b>不打光</b> —— 地图要的是信息，不是画面。
    /// </summary>
    public static byte[] RenderEquirectMap(PlanetRaster raster, RenderOptions opt,
                                           int width, int height, bool graticule = true)
    {
        var buf = new byte[width * height * 3];

        for (int y = 0; y < height; y++)
        {
            double lat = 90.0 - (y + 0.5) * 180.0 / height;
            for (int x = 0; x < width; x++)
            {
                double lon = -180.0 + (x + 0.5) * 360.0 / width;

                double elev = raster.SampleElevationM(lat, lon);
                bool ocean = elev <= opt.SeaLevelM;
                var c = SurfaceColor(raster, lat, lon, elev, ocean, opt);

                // 海岸线：陆地格元若四邻有海，就压一道深色边。
                //
                // 不是为了好看 —— 是为了<b>看得见</b>：两块寒带大陆整年压在冰盖色
                // （238,245,252）底下，与海冰（232,242,250）肉眼几乎同色，
                // 不描边的话「5 块大陆」在这张图上只数得出 3 块。
                if (IsCoastline(raster, x, y, width, height))
                    c = Lerp(c, (18, 38, 58), 0.55);

                // 每 30° 一道淡经纬网，便于读图定位。
                // 阈值取 1.2 个像素：太细会在缩放时闪断，太粗会糊成一片。
                if (graticule)
                {
                    bool onLat = Math.Abs(Math.IEEERemainder(lat, 30.0)) < (180.0 / height) * 1.2;
                    bool onLon = Math.Abs(Math.IEEERemainder(lon, 30.0)) < (360.0 / width) * 1.2;
                    if (onLat || onLon) c = Lerp(c, (255, 255, 255), 0.20);
                }

                int o = (y * width + x) * 3;
                buf[o] = Clamp8(c.R);
                buf[o + 1] = Clamp8(c.G);
                buf[o + 2] = Clamp8(c.B);
            }
        }
        return buf;
    }

    /// <summary>
    /// 该格元是否处在海陆交界处（四邻中有异类）。
    /// 经度方向环绕，纬度方向越界则跳过 —— 极点外侧不是海，
    /// 若按海算会在两极各描出一圈并不存在的海岸线。
    /// </summary>
    private static bool IsCoastline(PlanetRaster raster, int x, int y, int width, int height)
    {
        bool self = raster.IsLand[y * width + x];

        for (int k = 0; k < 4; k++)
        {
            int nx = raster.WrapX(x + (k == 0 ? 1 : k == 1 ? -1 : 0));
            int ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
            if (ny < 0 || ny >= height) continue;

            if (raster.IsLand[ny * width + nx] != self) return true;
        }
        return false;
    }

    /// <summary>地表颜色（不含光照）。</summary>
    private static (double R, double G, double B) SurfaceColor(
        PlanetRaster raster, double lat, double lon, double elevM, bool ocean, RenderOptions opt)
    {
        if (ocean)
        {
            // 海冰：算海面温度（海拔取 0），低于海水冰点即结冰。
            //
            // ⚠️ 这里<b>故意</b>不算洋流项：M4 的洋流距平定义在陆地上（要从「本格所在的
            // 大陆，海在西侧还是东侧」推出来），洋面格自己没有「岸」可依。
            // 后果是海温只随纬度变、海冰界线是一条平直的纬线 —— 与真实世界
            // （北大西洋暖流让挪威沿海不结冰）不符。<b>这是一处已知的简化，不是疏漏</b>；
            // 要做得更真，得给洋面格也定出「洋盆的东/西边界」，属后续milestone。
            double tSea = Climate.TemperatureC(lat, 0, opt.GlobalTempOffsetC);
            if (tSea < Climate.SeaWaterFreezingC) return Climate.SeaIceColor;

            // 越浅越亮：大陆架在冰期会露出来，先让它在水下就能看见
            double depth = opt.SeaLevelM - elevM;
            double u = Math.Clamp(depth / 3200.0, 0, 1);
            return Lerp(Climate.ShelfColor, Climate.DeepOceanColor, u);
        }

        return opt.Mode switch
        {
            PlanetColorMode.LandSea => (76.0, 132.0, 74.0),
            PlanetColorMode.Elevation => ElevationColor(elevM),
            _ => ToD(Climate.BiomeColor(BiomeCodeAt(lat, lon, elevM, opt))),
        };
    }

    /// <summary>
    /// 一个点的群系码。<b>所有上色路径都必须走这里</b> ——
    /// 有了气候场就用完整和式，没有才退回降维版。
    /// </summary>
    private static byte BiomeCodeAt(double lat, double lon, double elevM, RenderOptions opt)
        => opt.Climate is { } grid
            ? Climate.SampleAnnual(grid, lat, lon, elevM, opt.GlobalTempOffsetC).Biome
            : Climate.BiomeCode(lat, elevM, opt.GlobalTempOffsetC);

    /// <summary>高程分层设色（供 <see cref="PlanetColorMode.Elevation"/>）。</summary>
    private static (double R, double G, double B) ElevationColor(double h)
    {
        if (h < 200) return Lerp((60, 110, 70), (110, 145, 85), h / 200.0);
        if (h < 600) return Lerp((110, 145, 85), (165, 155, 95), (h - 200) / 400.0);
        if (h < 1100) return Lerp((165, 155, 95), (150, 115, 80), (h - 600) / 500.0);
        if (h < 1600) return Lerp((150, 115, 80), (185, 180, 175), (h - 1100) / 500.0);
        return (245, 248, 252);
    }

    /// <summary>云量 [0,1]。用球面 fBm 阈值化，得到成团的、有缝隙的云。</summary>
    private static double CloudCover(Simplex3 clouds, (double X, double Y, double Z) n)
    {
        const double freq = 2.6;
        double f = clouds.Fbm01(n.X * freq, n.Y * freq, n.Z * freq, 5, 2.1, 0.55);

        // 纬度调制：赤道辐合带与副极地多云，副热带少云（§7.6 气压带）
        double absLat = Math.Abs(Math.Asin(Math.Clamp(n.Y, -1, 1)) * 180.0 / Math.PI);
        double band = 0.5 + 0.5 * Math.Cos(absLat * Math.PI / 30.0);

        double v = f * 0.72 + band * 0.28;
        return Math.Clamp(Smoothstep(0.52, 0.78, v), 0.0, 1.0) * 0.88;
    }

    /// <summary>
    /// 深空底色。星点在 <see cref="SplatStars"/> 里已经投影进背景缓冲，此处不再逐像素查星表：
    /// 逐像素查是 O(像素 × 星星数)，而「把每颗星投影到屏幕」是 O(星星数)，差好几个数量级。
    /// </summary>
    private static (double R, double G, double B) Background() => (4, 5, 10);

    // ══════════════════════════════════════════════════════════════
    //  星空
    // ══════════════════════════════════════════════════════════════

    private static void SplatStars(byte[] buf, int w, int h, long seed,
        (double X, double Y, double Z) camDir, (double X, double Y, double Z) xCam,
        (double X, double Y, double Z) yCam, double tanHalf, double aspect, int block)
    {
        // block = 超采样倍率：星点画成 block×block 的一小块，
        // 降采样后恰好占满一个输出像素。只画 1 个亚像素的话，
        // 2× 降采样会把星等除以 4 —— 星空会淡到看不见。
        var rng = new Rng(unchecked((long)Hashing.Hash64(seed, StarDomain, 0, 0, 0)));

        for (int i = 0; i < StarCount; i++)
        {
            // 球面均匀采样
            double u = rng.NextRange(-1, 1);
            double phi = rng.NextRange(0, Math.Tau);
            double s = Math.Sqrt(Math.Max(0, 1 - u * u));
            var star = (X: s * Math.Cos(phi), Y: u, Z: s * Math.Sin(phi));

            double zs = Dot(star, camDir);        // 相机朝向 +z_cam；星在前方 ⟺ zs < 0
            if (zs > -1e-6) continue;

            double ndcX = Dot(star, xCam) / (-zs) / (tanHalf * aspect);
            double ndcY = Dot(star, yCam) / (-zs) / tanHalf;
            if (Math.Abs(ndcX) > 1 || Math.Abs(ndcY) > 1) continue;

            int px = (int)((ndcX + 1) * 0.5 * w);
            int py = (int)((1 - ndcY) * 0.5 * h);
            if (px < 0 || px >= w || py < 0 || py >= h) continue;

            double bright = Math.Pow(rng.NextDouble(), 2.4);   // 多数暗、少数亮
            double tint = rng.NextRange(0.82, 1.0);
            double v = 40 + 215 * bright;
            double vx = v * tint;

            for (int dy = 0; dy < block; dy++)
            {
                int yy = py + dy;
                if (yy >= h) break;
                for (int dx = 0; dx < block; dx++)
                {
                    int xx = px + dx;
                    if (xx >= w) break;

                    int o = (yy * w + xx) * 3;
                    buf[o] = Clamp8(Math.Max(buf[o], vx));
                    buf[o + 1] = Clamp8(Math.Max(buf[o + 1], vx * tint));
                    buf[o + 2] = Clamp8(Math.Max(buf[o + 2], v));
                }
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  降采样
    // ══════════════════════════════════════════════════════════════

    private static byte[] Downsample(byte[] hi, int sw, int sh, int ss)
    {
        int w = sw / ss, h = sh / ss;
        var lo = new byte[w * h * 3];
        int n = ss * ss;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int sr = 0, sg = 0, sb = 0;
                for (int dy = 0; dy < ss; dy++)
                {
                    int rowBase = ((y * ss + dy) * sw + x * ss) * 3;
                    for (int dx = 0; dx < ss; dx++)
                    {
                        int o = rowBase + dx * 3;
                        sr += hi[o]; sg += hi[o + 1]; sb += hi[o + 2];
                    }
                }
                int lo3 = (y * w + x) * 3;
                lo[lo3] = (byte)(sr / n);
                lo[lo3 + 1] = (byte)(sg / n);
                lo[lo3 + 2] = (byte)(sb / n);
            }
        }
        return lo;
    }

    // ══════════════════════════════════════════════════════════════
    //  小工具
    // ══════════════════════════════════════════════════════════════

    private static double Smoothstep(double a, double b, double x)
    {
        if (b <= a) return x < a ? 0 : 1;
        double t = Math.Clamp((x - a) / (b - a), 0.0, 1.0);
        return t * t * (3 - 2 * t);
    }

    private static byte Clamp8(double v) => v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)(v + 0.5);

    private static (double X, double Y, double Z) Unit((double X, double Y, double Z) v)
    {
        double m = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return m < 1e-12 ? (0, 0, 0) : (v.X / m, v.Y / m, v.Z / m);
    }

    private static (double X, double Y, double Z) Scale((double X, double Y, double Z) v, double s)
        => (v.X * s, v.Y * s, v.Z * s);

    private static (double X, double Y, double Z) Add((double X, double Y, double Z) a,
                                                      (double X, double Y, double Z) b)
        => (a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    private static double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b)
        => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static (double X, double Y, double Z) Cross((double X, double Y, double Z) a,
                                                        (double X, double Y, double Z) b)
        => (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static (double R, double G, double B) Lerp((double R, double G, double B) a,
                                                       (double R, double G, double B) b, double t)
        => (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    // 注：向量与颜色都是 (double, double, double)，元组元素名只是元数据、不参与类型判定，
    // 故 Scale / Add 各只需要一份实现，颜色直接复用（见上方 Scale/Add 的向量版本）。

    /// <summary>把 <paramref name="top"/> 以不透明度 <paramref name="alpha"/> 压在 <paramref name="bottom"/> 上。</summary>
    private static (byte R, byte G, byte B) Blend((double R, double G, double B) bottom,
                                                  (byte R, byte G, byte B) top, double alpha)
    {
        if (alpha <= 0) return (Clamp8(bottom.R), Clamp8(bottom.G), Clamp8(bottom.B));
        if (alpha >= 1) return top;
        return (Clamp8(bottom.R + (top.R - bottom.R) * alpha),
                Clamp8(bottom.G + (top.G - bottom.G) * alpha),
                Clamp8(bottom.B + (top.B - bottom.B) * alpha));
    }

    private static (double R, double G, double B) Blend((double R, double G, double B) bottom,
                                                        (double R, double G, double B) top, double alpha)
        => (bottom.R + (top.R - bottom.R) * alpha,
            bottom.G + (top.G - bottom.G) * alpha,
            bottom.B + (top.B - bottom.B) * alpha);

    /// <summary>把 <see cref="PlanetColorMode.Biome"/> 用到的元组隐式转成三元组。</summary>
    private static (double R, double G, double B) ToD((byte R, byte G, byte B) c) => (c.R, c.G, c.B);
}
