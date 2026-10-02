namespace Humen.Core;

/// <summary>
/// 气候场 —— design.md §7.6 中<b>必须知道地形与海陆分布</b>才算得出来的那几个量。
///
/// M1 的降维版降水只按纬度分带（<see cref="Geography.LatitudeRainBand"/>），
/// 于是降水图是规则的纬向条带、沙漠与雨林都是横贯整圈的直线。M4 补上：
///
/// <list type="bullet">
///   <item><b>洋流</b>：大陆<b>西岸</b>受暖流（西边界流向高纬），<b>东岸</b>受寒流
///         （东边界流向低纬）—— §7.6「西边界流暖水向高纬 / 东边界流冷水向低纬」。</item>
///   <item><b>大陆度</b>：离海越远越干 —— §7.6 的 <c>−P_continentality</c>。
///         这一项是「副热带沙」的主要来源：副热带基准降水本就有 420 mm，
///         不够干；再扣掉内陆的几百毫米才落到 250 mm 以下。</item>
///   <item><b>迎风坡 / 雨影</b>：按纬度带定的盛行风算上风方向的抬升 —— §7.6 的
///         <c>P_orographic(迎风坡/雨影)</c>。</item>
///   <item><b>季风</b>：热带海岸的季节性反转风，东岸更强 —— §7.6 的 <c>P_monsoon</c>。</item>
/// </list>
///
/// <b>为什么要先烘一张 1° 的格网</b>：渲染一帧要判上千万个采样点，而
/// 「到海距离」与「西岸还是东岸」只与海陆分布有关、与海拔无关。
/// 对每个采样点现场做一次球面 BFS 是不可能的；烘一次（180×360 = 64 800 格）
/// 之后，取样退化成一次 O(1) 查表。
/// </summary>
public sealed class ClimateGrid
{
    /// <summary>格网分辨率（度）。1° ≈ 111 km，比本类所有衰减尺度都细，够用。</summary>
    public const double StepDeg = 1.0;

    /// <summary>纬向格数（南极到北极）。</summary>
    public const int Ny = 180;

    /// <summary>经向格数（整圈）。</summary>
    public const int Nx = 360;

    /// <summary>每度的大圆距离（km），与 <see cref="Sphere"/> 的半径一致。</summary>
    public const double KmPerDeg = Math.PI * PlanetRaster.EarthRadiusKm / 180.0;   // ≈ 111.19

    /// <summary>大陆度开始计罚的距离（km）。海岸线附近不算「内陆」。</summary>
    public const double CoastGraceKm = 120.0;

    /// <summary>洋流的沿岸衰减尺度（km）。</summary>
    public const double CurrentDecayKm = 600.0;

    private readonly bool[] _land = new bool[Ny * Nx];
    private readonly double[] _distSeaKm = new double[Ny * Nx];
    private readonly sbyte[] _side = new sbyte[Ny * Nx];    // +1 西岸 / −1 东岸 / 0 内陆
    private readonly double[] _elevM = new double[Ny * Nx];

    /// <summary>烘格网时用的场。留着只是为了在注释与诊断里对得上号。</summary>
    public PlanetField Field { get; }

    /// <summary>陆地格数，用于自检（应 ≈ 全球陆地占比 10.9 %）。</summary>
    public int LandCells { get; }

    private ClimateGrid(PlanetField field)
    {
        Field = field;

        // ── ① 海陆与高程 ──
        for (int iy = 0; iy < Ny; iy++)
        {
            double lat = LatAt(iy);
            for (int ix = 0; ix < Nx; ix++)
            {
                double lon = LonAt(ix);
                int k = iy * Nx + ix;
                bool land = field.IsLandAt(lat, lon);
                _land[k] = land;
                // 海洋格也存高程（海底），迎风坡取样跨到海上时用得着
                _elevM[k] = field.ElevationM(lat, lon);
                if (land) LandCells++;
            }
        }

        // ── ② 到海距离：以全部海洋格为源的多源 Dijkstra ──
        //
        // 用 Dijkstra 而不是 BFS：经度方向的一格在不同纬度上对应的公里数差很多
        // （赤道 111 km、70° 上只有 38 km）。等权 BFS 会把高纬的内陆距离高估两三倍，
        // 而大陆度是直接按公里数扣降水的 —— 高纬会因此凭空多出一片「极地荒漠」，
        // 与本次验收要的「极地白」打架。权值随步长的实际大圆距离走就没有这个问题。
        BuildDistanceToSea();

        // ── ③ 西岸还是东岸 ──
        BuildCoastSide();
    }

    /// <summary>从地形场烘一张气候格网。决定性的：同 seed 同场恒等。</summary>
    public static ClimateGrid Build(PlanetField field) => new(field);

    /// <summary>第 <paramref name="iy"/> 行的纬度（格中心）。</summary>
    public static double LatAt(int iy) => 90.0 - (iy + 0.5) * StepDeg;

    /// <summary>第 <paramref name="ix"/> 列的经度（格中心）。</summary>
    public static double LonAt(int ix) => -180.0 + (ix + 0.5) * StepDeg;

    private static int IyOf(double latDeg)
        => Math.Clamp((int)Math.Floor((90.0 - latDeg) / StepDeg), 0, Ny - 1);

    private static int IxOf(double lonDeg)
    {
        double lon = Sphere.WrapLon(lonDeg);
        return Math.Clamp((int)Math.Floor((lon + 180.0) / StepDeg), 0, Nx - 1);
    }

    // ══════════════════════════════════════════════════════════════
    //  烘格网
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 多源 Dijkstra：全部海洋格距离 0，向陆地扩散，边权 = 两格中心的大圆距离。
    ///
    /// 用<b>手写二叉堆</b>而不是 <c>SortedSet</c> / <c>PriorityQueue</c>：
    /// 一是 .NET 8 的 <c>PriorityQueue</c> 在同优先级时出队顺序不保证稳定，
    /// 而这里要的是「同 seed 逐位一致」（INV-34）；
    /// 二是 6.5 万格、每格 8 条边，手写堆的常数最小、且比较函数里能显式定序。
    /// 定序规则：先比距离，距离相同再比格号 —— 于是堆的行为完全确定。
    /// </summary>
    private void BuildDistanceToSea()
    {
        var dist = _distSeaKm;
        Array.Fill(dist, double.PositiveInfinity);

        int n = Ny * Nx;
        var heap = new int[n];
        var pos = new int[n];          // 格号 → 堆中位置（−1 = 不在堆里）
        Array.Fill(pos, -1);
        int size = 0;

        for (int k = 0; k < n; k++)
        {
            if (_land[k]) continue;
            dist[k] = 0;
            HeapPush(heap, pos, ref size, k, dist);
        }

        while (size > 0)
        {
            int u = HeapPop(heap, pos, ref size, dist);
            if (pos[u] != -1) continue;      // 已出堆（懒删除）

            int uy = u / Nx, ux = u % Nx;
            double uLat = LatAt(uy), uLon = LonAt(ux);

            for (int d = 0; d < 8; d++)
            {
                int vy = uy + NeighborDy[d];
                if (vy < 0 || vy >= Ny) continue;
                int vx = (ux + NeighborDx[d] + Nx) % Nx;
                int v = vy * Nx + vx;

                double vLat = LatAt(vy), vLon = LonAt(vx);
                double step = Geography.DistanceKm((uLat, uLon), (vLat, vLon));
                double alt = dist[u] + step;
                if (alt >= dist[v]) continue;
                dist[v] = alt;
                HeapPush(heap, pos, ref size, v, dist);
            }
        }
    }

    private static readonly int[] NeighborDy = { -1, 1, 0, 0, -1, -1, 1, 1 };
    private static readonly int[] NeighborDx = { 0, 0, -1, 1, -1, 1, -1, 1 };

    /// <summary>堆序：距离小的优先，距离相同则格号小的优先（为了确定性）。</summary>
    private static bool Less(int a, int b, double[] dist)
    {
        int c = dist[a].CompareTo(dist[b]);
        return c != 0 ? c < 0 : a < b;
    }

    private static void HeapPush(int[] heap, int[] pos, ref int size, int k, double[] dist)
    {
        int i = size++;
        heap[i] = k;
        pos[k] = i;
        while (i > 0)
        {
            int p = (i - 1) / 2;
            if (!Less(heap[i], heap[p], dist)) break;
            Swap(heap, pos, i, p);
            i = p;
        }
    }

    private static int HeapPop(int[] heap, int[] pos, ref int size, double[] dist)
    {
        int top = heap[0];
        pos[top] = -1;
        size--;
        if (size == 0) return top;

        heap[0] = heap[size];
        pos[heap[0]] = 0;
        int i = 0;
        while (true)
        {
            int l = 2 * i + 1, r = l + 1, m = i;
            if (l < size && Less(heap[l], heap[m], dist)) m = l;
            if (r < size && Less(heap[r], heap[m], dist)) m = r;
            if (m == i) break;
            Swap(heap, pos, i, m);
            i = m;
        }
        return top;
    }

    private static void Swap(int[] heap, int[] pos, int a, int b)
    {
        (heap[a], heap[b]) = (heap[b], heap[a]);
        pos[heap[a]] = a;
        pos[heap[b]] = b;
    }

    /// <summary>
    /// 判定每个陆地格是「西岸」还是「东岸」：沿本行向东西各走，看哪边先遇到海。
    ///
    /// 这里的西/东指<b>该格所临的那片海在它的哪一侧</b>，不是「大陆的哪一头」。
    /// 与 §7.6 的洋流规则对应关系：
    /// 海在西侧 → 本格是大陆西缘 → 受<b>西边界流</b>（暖流，向高纬输送热量）；
    /// 海在东侧 → 大陆东缘 → 受<b>东边界流</b>（寒流，向低纬输送冷水）。
    /// </summary>
    private void BuildCoastSide()
    {
        for (int iy = 0; iy < Ny; iy++)
        {
            for (int ix = 0; ix < Nx; ix++)
            {
                int k = iy * Nx + ix;
                if (!_land[k]) continue;

                int dW = int.MaxValue, dE = int.MaxValue;
                for (int s = 1; s <= Nx; s++)
                {
                    if (dW == int.MaxValue && !_land[iy * Nx + ((ix - s + Nx) % Nx)]) dW = s;
                    if (dE == int.MaxValue && !_land[iy * Nx + ((ix + s) % Nx)]) dE = s;
                    if (dW != int.MaxValue && dE != int.MaxValue) break;
                }

                // 两侧都没海：整行都是陆地（本世界不会发生，陆地只占 10.9 %）。
                // 真发生了就按内陆处理（side = 0），洋流项归零 —— 总比瞎猜一边强。
                if (dW == int.MaxValue && dE == int.MaxValue) continue;
                _side[k] = dW <= dE ? (sbyte)1 : (sbyte)-1;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  取样
    // ══════════════════════════════════════════════════════════════

    public bool IsLandAt(double latDeg, double lonDeg) => _land[IyOf(latDeg) * Nx + IxOf(lonDeg)];

    /// <summary>到最近海洋的大圆距离（km）。海洋格为 0。</summary>
    public double DistToSeaKm(double latDeg, double lonDeg) => _distSeaKm[IyOf(latDeg) * Nx + IxOf(lonDeg)];

    /// <summary>+1 = 西岸（暖流）／−1 = 东岸（寒流）／0 = 内陆。</summary>
    public int CoastSide(double latDeg, double lonDeg) => _side[IyOf(latDeg) * Nx + IxOf(lonDeg)];

    /// <summary>格网上的高程（m）。用来取「上风方向」的高程做迎风坡/雨影。</summary>
    public double GridElevationM(double latDeg, double lonDeg) => _elevM[IyOf(latDeg) * Nx + IxOf(lonDeg)];

    /// <summary>
    /// 上风方向的高程（m）。盛行风按纬度带取 §7.6 的三段：
    /// 信风带（|lat| &lt; 30°）与极地东风带（|lat| ≥ 60°）是<b>东风</b>，风自东来；
    /// 西风带（30° ~ 60°）相反。上风取样点取 <paramref name="reachDeg"/> 度之外。
    /// </summary>
    public double UpwindElevationM(double latDeg, double lonDeg, double absLat, double reachDeg)
    {
        double sign = Climate.WindUpwindLonSign(absLat);
        return GridElevationM(latDeg, lonDeg + sign * reachDeg);
    }
}
