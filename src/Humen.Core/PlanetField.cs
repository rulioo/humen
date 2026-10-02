namespace Humen.Core;

/// <summary>
/// 星球表面标量场 —— design.md §7.3（大陆轮廓）+ §7.4（高程与海水）。
///
/// <b>§7.3 metaball 距离场</b>
/// <code>
/// field(p) = Σ_i w_i · smoothstep(greatCircleDist(p, seed_i) / r_i)
/// land     = field(p) > threshold
/// </code>
/// 每大陆 6~12 个种子圆盘（含负权种子造海湾/半岛），叠加 Simplex 噪声制造峡湾与离岛。
///
/// ⚠️ <b>原文的 <c>smoothstep</c> 必须理解为「倒扣的衰减核」</b>：标准
/// <c>smoothstep(t) = t²(3−2t)</c> 在 <c>t = 0</c> 处取 <b>0</b>，若照字面代入，
/// 每个种子圆盘<b>圆心</b>的场值反倒是 0、<c>field &gt; threshold</c> 在圆心处不成立 ——
/// 于是每块大陆都会变成一个圆环，中间是海。显然不是本意。
/// 故实现取 <c>falloff(u) = 1 − smoothstep(u)</c>：圆心 1、半径处 0、两端导数为 0（C¹ 连续）。
///
/// <b>为什么所有噪声都在三维单位球面上取</b>：见 <see cref="Simplex3"/> ——
/// 在 (lat, lon) 参数空间取会同时产生 180° 接缝与两极捏缩。
///
/// <b>确定性</b>：种子圆盘位置、权重、噪声置换表全部由 world seed 导出，无一处随机（INV-12/34）。
/// </summary>
public sealed class PlanetField
{
    // ══════════════════════════════════════════════════════════════
    //  场参数
    // ══════════════════════════════════════════════════════════════

    /// <summary>陆地判据的等值线高度。取 metaball 的天然等值面 0.5（见 <see cref="Falloff"/>）。</summary>
    public const double Threshold = 0.5;

    /// <summary>
    /// <see cref="MaskAt"/> 的过渡半宽。<c>mask</c> 在 <c>field ∈ [Threshold±w]</c> 上
    /// 由 0 平滑升到 1，宽 2w。它把硬的 <c>land</c> 布尔量变成软权重，
    /// 供高程混合出<b>大陆架</b>，否则海岸会是一道垂直的悬崖。
    /// </summary>
    public const double MaskHalfWidth = 0.18;

    /// <summary>大陆轮廓噪声：大尺度 fBm，制造海湾与半岛（量级与 threshold 同阶才对海岸有肉眼可见的推移）。</summary>
    private const double CoastFbmAmp = 0.100;
    private const double CoastFbmFreq = 2.2;
    private const int CoastFbmOctaves = 5;

    /// <summary>大陆轮廓噪声：脊状分量，制造峡湾式的尖锐切割。</summary>
    private const double CoastRidgeAmp = 0.070;
    private const double CoastRidgeFreq = 6.5;
    private const int CoastRidgeOctaves = 4;

    // ── §7.4 高程剖面：h = base + noise × amplitude ──
    //
    //   海底用 mask（陆海软权重）插值，做「大陆架 → 陆坡 → 深海」三段剖面。
    //   0 点钉在 mask = 0.5（即海岸线），使「§7.3 的 land 判据」与
    //   「§7.4 的 h > 海平面」两条定义在海岸线上重合 —— 否则会出现
    //   metaball 说是陆地、高程说是海底的自相矛盾区域。
    private static readonly double[] OceanKnots = { 0.00, 0.10, 0.35, 0.50 };
    private static readonly double[] OceanElevKnots = { -4200, -3000, -200, 0 };

    /// <summary>
    /// 陆地用「内陆度」而不是 mask 插值 —— <b>这是 §13.3「陆地是一整块高原」的修复</b>。
    ///
    /// mask 到 <c>field = Threshold + MaskHalfWidth</c>（= 0.68）就饱和了，
    /// 而实测陆地 field 的 P5~P99 是 0.57~4.98：<b>90.7 % 的陆地都挤在 mask = 1.0</b>。
    /// 高程模型因此在绝大部分陆地上分不出「刚上岸」和「大陆腹地」，
    /// 整块大陆只能取到同一个 base 值 —— 高原就是这么来的。
    ///
    /// 内陆度改用 field 本身归一：field 是大陆场的自然量，近岸小、腹地大，
    /// 正好当「离岸有多远」的代理量，且不必再额外做一次全局距离场。
    /// 跨度取 3.0，使 field ∈ [0.5, 3.5] 覆盖到陆地 P90 附近。
    /// </summary>
    private const double InlandFieldSpan = 3.0;

    /// <summary>内陆度 → 基准高程（m）。近岸 0~150 m 的平原带，腹地抬到 1 900 m 高原。</summary>
    private static readonly double[] InlandKnots = { 0.00, 0.12, 0.30, 0.55, 0.80, 1.00 };
    private static readonly double[] InlandElevKnots = { 0, 150, 450, 950, 1500, 1900 };

    private const double LandNoiseMinM = 60;     // 海岸处（平原上只有微起伏）
    private const double LandNoiseMaxM = 700;    // 大陆内部（山脉）
    private const double OceanNoiseMaxM = 400;   // 深海（海岭/海沟）
    private const double ShelfNoiseFadeMask = 0.35;  // mask < 此值才开始有海底起伏
    private const double ElevNoiseFreq = 2.6;
    private const int ElevNoiseOctaves = 6;      // §7.4：octaves = 6

    // ══════════════════════════════════════════════════════════════
    //  种子圆盘
    // ══════════════════════════════════════════════════════════════

    /// <param name="RadiusDeg">影响半径（度）。</param>
    /// <param name="Weight">权。负权 = 挖海湾 / 切半岛。</param>
    public sealed record SeedDisk(int ContinentId, double Lat, double Lon,
                                  double RadiusDeg, double Weight, bool IsNegative);

    private List<SeedDisk> _seeds;
    private readonly List<SeedDisk> _designSeeds;   // 未标定的原始布点，每次尝试都从这里重置
    private readonly Simplex3 _noise;
    private readonly long _seed;
    private readonly int[] _continents;

    /// <summary>
    /// 种子圆盘的<b>预计算三角量</b>。内层循环一次标定要跑几十万采样点 × 几十个种子，
    /// 若每次现算 <c>ToXyz</c>（两次三角函数）与 <c>cos(r)</c>，就是上千万次三角调用。
    /// 预先摊平后，内层只剩点积与一次比较。
    /// </summary>
    private readonly struct Prepped
    {
        public readonly double X, Y, Z;      // 单位球面上的种子方向
        public readonly double InvRadius;    // 1 / r（弧度）
        public readonly double CosRadius;    // cos(r)，用于精确排除
        public readonly double Weight;
        public readonly int ContinentId;
        public readonly bool Negative;

        public Prepped(SeedDisk s)
        {
            var (x, y, z) = Sphere.ToXyz(s.Lat, s.Lon, 1.0);
            X = x; Y = y; Z = z;
            double rRad = s.RadiusDeg * Math.PI / 180.0;
            InvRadius = rRad > 1e-12 ? 1.0 / rRad : 0.0;
            CosRadius = Math.Cos(rRad);
            Weight = s.Weight;
            ContinentId = s.ContinentId;
            Negative = s.IsNegative;
        }
    }

    private Prepped[] _prep = Array.Empty<Prepped>();

    public IReadOnlyList<SeedDisk> Seeds => _seeds;
    public long Seed => _seed;

    /// <summary>
    /// 实际收敛到的全球陆地占比。
    ///
    /// 它未必等于 <see cref="TargetLandFraction"/>：若按目标标定会撞上
    /// INV-8（大陆被陆桥连起来），就会自动收缩到<b>可行域上界</b>。
    /// 上界是多少由几何决定，不是调的 —— 见 <see cref="SearchLandTarget"/>。
    /// </summary>
    public double AchievedLandTarget { get; private set; }

    /// <summary>按 <see cref="TargetLandFraction"/> 标定后，大陆是否仍被陆桥连起来。</summary>
    public bool TargetWasFeasible { get; private set; }

    private PlanetField(long seed, List<SeedDisk> seeds)
    {
        _seed = seed;
        _designSeeds = seeds;
        _seeds = new List<SeedDisk>(seeds);
        _noise = new Simplex3(unchecked((long)Hashing.Hash64(seed, HashDomain.Elevation, 7, 7, 7)));
        _continents = Geography.Continents.Select(c => c.Id).ToArray();

        SearchLandTarget();
    }

    private void RecomputePrep()
    {
        if (_prep.Length != _seeds.Count) _prep = new Prepped[_seeds.Count];
        for (int i = 0; i < _seeds.Count; i++) _prep[i] = new Prepped(_seeds[i]);
    }

    public static PlanetField Build(long seed) => new(seed, PlaceSeeds(seed));

    // ══════════════════════════════════════════════════════════════
    //  种子布点
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 每大陆布 6~12 个种子圆盘（§7.3）。
    ///
    /// 布法：先沿一条横贯大陆的<b>脊线</b>等距放 5~7 个主种子（保证连成一整块而非群岛），
    /// 再在脊线两侧加 1~3 个<b>叶</b>种子造不规则轮廓，最后加 1~3 个<b>负权</b>种子
    /// 在大陆边缘挖出海湾、切出半岛。
    /// </summary>
    private static List<SeedDisk> PlaceSeeds(long seed)
    {
        var disks = new List<SeedDisk>();

        foreach (var c in Geography.Continents)
        {
            var rng = new Rng(unchecked((long)Hashing.Hash64(seed, HashDomain.Tribe, 500 + c.Id, 0, 0)));

            double latSpan = c.LatMax - c.LatMin;
            double lonSpan = c.LonMax - c.LonMin;
            if (lonSpan < 0) lonSpan += 360;

            // 脊线：在包围盒内取一条略带弯折的对角线
            int spine = rng.NextInt(5, 8);                       // 5~7
            double aLat = c.LatMin + latSpan * rng.NextRange(0.20, 0.34);
            double aLon = c.LonMin + lonSpan * rng.NextRange(0.12, 0.28);
            double bLat = c.LatMin + latSpan * rng.NextRange(0.66, 0.80);
            double bLon = c.LonMin + lonSpan * rng.NextRange(0.72, 0.88);
            double bowLat = rng.NextRange(-0.18, 0.18) * latSpan;  // 弓形弯曲
            double bowLon = rng.NextRange(-0.18, 0.18) * lonSpan;

            // 主种子半径：取短轴的 ~0.42，使相邻种子必然重叠成一块
            double rBase = Math.Min(latSpan, lonSpan) * rng.NextRange(0.36, 0.46);

            for (int i = 0; i < spine; i++)
            {
                double u = spine == 1 ? 0.5 : i / (double)(spine - 1);
                double bow = Math.Sin(u * Math.PI);                // 两端 0、中间 1
                double lat = aLat + (bLat - aLat) * u + bowLat * bow;
                double lon = aLon + (bLon - aLon) * u + bowLon * bow;

                // 中段最粗、两端收细 —— 大陆才有一副自然的纺锤形轮廓
                double taper = 0.72 + 0.28 * bow;
                disks.Add(new SeedDisk(c.Id, lat, Sphere.WrapLon(lon),
                                       rBase * taper * rng.NextRange(0.90, 1.10),
                                       rng.NextRange(0.85, 1.15), false));
            }

            // 叶：挂在脊线两侧，造不规则外凸。
            //
            // ⚠️ v0.24 由「1~3 个大叶」改成「**3~6 个小叶**」。
            //    作者要的是「非规则形状」，而大叶 + 圆润的 metaball 叠加出来的是一个
            //    **带疙瘩的椭圆** —— 出图一看就是"一颗蛋上贴了几个包"，不是大陆。
            //    真实大陆的轮廓来自**许多小尺度凸起**（半岛、岬角）与**深凹的海湾**
            //    交替咬合，所以这里把叶改多改小（半径 0.40~0.70 而不是 0.52~0.78），
            //    同时把海湾从 1~3 条加到 2~4 条、咬得更深。
            //    面积不受影响：总面积由 Calibrate 反解半径保证，改的只是**形状的频谱**。
            int lobes = rng.NextInt(3, 7);                          // 3~6
            for (int i = 0; i < lobes; i++)
            {
                double u = rng.NextRange(0.20, 0.80);
                double bow = Math.Sin(u * Math.PI);
                double lat = aLat + (bLat - aLat) * u + bowLat * bow;
                double lon = aLon + (bLon - aLon) * u + bowLon * bow;

                double side = rng.Chance(0.5) ? 1 : -1;
                double off = rBase * rng.NextRange(0.55, 1.05);
                lat += side * off * rng.NextRange(0.30, 0.95);
                lon += side * off * rng.NextRange(0.30, 0.95);

                // ⚠️ 必须夹回包围盒。叶的偏移量按 rBase（短轴 × 0.42）缩放，
                //    北温带大陆短轴 32° 时能甩出 ±11° —— 越过北边界之后
                //    就和北寒带大陆的陆地连成一片（INV-8 破），
                //    而且那块寒带大陆还因此长出了可农耕的温带陆地（§6.1 破）。
                var p = c.ClampToBBox(lat, lon);

                disks.Add(new SeedDisk(c.Id, p.Lat, p.Lon,
                                       rBase * rng.NextRange(0.40, 0.70),
                                       rng.NextRange(0.55, 0.95), false));
            }

            // 负权：挖海湾 / 切半岛
            int bays = rng.NextInt(2, 5);                           // 2~4
            for (int i = 0; i < bays; i++)
            {
                double u = rng.NextRange(0.15, 0.85);
                double bow = Math.Sin(u * Math.PI);
                double lat = aLat + (bLat - aLat) * u + bowLat * bow;
                double lon = aLon + (bLon - aLon) * u + bowLon * bow;

                double side = rng.Chance(0.5) ? 1 : -1;
                double off = rBase * rng.NextRange(0.70, 1.15);
                lat += side * off * rng.NextRange(0.40, 1.00);
                lon += side * off * rng.NextRange(0.40, 1.00);

                // 同上：负权种子也夹回盒内 —— 海湾本来就该是"从盒内咬掉一块"。
                var p = c.ClampToBBox(lat, lon);

                disks.Add(new SeedDisk(c.Id, p.Lat, p.Lon,
                                       rBase * rng.NextRange(0.45, 0.80),
                                       -rng.NextRange(0.55, 1.05), true));
            }
        }
        return disks;
    }

    // ══════════════════════════════════════════════════════════════
    //  面积标定
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 全球陆地面积占比的<b>期望值</b>（实际收敛到的值见 <see cref="AchievedLandTarget"/>）。
    ///
    /// ⚠️ <b>§7.2 的 面积占比 一栏（12/12/14/8/8，合计 54%）与 INV-7/INV-8 几何上不相容。</b>
    /// 这是可证明的，不是实现没调好：
    ///
    /// 5 块大陆两两中心大圆角距 ≥ 55°（§7.2 自验，即 INV-7）。两块球冠不相接要求
    /// <c>θ_a + θ_b ≤ 55°</c>；若五块均分，则 <c>θ ≤ 27.5°</c>，单块占
    /// <c>(1 − cos 27.5°)/2 = 5.65%</c>，<b>五块合计上界 28.3%</b>。
    /// 而 54% 要求每块平均 10.8%，即 <c>θ = acos(1 − 2×0.108) = 38.4°</c> —— 远超 27.5°。
    /// 更何况 §7.2 的包围盒五块加起来只覆盖球面 <b>10.9%</b>，本身就装不下 54% 的陆地。
    ///
    /// 顺带一提：28.3% 与地球实际陆地占比（29%）几乎相等。也就是说
    /// 「5 块大洲 + 55° 间隔」这套骨架本身是地球量级的，54% 是把地球乘以 1.9。
    ///
    /// 且按 §7.2 的<b>相对</b>大小（12:12:14:8:8）分配时，真正的瓶颈落在最近的那一对
    /// （C1↔C4，一胖一瘦）上，可行上界还会更低一些 —— 故不写死常数，
    /// 由 <see cref="SearchLandTarget"/> 实测。
    /// </summary>
    /// <summary>
    /// 全球陆地面积占比的目标值。
    ///
    /// ⚠️ v0.24 由 0.22 提到 <b>0.30</b>（作者要求"5 块大陆大约占全球面积的 30%"）。
    ///
    /// 但真正卡住陆地面积的是 <see cref="BoxOvergrowthLimit"/> 与 §7.2 的包围盒，
    /// <b>不是这个数</b>：旧布局五个盒合计只有球面的 10.90%，每块大陆的目标面积
    /// 都被"不得超过自身包围盒"削到顶格，于是把这里的 0.22 改成 0.30 一个像素都不会变。
    /// 实测：改之前全球陆地 10.89%、有效目标 10.90%，五个盒全部 100% 顶格。
    /// <b>想让陆地变多，得先把盒子放大</b>（见 Geography.Continents 处的注释）。
    ///
    /// 海平面仍然由 <see cref="SearchLandTarget"/> 二分搜索反解，并且该搜索会在
    /// 陆地连成一片（INV-8 陆桥）时<b>自动把目标缩回去</b>，所以这里给一个偏大的
    /// 0.30 是安全的：够不着就自己降，不会悄悄造出一座陆桥。
    /// </summary>
    public const double TargetLandFraction = 0.30;

    /// <summary>
    /// 求「既满足 <see cref="TargetLandFraction"/>、又满足 INV-8」的陆地占比。
    ///
    /// <b>为什么要搜，而不是直接取 22%：</b>
    /// metaball 场是<b>求和</b>的（§7.3 原文 <c>Σ w_i·…</c>，负权种子挖海湾也依赖求和）。
    /// 求和意味着两块大陆即使各自的陆地范围没有接触，中间地带「你贡献 0.3、我贡献 0.3」
    /// 加起来也能越过 threshold —— 于是凭空长出一道陆桥，INV-8 破。
    /// 这不是标定精度问题，是<b>几何上必然发生</b>的事：陆地占比越大，大陆越胖，
    /// 越容易在最近的一对之间连上。所以可行域存在一个上界，且只有靠<b>真的跑一遍 INV-8</b>
    /// 才能定出来（解析式要处理 5 块 × 每块 10 个圆盘的相互叠加，不如直接测）。
    ///
    /// 判据单调（占比越大越容易连），故用二分。判据就是 <see cref="PlanetRaster.AnalyzeConnectivity"/>
    /// —— 与验收时用的是同一段代码，不是另写一个近似判据。
    /// </summary>
    private void SearchLandTarget()
    {
        if (CalibrateAndTest(TargetLandFraction))
        {
            AchievedLandTarget = TargetLandFraction;
            TargetWasFeasible = true;
            return;
        }

        // 二分：lo 一定可行（近乎没陆地，不可能连），hi 已知不可行
        double lo = 0.001, hi = TargetLandFraction;
        for (int i = 0; i < 9; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (CalibrateAndTest(mid)) lo = mid; else hi = mid;
        }

        // 收尾：用 lo 再标定一次，使 _seeds 停在可行解上
        CalibrateAndTest(lo);
        AchievedLandTarget = lo;
        TargetWasFeasible = false;
    }

    /// <summary>
    /// 每块大陆的陆地面积上限 = 它自己的 §7.2 包围盒面积 × 本系数。
    ///
    /// <b>为什么必须有这道闸</b>：§7.2 的包围盒五块合计只占球面 <b>10.9%</b>，
    /// 而同一张表的 面积占比 一栏合计 <b>54%</b> —— 差了 5 倍。若不设闸，
    /// 「按 12:12:14:8:8 分摊」会把最吃亏的 C4/C5 逼到 5.5 倍于自身包围盒，
    /// 于是北寒带大陆（68°~84°N、<c>AgriculturePotential.None</c>、驯鹿）的陆地
    /// 有 82% 长到了温带 —— 那不只是难看，是<b>把 §6.1 的环境禀赋改掉了</b>：
    /// 一块本该无法农耕的大陆会变成可农耕的。模拟里没有比这更贵的事故。
    ///
    /// 取 1.0 表示「陆地面积不得超过自己的包围盒」。取 1.0 而非更松，是因为
    /// metaball 边界不规则，包围盒实际填充率约七成，最后落在盒内的陆地约占七成 —— 已经是
    /// 在不改 §7.2 布局的前提下能做到的上限。想更宽就调大本常数，代价是大陆外溢更多。
    /// </summary>
    public const double BoxOvergrowthLimit = 1.0;

    /// <summary>§7.2 包围盒占球面的比例（球面矩形的面积 / 4π）。</summary>
    public static double BBoxFraction(Continent c)
    {
        double dLon = c.LonMax - c.LonMin;
        if (dLon < 0) dLon += 360;                       // 跨 180°（大陆 3）
        double dLatSin = Math.Sin(c.LatMax * Math.PI / 180.0)
                       - Math.Sin(c.LatMin * Math.PI / 180.0);
        return Math.Abs(dLatSin * dLon * Math.PI / 180.0) / (4 * Math.PI);
    }

    /// <summary>
    /// 给定全局尺度，算出各大陆的目标面积占比（占整球）：
    /// 先按 §7.2 的相对大小（12:12:14:8:8）分摊，再各自施加 <see cref="BoxOvergrowthLimit"/> 上限。
    /// </summary>
    private static Dictionary<int, double> TargetsFor(double landTarget)
    {
        double weightSum = Geography.Continents.Sum(c => c.LandFraction);
        var t = new Dictionary<int, double>();
        foreach (var c in Geography.Continents)
            t[c.Id] = Math.Min(landTarget * c.LandFraction / weightSum,
                               BoxOvergrowthLimit * BBoxFraction(c));
        return t;
    }

    /// <summary>按给定全球占比标定，并判断大陆之间是否留有足够的洋面。</summary>
    private bool CalibrateAndTest(double landTarget)
    {
        Calibrate(landTarget);
        return !HasBridge();
    }

    /// <summary>大陆之间是否会被陆桥连起来（INV-8 的判据）。</summary>
    public bool HasBridge() => WorstBridge().GapDeg < MinOceanGapDeg;

    // ══════════════════════════════════════════════════════════════
    //  陆桥检测 —— INV-8 的解析判据
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 两块大陆之间至少要留多宽的洋面（度）。约 8° ≈ 890 km，比地中海最窄处还宽些。
    ///
    /// ⚠️ <b>判据是「洋面的宽度」，不是「洋面上的场峰值」。</b>
    /// 曾按峰值判过，判不出来：缺口两侧紧邻大陆，而场在陆地边缘必然从 threshold
    /// 连续降下来，于是「缺口峰值」永远≈0.499…，判据恒不成立，二分一路收缩到
    /// 全球只剩 0.1% 陆地。峰值在这里是个取不到的边界值，宽度才是实的。
    /// </summary>
    public const double MinOceanGapDeg = 8.0;

    /// <summary>最危险的一对大陆。</summary>
    /// <param name="A">大陆号。</param>
    /// <param name="B">大陆号。</param>
    /// <param name="GapDeg">两块<b>陆地</b>之间的洋面宽度（度）。0 即已相接。</param>
    /// <param name="MidField">洋面中点处的场值（参照用，恒 &lt; threshold）。</param>
    /// <param name="SeedGapDeg">最近的一对种子圆盘之间的角距（度）。</param>
    /// <param name="ClearDeg">净空角距 = 角距 − 两边半径和。负值即两个圆盘本身已重叠。</param>
    public sealed record Bridge(int A, int B, double GapDeg, double MidField,
                                double SeedGapDeg, double ClearDeg);

    private const int BridgeSamples = 512;

    /// <summary>
    /// 找出最危险的一对大陆。
    ///
    /// <b>为什么不用栅格上的连通分量，而要另算一遍：</b>连通分量的结论<b>依赖分辨率</b> ——
    /// 一条比格元还窄的地峡，粗网格采样时整个跳过去、判「互不连通」，细网格就判「连通」。
    /// 拿一个随分辨率翻转的判据去做二分搜索，搜出来的临界点毫无意义
    /// （实际就踩过：360×180 上可行、512×256 上不可行）。
    /// 这里改成沿大圆<b>直接采样整场</b>：<see cref="FieldAt"/> 含全部种子与噪声，
    /// 结论连续、与分辨率无关，正好拿来二分。
    ///
    /// 只查每对大陆<b>最近的那一对种子</b>之间的连线：求和型的场在最近处叠加最强，
    /// 陆桥必然先在那里出现。极少数绕开的情况由验收时的栅格连通性兜底。
    /// </summary>
    public Bridge WorstBridge()
    {
        // 第一步：每对大陆各找最近的一对种子
        var closest = new Dictionary<(int, int), (int I, int J, double D)>();
        for (int x = 0; x < _prep.Length; x++)
        {
            if (_prep[x].Negative) continue;
            for (int y = x + 1; y < _prep.Length; y++)
            {
                if (_prep[y].Negative) continue;
                int a = _prep[x].ContinentId, b = _prep[y].ContinentId;
                if (a == b) continue;
                var key = a < b ? (a, b) : (b, a);

                double d = AngleBetween(_prep[x], _prep[y]);
                if (!closest.TryGetValue(key, out var cur) || d < cur.D)
                    closest[key] = (x, y, d);
            }
        }

        // 第二步：沿每对之间的连线采样，量出两块陆地之间还剩多宽的洋面
        var worst = new Bridge(0, 0, double.PositiveInfinity, 0, 0, 0);
        foreach (var (key, pair) in closest)
        {
            double degPerSample = pair.D * 180.0 / Math.PI / BridgeSamples;
            var (gapDeg, midField) = AnalyzeArc(_prep[pair.I], _prep[pair.J], pair.D, degPerSample);

            if (gapDeg < worst.GapDeg)
            {
                double seedGap = pair.D * 180.0 / Math.PI;
                double clear = seedGap - _seeds[pair.I].RadiusDeg - _seeds[pair.J].RadiusDeg;
                worst = new Bridge(key.Item1, key.Item2, gapDeg, midField, seedGap, clear);
            }
        }
        return worst;
    }

    /// <summary>
    /// 沿大圆采样，量出两块大陆<b>之间</b>的洋面宽度。
    ///
    /// ⚠️ 只能量「两块陆地之间的那一段」。整条弧的<b>两端就是种子圆心</b>，
    /// 按定义就是陆地；而陆地边缘的场值必然从 threshold 连续降下来，
    /// 所以紧邻边缘的采样点场值恒≈0.499…，拿它当判据等于恒不成立
    /// （实际就这么错过一版：判据永不满足，二分把全球陆地一路压到 0.1%）。
    ///
    /// 故先把每个采样点归属判出来，再取「最后一次属于 A」到「第一次属于 B」的跨度。
    ///
    /// 用 slerp（球面线性插值）而不是经纬度直线插值 —— 后者不在大圆上，会绕远路。
    /// </summary>
    /// <returns>洋面宽度（度）；以及洋面中点处的场值（仅供参照）。</returns>
    private (double GapDeg, double MidField) AnalyzeArc(in Prepped u, in Prepped v,
                                                        double angleRad, double degPerSample)
    {
        double sinD = Math.Sin(angleRad);
        int n = BridgeSamples;
        var field = new double[n + 1];
        var owner = new int[n + 1];

        for (int s = 0; s <= n; s++)
        {
            double t = s / (double)n;
            double wa = Math.Sin((1 - t) * angleRad) / sinD;
            double wb = Math.Sin(t * angleRad) / sinD;

            var (lat, lon) = Sphere.ToLatLon(u.X * wa + v.X * wb,
                                             u.Y * wa + v.Y * wb,
                                             u.Z * wa + v.Z * wb);
            field[s] = FieldAt(lat, lon);
            owner[s] = field[s] > Threshold ? NearestContinent(lat, lon) : -1;
        }

        int lastA = -1;
        for (int s = n; s >= 0; s--) if (owner[s] == u.ContinentId) { lastA = s; break; }

        int firstB = -1;
        for (int s = 0; s <= n; s++) if (owner[s] == v.ContinentId) { firstB = s; break; }

        // 端点必属于各自大陆（种子圆心即陆地），故两者不可能都找不到
        if (lastA < 0 || firstB < 0 || lastA >= firstB) return (0, field[Math.Max(n / 2, 0)]);

        int mid = (lastA + firstB) / 2;
        return ((firstB - lastA) * degPerSample, field[mid]);
    }

    private static double AngleBetween(in Prepped u, in Prepped v)
        => Math.Acos(Math.Clamp(u.X * v.X + u.Y * v.Y + u.Z * v.Z, -1.0, 1.0));

    /// <summary>
    /// 把每块大陆的种子半径标定到给定全球占比下的目标面积。
    ///
    /// ⚠️ 归一化必须用<b>整球面积</b>（含海洋），不能用「陆地总面积」——
    /// 后者会把「占全球 12%」误读成「占陆地 12%」，而 5 块的陆地占比之和恒为 100%，
    /// 于是标定会朝着「把每块都缩小」的方向一路迭代下去（曾经就这么错过一次：
    /// 迭代 5 轮后全球陆地只剩 1.4%）。
    ///
    /// 纯几何标定，不引入任何随机性：同样的 seed 永远得到同样的缩放。
    /// </summary>
    private void Calibrate(double landTarget)
    {
        const int latRes = 180, lonRes = 360;        // 1° 粗网格
        const int iterations = 10;
        const double relTol = 0.03;                  // 相对误差 3% 即停

        // 每次从原始布点重来，否则上一轮的缩放会累积，二分就不再是二分了
        _seeds = new List<SeedDisk>(_designSeeds);
        RecomputePrep();

        var target = TargetsFor(landTarget);

        for (int iter = 0; iter < iterations; iter++)
        {
            var (count, total) = SampleAreas(latRes, lonRes);
            if (total <= 0) return;

            bool done = true;
            for (int k = 0; k < _seeds.Count; k++)
            {
                var s = _seeds[k];
                double actual = count[s.ContinentId] / total;   // ← 占整球的比例

                if (Math.Abs(actual - target[s.ContinentId]) / target[s.ContinentId] > relTol)
                    done = false;

                // 面积 ∝ r²  ⇒  r ← r · sqrt(target / actual)
                double scale = Math.Sqrt(target[s.ContinentId] / Math.Max(actual, 1e-9));
                scale = Math.Clamp(scale, 0.82, 1.22);          // 限步，防振荡
                _seeds[k] = s with { RadiusDeg = Math.Clamp(s.RadiusDeg * scale, 1.0, 46.0) };
            }

            RecomputePrep();
            if (done) break;
        }
    }

    /// <summary>
    /// 在粗网格上统计各大陆的<b>整球面积占比</b>。
    /// <c>total</c> 是所有格元的加权和（含海洋），故 <c>count[c] / total</c>
    /// 就是该大陆占星球表面的比例，正是 §7.2 那一栏的口径。
    /// </summary>
    private (Dictionary<int, double> Count, double Total) SampleAreas(int latRes, int lonRes)
    {
        var count = new Dictionary<int, double>();
        foreach (var c in _continents) count[c] = 0;
        double total = 0;

        for (int i = 0; i < latRes; i++)
        {
            double lat = -90 + (i + 0.5) * 180.0 / latRes;
            double wCos = Math.Cos(lat * Math.PI / 180.0);   // 等距圆柱：格元面积 ∝ cos(lat)

            for (int j = 0; j < lonRes; j++)
            {
                double lon = -180 + (j + 0.5) * 360.0 / lonRes;
                total += wCos;                               // ← 先无条件累加：分母是整球

                if (FieldAt(lat, lon) <= Threshold) continue;
                int cid = NearestContinent(lat, lon);
                if (cid > 0) count[cid] += wCos;
            }
        }
        return (count, total);
    }

    // ══════════════════════════════════════════════════════════════
    //  场求值
    // ══════════════════════════════════════════════════════════════

    /// <summary>衰减核：圆心 1、半径处 0。（原文 <c>smoothstep</c> 的倒扣写法，见类注释。）</summary>
    public static double Falloff(double u)
    {
        if (u <= 0) return 1.0;
        if (u >= 1) return 0.0;
        double ss = u * u * (3.0 - 2.0 * u);
        return 1.0 - ss;
    }

    /// <summary>§7.3 的距离场 <c>field(p)</c>，含海岸噪声。</summary>
    public double FieldAt(double latDeg, double lonDeg)
    {
        var (ux, uy, uz) = Sphere.ToXyz(latDeg, lonDeg, 1.0);

        double f = 0;
        for (int i = 0; i < _prep.Length; i++)
        {
            ref readonly var s = ref _prep[i];
            double dot = ux * s.X + uy * s.Y + uz * s.Z;
            if (dot <= s.CosRadius) continue;          // 精确排除：ang > r ⇔ dot < cos r

            double cross = CrossNorm(ux, uy, uz, s.X, s.Y, s.Z);
            double ang = Math.Asin(Math.Min(1.0, cross));   // ang < r ≤ 45° < 90°，asin 良态
            f += s.Weight * Falloff(ang * s.InvRadius);
        }

        // 海岸噪声。幅度刻意压在 Threshold 以下（0.10 + 0.07 < 0.5），
        // 否则开阔洋面上会凭噪声凭空长出岛屿 —— INV-8 正是要盯住这种情形。
        double n = CoastFbmAmp * _noise.Fbm(ux * CoastFbmFreq, uy * CoastFbmFreq, uz * CoastFbmFreq, CoastFbmOctaves)
                 + CoastRidgeAmp * (_noise.Ridged(ux * CoastRidgeFreq, uy * CoastRidgeFreq, uz * CoastRidgeFreq,
                                                  CoastRidgeOctaves) - 0.5);
        return f + n;
    }

    /// <summary>
    /// 两单位向量的叉积模长 = <c>sin(夹角)</c>。
    /// <b>不用 <c>acos(dot)</c></b>：它在夹角趋近 0 与 π 时导数发散、丢精度；
    /// <c>sin</c> 形式在近 0 处方差最小，正是我们最关心的近距离判据。
    /// </summary>
    private static double CrossNorm(double ax, double ay, double az,
                                    double bx, double by, double bz)
    {
        double cx = ay * bz - az * by;
        double cy = az * bx - ax * bz;
        double cz = ax * by - ay * bx;
        return Math.Sqrt(cx * cx + cy * cy + cz * cz);
    }

    /// <summary>
    /// 软化的陆海权重：海岸处 0.5，向内 → 1，向外 → 0。
    /// 拆成静态版是为了让栅格生成器<b>一次求场、两处复用</b> —— 否则
    /// 「算 mask」和「算高程」会各自把 <see cref="FieldAt"/> 完整跑一遍。
    /// </summary>
    public static double MaskFromField(double field)
        => Math.Clamp((field - (Threshold - MaskHalfWidth)) / (2 * MaskHalfWidth), 0.0, 1.0);

    public double MaskAt(double latDeg, double lonDeg) => MaskFromField(FieldAt(latDeg, lonDeg));

    /// <summary>§7.3 的判据：<c>land = field(p) &gt; threshold</c>。</summary>
    public bool IsLandAt(double latDeg, double lonDeg) => FieldAt(latDeg, lonDeg) > Threshold;

    /// <summary>
    /// 该点归属哪块大陆：取正权贡献最大者。<b>不是</b>「最近的种子」——
    /// 而是「场值贡献最大」，这样在大陆交界处归属仍然连续。返回 −1 表示不属任何大陆。
    /// </summary>
    public int NearestContinent(double latDeg, double lonDeg)
    {
        var (ux, uy, uz) = Sphere.ToXyz(latDeg, lonDeg, 1.0);

        int best = -1;
        double bestVal = 0;
        for (int i = 0; i < _prep.Length; i++)
        {
            ref readonly var s = ref _prep[i];
            if (s.Negative) continue;

            double dot = ux * s.X + uy * s.Y + uz * s.Z;
            if (dot <= s.CosRadius) continue;

            double cross = CrossNorm(ux, uy, uz, s.X, s.Y, s.Z);
            double ang = Math.Asin(Math.Min(1.0, cross));
            double v = s.Weight * Falloff(ang * s.InvRadius);
            if (v > bestVal) { bestVal = v; best = s.ContinentId; }
        }
        return best;
    }

    /// <summary>§7.4 的高程（米，相对今日海平面）。</summary>
    public double ElevationM(double latDeg, double lonDeg)
        => ElevationFromField(FieldAt(latDeg, lonDeg), latDeg, lonDeg);

    /// <summary>
    /// 由已算出的 <paramref name="field"/>（大陆场值）求高程。
    ///
    /// ⚠️ 参数是 <b>field 而不是 mask</b>：mask 在 0.68 处饱和，无法区分近岸与腹地
    /// （见 <see cref="InlandFieldSpan"/>）。调用方本来就都先算出了 field，
    /// 让它们多乘一次 <see cref="MaskFromField"/> 反而丢掉了信息。
    /// </summary>
    public double ElevationFromField(double field, double latDeg, double lonDeg)
    {
        var (ux, uy, uz) = Sphere.ToXyz(latDeg, lonDeg, 1.0);

        double fbm = _noise.Fbm(ux * ElevNoiseFreq, uy * ElevNoiseFreq, uz * ElevNoiseFreq, ElevNoiseOctaves);

        double baseM, amp, noise;
        if (field > Threshold)
        {
            // 陆地：单边噪声（恒 ≥ 0）。刻意不让陆地上出现低于海平面的内陆盆地 ——
            // 真实大陆确有（里海、死海），但 M2 阶段那会与「land 判据」打架，
            // 先换取鲁棒性，留给 M4 气候/水文阶段再放开水系与盆地。
            double inland = Math.Clamp((field - Threshold) / InlandFieldSpan, 0.0, 1.0);
            baseM = PiecewiseLinear(InlandKnots, InlandElevKnots, inland);
            // 噪声振幅随内陆度增长：海岸平原上只有微起伏，腹地才长得出山脉。
            amp = LandNoiseMinM + (LandNoiseMaxM - LandNoiseMinM) * inland;
            noise = (0.5 + 0.5 * fbm) * amp;
        }
        else
        {
            // 海底：大陆架上噪声渐隐到 0，否则噪声会把陆架顶穿海面，
            // 在离岸几十公里处凭空造出一圈假陆地。
            double m = MaskFromField(field);
            baseM = PiecewiseLinear(OceanKnots, OceanElevKnots, m);
            amp = OceanNoiseMaxM * Math.Clamp((ShelfNoiseFadeMask - m) / ShelfNoiseFadeMask, 0, 1);
            noise = fbm * amp;
        }

        return baseM + noise;
    }

    private static double PiecewiseLinear(double[] xs, double[] ys, double x)
    {
        if (x <= xs[0]) return ys[0];
        if (x >= xs[^1]) return ys[^1];
        for (int i = 0; i < xs.Length - 1; i++)
        {
            if (x <= xs[i + 1])
            {
                double u = (x - xs[i]) / (xs[i + 1] - xs[i]);
                return ys[i] + u * (ys[i + 1] - ys[i]);
            }
        }
        return ys[^1];
    }

    // ══════════════════════════════════════════════════════════════
    //  面积实测（供验收打印）
    // ══════════════════════════════════════════════════════════════

    /// <summary>面积实测结果。</summary>
    /// <param name="Shares">各大陆占<b>整球</b>表面的比例（含海洋，合计即全球陆地占比）。</param>
    /// <param name="LandFraction">全球陆地占比。</param>
    /// <param name="TargetShares">各大陆的标定目标，口径同上，供对照。</param>
    /// <param name="InBoxShare">
    /// 各大陆的陆地中，落在 §7.2 自己那个包围盒内的比例。
    ///
    /// 这个数是给设计者看的：<b>§7.2 的包围盒装不下 §7.2 的面积占比</b>
    /// （五个盒合计仅覆盖球面 10.9%，而那一栏合计 54%）。标定只能向外扩，
    /// 于是面积达标的大陆必然有大片陆地长到盒外。该值越低，说明偏离 §7.2 布局越远。
    /// </param>
    public sealed record AreaReport(
        IReadOnlyDictionary<int, double> Shares,
        double LandFraction,
        IReadOnlyDictionary<int, double> TargetShares,
        IReadOnlyDictionary<int, double> InBoxShare);

    /// <summary>在 <paramref name="latRes"/>×<paramref name="lonRes"/> 网格上实测各大陆面积占比。</summary>
    public AreaReport MeasureAreaShares(int latRes = 180, int lonRes = 360)
    {
        var count = new Dictionary<int, double>();
        var inBox = new Dictionary<int, double>();
        foreach (var c in _continents) { count[c] = 0; inBox[c] = 0; }

        double total = 0;
        for (int i = 0; i < latRes; i++)
        {
            double lat = -90 + (i + 0.5) * 180.0 / latRes;
            double wCos = Math.Cos(lat * Math.PI / 180.0);

            for (int j = 0; j < lonRes; j++)
            {
                double lon = -180 + (j + 0.5) * 360.0 / lonRes;
                total += wCos;                               // 分母是整球

                if (FieldAt(lat, lon) <= Threshold) continue;
                int cid = NearestContinent(lat, lon);
                if (cid <= 0) continue;

                count[cid] += wCos;
                if (Geography.Continents.First(x => x.Id == cid).ContainsBBox(lat, lon))
                    inBox[cid] += wCos;
            }
        }

        var targets = TargetsFor(AchievedLandTarget);
        var shares = new Dictionary<int, double>();
        var boxes = new Dictionary<int, double>();
        double land = 0;

        foreach (var c in _continents)
        {
            shares[c] = total > 0 ? count[c] / total : 0;
            boxes[c] = count[c] > 0 ? inBox[c] / count[c] : 0;
            land += shares[c];
        }

        return new AreaReport(shares, land, targets, boxes);
    }

    /// <summary>各大陆 §7.2 包围盒占球面的比例，供设计者核对。</summary>
    public static IReadOnlyDictionary<int, double> BBoxFractions()
        => Geography.Continents.ToDictionary(c => c.Id, BBoxFraction);
}
