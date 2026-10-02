namespace Humen.Core;

/// <summary>
/// 星球表面的<b>等距圆柱</b>（equirectangular）栅格 —— M2 的几何真值来源。
///
/// 渲染器、网格生成器、连通性判定（INV-8）都从这一份栅格取数，
/// 保证「看到的海岸线」与「判定的海岸线」是同一条。
///
/// <b>为什么连通性可以直接在等距圆柱格上用 8 邻接判</b>：
/// 有人会担心两极处经度被压缩、格元退化成一点，从而产生虚假连通。
/// 实际相反 —— 在纬度 89.96° 处，相邻两格（W = 2048）的球面距离只有约 <b>15 米</b>，
/// 远小于它们与<b>下一行</b>之间的距离（约 9.8 km）。
/// 也就是说极区格元在球面上本来就彼此紧贴，8 邻接给出的拓扑<b>恰好正确</b>，
/// 不需要把整行极性格元强行并成一个点。（真正需要小心的是 180° 经线接缝，
/// 已由 <see cref="WrapX"/> 处理。）
/// </summary>
public sealed class PlanetRaster
{
    /// <summary>经度方向格数。</summary>
    public int Width { get; }

    /// <summary>纬度方向格数。</summary>
    public int Height { get; }

    /// <summary>高程（米，相对今日海平面）。</summary>
    public float[] ElevationM { get; }

    /// <summary>归属大陆（0 = 不属任何大陆，1..5 为大陆号）。</summary>
    public byte[] Continent { get; }

    /// <summary>§7.3 判定为陆地的格元（即 <c>field &gt; threshold</c>）。</summary>
    public bool[] IsLand { get; }

    /// <summary>球面上一格在赤道处的边长（km）—— 等距圆柱是纬度相关的，故以赤道为准。</summary>
    public double CellKmAtEquator => 2 * Math.PI * EarthRadiusKm / Width;

    /// <summary>地球半径（km）。§7.1：1 Unity unit ≈ 6.371 km。</summary>
    public const double EarthRadiusKm = 6371.0;

    private PlanetRaster(int width, int height)
    {
        Width = width;
        Height = height;
        int n = width * height;
        ElevationM = new float[n];
        Continent = new byte[n];
        IsLand = new bool[n];
    }

    // ══════════════════════════════════════════════════════════════
    //  生成
    // ══════════════════════════════════════════════════════════════

    public static PlanetRaster Build(PlanetField field, int width, int height)
    {
        var r = new PlanetRaster(width, height);

        for (int y = 0; y < height; y++)
        {
            double lat = r.LatAt(y);
            for (int x = 0; x < width; x++)
            {
                double lon = r.LonAt(x);
                int i = y * width + x;

                // 一次求场，mask / 高程 / 归属三处复用（FieldAt 是最贵的一步）
                double f = field.FieldAt(lat, lon);
                bool land = f > PlanetField.Threshold;
                double m = PlanetField.MaskFromField(f);

                // 归属：陆地才问是哪块大陆；问不出（开阔洋面的孤立陆块）记 0，
                // 不能写成 max(1, ·) —— 那会把"不属于任何大陆"静默算成大陆 1。
                int cid = land ? field.NearestContinent(lat, lon) : -1;

                r.IsLand[i] = land;
                r.ElevationM[i] = (float)field.ElevationFromField(f, lat, lon);
                r.Continent[i] = cid > 0 ? (byte)cid : (byte)0;
            }
        }
        return r;
    }

    // ══════════════════════════════════════════════════════════════
    //  坐标换算
    // ══════════════════════════════════════════════════════════════

    /// <summary>第 <paramref name="y"/> 行中心的纬度。</summary>
    public double LatAt(int y) => 90.0 - (y + 0.5) * 180.0 / Height;

    /// <summary>第 <paramref name="x"/> 列中心的经度。</summary>
    public double LonAt(int x) => -180.0 + (x + 0.5) * 360.0 / Width;

    /// <summary>经度 → 最近的列号（自动环绕）。</summary>
    public int WrapX(int x)
    {
        x %= Width;
        return x < 0 ? x + Width : x;
    }

    // ══════════════════════════════════════════════════════════════
    //  采样（供渲染器与网格生成器使用）
    // ══════════════════════════════════════════════════════════════

    /// <summary>双线性插值高程（米）。用于渲染出平滑地形，避免格子感。</summary>
    public double SampleElevationM(double latDeg, double lonDeg)
    {
        double fy = (90.0 - latDeg) * Height / 180.0 - 0.5;
        double fx = (lonDeg + 180.0) * Width / 360.0 - 0.5;

        int y0 = (int)Math.Floor(fy);
        double ty = fy - y0;
        int x0 = (int)Math.Floor(fx);
        double tx = fx - x0;

        double a = ElevAt(WrapX(x0), ClampY(y0));
        double b = ElevAt(WrapX(x0 + 1), ClampY(y0));
        double c = ElevAt(WrapX(x0), ClampY(y0 + 1));
        double d = ElevAt(WrapX(x0 + 1), ClampY(y0 + 1));

        return (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty;
    }

    private int ClampY(int y) => y < 0 ? 0 : (y >= Height ? Height - 1 : y);

    private double ElevAt(int x, int y) => ElevationM[y * Width + x];

    /// <summary>最近邻取大陆归属。</summary>
    public int ContinentAt(double latDeg, double lonDeg)
    {
        int y = ClampY((int)Math.Round((90.0 - latDeg) * Height / 180.0 - 0.5));
        int x = WrapX((int)Math.Round((lonDeg + 180.0) * Width / 360.0 - 0.5));
        return Continent[y * Width + x];
    }

    // ══════════════════════════════════════════════════════════════
    //  INV-8：连通性判定
    // ══════════════════════════════════════════════════════════════

    public sealed record ConnectivityResult(
        double SeaLevelM,
        bool ContinentsSeparated,
        int ComponentCount,
        int LandCells,
        IReadOnlyList<string> BridgingComponents);

    /// <summary>
    /// 给定海平面下、<b>按球面面积加权</b>的陆地占比。
    ///
    /// 不能用「陆格数 / 总格数」：等距圆柱栅格的格元面积 ∝ cos(纬度)，
    /// 高纬格元被严重高估（极点那一行真实面积趋近于 0）。
    /// 直接数格子会把极区放大好几倍 —— 而 5 块大陆里有两块就在极区，
    /// 误差方向还是偏大的，正好把「网格有没有忠实采样栅格」这件事看糊。
    ///
    /// 判据与 <see cref="AnalyzeConnectivity"/> 一致：露出水面 = 高程 &gt; 海平面。
    /// </summary>
    public double LandFractionAt(double seaLevelM)
    {
        double land = 0, total = 0;
        for (int y = 0; y < Height; y++)
        {
            double w = Math.Cos(LatAt(y) * Math.PI / 180.0);
            if (w <= 0) continue;
            int row = y * Width;
            for (int x = 0; x < Width; x++)
            {
                total += w;
                if (ElevationM[row + x] > seaLevelM) land += w;
            }
        }
        return total > 0 ? land / total : 0.0;
    }

    /// <summary>
    /// 在给定海平面下，把露出水面的格元做连通分量分解，检查<b>是否有分量跨越两块大陆</b>。
    ///
    /// 这就是 INV-8 的判定本身（R2 硬约束：海平面最低时 5 大陆仍不连通）。
    /// 用并查集一次扫完，跨 180° 经线与极区均按 <see cref="WrapX"/> 与 8 邻接处理。
    /// </summary>
    public ConnectivityResult AnalyzeConnectivity(double seaLevelM)
    {
        int w = Width, h = Height;
        int n = w * h;

        int[] parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        int Find(int a)
        {
            while (parent[a] != a)
            {
                parent[a] = parent[parent[a]];   // 路径压缩（折半）
                a = parent[a];
            }
            return a;
        }
        void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) parent[rb] = ra;
        }

        // 露出水面 = 高程 > 海平面
        var land = new bool[n];
        int landCells = 0;
        for (int i = 0; i < n; i++)
        {
            if (ElevationM[i] > seaLevelM) { land[i] = true; landCells++; }
        }

        // 8 邻接。只需向右、向下、向右下、向左下四个方向做并，
        // 反向的四个由邻格在它自己那一轮补上；行内环绕由 WrapX 处理。
        for (int y = 0; y < h; y++)
        {
            int rightBase = y * w;
            int downBase = rightBase + w;

            for (int x = 0; x < w; x++)
            {
                int i = rightBase + x;
                if (!land[i]) continue;

                int iright = rightBase + WrapX(x + 1);
                if (iright != i && land[iright]) Union(i, iright);

                if (y + 1 >= h) continue;

                int idown = i + w;
                if (land[idown]) Union(i, idown);

                int idownLeft = downBase + WrapX(x - 1);
                if (land[idownLeft]) Union(i, idownLeft);

                int idownRight = downBase + WrapX(x + 1);
                if (land[idownRight]) Union(i, idownRight);
            }
        }

        // 一遍扫完：分量计数 + 每个分量出现过哪些大陆（按位标记）
        var masks = new Dictionary<int, int>();
        var roots = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            if (!land[i]) continue;
            int root = Find(i);
            roots.Add(root);

            int c = Continent[i];
            if (c == 0) continue;                 // 落在开阔洋面的孤立陆块：不归属任何大陆

            masks.TryGetValue(root, out int m);
            masks[root] = m | (1 << c);
        }

        var bridging = new List<string>();
        foreach (var kv in masks)
        {
            int bits = kv.Value;
            if (System.Numerics.BitOperations.PopCount((uint)bits) <= 1) continue;

            var names = new List<string>();
            for (int c = 1; c <= 5; c++)
                if ((bits & (1 << c)) != 0) names.Add($"C{c}");
            bridging.Add(string.Join("+", names));
        }

        return new ConnectivityResult(seaLevelM, bridging.Count == 0,
                                      roots.Count, landCells, bridging);
    }

    /// <summary>
    /// 设计意图上的<b>最窄间隔</b>：所有正权种子两两之间，
    /// 大圆距离减去各自影响半径，取最小值（km）。它衡量的是「大陆场本身把两块大陆分开了多远」，
    /// 与 <see cref="AnalyzeConnectivity"/> 的「实际有没有连上」互为佐证。
    /// </summary>
    public static (int A, int B, double GapKm) NarrowestDesignGap(PlanetField field)
    {
        var pos = field.Seeds.Where(s => !s.IsNegative).ToList();
        double best = double.MaxValue;
        int bestA = 0, bestB = 0;

        for (int i = 0; i < pos.Count; i++)
        {
            for (int j = i + 1; j < pos.Count; j++)
            {
                if (pos[i].ContinentId == pos[j].ContinentId) continue;

                var (ax, ay, az) = Sphere.ToXyz(pos[i].Lat, pos[i].Lon, 1.0);
                var (bx, by, bz) = Sphere.ToXyz(pos[j].Lat, pos[j].Lon, 1.0);
                double dot = Math.Clamp(ax * bx + ay * by + az * bz, -1.0, 1.0);
                double ang = Math.Acos(dot);

                double gapKm = (ang - (pos[i].RadiusDeg + pos[j].RadiusDeg) * Math.PI / 180.0) * EarthRadiusKm;
                if (gapKm < best)
                {
                    best = gapKm;
                    bestA = pos[i].ContinentId;
                    bestB = pos[j].ContinentId;
                }
            }
        }
        return (bestA, bestB, best == double.MaxValue ? 0 : best);
    }
}
