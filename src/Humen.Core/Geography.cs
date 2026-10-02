namespace Humen.Core;

/// <summary>
/// 世界生成：5 块大陆 → 5 条主河 → 25 条支流 → 100 个部落（design.md §7）。
///
/// <b>全流程无随机自由发挥</b>：所有几何量都由种子经 <see cref="Rng"/>/<see cref="Hashing"/>
/// 确定，同种子逐位可复现（INV-12 / INV-34）。
///
/// <para>
/// <b>★ M3（v0.11）改写：河流与部落改为「长在地形上」。</b>
/// M1 的版本用包围盒正弦摆动凭空画河、用公式估算部落海拔 —— 从不读 M2 的地形场。
/// 后果是可测的：把 M1 的 100 个部落点拿去 M2 的地形上采样，<b>21 个在水下</b>，
/// 平均海拔差 1883 m（见 <c>humen planet</c> 步骤 7 的诊断）。
/// 现在主河自「大陆最高点」沿地形梯度下降走到入海，支流自汇入点逆坡上行，
/// 部落再落在支流两岸 —— 每个坐标都经过 <see cref="PlanetField"/> 实测。
/// </para>
///
/// <para>
/// design.md §7.5 只给到步骤层级，有四处算法未指定，本文件的选择见各方法注释，
/// 已一并记入 design.md §13 待作者追认。
/// </para>
/// </summary>
public static class Geography
{
    // ──────────────────────────────────────────────────────────────────
    //  大陆（design.md §7.2 布局 + §6.1 环境禀赋）
    //
    //  ⚠️ v0.24 放宽：**包围盒是陆地面积的硬约束**，不是"参考范围"。
    //     旧表的五个盒合计只覆盖球面 **10.90%**，而标定时每块大陆的目标面积
    //     被 BoxOvergrowthLimit 卡在「不得超过自身包围盒」上 ——
    //     于是实测全球陆地恒为 10.89%，五个盒**全部顶格**（C1 3.15%/3.15%、
    //     C4 0.56%/0.56%…）。也就是说：**想让陆地变多，改目标占比没有用，
    //     必须把盒子本身放大**（这一点是量出来的，不是推出来的）。
    //
    //     每个大陆仍留在自己的气候带内 —— 这是硬的。§6.1 的环境禀赋
    //     （北寒带 AgriculturePotential.None、驯鹿、煤/石油）挂在**大陆**上，
    //     一旦寒带大陆长出寒带，一块本该无法农耕的大陆就变成可农耕的，
    //     那是在改模拟而不是改画面。故纬向只做小幅外扩，主要靠**经向**放大
    //     （经度不改变气候带，只增加同一带内的面积）。
    //
    //     权重 12:12:14:8:8 → **14:14:16:6:6**：两块极地大陆near极点，
    //     球面面积本来就小（cos(lat) 小），给它们 8 份会让它们成为
    //     拖住全局的那一环（各自顶格也凑不出目标）。瘦极地、胖温带/赤道，
    //     全球才上得去。
    // ──────────────────────────────────────────────────────────────────

    public static readonly Continent[] Continents =
    {
        new(1, "北温带大陆", "北温带", 12, 44, -100, 40, 28, -30, 0.15,
            AgriculturePotential.Good,
            new[] { "牛", "马", "羊" },
            new[] { "铜", "铁" }),

        new(2, "南温带大陆", "南温带", -44, -12, -40, 100, -28, 30, 0.15,
            AgriculturePotential.Excellent,
            new[] { "牛", "马", "羊" },
            new[] { "铜", "铁", "锡" }),

        new(3, "赤道大陆", "赤道", -24, 24, 125, -135, 0, 175, 0.16,
            AgriculturePotential.Marginal,
            new[] { "禽", "猪" },
            new[] { "稀有金属" }),

        // 极地两块：铺满全部经度 —— 靠近极点处 1° 经度只有 cos(lat) 那么宽，
        // 不给足经度，极地大陆永远只是一小块。
        //
        // ⚠️ 南/北边界 68° 是**试出来的，不是配出来的**。
        //    试过 66°（配上北温带大陆的北边界 62°），两块只差 4° 纬度，
        //    种子甩出去就直接长到一起，栅格连通性报「跨陆分量 C1+C4」，M2 验收红。
        //    寒带与温带**本来就相邻**（地球上西伯利亚就挨着北冰洋），
        //    所以问题不是"把两块挪开"，而是必须给足**一条可通航的洋面**：
        //    温带北边界 44° ↔ 寒带南边界 68°，24° 纬度（≈2700 km）当洋面。
        new(4, "北寒带大陆", "北寒带", 68, 90, -180, 180, 79, 0, 0.07,
            AgriculturePotential.None,
            new[] { "驯鹿" },
            new[] { "煤", "石油" }),

        new(5, "南寒带大陆", "南寒带", -90, -68, -180, 180, -79, 0, 0.07,
            AgriculturePotential.None,
            new[] { "驯鹿" },
            new[] { "铀矿" }),
    };

    // ──────────────────────────────────────────────────────────────────
    //  沿河参数位（design.md §7.5，均为硬编码，非随机）
    // ──────────────────────────────────────────────────────────────────

    /// <summary>支流汇入主河处的参数位置。</summary>
    public static readonly double[] TributaryJunctionT = { 0.15, 0.32, 0.50, 0.68, 0.85 };

    /// <summary>部落沿支流的参数位置。t=0 为汇入点（下游，低海拔），t=1 为源头（上游，高海拔）。</summary>
    public static readonly double[] TribeSlotT = { 0.20, 0.45, 0.70, 0.92 };

    // ──────────────────────────────────────────────────────────────────
    //  M3 约束（design.md §7.5「约束条件」一栏）
    // ──────────────────────────────────────────────────────────────────

    /// <summary>部落点海拔必须高于海平面多少米。INV-10 的阈值来源。</summary>
    /// <remarks>
    /// <b>为什么是「今日海平面 + 5 m」而不是某个年份的海平面</b>：
    /// §6.2 的海平面项是 <c>−120 m × IceVolume(year)</c>，而 <c>IceVolume ∈ [0,1]</c>，
    /// 故整个模拟区间内<b>海平面最高就是今日的 0 m</b>（间冰期）。
    /// 取今日海平面即「全程最严」口径：只要满足它，部落在任何一年都不会被淹。
    /// 反过来说，任何更宽松的口径（例如按 −30 万年的冰期海平面）只会让通过数更多，
    /// 所以这里不需要等作者裁定也不会误判。
    /// </remarks>
    public const double SeaLevelPlusM = 5.0;

    /// <summary>部落点地表坡度上限（度）。</summary>
    public const double MaxSlopeDeg = 15.0;

    /// <summary>同大陆内部落点最小间距（km）。</summary>
    public const double MinSpacingKm = 30.0;

    /// <summary>部落自河岸法线外移的距离区间（km），design.md §7.5「沿河岸 2~5 km」。</summary>
    public const double BankOffsetMinKm = 2.0;
    public const double BankOffsetMaxKm = 5.0;

    /// <summary>每块大陆的部落数（INV-1）。</summary>
    public const int TribesPerContinent = 20;

    // ──────────────────────────────────────────────────────────────────
    //  顶层入口
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 部落落位质量报告。M3 之前部落海拔是公式估的，无从校验；
    /// 现在每个坐标都经地形实测，于是「有多少个真正满足了 §7.5 的四条约束」变成一个可观测量。
    /// </summary>
    /// <param name="Total">部落总数。</param>
    /// <param name="AboveSeaPlus5">海拔 &gt; 海平面 + 5 m 的个数（INV-10）。</param>
    /// <param name="IceFree">生物群系不是「冰盖」的个数。</param>
    /// <param name="SlopeUnder15">坡度 &lt; 15° 的个数。</param>
    /// <param name="SpacingOk">与同大陆其它部落的最小间距 ≥ 30 km 的个数。</param>
    /// <param name="FullyCompliant">四条全满足的个数。</param>
    /// <param name="MinSpacingKm">全局最小间距（km），含跨部落。越大越松快。</param>
    /// <param name="WorstSlopeDeg">全局最大坡度（度）。</param>
    /// <param name="TributariesShortOfSpec">实际长度不足主河 25% 的支流数（§7.5 要求 25%~45%）。</param>
    /// <param name="MinTributaryFrac">最短支流 ÷ 其主河长度。</param>
    /// <param name="MaxTributaryFrac">最长支流 ÷ 其主河长度。</param>
    /// <param name="MaxTribeCapacity">最大的单条支流养育能力 —— 与间距约束直接冲突的那个数。</param>
    public sealed record PlacementReport(
        int Total,
        int AboveSeaPlus5,
        int IceFree,
        int SlopeUnder15,
        int SpacingOk,
        int FullyCompliant,
        double MinSpacingKm,
        double WorstSlopeDeg,
        int TributariesShortOfSpec,
        double MinTributaryFrac,
        double MaxTributaryFrac,
        int MaxTribeCapacity);

    public sealed record World(IReadOnlyList<River> Rivers,
                               IReadOnlyList<Tributary> Tributaries,
                               IReadOnlyList<Tribe> Tribes,
                               PlacementReport Placement,
                               ClimateGrid Climate);

    /// <summary>
    /// 生成整个世界的地理骨架。
    /// </summary>
    /// <param name="seed">世界种子。</param>
    /// <param name="field">
    /// M2 的行星场。河流、支流、部落的每一个坐标都向它索取海拔/归属，
    /// <b>不再有任何公式估算</b>。
    /// </param>
    public static World Build(long seed, PlanetField field)
    {
        var rivers = new List<River>();
        var tributaries = new List<Tributary>();
        var tribes = new List<Tribe>();

        // §7.6 的洋流 / 大陆度两项要有海陆分布才算得出来。烘一次（180×360 格），
        // 后面每个候选点都只是查表 —— 部落选址一轮要试 ~2700 个候选，
        // 现场做球面 BFS 是不可能的。
        var climate = ClimateGrid.Build(field);

        int tributaryId = 0;
        int globalTribeIndex = 0;

        foreach (var cont in Continents)
        {
            // ── 主河：自大陆最高点沿地形下降到海 ──
            var ctrl = MakeMainRiver(seed, cont, field);
            var river = new River(cont.Id, cont.Id, $"{cont.Name}主河", ctrl);
            rivers.Add(river);

            // 支流长度以主河全长的 25%~45% 为准（§7.5），先量主河
            double mainLenKm = PolylineLengthKm(ctrl);

            // ── 支流：自汇入点逆坡上行 ──
            // 先把 5 条支流的几何都算出来，才能按「长度 / 流域面积」分配养育能力，
            // 再决定每条支流上放几个部落（§7.5）。
            var drafts = new List<(int Index, double JunctionT, List<(double Lat, double Lon)> Ctrl,
                                  double LengthKm, double HeadingDeg)>(TributaryJunctionT.Length);

            for (int k = 0; k < TributaryJunctionT.Length; k++)
            {
                double jt = TributaryJunctionT[k];
                var junction = CatmullRom(ctrl, jt);
                var tangent = Tangent(ctrl, jt);

                var triCtrl = MakeTributary(seed, cont, k, field, junction, tangent, mainLenKm);
                drafts.Add((k + 1, jt, triCtrl,
                            PolylineLengthKm(triCtrl),
                            Bearing(junction, triCtrl[^1])));
            }

            int[] caps = AllocateCapacity(drafts.Select(d => d.LengthKm).ToArray(), TribesPerContinent);

            for (int k = 0; k < drafts.Count; k++)
            {
                var d = drafts[k];
                var tri = new Tributary(
                    Id: tributaryId,
                    ContinentId: cont.Id,
                    RiverId: river.Id,
                    Index: d.Index,
                    Name: $"{cont.Name}{d.Index}支流",
                    JunctionT: d.JunctionT,
                    LengthKm: d.LengthKm,
                    HeadingDeg: d.HeadingDeg,
                    ControlPoints: d.Ctrl,
                    TribeCapacity: caps[k]);
                tributaries.Add(tri);

                // ── 沿岸部落 ──
                double[] ts = TribeSlotPositions(caps[k]);
                for (int s = 0; s < ts.Length; s++)
                {
                    int localIndex = (globalTribeIndex % TribesPerContinent) + 1;

                    var tribe = MakeTribe(seed, cont, field, climate, globalTribeIndex, localIndex,
                                          tri, s, ts[s], tribes);
                    tribes.Add(tribe);
                    globalTribeIndex++;
                }

                tributaryId++;
            }
        }

        var report = MeasurePlacement(field, tribes, rivers, tributaries);
        // 格网一并交出去：M4 的验收（§13 赤道绿/副热带沙/极地白/冰期可驱动）要扫全星球，
        // 调用方拿得到就不用再烘一次（一次 ~1.2 s）。它本来就是「这个世界的气候」。
        return new World(rivers, tributaries, tribes, report, climate);
    }

    // ──────────────────────────────────────────────────────────────────
    //  养育能力分配（design.md §7.5）
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 把 <paramref name="total"/> 个部落席位按支流的养育能力分配到各支流。
    ///
    /// §7.5 原文：「支流长度与流域面积决定该支流的养育能力（承载部落数）」。
    /// 流域面积沿用 <see cref="BasinAreaKm2"/> 的矩形估计，于是能力 ∝ 面积 ∝ 长度²。
    ///
    /// <b>为什么不用「每条支流固定 4 个」</b>：固定 4 会让 INV-9b
    /// （每条主河的 tribe_capacity 之和 == 该大陆部落数）退化成
    /// <c>5 × 4 == 20</c> 的恒等式 —— 一个永远不可能失败的检查。
    /// 改成按能力分配后，该不变量才真正在验证「承载模型与实际落位一致」。
    ///
    /// 用最大余数法保证：每条支流至少 1 个、合计恰好 <paramref name="total"/> 个。
    /// </summary>
    public static int[] AllocateCapacity(double[] lengthsKm, int total)
    {
        int n = lengthsKm.Length;
        var caps = new int[n];
        if (n == 0) return caps;

        double[] w = lengthsKm.Select(BasinAreaKm2).ToArray();
        double sum = w.Sum();
        if (sum <= 0)                      // 退化：长度全 0 时均分
        {
            for (int i = 0; i < n; i++) caps[i] = total / n;
            for (int i = 0; i < total - caps.Sum(); i++) caps[i]++;
            return caps;
        }

        // 下限 1：支流存在就至少养得起一个部落，否则「每大陆 5 条支流」形同虚设
        var frac = new double[n];
        int used = 0;
        for (int i = 0; i < n; i++)
        {
            double raw = total * w[i] / sum;
            caps[i] = Math.Max(1, (int)Math.Floor(raw));
            frac[i] = raw - Math.Floor(raw);
            used += caps[i];
        }

        // 余数最大的先补；若下限把总数顶超了（n > total 时不可能，此处 n=5 ≤ total=20），
        // 则从余数最小的开始扣，同样不会跌破下限 1。
        while (used < total)
        {
            int best = -1;
            for (int i = 0; i < n; i++)
                if (best < 0 || frac[i] > frac[best]) best = i;
            caps[best]++;
            frac[best] = -1;               // 本轮已补过，排到最后
            used++;
        }
        while (used > total)
        {
            int worst = -1;
            for (int i = 0; i < n; i++)
                if (caps[i] > 1 && (worst < 0 || frac[i] < frac[worst])) worst = i;
            if (worst < 0) break;          // 全在下限，无法再扣
            caps[worst]--;
            frac[worst] = 2;               // 排到最后
            used--;
        }
        return caps;
    }

    /// <summary>
    /// 一阶流域估计：把支流近似为长 L、宽 0.35L 的矩形汇水区（与 <c>WorldDb</c> 同口径）。
    /// ⚠️ 仅供养育能力排序用，不是水文学结论。
    /// </summary>
    public static double BasinAreaKm2(double lengthKm) => lengthKm * lengthKm * 0.35;

    /// <summary>
    /// 某条支流上 <paramref name="capacity"/> 个部落的参数位。
    /// 恰好 4 个时返回 §7.5 明写的 <see cref="TribeSlotT"/>（默认布局逐字不变）；
    /// 其它个数则在 [0.20, 0.92] 上等距铺开。
    /// </summary>
    public static double[] TribeSlotPositions(int capacity)
    {
        if (capacity == TribeSlotT.Length) return (double[])TribeSlotT.Clone();

        var ts = new double[capacity];
        if (capacity == 1) { ts[0] = 0.56; return ts; }   // 单点取段中
        for (int i = 0; i < capacity; i++)
            ts[i] = TribeSlotT[0] + (TribeSlotT[^1] - TribeSlotT[0]) * i / (capacity - 1);
        return ts;
    }

    // ──────────────────────────────────────────────────────────────────
    //  河流几何（地形驱动）
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 主河控制点：12~20 个，自大陆最高点沿地形梯度下降到入海口。
    ///
    /// §7.5 原文：「自大陆质心附近最高海拔点起，沿地形梯度下降，用 Catmull-Rom
    /// 样条在经纬度空间插值 12~20 个控制点」。
    /// 未指定处之一：<b>「质心附近」没有给半径</b>。这里取<b>整块大陆的最高陆地点</b> ——
    /// 与 §7.5 层级图里「源头 = 内陆高地（海拔最高点）」的自述一致，
    /// 也比「质心某个半径内」少一个魔法参数。
    /// </summary>
    private static List<(double Lat, double Lon)> MakeMainRiver(long seed, Continent c, PlanetField field)
    {
        var rng = new Rng(unchecked((long)Hashing.Hash64(seed, HashDomain.Tribe, c.Id, -1, -1)));
        int n = rng.NextInt(12, 21);              // §7.5：12~20 个控制点

        var g = BuildDrainGrid(field, c);
        var src = PickSource(g)
            ?? throw new InvalidOperationException(
                $"大陆 {c.Id}（{c.Name}）没有任何格点能下泄入海 —— 无法定河源。");

        var path = TraceDownhill(field, g, src.Iy, src.Ix);

        // 路径点可能少于目标控制点数（小大陆 + 28 km 格距）。此时按实际点数取，
        // <b>不补重复点</b> —— 重复控制点会让那一小段样条退化，t 参数在那里挤成一团，
        // 下游取汇入点时会连着好几个 t 落在同一个坐标上。
        var (ctrl, idx) = SubsampleByArcLength(path, Math.Min(n, path.Count));
        ApplyDownhillCorrection(field, ctrl, path, idx);
        return ctrl;
    }

    /// <summary>
    /// 支流逆坡上行的步长（度）。约 39 km。
    /// <b>只给支流用</b>：主河走的是 <see cref="DrainGrid"/>，不再按方位步进。
    /// </summary>
    private const double StepDeg = 0.35;

    /// <summary>支流逆坡上行的步数上限（防止在主河比例异常大时跑穿大陆）。</summary>
    private const int MaxTributarySteps = 40;

    // ── 下泄网格（类 D8） ─────────────────────────────────────────────
    //
    // 为什么要整个换成网格，而不是继续用「沿方位逐步步进 + 洼地逃逸」：
    //
    // M2 的高原很大，贪心下降会走进被矮脊围住的盆地，此后**无论怎么绕都要先爬升**。
    // 实测：把逃逸放宽到「允许单步抬升 ≤ 50 m」后，5 条主河全部出现 10~81 m 的逆升，
    // INV-9 由 1 条失败变成 6 条失败；束紧又不肯，盆地锁死，河口停在内陆高原上
    // （R1/R2 的"入海口"在 1340 m / 1320 m）。
    //
    // 症结在于「先走再补救」：路径已经踩进盆地，几何上就无解了。
    // 换成**先判可达、再走**：
    //   1. 在格网上从海向陆做一次定案（按海拔升序），标出哪些格「能一路严格下泄入海」；
    //   2. 河源只在这些格里挑（最高 + 最靠内陆）；
    //   3. 沿格走时每一步都跳到**严格更低**且同样能入海的邻居。
    // 于是「源高于口」与「沿程严格单调」由构造保证，而不是靠事后校正去追。
    // 到不了海的格根本不当河源，也就不会产生断在内陆的河。

    /// <summary>下泄网格的格距（度）。约 28 km —— 比 §7.5 要的 12~20 个控制点密得多，抽稀后足够。</summary>
    private const double GridStepDeg = 0.25;

    /// <summary>包围盒外扩比例。§7.2 的盒子装不下标定面积，实测陆地会长到盒外（见 PlanetField.AreaReport）。</summary>
    private const double BBoxPad = 0.40;

    /// <summary>河源高度带：先在「离最高点不超过此值」的格里挑，再在其中取最靠内陆的。</summary>
    private const double SourceHeightBandM = 400.0;

    /// <summary>格网中的八邻域偏移（可对角，河道因此不会只有横平竖直的折角）。</summary>
    private static readonly (int Dy, int Dx)[] GridNeighbors8 =
    {
        (-1, 0), (1, 0), (0, -1), (0, 1),
        (-1, -1), (-1, 1), (1, -1), (1, 1),
    };

    /// <summary>
    /// 一块大陆的下泄格网。三种格子：
    /// <list type="bullet">
    ///   <item><see cref="Land"/> —— 属于本大陆的陆地（河道只能在上面走）；</item>
    ///   <item><see cref="Sea"/> —— <b>真的海</b>（<see cref="PlanetField.Threshold"/> 以下）；</item>
    ///   <item>两者皆否 —— <b>屏障</b>：别洲的陆地。既不当下泄出口（否则会从别人的岸上入海），
    ///         也不当河道。跨洲的包围盒理论上会重叠，先挡住。</item>
    /// </list>
    /// </summary>
    private sealed class DrainGrid
    {
        public int Ny, Nx;
        public double Lat0, Lon0;

        public bool[,] Land = null!;
        public bool[,] Sea = null!;
        public double[,] Elev = null!;

        /// <summary>本格能否一路严格下泄到海。</summary>
        public bool[,] Drains = null!;

        /// <summary>下泄去向；−2 = 本格已临海（就地入海），−1 = 无去向。</summary>
        public int[,] NextY = null!, NextX = null!;

        /// <summary>到最近海格的格数（八邻域 BFS）。只用于挑河源。</summary>
        public int[,] DistToSea = null!;

        public double LatOf(int iy) => Lat0 + iy * GridStepDeg;
        public double LonOf(int ix) => Sphere.WrapLon(Lon0 + ix * GridStepDeg);
    }

    /// <summary>
    /// 建格网并<b>一次性定案哪些格能入海</b>。
    ///
    /// 定案规则（按海拔升序处理，轮到某格时所有更低的邻居都已定案）：
    /// 该格能入海 ⟺ 它有一个海邻居，或有一个<b>更低且自己也能入海</b>的陆邻居。
    /// 严格更低是关键 —— 等高的相邻格互不相通，故不存在平段，单调性天然成立。
    /// </summary>
    private static DrainGrid BuildDrainGrid(PlanetField field, Continent c)
    {
        double span = c.LonMax - c.LonMin;
        if (span < 0) span += 360;                       // 跨 180° 的大陆 3
        double latSpan = c.LatMax - c.LatMin;

        double lat0 = Math.Max(-89.0, c.LatMin - latSpan * BBoxPad);
        double lat1 = Math.Min(89.0, c.LatMax + latSpan * BBoxPad);
        double lon0 = c.LonMin - span * BBoxPad;
        double lonSpan = span * (1 + 2 * BBoxPad);

        int ny = Math.Max(3, (int)Math.Floor((lat1 - lat0) / GridStepDeg) + 1);
        int nx = Math.Max(3, (int)Math.Floor(lonSpan / GridStepDeg) + 1);

        var g = new DrainGrid
        {
            Ny = ny, Nx = nx, Lat0 = lat0, Lon0 = lon0,
            Land = new bool[ny, nx],
            Sea = new bool[ny, nx],
            Elev = new double[ny, nx],
            Drains = new bool[ny, nx],
            NextY = new int[ny, nx],
            NextX = new int[ny, nx],
            DistToSea = new int[ny, nx],
        };

        for (int iy = 0; iy < ny; iy++)
        {
            double lat = g.LatOf(iy);
            for (int ix = 0; ix < nx; ix++)
            {
                double lon = g.LonOf(ix);

                // 一次求场、两处复用：FieldAt 是这里最贵的一步，别为了判陆/取高程各算一遍
                double f = field.FieldAt(lat, lon);
                if (f <= PlanetField.Threshold) { g.Sea[iy, ix] = true; continue; }
                if (field.NearestContinent(lat, lon) != c.Id) continue;   // 别洲陆地 = 屏障

                g.Land[iy, ix] = true;
                g.Elev[iy, ix] = field.ElevationFromField(f, lat, lon);
            }
        }

        // 按海拔升序定案「能否入海」
        var cells = new List<int>(ny * nx);
        for (int iy = 0; iy < ny; iy++)
            for (int ix = 0; ix < nx; ix++)
                if (g.Land[iy, ix]) cells.Add(iy * nx + ix);

        cells.Sort((a, b) => g.Elev[a / nx, a % nx].CompareTo(g.Elev[b / nx, b % nx]));

        foreach (int code in cells)
        {
            int iy = code / nx, ix = code % nx;
            double e = g.Elev[iy, ix];

            bool toSea = false;
            int by = -1, bx = -1;
            double be = double.PositiveInfinity;

            foreach (var (dy, dx) in GridNeighbors8)
            {
                int jy = iy + dy, jx = ix + dx;
                if (jy < 0 || jy >= ny || jx < 0 || jx >= nx) continue;

                if (g.Sea[jy, jx]) { toSea = true; continue; }
                if (!g.Land[jy, jx] || !g.Drains[jy, jx]) continue;    // 屏障，或邻居自己也入不了海
                if (g.Elev[jy, jx] < e && g.Elev[jy, jx] < be)
                {
                    be = g.Elev[jy, jx]; by = jy; bx = jx;
                }
            }

            if (toSea) { g.Drains[iy, ix] = true; g.NextY[iy, ix] = -2; g.NextX[iy, ix] = -2; }
            else if (by >= 0) { g.Drains[iy, ix] = true; g.NextY[iy, ix] = by; g.NextX[iy, ix] = bx; }
        }

        // 多源 BFS：每个格「离海多少格」，海格为 0。只给挑河源用（越远越像内陆）。
        var q = new Queue<int>();
        for (int iy = 0; iy < ny; iy++)
            for (int ix = 0; ix < nx; ix++)
            {
                if (g.Sea[iy, ix]) { g.DistToSea[iy, ix] = 0; q.Enqueue(iy * nx + ix); }
                else g.DistToSea[iy, ix] = -1;
            }

        while (q.Count > 0)
        {
            int code = q.Dequeue();
            int iy = code / nx, ix = code % nx;
            foreach (var (dy, dx) in GridNeighbors8)
            {
                int jy = iy + dy, jx = ix + dx;
                if (jy < 0 || jy >= ny || jx < 0 || jx >= nx) continue;
                if (g.DistToSea[jy, jx] != -1) continue;
                g.DistToSea[jy, jx] = g.DistToSea[iy, ix] + 1;
                q.Enqueue(jy * nx + jx);
            }
        }
        // 格网内根本没有海（整块都是陆地）→ 视为极内陆
        for (int iy = 0; iy < ny; iy++)
            for (int ix = 0; ix < nx; ix++)
                if (g.DistToSea[iy, ix] < 0) g.DistToSea[iy, ix] = ny + nx;

        return g;
    }

    /// <summary>
    /// 挑河源：在<b>能入海</b>的陆格里，先取「接近最高海拔」（±<see cref="SourceHeightBandM"/>）
    /// 的一批，再在其中挑<b>最靠内陆</b>的那个。
    ///
    /// <b>为什么要加「最靠内陆」这一条</b>：§7.5 只写了「自大陆质心附近最高海拔点起」。
    /// 早先直接取全大陆最高点，实测在两块寒带大陆上取到了<b>贴着海岸</b>的高点
    /// （C4 河源在 84.1°N，离海 34 km），于是"大陆主河"只有 34 km；
    /// 支流按 25%~45% 只剩十几公里，再经 capacity ∝ 长度² 放大，
    /// 单条支流硬塞进 14 个部落 —— 30 km 间距约束全线崩溃。
    /// 河流本该<b>横穿</b>大陆，所以河源必须离海足够远。
    /// </summary>
    private static (int Iy, int Ix)? PickSource(DrainGrid g)
    {
        double emax = double.NegativeInfinity;
        int candidates = 0;
        for (int iy = 0; iy < g.Ny; iy++)
            for (int ix = 0; ix < g.Nx; ix++)
                if (g.Land[iy, ix] && g.Drains[iy, ix])
                {
                    candidates++;
                    if (g.Elev[iy, ix] > emax) emax = g.Elev[iy, ix];
                }
        if (candidates == 0) return null;

        int by = -1, bx = -1, bd = -1;
        for (int iy = 0; iy < g.Ny; iy++)
            for (int ix = 0; ix < g.Nx; ix++)
            {
                if (!g.Land[iy, ix] || !g.Drains[iy, ix]) continue;
                if (g.Elev[iy, ix] < emax - SourceHeightBandM) continue;

                bool better = g.DistToSea[iy, ix] > bd
                              || (g.DistToSea[iy, ix] == bd && by >= 0 && g.Elev[iy, ix] > g.Elev[by, bx]);
                if (better) { bd = g.DistToSea[iy, ix]; by = iy; bx = ix; }
            }
        return by < 0 ? null : (by, bx);
    }

    /// <summary>
    /// 自河源逐格走到海。每一步都跳到<b>严格更低</b>且同样能入海的邻居 ——
    /// 所以「沿程严格单调下降」是构造出来的，不是校正出来的。
    /// 走到临海格时在它与海格之间二分，把河口钉在岸线上（海拔 ≈ 0）。
    ///
    /// <b>为什么河口必须钉在岸线</b>：§7.5 的支流汇入点 t ∈ {0.15…0.85} 是<b>沿主河参数</b>取的。
    /// 河口若落进深海（早先一步 39 km 直接踩下岸，实测到过 −4012 m），
    /// 下游那几个汇入点会一并被拖进海里，支流从水下起步，沿岸部落跟着落水。
    /// </summary>
    private static List<(double Lat, double Lon)> TraceDownhill(
        PlanetField field, DrainGrid g, int iy, int ix)
    {
        var path = new List<(double Lat, double Lon)>(64);
        int guard = g.Ny * g.Nx + 8;

        while (guard-- > 0)
        {
            (double Lat, double Lon) here = (g.LatOf(iy), g.LonOf(ix));
            path.Add(here);

            if (!g.Drains[iy, ix]) break;              // 不该发生；真发生了由 INV-9 暴露
            if (g.NextY[iy, ix] == -2)                 // 临海：就地入海
            {
                var sea = FindSeaNeighbor(g, iy, ix);
                if (sea is { } s)
                    path.Add(BisectToShore(field, here, (g.LatOf(s.Iy), g.LonOf(s.Ix)), 0.0));
                break;
            }
            if (g.NextY[iy, ix] < 0) break;

            // ⚠️ 必须先把两维都取出来再赋值。写成
            //     iy = g.NextY[iy, ix];  ix = g.NextX[iy, ix];
            // 是先改了行号、再拿新行号去查列 —— 读到的是<b>另一行</b>的 NextX。
            // 那一行往往是没被赋过值的 0，于是河道凭空跳到本初子午线那一列，
            // 实测 R2 因此"一步跨 5900 km"、控制点只剩 2 个，河口落进 −4223 m 的深海。
            int jy = g.NextY[iy, ix], jx = g.NextX[iy, ix];
            iy = jy;
            ix = jx;
        }
        return path;
    }

    /// <summary>八邻域里第一个真海格（屏障不算）。</summary>
    private static (int Iy, int Ix)? FindSeaNeighbor(DrainGrid g, int iy, int ix)
    {
        foreach (var (dy, dx) in GridNeighbors8)
        {
            int jy = iy + dy, jx = ix + dx;
            if (jy < 0 || jy >= g.Ny || jx < 0 || jx >= g.Nx) continue;
            if (g.Sea[jy, jx]) return (jy, jx);
        }
        return null;
    }


    /// <summary>在陆地点 <paramref name="land"/> 与海点 <paramref name="sea"/> 之间二分，求海拔恰在阈值上的岸点。</summary>
    private static (double Lat, double Lon) BisectToShore(
        PlanetField field, (double Lat, double Lon) land, (double Lat, double Lon) sea, double seaLevelM)
    {
        var lo = land;                       // 已知高于阈值
        var hi = sea;                        // 已知低于阈值
        for (int i = 0; i < 24; i++)
        {
            var mid = Lerp(lo, hi, 0.5);
            if (field.ElevationM(mid.Lat, mid.Lon) > seaLevelM) lo = mid;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>经纬度线性插值（经度按最短弧解缠）。</summary>
    private static (double Lat, double Lon) Lerp(
        (double Lat, double Lon) a, (double Lat, double Lon) b, double u)
    {
        double dLon = Sphere.WrapLon(b.Lon - a.Lon);
        return (a.Lat + (b.Lat - a.Lat) * u, Sphere.WrapLon(a.Lon + dLon * u));
    }

    /// <summary>
    /// 支流上行的步长档。<b>刻意不含 2.0</b>：支流长度要落在主河的 25%~45%，
    /// 而步数是由目标长度除出来的；若每步还能走 2×，实际长度最高会到目标的 2 倍 ——
    /// 实测出现过 345% 的支流，反过来把能力分配（∝ 长度²）推得更偏。
    /// </summary>
    private static readonly double[] AscentStepMultipliers = { 1.0, 0.5 };

    /// <summary>球面目标点：自 (lat,lon) 沿方位角 <paramref name="bearingRad"/> 走 <paramref name="angDeg"/> 度。</summary>
    private static (double Lat, double Lon) Destination(double latDeg, double lonDeg, double bearingRad, double angDeg)
    {
        double phi1 = latDeg * Math.PI / 180.0;
        double lam1 = lonDeg * Math.PI / 180.0;
        double d = angDeg * Math.PI / 180.0;

        double sinPhi2 = Math.Sin(phi1) * Math.Cos(d) + Math.Cos(phi1) * Math.Sin(d) * Math.Cos(bearingRad);
        double phi2 = Math.Asin(Math.Clamp(sinPhi2, -1.0, 1.0));
        double lam2 = lam1 + Math.Atan2(Math.Sin(bearingRad) * Math.Sin(d) * Math.Cos(phi1),
                                        Math.Cos(d) - Math.Sin(phi1) * Math.Sin(phi2));

        return (phi2 * 180.0 / Math.PI, Sphere.WrapLon(lam2 * 180.0 / Math.PI));
    }

    /// <summary>把稠密路径按弧长等分抽成 <paramref name="n"/> 个控制点（含首尾），并返回其在路径中的下标。</summary>
    private static (List<(double Lat, double Lon)> Pts, int[] Idx) SubsampleByArcLength(
        IReadOnlyList<(double Lat, double Lon)> path, int n)
    {
        var pts = new List<(double Lat, double Lon)>(n);
        var idx = new int[n];

        // 调用方保证 n ≤ path.Count（见 MakeMainRiver 的 Math.Min）
        var cum = new double[path.Count];
        for (int i = 1; i < path.Count; i++)
            cum[i] = cum[i - 1] + Sphere.Distance(path[i - 1].Lat, path[i - 1].Lon, path[i].Lat, path[i].Lon, 1.0);

        double total = cum[^1];
        int cursor = 0;
        for (int i = 0; i < n; i++)
        {
            double target = total * i / (n - 1);
            while (cursor < path.Count - 1 && cum[cursor + 1] < target) cursor++;
            idx[i] = cursor;
            pts.Add(path[cursor]);
        }
        idx[^1] = path.Count - 1;
        pts[^1] = path[^1];
        return (pts, idx);
    }

    /// <summary>
    /// 下坡校正（§7.5 明写的一步，但没给算法）。
    ///
    /// 本实现：沿稠密路径<b>只许前进、不许后退</b> —— 若第 i 个控制点的海拔没有低于第 i−1 个，
    /// 就把它的下标往后推，直到找到更低的点。因为路径终点必在海平面以下，
    /// 而后退不可能发生，所以这个过程一定收敛，且改完必然严格递减。
    ///
    /// <b>未指定处之二</b>：原文「沿程单调下降」没说是否允许平段。
    /// 这里按<b>严格递减</b>执行（河口也比上一控制点低），因为终点已 ≤ 海平面 + 5 m。
    /// </summary>
    private static void ApplyDownhillCorrection(
        PlanetField field, List<(double Lat, double Lon)> ctrl,
        IReadOnlyList<(double Lat, double Lon)> path, int[] idx)
    {
        for (int i = 1; i < ctrl.Count; i++)
        {
            double prevE = field.ElevationM(ctrl[i - 1].Lat, ctrl[i - 1].Lon);
            while (idx[i] < path.Count - 1
                   && field.ElevationM(ctrl[i].Lat, ctrl[i].Lon) >= prevE)
            {
                idx[i]++;
                ctrl[i] = path[idx[i]];
            }
            // 逼到路径尽头仍不低（整段都在平地上）：把几何点钉到最后一点，保证收敛
            if (field.ElevationM(ctrl[i].Lat, ctrl[i].Lon) >= prevE)
                ctrl[i] = path[^1];
        }
    }

    /// <summary>
    /// 支流控制点：自汇入点<b>逆坡上行</b>，长度取主河的 25%~45%（§7.5）。
    ///
    /// <b>未指定处之三</b>：原文只说「一级支流 5 条、长度为主河 25%~45%」，
    /// 没说走向。这里让初始方位与主河成 ±(50°~85°)（左右交替，形成羽状），
    /// 之后每一步在该方位的 ±60° 锥内挑<b>海拔最高</b>的落点。
    /// </summary>
    private static List<(double Lat, double Lon)> MakeTributary(
        long seed, Continent c, int k, PlanetField field,
        (double Lat, double Lon) junction, (double Lat, double Lon) tangent, double mainLenKm)
    {
        var rng = new Rng(unchecked((long)Hashing.Hash64(seed, HashDomain.Tribe, c.Id, 200 + k, 0)));

        // 主河在这里的走向（取上游方向 = 与下游切向相反）
        double mainBearing = Math.Atan2(tangent.Lat, Sphere.WrapLon(tangent.Lon)) + Math.PI;

        // §7.5 明写：一级支流长度为主河的 25%~45%。比例由种子定，故同种子可复现。
        double targetLenKm = mainLenKm * rng.NextRange(0.25, 0.45);
        int targetSteps = Math.Clamp(
            (int)Math.Round(targetLenKm / (StepDeg * 111.32)), 3, MaxTributarySteps);

        double side = (k % 2 == 0) ? 1 : -1;
        double turn = (50 + rng.NextRange(0, 35)) * Math.PI / 180.0 * side;
        double baseBearing = mainBearing + turn;

        var pts = new List<(double Lat, double Lon)>(targetSteps + 1) { junction };

        var cur = junction;
        double curE = field.ElevationM(cur.Lat, cur.Lon);

        // 走向自适应：偏好方位从「固定的羽状夹角」改成「上一步实际走出的方位」。
        // 固定的偏好方位会让支流反复拐向同一条基准线，实测出现原地打转 ——
        // 折线长度虚长、首尾却挨得近，过这些点的样条在拐点处过冲，INV-9 因此判死。
        double heading = baseBearing;
        for (int i = 0; i < targetSteps; i++)
        {
            var (next, nextE, taken) = AscendStep(field, c, cur, heading, curE);

            // 上行受阻（已到分水岭）：停在这里。长度会短于 §7.5 的目标，
            // 由 Build 汇总报出，不静默接受。
            if (next.Lat == cur.Lat && next.Lon == cur.Lon) break;

            pts.Add(next);
            cur = next;
            curE = nextE;
            heading = taken;
        }
        return pts;
    }

    /// <summary>
    /// 逆坡上行一步：<b>全 360° 搜最高点</b>，但在「与最高点高差不超过
    /// <paramref name="BandM"/>」的方位里，取最贴近 <paramref name="preferredBearing"/> 的那个。
    ///
    /// <b>为什么不是「固定锥」</b>：早先只在「与主河成 ±(50°~85°)」的 ±60° 锥内搜。
    /// 汇入点现在钉在岸线上（见 <see cref="BisectToShore"/>），那里地势低平，
    /// 固定锥完全可能整个朝向海面 —— 实测多条支流一步都上不去，
    /// 退化成一个点（源海拔 == 汇海拔），INV-9 直接判死；
    /// 更糟的是长度塌到 19 km（目标应是主河的 25%~45%），
    /// 经 capacity ∝ 长度² 放大后，有个 175 km 的支流被分到 14 个部落 —— 间距全崩。
    ///
    /// 带宽优先的做法两全：地形允许时保持羽状走向（好看、也贴近 §7.5 的本意），
    /// 地形不允许时仍然真的往高处走。
    /// </summary>
    private static ((double Lat, double Lon) P, double E, double B) AscendStep(
        PlanetField field, Continent c, (double Lat, double Lon) cur,
        double preferredBearing, double curE)
    {
        // 高差在此带宽内的方位视为「一样高」，按走向偏好取舍
        const double BandM = 30.0;
        const int NB = 24;
        int cap = NB * AscentStepMultipliers.Length;

        var pt = new (double Lat, double Lon)[cap];
        var el = new double[cap];
        var br = new double[cap];
        int n = 0;

        for (int a = 0; a < NB; a++)
        {
            double b = a * Math.Tau / NB;
            foreach (double mult in AscentStepMultipliers)
            {
                var p = Destination(cur.Lat, cur.Lon, b, StepDeg * mult);
                if (p.Lat < c.LatMin - 20 || p.Lat > c.LatMax + 20) continue;

                pt[n] = p; el[n] = field.ElevationM(p.Lat, p.Lon); br[n] = b; n++;
            }
        }

        if (n == 0) return (cur, curE, preferredBearing);

        // 两轮：
        //   第 0 轮只在「与上一步走向夹角 ≤ MaxTurnRad」的方位里挑 —— 支流因此不会折返。
        //   第 1 轮放开夹角限制，保证真有地形可用时绝不白白停住。
        // 两轮都按各自的局域最高点算带宽（不能用全方位的最高点，否则被夹角挡掉的那些
        // 会把带宽拉高，剩下能选的反而不在带内）。
        for (int pass = 0; pass < 2; pass++)
        {
            double passMax = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                // ★ 必须<b>严格更高</b>才算数。带宽只是用来在"差不多高"的方位里按走向偏好取舍，
                // 若不加这一条，带宽下沿（passMax − 30 m）可能落到 curE 以下，于是一步"上行"
                // 反而降低了海拔 —— 支流的单调性就没了，INV-9 会判死。
                if (el[i] <= curE) continue;
                if (Rejected(br[i], preferredBearing, pass)) continue;
                if (el[i] > passMax) passMax = el[i];
            }
            if (double.IsNegativeInfinity(passMax)) continue;

            int best = -1;
            double bestDev = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                if (el[i] <= curE || el[i] < passMax - BandM) continue;
                if (Rejected(br[i], preferredBearing, pass)) continue;

                double dev = Math.Abs(AngleDiff(br[i], preferredBearing));
                if (dev < bestDev) { bestDev = dev; best = i; }
            }
            if (best >= 0) return (pt[best], el[best], br[best]);
        }
        return (cur, curE, preferredBearing);
    }

    /// <summary>支流单步允许的最大转向。超过就是"折返"—— 会让样条在拐点处剧烈过冲。</summary>
    private const double MaxTurnRad = 100.0 * Math.PI / 180.0;

    private static bool Rejected(double bearing, double preferred, int pass)
        => pass == 0 && Math.Abs(AngleDiff(bearing, preferred)) > MaxTurnRad;

    /// <summary>两个方位角之差，规整到 (−π, π]。</summary>
    private static double AngleDiff(double a, double b)
    {
        double d = (a - b) % Math.Tau;
        if (d > Math.PI) d -= Math.Tau;
        if (d < -Math.PI) d += Math.Tau;
        return d;
    }

    // ──────────────────────────────────────────────────────────────────
    //  几何
    // ──────────────────────────────────────────────────────────────────

    /// <summary>Catmull-Rom 样条求值，<paramref name="t"/> ∈ [0,1] 覆盖整条折线。</summary>
    public static (double Lat, double Lon) CatmullRom(IReadOnlyList<(double Lat, double Lon)> pts, double t)
    {
        if (pts.Count == 0) return (0, 0);
        if (pts.Count == 1) return pts[0];

        t = Math.Clamp(t, 0.0, 1.0);
        int segs = pts.Count - 1;
        double scaled = t * segs;
        int i = Math.Min((int)scaled, segs - 1);
        double u = scaled - i;

        var p0 = pts[Math.Max(i - 1, 0)];
        var p1 = pts[i];
        var p2 = pts[Math.Min(i + 1, pts.Count - 1)];
        var p3 = pts[Math.Min(i + 2, pts.Count - 1)];

        // 经度需处理跨 180° 的环绕，故先解缠再插值
        double lon1 = p1.Lon, lon2 = Unwrap(lon1, p2.Lon);
        double lon0 = Unwrap(lon1, p0.Lon), lon3 = Unwrap(lon2, p3.Lon);

        double lat = Cr(p0.Lat, p1.Lat, p2.Lat, p3.Lat, u);
        double lon = Cr(lon0, lon1, lon2, lon3, u);
        return (lat, Sphere.WrapLon(lon));
    }

    private static double Cr(double a, double b, double c, double d, double u)
    {
        double u2 = u * u, u3 = u2 * u;
        return 0.5 * ((2 * b) + (-a + c) * u + (2 * a - 5 * b + 4 * c - d) * u2 + (-a + 3 * b - 3 * c + d) * u3);
    }

    /// <summary>把 <paramref name="lon"/> 解缠到离 <paramref name="reference"/> 最近的等价经度。</summary>
    private static double Unwrap(double reference, double lon)
    {
        double d = Sphere.WrapLon(lon - reference);
        return reference + d;
    }

    /// <summary>折线在参数 t 处的切向（未归一化）。</summary>
    private static (double Lat, double Lon) Tangent(IReadOnlyList<(double Lat, double Lon)> pts, double t)
    {
        const double e = 0.01;
        var a = CatmullRom(pts, Math.Max(0, t - e));
        var b = CatmullRom(pts, Math.Min(1, t + e));
        double dLat = b.Lat - a.Lat;
        double dLon = Sphere.WrapLon(b.Lon - a.Lon);
        if (Math.Abs(dLat) < 1e-9 && Math.Abs(dLon) < 1e-9) return (1, 0);
        return (dLat, dLon);
    }

    /// <summary>两点的近似方位角（度）。</summary>
    private static double Bearing((double Lat, double Lon) a, (double Lat, double Lon) b)
    {
        double dLat = b.Lat - a.Lat;
        double dLon = Sphere.WrapLon(b.Lon - a.Lon);
        return Math.Atan2(dLat, dLon) * 180.0 / Math.PI;
    }

    /// <summary>折线长度（km）。1° ≈ 111.32 km，经度按纬度收缩。</summary>
    public static double PolylineLengthKm(IReadOnlyList<(double Lat, double Lon)> pts)
    {
        double sum = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            double midLat = (pts[i].Lat + pts[i - 1].Lat) * 0.5 * Math.PI / 180.0;
            double dLat = (pts[i].Lat - pts[i - 1].Lat) * 111.32;
            double dLon = Sphere.WrapLon(pts[i].Lon - pts[i - 1].Lon) * 111.32 * Math.Cos(midLat);
            sum += Math.Sqrt(dLat * dLat + dLon * dLon);
        }
        return sum;
    }

    /// <summary>两点大圆距离（km）。</summary>
    public static double DistanceKm((double Lat, double Lon) a, (double Lat, double Lon) b)
    {
        double midLat = (a.Lat + b.Lat) * 0.5 * Math.PI / 180.0;
        double dLat = (b.Lat - a.Lat) * 111.32;
        double dLon = Sphere.WrapLon(b.Lon - a.Lon) * 111.32 * Math.Cos(midLat);
        return Math.Sqrt(dLat * dLat + dLon * dLon);
    }

    /// <summary>
    /// 地表坡度（度）：由 <see cref="PlanetField.ElevationM"/> 的中心差分梯度求得。
    /// §7.5 的约束「坡度 &lt; 15°」此前从未真正算过 —— M1 连地形都没读。
    /// </summary>
    public static double SlopeDeg(PlanetField field, double lat, double lon)
    {
        const double h = 0.05;                                  // ≈ 5.6 km
        double eN = field.ElevationM(Math.Min(89.0, lat + h), lon);
        double eS = field.ElevationM(Math.Max(-89.0, lat - h), lon);
        double eE = field.ElevationM(lat, Sphere.WrapLon(lon + h));
        double eW = field.ElevationM(lat, Sphere.WrapLon(lon - h));

        double mPerDeg = 111_320.0;
        double cosLat = Math.Max(0.05, Math.Cos(lat * Math.PI / 180.0));

        double dLat = (eN - eS) / (2 * h * mPerDeg);
        double dLon = (eE - eW) / (2 * h * mPerDeg * cosLat);

        return Math.Atan(Math.Sqrt(dLat * dLat + dLon * dLon)) * 180.0 / Math.PI;
    }

    // ──────────────────────────────────────────────────────────────────
    //  部落属性
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 落一个部落：先取支流上的参数位，再沿河岸法线外移 2~5 km，
    /// 然后在候选点里挑违反 §7.5 约束最轻的一个。
    ///
    /// <b>为什么要「挑」而不是「直接放」</b>：四条约束（海拔/坡度/非冰盖/间距）
    /// 在真实地形上未必同时可满足 —— 尤其两块寒带大陆（§7.2 大陆 4、5）纬度 68~84°，
    /// <c>BaseTemp</c> 在 68° 只有 −8°C、再往北迅速跌破 −10°C 的冰盖线，可用的无冰陆地很窄。
    /// 与其静默放一个违反约束的点、或默默少放几个（破坏 INV-1 的 100），
    /// 不如枚举候选、取罚分最小者，<b>并把不达标数如实报出来</b>
    /// （见 <see cref="PlacementReport"/> 与 INV-10）。
    /// </summary>
    private static Tribe MakeTribe(
        long seed, Continent c, PlanetField field, ClimateGrid climate,
        int globalIndex, int localIndex,
        Tributary tri, int slot, double t,
        IReadOnlyList<Tribe> placed)
    {
        ulong h = Hashing.Hash64(seed, HashDomain.Elevation, c.Id, localIndex, slot);
        var rng = new Rng(unchecked((long)h));

        var (lat, lon) = CatmullRom(tri.ControlPoints, t);
        var tan = Tangent(tri.ControlPoints, t);

        // 河岸法线（切向转 90°），左右随机
        double side = rng.NextRange(0, 1) < 0.5 ? 1 : -1;
        double nx = -tan.Lon * side, ny = tan.Lat * side;
        double nlen = Math.Sqrt(nx * nx + ny * ny);
        if (nlen < 1e-12) { nx = 1; ny = 0; nlen = 1; }
        nx /= nlen; ny /= nlen;

        double offsetKm = rng.NextRange(BankOffsetMinKm, BankOffsetMaxKm);
        double offsetDeg = offsetKm / 111.32;

        (double Lat, double Lon) best = (lat, lon);
        double bestPenalty = double.PositiveInfinity;
        double bestElev = 0, bestSlope = 0;

        // 候选：沿岸微扰 × 两岸 × 三档外移距离
        foreach (double dt in new[] { 0.0, -0.03, 0.03 })
        {
            double tt = Math.Clamp(t + dt, 0.02, 0.99);
            foreach (double sgn in new[] { side, -side })
            {
                foreach (double offKm in new[] { offsetKm, BankOffsetMinKm, BankOffsetMaxKm })
                {
                    double od = offKm / 111.32;

                    // 沿岸微扰重取基准点，再叠加法线外移
                    var (bl, bo) = CatmullRom(tri.ControlPoints, tt);
                    double la = Math.Clamp(bl + ny * od * sgn, -89, 89);
                    double lo = Sphere.WrapLon(bo + nx * od * sgn);

                    double e = field.ElevationM(la, lo);
                    double sl = SlopeDeg(field, la, lo);

                    // 冰盖判据必须与最终落库的群系<b>同源</b>（都走 Climate.SampleAnnual）。
                    // 曾经这里手写 `BaseTemp − 6.5×alt` 只按纬度取雨，而落库时另算一遍 ——
                    // 于是出现「罚分认为不是冰盖、落库却是冰盖」的候选被选中。
                    bool isIce = Climate.SampleAnnual(climate, la, lo, e).Biome == Climate.IceCap;

                    // 罚分是<b>分级</b>的，量级刻意拉开，等价于字典序：
                    //   海拔(1e8/m) ≫ 冰盖(1e7) ≫ 间距(1e5/km) ≫ 坡度(1e3/°)。
                    // 早先间距只给 100/km，被海拔项压得毫无作用 ——
                    // 当某个汇入点四周全是水时，每个候选的地形罚分都一样大，
                    // 于是多个部落被"最省事"地推到同一个点上（实测最小间距 0.2 km）。
                    // 拉平量级后，同样糟糕的地形条件下会优先挑离已有部落远的那个。
                    double p = 0;
                    if (e <= SeaLevelPlusM) p += 1e8 * (SeaLevelPlusM - e) + 1e8;   // 绝不下水
                    if (isIce) p += 1e7;
                    if (sl > MaxSlopeDeg) p += 1e3 * (sl - MaxSlopeDeg);
                    p += MinSpacingPenalty(placed, c.Id, la, lo);

                    if (p < bestPenalty)
                    {
                        bestPenalty = p; best = (la, lo); bestElev = e; bestSlope = sl;
                    }
                }
            }
        }

        double latF = best.Lat, lonF = best.Lon;
        double elev = bestElev;
        double absLat = Math.Abs(latF);

        // §7.6 的完整和式（M4）。这里是全项目唯一给部落定气候的地方。
        var cs = Climate.SampleAnnual(climate, latF, lonF, elev);
        double tempF = cs.TempC;
        double rainF = cs.RainMm;
        string biome = Climate.BiomeName(cs.Biome);

        // ⚠️ M4 去掉了一处「每部落随机 ±40% 降水」的老写法。
        // 它的本意是制造局地差异，但它让<b>库里的降水与渲染图上的降水不是同一个数</b> ——
        // 偏差足以把一个点从雨林翻成季雨林，正是本文件开头警告的那种「两处各写一份」。
        // 而且 M4 之后降水本来就真的随地形变了（大陆度、迎风坡），
        // 再叠一层随机数只会掩盖模型，不会增加真实感。
        var soil = Soil.Profile(tempF, rainF, cs.ColdestMonthC, bestSlope, cs.Biome);

        return new Tribe(
            GlobalIndex: globalIndex,
            ContinentId: c.Id,
            LocalIndex: localIndex,
            Code: Identity.TribeCode(c.Id, localIndex),
            TributaryId: tri.Id,
            SlotOnTributary: slot,
            Name: $"{c.Name}{localIndex}部落",
            Lat: latF,
            Lon: lonF,
            ElevationM: Math.Round(elev, 1),
            MeanTempC: Math.Round(tempF, 2),
            AnnualRainMm: Math.Round(rainF, 1),
            Biome: biome,
            SeasonAmpC: Math.Round(cs.SeasonAmpC, 2),
            ColdestMonthC: Math.Round(cs.ColdestMonthC, 2),
            DistToSeaKm: Math.Round(cs.DistToSeaKm, 1),
            TopsoilM: Math.Round(soil.TopsoilM, 3),
            SoilType: soil.TypeName,
            AgricultureFactor: Math.Round(soil.AgricultureFactor, 4));
    }

    /// <summary>与同大陆已放置部落的间距罚分：不足 <see cref="MinSpacingKm"/> 时按缺口加权。</summary>
    private static double MinSpacingPenalty(IReadOnlyList<Tribe> placed, int continentId, double lat, double lon)
    {
        double worst = 0;
        foreach (var t in placed)
        {
            if (t.ContinentId != continentId) continue;
            double d = DistanceKm((lat, lon), (t.Lat, t.Lon));
            if (d < MinSpacingKm) worst = Math.Max(worst, MinSpacingKm - d);
        }
        return worst * 1e5;
    }

    /// <summary>统计落位质量，供 INV-10 与验收打印。</summary>
    private static PlacementReport MeasurePlacement(
        PlanetField field, IReadOnlyList<Tribe> tribes,
        IReadOnlyList<River> rivers, IReadOnlyList<Tributary> tributaries)
    {
        int above = 0, iceFree = 0, slopeOk = 0, spacingOk = 0, full = 0;
        double minSpacing = double.PositiveInfinity;
        double worstSlope = 0;

        for (int i = 0; i < tribes.Count; i++)
        {
            var t = tribes[i];
            bool a = t.ElevationM > SeaLevelPlusM;
            bool b = t.Biome != Climate.BiomeName(Climate.IceCap);
            double sl = SlopeDeg(field, t.Lat, t.Lon);
            bool c = sl <= MaxSlopeDeg;

            double nearest = double.PositiveInfinity;
            for (int j = 0; j < tribes.Count; j++)
            {
                if (j == i || tribes[j].ContinentId != t.ContinentId) continue;
                nearest = Math.Min(nearest, DistanceKm((t.Lat, t.Lon), (tribes[j].Lat, tribes[j].Lon)));
            }
            bool d = nearest >= MinSpacingKm;

            if (a) above++;
            if (b) iceFree++;
            if (c) slopeOk++;
            if (d) spacingOk++;
            if (a && b && c && d) full++;

            if (!double.IsPositiveInfinity(nearest) && nearest < minSpacing) minSpacing = nearest;
            worstSlope = Math.Max(worstSlope, sl);
        }

        if (double.IsPositiveInfinity(minSpacing)) minSpacing = 0;

        // 支流长度 vs §7.5 的「主河 25%~45%」。
        // 这一项必须单独报：支流长度不只是好看不好看 —— 它经能力分配（∝ 长度²）
        // 决定每条支流上放几个部落，进而决定部落间距能否满足 30 km。
        int shortOfSpec = 0;
        double minFrac = double.PositiveInfinity, maxFrac = 0;
        foreach (var tri in tributaries)
        {
            var river = rivers.FirstOrDefault(r => r.Id == tri.RiverId);
            if (river is null) continue;
            double mainLen = PolylineLengthKm(river.ControlPoints);
            if (mainLen <= 0) continue;

            double frac = tri.LengthKm / mainLen;
            if (frac < 0.25) shortOfSpec++;
            if (frac < minFrac) minFrac = frac;
            if (frac > maxFrac) maxFrac = frac;
        }
        if (double.IsPositiveInfinity(minFrac)) minFrac = 0;

        int maxCap = tributaries.Count > 0 ? tributaries.Max(t => t.TribeCapacity) : 0;

        return new PlacementReport(tribes.Count, above, iceFree, slopeOk, spacingOk, full,
                                   Math.Round(minSpacing, 2), Math.Round(worstSlope, 2),
                                   shortOfSpec, Math.Round(minFrac, 3), Math.Round(maxFrac, 3),
                                   maxCap);
    }

    /// <summary>
    /// §7.6 的 <c>T_base(lat)</c>：0°→28°C，30°→20°C，60°→0°C，90°→−30°C。
    /// 四个锚点之间分段线性插值（原文只给了锚点，未给插值方式）。
    /// </summary>
    public static double BaseTemp(double absLat)
    {
        ReadOnlySpan<double> lat = stackalloc double[] { 0, 30, 60, 90 };
        ReadOnlySpan<double> t = stackalloc double[] { 28, 20, 0, -30 };

        absLat = Math.Clamp(absLat, 0, 90);
        for (int i = 0; i < lat.Length - 1; i++)
        {
            if (absLat <= lat[i + 1])
            {
                double u = (absLat - lat[i]) / (lat[i + 1] - lat[i]);
                return t[i] + u * (t[i + 1] - t[i]);
            }
        }
        return t[^1];
    }

    /// <summary>
    /// 纬向降水基准（mm/年）。
    /// 公开是为了让 M2 的星球渲染器复用同一套气候，而不是另写一份 —— 两处若各写各的，
    /// 迟早出现「渲染图上这里是雨林、数据库里这里是草原」的分裂。
    /// </summary>
    public static double LatitudeRainBand(double absLat)
    {
        if (absLat < 12) return 2200;   // 赤道辐合带：全年多雨
        if (absLat < 25) return 1100;   // 信风带
        if (absLat < 38) return 420;    // 副热带高压：干
        if (absLat < 58) return 850;    // 西风带
        if (absLat < 70) return 380;    // 副极地
        return 180;                      // 极地荒漠
    }

    /// <summary>
    /// Whittaker 式生物群系分类，词汇表取自 §7.7：
    ///   冰盖 / 苔原 / 针叶林 / 温带落叶林 / 温带草原 / 地中海灌丛 /
    ///   沙漠 / 热带稀树草原 / 热带雨林 / 高山
    ///
    /// <b>M1 追加的 5 个词</b>（§7.7 原表未列，但降维模型会产出这些格位）：
    ///   极地荒漠、温带雨林、热带季雨林、热带山地雨林、热带高地疏林。
    /// 建议 v0.11 把它们并回 §7.7，否则两张表迟早对不上。
    ///
    /// <paramref name="absLat"/> 不可省：赤道大陆（§7.2 大陆 3）的部落海拔最高近 1000 m，
    /// 按递减率会掉到 12~20℃。只按气温判会落进"温带落叶林"，
    /// 而它其实是低纬山地林 —— 两者对后续农业模型的含义完全不同。
    /// </summary>
    /// <remarks>
    /// v0.11 起<b>判定逻辑搬到 <see cref="Climate"/></b>，本方法只做码→名的翻译。
    /// 原因：M2 的渲染器要按<b>同一套</b>判据给地表上色，若两处各写一份，
    /// 迟早出现「渲染图上是雨林、库里是草原」。词汇表与判定顺序从此只有一份。
    /// </remarks>
    public static string ClassifyBiome(double tempC, double rainMm, double absLat)
        => Climate.BiomeName(Climate.BiomeCodeOf(tempC, rainMm, absLat));
}
