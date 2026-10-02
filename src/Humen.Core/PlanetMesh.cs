using System.Globalization;
using System.Text;

namespace Humen.Core;

/// <summary>网格导出参数（M2-E）。</summary>
public sealed record MeshOptions
{
    /// <summary>经度方向分段数。会被向上取到偶数，见 <see cref="PlanetMesh.Build"/>。</summary>
    public int Segments { get; init; } = 256;

    /// <summary>纬度方向分段数。取 <see cref="Segments"/> 的一半即与等距圆柱等比。</summary>
    public int Rings { get; init; } = 128;

    /// <summary>壳层（海水/大气/云）的经度分段数。壳层是光滑球，不需要地表那么密。</summary>
    public int ShellSegments { get; init; } = 128;

    /// <summary>壳层的纬度分段数。</summary>
    public int ShellRings { get; init; } = 64;

    public double SeaLevelM { get; init; } = 0;
    public double GlobalTempOffsetC { get; init; } = 0;

    /// <summary>M4 的气候场（§7.6）。与 <see cref="PlanetRenderer.RenderOptions.Climate"/> 同义：
    /// 给了它，地表材质按完整和式分群系；<c>null</c> 则退回只按纬度的降维版。</summary>
    public ClimateGrid? Climate { get; init; }

    /// <summary>
    /// 垂直放大倍率。<b>仅供观看，不进入任何模拟</b>，理由见 <see cref="PlanetMesh"/> 类注释。
    /// 1.0 = §7.4 的严格比例。
    /// </summary>
    public double VerticalExaggeration { get; init; } = 20.0;

    public bool OceanShell { get; init; } = true;
    public bool AtmosphereShell { get; init; } = true;
    public bool CloudShell { get; init; } = true;
}

/// <summary>
/// 球体网格与 OBJ/MTL 导出 —— design.md §7.4 的四层（海底/地表 · 海水 · 大气壳 · 云层）
/// 与 §13 的 M2-E。
///
/// <b>这是真正交给 Unity 的东西</b>。光线投射渲染器（<see cref="PlanetRenderer"/>）只是
/// 在没有引擎的机器上「看一眼」的临时眼睛；最终画面由 Unity 用这里的网格 + 材质画出来。
/// 两者共用同一份 <see cref="PlanetRaster"/>，所以网格上的海岸线与 PNG 上的海岸线是同一条。
///
/// <b>为什么顶点从栅格采样、而不是重新向场求值</b>：
/// 场求值（<see cref="PlanetField.FieldAt"/>）在浮点意义上比栅格精确，
/// 但「渲染图上的海岸线」与「INV-8 判定的海岸线」必须是同一条 —— 否则会出现
/// 视觉上五块大陆分开、判定里连成一片的鬼故事。栅格是二者的公共真值，故网格也取自它。
/// 栅格分辨率（默认 2048×1024）高于网格（默认 256×128），所以这一步是降采样，不会走样。
///
/// <b>关于垂直放大（默认 20×）</b>：
/// §7.4 的高程是真实比例 —— 珠峰 8.85 km 在 R = 6371 km 的星球上只有半径的 0.139%，
/// 在 1000 单位的网格里是 1.389 单位。这个量级下整个星球看起来是一个光滑的球，
/// 且海面（R + 海平面）与最浅的大陆架之间只差千分之几单位，任何查看器都会 z-fighting。
/// 故导出时默认放大 20×：珠峰 27.8 单位（2.8% R），地形一眼可见。
/// <b>放大不影响任何拓扑</b>：它是径向的单调变换，不会让两块大陆连上，也不会把海变成陆，
/// 所以 INV-8 的结论不受影响。<b>Unity 运行时应取 1.0</b> —— 放大是给人看的，不是给物理用的。
/// </summary>
public static class PlanetMesh
{
    /// <summary>1 个 Unity 单位等于多少米。§7.1：R = 1000 单位 ≈ 6371 km ⇒ 6371 m/单位。</summary>
    public const double MetresPerUnit = 1000.0 * PlanetRaster.EarthRadiusKm / Sphere.Radius;

    // ══════════════════════════════════════════════════════════════
    //  材质表 —— ★ 唯一真源
    // ══════════════════════════════════════════════════════════════
    //
    //  0..14 : 群系（§7.7），名字与颜色直接取自 Climate，不另立一份
    //  15..17: 海底（按深度分三档）
    //  18    : 海水（海面本身在 planet_shells.obj 的 ocean 组）
    //  19    : 海冰
    //  20    : 大气壳
    //  21    : 云层

    public const int MatSeabedShelf = Climate.BiomeCount;        // 15
    public const int MatSeabedSlope = Climate.BiomeCount + 1;    // 16
    public const int MatSeabedDeep = Climate.BiomeCount + 2;     // 17
    public const int MatWater = Climate.BiomeCount + 3;          // 18
    public const int MatSeaIce = Climate.BiomeCount + 4;         // 19
    public const int MatAtmosphere = Climate.BiomeCount + 5;     // 20
    public const int MatClouds = Climate.BiomeCount + 6;         // 21
    public const int MaterialCount = Climate.BiomeCount + 7;     // 22

    /// <summary>大陆架与大陆坡的分界（米）。</summary>
    public const double ShelfDepthM = 200.0;

    /// <summary>大陆坡与深海盆的分界（米）。</summary>
    public const double SlopeDepthM = 2000.0;

    /// <summary>地表物体的名字。只有它参与陆海面积统计（壳层不算，否则云层会稀释陆地占比）。</summary>
    public const string SurfaceObject = "surface";

    /// <summary>材质名。第 0..14 项与 <see cref="Climate.BiomeNames"/> 一一对应。</summary>
    public static readonly string[] MaterialNames = BuildMaterialNames();

    /// <summary>材质基色（sRGB 0..255），写进 MTL 时直接除以 255。</summary>
    public static readonly (byte R, byte G, byte B)[] MaterialColors = BuildMaterialColors();

    /// <summary>不透明度（MTL 的 <c>d</c>）。只有壳层不是 1。</summary>
    public static readonly double[] MaterialAlpha = BuildMaterialAlpha();

    public static string MaterialName(int id)
        => id >= 0 && id < MaterialCount ? MaterialNames[id] : $"unknown_{id}";

    private static string[] BuildMaterialNames()
    {
        var names = new string[MaterialCount];
        for (int i = 0; i < Climate.BiomeCount; i++) names[i] = $"biome_{i:D2}";
        names[MatSeabedShelf] = "seabed_shelf";
        names[MatSeabedSlope] = "seabed_slope";
        names[MatSeabedDeep] = "seabed_deep";
        names[MatWater] = "water";
        names[MatSeaIce] = "sea_ice";
        names[MatAtmosphere] = "atmosphere";
        names[MatClouds] = "clouds";
        return names;
    }

    private static (byte, byte, byte)[] BuildMaterialColors()
    {
        var c = new (byte, byte, byte)[MaterialCount];
        for (int i = 0; i < Climate.BiomeCount; i++) c[i] = Climate.BiomeColors[i];
        c[MatSeabedShelf] = Climate.ShelfColor;
        c[MatSeabedSlope] = Mix(Climate.ShelfColor, Climate.DeepOceanColor, 0.5);
        c[MatSeabedDeep] = Climate.DeepOceanColor;
        c[MatWater] = (30, 86, 142);
        c[MatSeaIce] = Climate.SeaIceColor;
        c[MatAtmosphere] = (120, 170, 255);
        c[MatClouds] = (255, 255, 255);
        return c;
    }

    private static double[] BuildMaterialAlpha()
    {
        var a = new double[MaterialCount];
        Array.Fill(a, 1.0);
        a[MatWater] = 0.78;
        a[MatAtmosphere] = 0.22;
        a[MatClouds] = 0.72;
        return a;
    }

    private static (byte, byte, byte) Mix((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, double t)
        => ((byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));

    // ══════════════════════════════════════════════════════════════
    //  网格容器
    // ══════════════════════════════════════════════════════════════

    /// <summary>一个（物体，材质）组合下的面片集合。<c>Faces</c> 的元素是顶点索引（1 基，OBJ 口径）。</summary>
    public sealed record SubMesh(string ObjectName, int MaterialId, List<int[]> Faces);

    /// <summary>
    /// 网格统计。<b>这是自检，不是装饰</b>：<see cref="LandFraction"/> 与
    /// <see cref="SharesByContinent"/> 由<b>面片面积</b>独立积分得出，
    /// 拿去和栅格按格元数出的面积占比对表，就能验证「网格确实复现了栅格」
    /// ——两者对不上，说明顶点采样或绕序写错了。
    /// </summary>
    public sealed record MeshStats(
        int VertexCount,
        int FaceCount,
        int TriangleCount,
        IReadOnlyDictionary<string, int> FacesPerObject,
        IReadOnlyDictionary<int, double> SharesByContinent,
        double LandFraction,
        double MinElevationM,
        double MaxElevationM,
        double MinRadius,
        double MaxRadius,
        int InwardFacingTriangles,
        IReadOnlyDictionary<string, int> InwardPerObject,
        IReadOnlyDictionary<string, int> TrianglesPerObject,
        string WorstFacingObject,
        double WorstFacingDot)
    {
        /// <summary>
        /// 朝外的三角占比。<b>这是绕序的自检</b>：写反了模型在 Unity 里会整颗内翻，
        /// 被背面剔除之后只剩一个黑球 —— 而 OBJ 文件本身「看起来」完全正常，
        /// 用文本编辑器怎么也看不出来。所以必须在这里用几何验，不能靠人眼。
        /// </summary>
        public double OutwardRatio
            => TriangleCount > 0 ? 1.0 - InwardFacingTriangles / (double)TriangleCount : 0.0;

        public bool WindingIsOutward => OutwardRatio > 0.999;
    }

    /// <summary>网格本体。顶点全局一份（OBJ 的索引本来就是全局的），各物体/材质通过 <see cref="SubMeshes"/> 分组。</summary>
    public sealed class Mesh
    {
        public List<(double X, double Y, double Z)> Vertices { get; } = new();
        public List<SubMesh> SubMeshes { get; } = new();
        public MeshStats Stats { get; internal set; } = null!;
    }

    // ══════════════════════════════════════════════════════════════
    //  构建
    // ══════════════════════════════════════════════════════════════

    /// <summary>第 <paramref name="elevM"/> 米高程处的球面半径（单位）。</summary>
    public static double RadiusOf(double elevM, double exaggeration = 1.0)
        => Sphere.Radius + elevM / MetresPerUnit * exaggeration;

    public static Mesh Build(PlanetRaster raster, MeshOptions opt)
    {
        var acc = new Acc(raster, opt.SeaLevelM);
        var m = acc.Mesh;

        // 半径换算。放大只作用在「相对海平面的高程」上，不影响球心、地轴与经纬度。
        double RadiusOfE(double elevM) => RadiusOf(elevM, opt.VerticalExaggeration);

        // ── 地表（陆 + 海底，一张连续曲面）──
        //
        // 为什么海底也画进同一张网格，而不是「陆地一张 + 海面一张」：
        // 海面在 R，最浅的大陆架在 R − 几十米，两者差千分之几单位。两张网格叠在一起，
        // 任何查看器都会在近岸处 z-fighting 成一片雪花。
        // 一张连续曲面则完全没有这个问题 —— 陆海分界就是高程穿过海平面的那条等值线，
        // 本来就是连续的。海面另作一个壳层单独导出（见 planet_shells.obj）。
        BuildSphere(acc, SurfaceObject, Even(opt.Segments), Math.Max(2, opt.Rings), RadiusOfE,
                    (lat, lon, elev) => SurfaceMaterial(elev, lat, lon, opt));

        int sseg = Even(opt.ShellSegments);
        int sring = Math.Max(2, opt.ShellRings);

        if (opt.OceanShell)
        {
            double seaR = RadiusOfE(opt.SeaLevelM);
            BuildSphere(acc, "ocean", sseg, sring, _ => seaR,
                        (lat, _, _) => Climate.TemperatureC(lat, 0, opt.GlobalTempOffsetC)
                                       < Climate.SeaWaterFreezingC ? MatSeaIce : MatWater);
        }

        if (opt.AtmosphereShell)
        {
            double r = Sphere.Radius * Climate.AtmosphereShellFactor;   // §7.4 表：R × 1.02
            BuildSphere(acc, "atmosphere", sseg, sring, _ => r, (_, _, _) => MatAtmosphere);
        }

        if (opt.CloudShell)
        {
            double r = Sphere.Radius * Climate.CloudShellFactor;        // §7.4 表：R × 1.03
            BuildSphere(acc, "clouds", sseg, sring, _ => r, (_, _, _) => MatClouds);
        }

        m.Stats = acc.Finish();
        return m;
    }

    /// <summary>
    /// 向上取到偶数。<b>不是审美</b>：奇数分段时 ±180° 经线上没有顶点列，
    /// 顶点的经度落不到格心，接缝处会错开半格 —— 表现为一条贯穿南北的锯齿裂缝。
    /// </summary>
    private static int Even(int n) => (Math.Max(4, n) + 1) & ~1;

    /// <summary>
    /// 地表材质：陆地取群系（§7.7），海洋取海底按深度分档。
    /// 陆海判据与 <see cref="PlanetRenderer"/> 的 <c>SurfaceColor</c> 一致
    /// （<c>elev &lt;= SeaLevelM</c> 即海），保证网格与 PNG 不在海岸上分歧。
    /// </summary>
    private static int SurfaceMaterial(double elev, double lat, double lon, MeshOptions opt)
    {
        if (elev <= opt.SeaLevelM)
        {
            double depth = opt.SeaLevelM - elev;
            if (depth < ShelfDepthM) return MatSeabedShelf;
            return depth < SlopeDepthM ? MatSeabedSlope : MatSeabedDeep;
        }
        return opt.Climate is { } grid
            ? Climate.SampleAnnual(grid, lat, lon, elev, opt.GlobalTempOffsetC).Biome
            : Climate.BiomeCode(lat, elev, opt.GlobalTempOffsetC);
    }

    /// <summary>
    /// 生成一张 UV 球。
    ///
    /// 顶点布局（<paramref name="seg"/> 条经线、<paramref name="ring"/> 圈纬线）：
    /// <code>
    ///   1                      北极（1 个顶点）
    ///   2 + (i-1)*seg + j      内部纬圈 i ∈ [1, ring)，j ∈ [0, seg)
    ///   2 + (ring-1)*seg       南极（1 个顶点）
    ///   合计 2 + (ring-1)*seg
    /// </code>
    /// 两极各只放一个顶点、并把首尾两圈的四边形退化成三角形 ——
    /// 若照搬「整行 seg 个顶点」的写法，极点那一整行会重合在一起，
    /// 导入后是一堆零面积面、法线全为 NaN，多数引擎会直接判定网格损坏。
    ///
    /// <b>绕序</b>：取 <c>(v(i,j), v(i,j+1), v(i+1,j+1), v(i+1,j))</c>，
    /// 其中 <c>i</c> 增大是向南、<c>j</c> 增大是向东。
    /// 以 <c>p̂</c> 为外法线可验：<c>∂p/∂lon × ∂p/∂lat = −R²·p̂</c>（指向球心），
    /// 故「先向东、再向南」的绕法给出的 <c>(b−a)×(c−a)</c> 恰好朝外。
    /// 写反的话，模型在 Unity 里会整颗内翻，只剩背面剔除之后的一个黑球。
    /// </summary>
    private static void BuildSphere(
        Acc acc, string objName, int seg, int ring,
        Func<double, double> radiusOf,
        Func<double, double, double, int> materialOf)
    {
        // ★ 必须记住本球开始处的顶点计数。
        //
        // 所有球共用一份全局顶点表（OBJ 的索引本来就是全局的），所以顶点号是<b>绝对</b>的。
        // 若在这里写死 1 / 2 这种「从 1 开始」的偏移，只有第一张球是对的 ——
        // 后面每张壳层的面都会指到地表的顶点上去。而且因为编号仍在合法范围内，
        // 不会报任何错，只是几何全乱：顶点表里躺着 8000 个没人引用的壳层顶点。
        // M2-E 的绕序自检正是在这里抓到它的（洋面与大气各有一半三角朝内、夹角余弦 −1.000）。
        int baseIdx = acc.VertexCount;
        int north = baseIdx + 1;
        int Interior(int i, int j) => baseIdx + 2 + (i - 1) * seg + (j % seg);
        int south = baseIdx + 2 + (ring - 1) * seg;

        // 北极顶点的高程取纬度 90° 处的采样（SampleElevationM 在极区会夹到首行，正是我们要的）
        acc.AddVertex(Xyz(90, 0, radiusOf(acc.ElevationAt(90, 0))));
        for (int i = 1; i < ring; i++)
        {
            double lat = 90.0 - i * 180.0 / ring;
            for (int j = 0; j < seg; j++)
            {
                double lon = -180.0 + j * 360.0 / seg;
                acc.AddVertex(Xyz(lat, lon, radiusOf(acc.ElevationAt(lat, lon))));
            }
        }
        acc.AddVertex(Xyz(-90, 0, radiusOf(acc.ElevationAt(-90, 0))));

        // 北冠：四边形 (0,j) (0,j+1) (1,j+1) (1,j) 退化掉两极，取三角形
        for (int j = 0; j < seg; j++)
        {
            int a = north, b = Interior(1, j + 1), c = Interior(1, j);
            acc.AddFace(objName, acc.FaceMaterial(a, b, c, materialOf), a, b, c);
        }

        // 中段四边形
        for (int i = 1; i < ring - 1; i++)
            for (int j = 0; j < seg; j++)
            {
                int a = Interior(i, j), b = Interior(i, j + 1);
                int c = Interior(i + 1, j + 1), d = Interior(i + 1, j);
                acc.AddFace(objName, acc.FaceMaterial(a, b, c, d, materialOf), a, b, c, d);
            }

        // 南冠
        for (int j = 0; j < seg; j++)
        {
            int a = Interior(ring - 1, j), b = Interior(ring - 1, j + 1), c = south;
            acc.AddFace(objName, acc.FaceMaterial(a, b, c, materialOf), a, b, c);
        }
    }

    private static (double X, double Y, double Z) Xyz(double lat, double lon, double radius)
        => Sphere.ToXyz(lat, lon, radius);

    // ══════════════════════════════════════════════════════════════
    //  累积器
    // ══════════════════════════════════════════════════════════════

    private sealed class Acc
    {
        public Mesh Mesh { get; } = new();

        private readonly PlanetRaster _raster;
        private readonly double _seaLevel;
        private readonly Dictionary<(string, int), SubMesh> _buckets = new();
        private readonly Dictionary<string, int> _facesPerObject = new();
        private readonly Dictionary<int, double> _areaPerContinent = new();

        private double _landArea, _totalArea;
        private double _minElev = double.MaxValue, _maxElev = double.MinValue;
        private double _minR = double.MaxValue, _maxR = double.MinValue;
        private int _triangles;
        private int _inward;
        private readonly Dictionary<string, int> _inwardPerObject = new();
        private readonly Dictionary<string, int> _triPerObject = new();
        private double _worstDot = 1;
        private string _worstObject = "";

        public Acc(PlanetRaster raster, double seaLevel)
        {
            _raster = raster;
            _seaLevel = seaLevel;
        }

        /// <summary>当前已累积的顶点数。各球共用一份全局顶点表，故编号是绝对的。</summary>
        public int VertexCount => Mesh.Vertices.Count;

        public double ElevationAt(double lat, double lon) => _raster.SampleElevationM(lat, lon);

        public void AddVertex((double X, double Y, double Z) p)
        {
            Mesh.Vertices.Add(p);
            double r = Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
            if (r < _minR) _minR = r;
            if (r > _maxR) _maxR = r;
        }

        public void AddFace(string objName, int material, params int[] idx)
        {
            var key = (objName, material);
            if (!_buckets.TryGetValue(key, out var sub))
            {
                sub = new SubMesh(objName, material, new List<int[]>());
                _buckets[key] = sub;
                Mesh.SubMeshes.Add(sub);
            }
            sub.Faces.Add(idx);

            _facesPerObject.TryGetValue(objName, out int n);
            _facesPerObject[objName] = n + 1;

            if (idx.Length == 3)
            {
                _triangles += 1;
                AccumulateArea(objName, idx[0], idx[1], idx[2]);
                CheckWinding(objName, idx[0], idx[1], idx[2]);
            }
            else
            {
                // 四边形拆成两个三角。对凸且近平面的四边形，这是精确的。
                _triangles += 2;
                AccumulateArea(objName, idx[0], idx[1], idx[2]);
                AccumulateArea(objName, idx[0], idx[2], idx[3]);
                CheckWinding(objName, idx[0], idx[1], idx[2]);
                CheckWinding(objName, idx[0], idx[2], idx[3]);
            }
        }

        /// <summary>
        /// 绕序自检：平面法线必须与「球心指向面片」同向。
        ///
        /// 用<b>顶点实际坐标</b>（含垂直放大）而不是单位球面 —— 放大 20× 后
        /// 陡坡上的面片法线会明显偏离径向，若拿单位球面去验就验不出真实几何。
        /// 判据留了退路：只有法线与径向夹角超过 90° 才算反，
        /// 所以轻微的坡度不会误报。极点三角形的两个顶点重合在一个点上，
        /// 面积不为零，同样适判。
        /// </summary>
        private void CheckWinding(string objName, int ia, int ib, int ic)
        {
            var a = Mesh.Vertices[ia - 1];
            var b = Mesh.Vertices[ib - 1];
            var c = Mesh.Vertices[ic - 1];

            double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
            double vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;

            double nx = uy * vz - uz * vy;
            double ny = uz * vx - ux * vz;
            double nz = ux * vy - uy * vx;

            double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            // 退化的零面积面（相邻顶点重合）不计入 —— 它们没有朝向可言
            if (nl < 1e-9) return;

            double cx = (a.X + b.X + c.X) / 3.0;
            double cy = (a.Y + b.Y + c.Y) / 3.0;
            double cz = (a.Z + b.Z + c.Z) / 3.0;
            double cl = Math.Sqrt(cx * cx + cy * cy + cz * cz);
            if (cl < 1e-9) return;

            double dot = (nx * cx + ny * cy + nz * cz) / (nl * cl);

            _inwardPerObject.TryGetValue(objName, out int n);
            _triPerObject.TryGetValue(objName, out int t);
            _triPerObject[objName] = t + 1;

            if (dot < _worstDot) { _worstDot = dot; _worstObject = objName; }
            if (dot < 0)
            {
                _inward++;
                _inwardPerObject[objName] = n + 1;
            }
        }

        /// <summary>
        /// 面材质 = 面心的材质。面心取顶点的三维重心再转回经纬度 ——
        /// 直接对经度求平均会在 ±180° 那条格上出错（−179.9 与 +179.9 的平均是 0，
        /// 跑到地球另一面去了），三维重心没有这个问题。
        /// </summary>
        public int FaceMaterial(int a, int b, int c,
                                Func<double, double, double, int> materialOf)
            => FaceMaterial(a, b, c, -1, materialOf);

        public int FaceMaterial(int a, int b, int c, int d,
                                Func<double, double, double, int> materialOf)
        {
            double x = 0, y = 0, z = 0;
            int n = 0;
            Span<int> idx = stackalloc int[4] { a, b, c, d };
            for (int k = 0; k < 4; k++)
            {
                if (k == 3 && d < 0) break;
                var (vx, vy, vz) = Mesh.Vertices[idx[k] - 1];
                double r = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                if (r < 1e-12) continue;
                x += vx / r; y += vy / r; z += vz / r; n++;
            }
            if (n == 0) return 0;

            var (lat, lon) = Sphere.ToLatLon(x, y, z);
            return materialOf(lat, lon, _raster.SampleElevationM(lat, lon));
        }

        /// <summary>
        /// 面积用<b>球面三角的球面盈余</b>（L'Huilier）算，不是平面三角形。
        /// 平面近似在 256×128 这种粗网格上会低估百分之一量级，
        /// 而这个数要拿去和栅格的面积占比对表 —— 差 1% 就失去对照意义了。
        ///
        /// 只有 <see cref="SurfaceObject"/> 参与陆海统计：云层若算进去，
        /// 「全球陆地占比」会被云的面积稀释成毫无意义的数。
        /// </summary>
        private void AccumulateArea(string objName, int ia, int ib, int ic)
        {
            if (objName != SurfaceObject) return;

            var a = Unit(ia); var b = Unit(ib); var c = Unit(ic);
            double area = SphericalTriangleArea(a, b, c);
            _totalArea += area;

            var (lat, lon) = Sphere.ToLatLon((a.X + b.X + c.X) / 3.0,
                                             (a.Y + b.Y + c.Y) / 3.0,
                                             (a.Z + b.Z + c.Z) / 3.0);
            double elev = _raster.SampleElevationM(lat, lon);
            if (elev < _minElev) _minElev = elev;
            if (elev > _maxElev) _maxElev = elev;

            if (elev <= _seaLevel) return;
            _landArea += area;

            int cid = _raster.ContinentAt(lat, lon);
            if (cid > 0)
            {
                _areaPerContinent.TryGetValue(cid, out double s);
                _areaPerContinent[cid] = s + area;
            }
        }

        private (double X, double Y, double Z) Unit(int v)
        {
            var (x, y, z) = Mesh.Vertices[v - 1];
            double r = Math.Sqrt(x * x + y * y + z * z);
            return r < 1e-12 ? (0, 1, 0) : (x / r, y / r, z / r);
        }

        public MeshStats Finish()
        {
            // 排序（物体名，材质号）：保证输出顺序确定，同 seed 逐字节一致（INV-34/35）
            Mesh.SubMeshes.Sort((x, y) =>
            {
                int c = string.CompareOrdinal(x.ObjectName, y.ObjectName);
                return c != 0 ? c : x.MaterialId.CompareTo(y.MaterialId);
            });

            var shares = new Dictionary<int, double>();
            for (int c = 1; c <= 5; c++)
                shares[c] = _areaPerContinent.TryGetValue(c, out double s) ? s / (4 * Math.PI) : 0.0;

            return new MeshStats(
                Mesh.Vertices.Count,
                _facesPerObject.Values.Sum(),
                _triangles,
                new SortedDictionary<string, int>(_facesPerObject, StringComparer.Ordinal),
                shares,
                _totalArea > 0 ? _landArea / _totalArea : 0,
                _minElev == double.MaxValue ? 0 : _minElev,
                _maxElev == double.MinValue ? 0 : _maxElev,
                _minR == double.MaxValue ? 0 : _minR,
                _maxR == double.MinValue ? 0 : _maxR,
                _inward,
                new SortedDictionary<string, int>(_inwardPerObject, StringComparer.Ordinal),
                new SortedDictionary<string, int>(_triPerObject, StringComparer.Ordinal),
                _worstObject,
                _worstDot);
        }

        /// <summary>单位球面上三角形的面积（球面盈余）。三边均为单位向量，边长即夹角。</summary>
        private static double SphericalTriangleArea(
            (double X, double Y, double Z) a, (double X, double Y, double Z) b,
            (double X, double Y, double Z) c)
        {
            double sa = Angle(b, c), sb = Angle(c, a), sc = Angle(a, b);
            double s = 0.5 * (sa + sb + sc);
            double t = Math.Tan(s / 2) * Math.Tan((s - sa) / 2)
                     * Math.Tan((s - sb) / 2) * Math.Tan((s - sc) / 2);
            return 4 * Math.Atan(Math.Sqrt(Math.Max(0, t)));
        }

        private static double Angle((double X, double Y, double Z) p, (double X, double Y, double Z) q)
            => Math.Acos(Math.Clamp(p.X * q.X + p.Y * q.Y + p.Z * q.Z, -1.0, 1.0));
    }

    // ══════════════════════════════════════════════════════════════
    //  导出
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 写 OBJ + MTL，返回写出的文件清单。
    ///
    /// 地表单独一个文件、壳层（ocean / atmosphere / clouds）合并到另一个文件 ——
    /// 因为壳层材质是半透明的，放进同一个文件的话，多数查看器会用大气壳把整颗星球罩住，
    /// 什么都看不见。分开之后 <c>planet_surface.obj</c> 可以直接打开，
    /// 壳层按需加载（Unity 里本来就是分开的对象）。两个文件共用同一份 <c>planet.mtl</c>。
    /// </summary>
    public static List<string> WriteObj(string dir, Mesh mesh, string baseName = "planet")
    {
        Directory.CreateDirectory(dir);

        string mtlPath = Path.Combine(dir, baseName + ".mtl");
        WriteMtl(mtlPath);

        var written = new List<string> { mtlPath };

        foreach (var g in mesh.SubMeshes
                     .GroupBy(s => s.ObjectName == SurfaceObject ? "surface" : "shells")
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            string name = g.Key == "surface" ? baseName + "_surface" : baseName + "_shells";
            string path = Path.Combine(dir, name + ".obj");
            WriteObjFile(path, mesh, g.ToList(), baseName + ".mtl");
            written.Add(path);
        }
        return written;
    }

    private static void WriteObjFile(string path, Mesh mesh, List<SubMesh> subs, string mtlName)
    {
        var ci = CultureInfo.InvariantCulture;

        // 顶点重编号：所有球共用一份全局顶点表，但一个文件往往只用到其中一部分
        // （地表文件只用前 32514 个，壳层文件只用后 24198 个）。
        // 整个表照抄一遍，文件会凭空大一倍，而且躺着一堆没人引用的顶点 ——
        // 有些查看器会据此报「游离顶点」警告。故按实际引用压缩成 1..N。
        var used = new SortedSet<int>();
        foreach (var s in subs)
            foreach (var f in s.Faces)
                foreach (int i in f) used.Add(i);

        var remap = new Dictionary<int, int>(used.Count);
        int next = 1;
        foreach (int i in used) remap[i] = next++;

        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        w.NewLine = "\n";

        w.WriteLine("# 蓝星（Humen）星球网格 —— design.md §7.4 / §13 M2-E");
        w.WriteLine($"# 顶点 {used.Count:N0} · 面 {subs.Sum(s => s.Faces.Count):N0}" +
                    $" · 三角 {subs.Sum(s => s.Faces.Count * (s.Faces[0].Length == 3 ? 1 : 2)):N0}");
        w.WriteLine("# 单位：1 unit = 6.371 km（§7.1，R = 1000 units ≈ 6371 km）");
        w.WriteLine("# Y 轴为地轴，x = R·cos(lat)·cos(lon)，z = R·cos(lat)·sin(lon)（§7.1）");
        w.WriteLine("# 材质按 §7.7 群系分组；材质定义见 " + mtlName);
        w.WriteLine($"mtllib {mtlName}");
        w.WriteLine();

        foreach (int old in used)
        {
            var v = mesh.Vertices[old - 1];
            w.WriteLine($"v {v.X.ToString("F4", ci)} {v.Y.ToString("F4", ci)} {v.Z.ToString("F4", ci)}");
        }
        w.WriteLine();

        // 法线 = 单位化的顶点位置（球面法线），不必另算。
        // 写出来是为了让查看器做平滑着色 —— 不写的话，多数查看器会按面算法线，
        // 256×128 的球看上去是一颗多面体。地形起伏不参与法线，这是有意的：
        // 20× 放大后的起伏若进法线，表面会碎成一片噪点。
        foreach (int old in used)
        {
            var v = mesh.Vertices[old - 1];
            double r = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (r < 1e-12) { w.WriteLine("vn 0 1 0"); continue; }
            w.WriteLine($"vn {(v.X / r).ToString("F6", ci)} {(v.Y / r).ToString("F6", ci)} {(v.Z / r).ToString("F6", ci)}");
        }
        w.WriteLine();

        foreach (var sub in subs)
        {
            w.WriteLine($"o {sub.ObjectName}__{MaterialName(sub.MaterialId)}");
            w.WriteLine($"usemtl {MaterialName(sub.MaterialId)}");
            foreach (var f in sub.Faces)
            {
                w.Write('f');
                foreach (int i in f) { int n = remap[i]; w.Write(' '); w.Write(n); w.Write("//"); w.Write(n); }
                w.Write('\n');
            }
            w.WriteLine();
        }
    }

    private static void WriteMtl(string path)
    {
        var ci = CultureInfo.InvariantCulture;

        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        w.NewLine = "\n";

        w.WriteLine("# 蓝星（Humen）材质库 —— design.md §7.7 群系配色");
        w.WriteLine("# Kd 直接写 sRGB 分量除以 255（0..1）。这是 OBJ 资产界的通行口径：");
        w.WriteLine("# 绝大多数查看器把 Kd 当作显示色，转成线性是渲染器自己的事。");
        w.WriteLine("# 若你的管线把 Kd 当线性色（部分 PBR 渲染器），画面会偏亮 ——");
        w.WriteLine("# 此时请对 Kd 做一次 sRGB→linear 再喂进去。");
        w.WriteLine("# d = 不透明度；只有壳层（water / atmosphere / clouds）不是 1。");
        w.WriteLine();

        for (int i = 0; i < MaterialCount; i++)
        {
            var (r, g, b) = MaterialColors[i];
            w.WriteLine($"newmtl {MaterialNames[i]}");
            w.WriteLine($"Kd {(r / 255.0).ToString("F6", ci)} {(g / 255.0).ToString("F6", ci)} {(b / 255.0).ToString("F6", ci)}");
            w.WriteLine("Ka 0.000000 0.000000 0.000000");
            w.WriteLine("Ks 0.000000 0.000000 0.000000");
            w.WriteLine("Ns 1.000000");
            w.WriteLine($"d {MaterialAlpha[i].ToString("F6", ci)}");
            w.WriteLine("illum 2");
            if (i < Climate.BiomeCount)
                w.WriteLine($"# {Climate.BiomeName((byte)i)}（§7.7）");
            w.WriteLine();
        }
    }
}
