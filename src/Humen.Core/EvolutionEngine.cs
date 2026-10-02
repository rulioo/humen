namespace Humen.Core;

/// <summary>进化引擎的可调参数。全部集中在此，禁止散落到逻辑里。</summary>
public sealed record EvolutionConfig
{
    public long Seed { get; init; } = WorldConfig.DefaultSeed;

    /// <summary>
    /// 模拟步长（年）。默认 25。
    /// INV-18 要求 Δt=25 与 Δt=1 在 1000 年跨度上差异 &lt; 2% ——
    /// 本引擎为此把每一步都写成<b>解析可积</b>的形式（发现用 <c>1−exp(−λNΔt)</c>、
    /// 人口用 logistic 闭式解），故步长只通过子系统之间的耦合产生微小差异。
    /// </summary>
    public double StepYears { get; init; } = 25.0;

    /// <summary>部落数上限。§6.3 说 100 个会繁衍成"成百上千个"，但要有个头，否则跑不完。</summary>
    public int MaxTribes { get; init; } = 3000;

    /// <summary>
    /// §4.6 碳-气候耦合开关。<b>默认关闭</b>——§4.6 明写"默认关闭，留作实验开关（Q-A8）"，
    /// 且 INV-26 要求"关闭后 GlobalTempOffset 与 v0.3 逐位一致"。
    /// </summary>
    public bool CarbonClimateCoupling { get; init; }

    /// <summary>
    /// 技能域的基础练习速率（1/年，作用于 logistic 的指数）。
    ///
    /// <b>它定的是"开场有多快"，不决定世界能走多深。</b>这一点是十几次全程实测
    /// （每次 302,126 年，表见 <c>design.md</c> 待裁 ⑯）换来的：
    /// <c>r</c> 太低则世界根本起不来（<c>5×10⁻⁵</c> 跑完只有 13 项技术、停在旧石器，
    /// 且人口只有一千多，于是 <c>practice</c> 也低，形成"技术少→人口少→发现慢"的自锁）；
    /// 而把 <c>r</c> 抬到 <c>1×10⁻³</c> 单独用也只有 85 项 —— <b>深度靠的是 k₂ 那一项</b>。
    ///
    /// <b>⚠️ 下面这三个数都是我标的、不是作者给的</b>，它们共同决定整个世界史的节奏
    /// （几万年出农业、几万年出金属、工业落在哪一段），是本项目<b>最该被作者过目的数</b>。
    /// 三者已做成命令行开关（<c>--skill-rate</c> / <c>--owned-accel</c> / <c>--tech-accel</c>），
    /// 作者可直接试跑比较，不必改代码。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>两次历史标定都已作废，记在这里免得重犯</b>：
    /// · 最早按"复现真实时间线"反推 <c>r ≈ 8×10⁻⁵</c>（令 <c>plant</c> 在公元前 1 万年前后
    ///   摸到 20 分）—— 那是按 <c>A ≈ 1</c> 算的，而当时加速度被一个<b>全局项</b>顶到 35，
    ///   标定与实现早已脱节；那个全局项后来被 <see cref="TribeState.Exposed"/> 取代，
    ///   因为它让"连铜矿都没见过的部落"把 <c>cs</c>/<c>semi</c>/<c>aero</c> 一起推到 33，
    ///   于是 <c>optics:30</c> 这类低门槛全部免费通过，<b>量子力学与光纤出现在炼铜之前</b>。
    /// · 随后取 <c>r=5×10⁻⁵</c> 想让"有效速率回到 8×10⁻⁵" —— 结果只有 13 项。
    ///
    /// <b>当前取值与实测</b>（<c>r=2×10⁻⁴, k₁=0.6, k₂=0.08, 后继曝光=开</c>）：
    /// 30 万年后技术前沿 <b>130/175</b>，材料阶梯按序出现（陶器 → 铜 → 青铜 → 铁），
    /// 能源走到炼油（50,000 kWh），人口 650 万 / 200 部落。
    /// 剩下的 45 项卡在"没有任何一个部落能同时凑齐前置 + 禀赋 + 技能"，见 待裁 ⑳。
    /// </remarks>
    public double SkillBaseRate { get; init; } = 2.0e-4;

    /// <summary>
    /// 技能加速器：<b>本域</b>每多掌握一项技术，本域练习速率乘 <c>(1 + 它)</c>。
    ///
    /// 这是"干得越多长得越快"（§1.6 + §4.5 能源锚点的收益递增形状）。
    /// 它<b>必须存在</b>：logistic 的门槛到达时间之比恒为 1 : 2.1 : 5.7 : 15.3
    /// （见 <see cref="GrowSkills"/> 的推导），<b>与 a 无关</b> ——
    /// 单靠调 a 永远无法既让农业（门槛 20）落在公元前 1 万年、
    /// 又让最难的门槛（80）落在公元前后；唯一出路就是让 a 本身随部落成长而变大。
    ///
    /// 但它<b>单独用会掉进陷阱</b>：树还小的时候本域只掌握 1~2 项，加速器压着不动，
    /// 于是"技术少 → 加速小 → 技术少"，世界永远停在旧石器（实测 r=5e-5、0.6 时只有 13/175）。
    /// 所以它必须与 <see cref="SkillTechAccel"/> 配对使用 —— 后者从第一项技术起就开始见效。
    /// </summary>
    public double SkillOwnedAccel { get; init; } = 0.6;

    /// <summary>
    /// 技能加速器：<b>全部</b>已掌握技术数每多一项，<b>每一个</b>已见过域
    /// 的练习速率乘 <c>(1 + 它)</c>。
    ///
    /// <b>为什么要有一个跨域的项</b>：真实技术史里，"会做陶"会加快"种地"，
    /// 不是因为陶和种地同域，而是因为更会做工具的人<b>整体上</b>学什么都更快
    /// （工具、余粮、闲暇、识字的人更多）。这一项就是那个"整体"。
    ///
    /// <b>它为什么不会重犯 <c>optics:30</c> 那个错</b>：它只<b>乘</b>在速率上，
    /// 而"这个域长不长"仍由 <see cref="TribeState.Exposed"/> 这道硬门把着 ——
    /// 没见过计算机的部落，<c>cs</c> 恒为 0，加速器再大也乘不出知识来。
    /// 被加速的只有"已经见过、正在练"的域，那正是 §1.6 说的"环境决定哪个技能先长起来"。
    ///
    /// 与 <see cref="SkillOwnedAccel"/> 的分工：这一项管<b>起飞</b>（小树时也生效），
    /// 那一项管<b>冲刺</b>（本域深耕时加成更大）。
    /// </summary>
    public double SkillTechAccel { get; init; } = 0.08;

    /// <summary>
    /// 是否把"<b>已掌握技术的直接后继</b>所属的技能域"也算作"见过"。
    ///
    /// <b>要解决的问题</b>：树是分层的，而<b>每一跳都要付一次完整的技能爬升</b>。
    /// `writing` 需要 `letters ≥ 30`，而 `letters` 只有在 `writing` 的<b>前置全齐、
    /// 它本身进了候选前沿</b>那一刻才被标成"见过"，此后才从 0 开始爬 ——
    /// 按 <c>t(30) ≈ 17.8 万年</c>（r=2×10⁻⁴）算，一跳就吃掉大半条时间轴。
    /// 实测就是这个后果：`letters` 跑到世界末日停在 25/30，`writing` 永远差 5 分，
    /// 它底下<b>92 个节点</b>整片够不着。
    ///
    /// <b>这一条改的是什么</b>：学会一样东西，你就会<b>听说</b>它下面接着的那件事，
    /// 哪怕你还做不了。会数数的人知道有"写字"这回事，只是还没学会 ——
    /// 于是 `letters` 从"数数"那一刻开始爬，而不是从"能写字"那一刻开始爬。
    ///
    /// <b>它为什么不会把难度梯度抹平</b>：暴露仍然<b>只能沿着树走</b> ——
    /// 你必须先掌握某个技术，它的后继所属的域才会亮。没走过那条链的部落，
    /// 那些域恒为 0。而真正决定"能不能拿到某个节点"的 `RequiresMet` 一点没放松，
    /// 所以"技能高但链没走到"不会凭空点亮节点（这正是先前 `optics:30` 那个坑
    /// 的反面：那次是域与链<b>无关</b>地涨，这次域必须<b>贴着链</b>涨）。
    ///
    /// <b>默认关闭</b>：它是一个机制改动、不是标定值，应当与现有行为对比后再定。
    /// </summary>
    public bool ExposeSuccessors { get; init; } = true;

    /// <summary>
    /// 知识扩散的基准速率（§1.8 P1a）。实际速率 = 它 × (1 + P1a) × P1b，见
    /// <see cref="Diffusion"/>。
    ///
    /// <b>⚠️ 这个数与 <see cref="SkillBaseRate"/> 一样，是我标的、不是作者给的。</b>
    /// 它的量纲是"每个部落每多少年有一次和邻邦交流的机会"：
    /// 取 2×10⁻⁴ 时，无通信技术的部落约每 5000 年得到一次机会，
    /// 300 ka 里约 60 次；有了烽火/文字/造纸印刷之后按 P1a 成倍放大。
    /// 它决定"同代文明趋同的速度"，与"发现"的速率是两回事，故必须分开标定。
    /// </summary>
    public double DiffusionBaseRate { get; init; } = 2.0e-4;

    /// <summary>元年每部落的初始疆域（km²）。1000 人的狩猎采集部落，量级参考新石器村落。</summary>
    public double InitialTerritoryKm2 { get; init; } = 800.0;

    /// <summary>疆域扩张速率上限（km²/年），仅在人口逼近承载上限时启动。</summary>
    public double TerritoryExpansionRate { get; init; } = 0.02;

    /// <summary>
    /// 基础人口密度（人/km²，在农业因子 = 1、技术为零时）。
    /// 取 3.0 的依据：§5.4 的元年部落是 1000 人 / 800 km² ≈ 1.25 人/km²，
    /// 而部落必须在元年就<b>活得下去</b>（承载上限略高于现有入口），故密度必须 &gt; 1.25/AgF。
    /// 典型 AgF ≈ 0.5 给出 1200 人的上限，比 1000 人略高 —— 元年是"温饱且略有盈余"。
    /// <b>这个数是我拟的</b>，它决定整个人口量级。
    /// </summary>
    public double CapacityBase { get; init; } = 3.0;

    /// <summary>§4.3 的基础生育率（粗出生率，1/年）。</summary>
    public double BaseFertility { get; init; } = 0.050;

    /// <summary>§4.3 的基础死亡率（粗死亡率，1/年）。</summary>
    public double BaseMortality { get; init; } = 0.045;

    /// <summary>
    /// 冲击的平均间隔（年）。§4.7 只给了<b>强度</b>公式，<b>没有给频率</b> —— 故这个数是拟的。
    ///
    /// 取 800 的依据是"要让它成为筛选器而不是背景噪声"：§4.7 说抗逆力的作用是决定
    /// "谁出局"，若 300 年一次，一次冲击还没恢复下一次就来了，人口永远回不到承载上限，
    /// 大陆差异就只剩噪声。800 年一次、约 3 代人一次大灾，既够频繁到能筛人，
    /// 又留得下恢复的窗口。**这个数直接决定"5 块大陆差异有多大"，最该被作者过目。**
    /// </summary>
    public double ShockMeanIntervalYears { get; init; } = 800.0;

    /// <summary>
    /// 只有强度超过它的冲击才写进 <c>events</c> 表。
    /// 理由：<c>events</c> 是 §8.4 的"大事记"，不是逐次灾害的流水账；
    /// 而模拟里每一次都要结算（不然抗逆力就没意义了）。强度低于它的照常削人口、不记档。
    /// </summary>
    public double ShockLogThreshold { get; init; } = 0.30;

    /// <summary>是否允许部落灭绝。§1.0 允许——它不改变方向，只改变"谁继续走"。</summary>
    public bool AllowExtinction { get; init; } = true;
}

/// <summary>一条历史事件（落进 <c>events</c> 表）。</summary>
public sealed record SimEvent(
    double Year, int TribeId, int ContinentId,
    string Type, string? TechId, double Magnitude, string Description);

/// <summary>
/// <b>进化引擎</b>：让世界从公元前 30 万年走到公元 2126 年的那台机器。
///
/// 对应 <c>design.md</c> §1.3（发现）、§1.5（失传）、§1.7（内耗）、§4.2（六力演化）、
/// §4.3（P6→P4 负反馈）、§4.4（资源深度分层）、§4.6（熵代价）、§4.7（抗逆力）、§6.3（分裂）。
///
/// <b>设计原则（三条，都是被不变量逼出来的）</b>：
///
/// ① <b>每一步都解析可积</b>。发现概率用 §1.3 的 <c>1−exp(−λNΔt)</c>（作者明说"对 Δt=1 或 25
///    同样精确"），人口用 logistic 闭式解，技能用 logistic 闭式解，知识衰减用指数闭式解。
///    于是步长只通过子系统间的耦合产生差异 → INV-18 自然成立，而不是靠调参凑。
///
/// ② <b>随机数由哈希导出，不走顺序流</b>。<c>Random(a,b,c)</c> 一律是
///    <c>Hashing.Hash64(seed, 域, 部落, 项, 年桶)</c> 的纯函数。于是"算了哪些部落、按什么顺序算"
///    完全不影响每个部落自己那份随机数 → INV-12（LOD 无关）成立。
///
/// ③ <b>P3 主轴不参与状态推进</b>。§4.5 的文明指数（<c>EnergyPerCapita × Π 各力乘数</c>）
///    只在排序与着色时算，任何状态更新都不读它 → INV-27。见 <see cref="CivilizationIndex"/>。
/// </summary>
public sealed class EvolutionEngine
{
    public TechTree Tree { get; }
    public Geography.World World { get; }
    public EvolutionConfig Cfg { get; }

    /// <summary>全部部落，含已灭绝的（§1.0 允许灭绝，但技术前沿的并集不许缩小 → INV-23）。</summary>
    public IReadOnlyList<TribeState> Tribes => _tribes;
    private readonly List<TribeState> _tribes = new();

    /// <summary>当前模拟年份（内部纪年，见 <see cref="WorldConfig.FormatYear"/>）。</summary>
    public double Year { get; private set; } = WorldConfig.StartYear;

    public List<SimEvent> Events { get; } = new();

    /// <summary>全球累积碳排放（§4.6）。</summary>
    public double TotalCarbon { get; private set; }

    /// <summary>§4.6 的 <c>GlobalTempOffset</c>。默认关闭耦合时恒为 0（INV-26）。</summary>
    public double GlobalTempOffset { get; private set; }

    /// <summary>全局技术前沿 = 所有部落（含已灭绝）掌握过的技术的并集。INV-23 要求它单调不减。</summary>
    private readonly HashSet<string> _frontierUnion = new(StringComparer.Ordinal);
    public IReadOnlyCollection<string> TechFrontierUnion => _frontierUnion;

    /// <summary>节点 id → 在技术树中的序号。用于哈希取数（必须稳定）。</summary>
    private readonly Dictionary<string, int> _index;

    /// <summary>
    /// 与 <see cref="TechTree.Nodes"/> <b>同序</b>的效果表，构造时算一次。
    ///
    /// 存在的理由是性能而非设计：<see cref="RecomputeForces"/> 每个部落每步都要遍历它掌握的技术，
    /// 而 <c>TechEffects.Of</c> 内部要做覆盖表查找 + 默认推导 + 作者数据覆盖三步。
    /// 3000 个部落 × 12,085 步 × 几十项技术 = 十亿次量级 —— 实测不缓存时 25 万年跑不完 2 分钟。
    /// <c>TechEffect</c> 是不变记录，且只由节点决定，故缓存是精确的，不影响任何不变量。
    /// </summary>
    private readonly TechEffect[] _effects;

    private readonly Continent[] _continents;

    // ── 贸易与扩散（§1.8 P1a/P1b、§6.1 分工、design.md 第 242 行）──

    /// <summary>每个大陆的禀赋并集。贸易让部落能用上自己境内没有的原料。</summary>
    private readonly Dictionary<int, HashSet<string>> _continentGeo = new();

    /// <summary>全星球禀赋并集。只有走通了跨大陆贸易的部落才用得上。</summary>
    private readonly HashSet<string> _globalGeo = new(StringComparer.Ordinal);

    /// <summary>每个大陆的技术池 = 该大陆所有部落掌握过的技术并集。同大陆内扩散的来源。</summary>
    private readonly Dictionary<int, SortedSet<string>> _continentTechs = new();

    /// <summary>走通了跨大陆扩散的部落，其可学技术来自全球池。</summary>
    private readonly SortedSet<string> _globalTechs = new(StringComparer.Ordinal);

    public EvolutionEngine(TechTree tree, Geography.World world, EvolutionConfig? cfg = null)
    {
        Tree = tree;
        World = world;
        Cfg = cfg ?? new EvolutionConfig();

        _index = new Dictionary<string, int>(StringComparer.Ordinal);
        _effects = new TechEffect[tree.Nodes.Count];
        for (int i = 0; i < tree.Nodes.Count; i++)
        {
            _index[tree.Nodes[i].Id] = i;
            _effects[i] = TechEffects.Of(tree.Nodes[i]);
        }

        _continents = Geography.Continents;
        foreach (var c in _continents)
        {
            _continentGeo[c.Id] = new HashSet<string>(StringComparer.Ordinal);
            _continentTechs[c.Id] = new SortedSet<string>(StringComparer.Ordinal);
        }

        SeedTribes();

        // 禀赋池在播种后即固定：分裂的子部落继承母部落的 Geo（"同处一域，禀赋相同"），
        // 故此后不会再往池里加新词条。技术池则会随历史增长，故在 Step 里增量维护。
        foreach (var t in _tribes)
        {
            _continentGeo[t.ContinentId].UnionWith(t.Geo);
            _globalGeo.UnionWith(t.Geo);
        }
        foreach (var t in _tribes) CacheEffectiveGeo(t);
    }

    // ══════════════════════════════════════════════════════════════════
    //  初始化：100 个元年部落
    // ══════════════════════════════════════════════════════════════════

    private void SeedTribes()
    {
        foreach (var t in World.Tribes)
        {
            var continent = _continents.First(c => c.Id == t.ContinentId);

            var s = new TribeState
            {
                Id = _tribes.Count,
                Code = t.Code,
                ContinentId = t.ContinentId,
                FoundedYear = WorldConfig.StartYear,
                Lat = t.Lat,
                Lon = t.Lon,
                ElevationM = t.ElevationM,
                MeanTempC = t.MeanTempC,
                AnnualRainMm = t.AnnualRainMm,
                ColdestMonthC = t.ColdestMonthC,
                DistToSeaKm = t.DistToSeaKm,
                TopsoilM = t.TopsoilM,
                AgricultureFactor = t.AgricultureFactor,
                Biome = t.Biome,
                TerritoryKm2 = Cfg.InitialTerritoryKm2,
                TerritoryCapKm2 = ContinentAreaKm2(continent) / WorldConfig.TribesPerContinent,
                Geo = new HashSet<string>(GeoOf(t, continent), StringComparer.Ordinal),
                Population = WorldConfig.MembersPerTribe,
            };

            // 门槛判定查的是 EffectiveGeo，而 RefreshFrontier 就在下面几行调用它 ——
            // 故此处必须先给它一个初值（播种时还没有贸易，等于境内禀赋）。
            // 构造函数的末尾会根据各自掌握的贸易技术再精算一次。
            s.EffectiveGeo.UnionWith(s.Geo);

            // §5.4 初始技术集。
            // ⚠️ 必须是"数据而非硬编码在部落里"—— 故读的是 WorldConfig.InitialTechSet，
            //    将来把起点再往前挪时只改那一处。
            foreach (string id in WorldConfig.InitialTechSet)
                if (Tree.Has(id)) { s.Techs.Add(id); s.AcquiredYear[id] = WorldConfig.StartYear; }

            // 开局给一点技能底子：会打制石器/用火的族群，stone / fire 不该是 0。
            // 取"已掌握节点的 skill 门槛"作为起点 —— 即"能做这件事的最低水平"。
            // 同时标为"见过"：种子技术直接塞进 Techs，绕过了 Acquire，
            // 若不在此补标，这些域会被曝光门挡掉，开局连石器和火都长不起来。
            foreach (string id in s.Techs)
                if (Tree.TryGet(id, out var n))
                {
                    foreach (var (d, v) in n.Skills)
                        s.Skills[d] = Math.Max(s.GetSkill(d), v);
                    Expose(s, n);
                }

            _frontierUnion.UnionWith(s.Techs);
            RefreshFrontier(s);
            RecomputeForces(s);
            s.Capacity = CapacityOf(s);
            _tribes.Add(s);
        }
    }

    private static double ContinentAreaKm2(Continent c) => c.LandFraction * 510_000_000.0;

    /// <summary>
    /// 该部落境内的原料与矿藏。
    ///
    /// <b>这张表必须是"翻译"，不能是"发明"</b> —— 第一版我用自己的词（<c>铜</c>/<c>铁</c>/<c>林木</c>）
    /// 去填 <c>tech_tree.md</c> 的 <c>geo</c> 门槛，而树里写的是 <c>铜矿</c>/<c>铁矿石</c>。
    /// 两个词表一个都没对上，于是<b>全星球 25 个节点的地理门槛永远命不中</b>：
    /// 铜冶炼、青铜、铁、盐、陶器全部"从未出现"，而引擎不报任何错 ——
    /// 地理门槛是"不满足就静默跳过"的，所以这种错<b>只会表现为"历史上什么都没发生"</b>。
    ///
    /// 故此处逐条对齐到树的真实词表（36 个词，来自 <c>grep 'geo: \['</c> 的并集）。
    /// 每一条的来源都标了出处：<b>§6.1 的是作者的，其余是从 biome/气候/§7.8 土壤推出的，是我的。</b>
    /// </summary>
    private HashSet<string> GeoOf(Tribe t, Continent c)
    {
        var g = new HashSet<string>(StringComparer.Ordinal);

        // ── 矿产：§6.1 的大陆禀赋（作者定的五条分叉起点）──
        foreach (string m in c.Minerals)
            foreach (string term in MineralTerms(m))
                g.Add(term);

        // ── 类别词「金属」的展开 ──
        // 树的 34 个 geo 词里，33 个都是具体的矿体/物产（铜矿、盐泉、甘蔗…），
        // 只有 battery 写的是 `geo: [金属]` —— 一个<b>类别</b>名。
        // 它是唯一一个我的 GeoOf 产不出来的词，而它一个人堵住了
        // battery → direct_current → electromagnetism → telegraph → computer_arch ……
        // 即<b>整条电气/电信/计算机主干</b>（树里 cs 32 个节点 + elec 17 + comm 12）。
        //
        // 把类别展开为"其下任一成员即可"是 §4.2 ② 本来的做法
        // （MaterialTier ← max{ m : 可采储量(m) > 0 }，就是按类别在具体材料上取最大），
        // 故此处同样处理，不算替作者发明数据。⚠️ 但仍应请作者把「金属」
        // 换成具体矿体名，或把「金属」正式写进 geo 受控词表 —— 见 待裁。
        if (MetalOres.Any(g.Contains)) g.Add("金属");

        // ── 驯化物种：§6.1 ──
        foreach (string d in c.Domesticates) g.Add(d);

        bool grass = t.Biome.Contains("草原");
        bool forest = t.Biome.Contains("林");
        bool desert = t.Biome.Contains("沙漠");
        bool polar = t.Biome is "冰盖" or "苔原";
        bool tropic = t.MeanTempC > 20.0 && t.AnnualRainMm > 700.0;
        bool temperate = t.MeanTempC is > 3.0 and < 20.0 && t.AnnualRainMm > 350.0;
        bool dry = t.AnnualRainMm < 300.0;
        bool nearWater = t.DistToSeaKm < 400.0;

        // 草场与牧群（§1.6：驯化必须适配本地环境 —— 有草场才谈得上驯马驯牛）
        //
        // ⚠️ 这里必须同时产出「草原」和「牛草场」两个词 —— 树里是两个不同的门槛：
        //    horse_dom / horse_power 要的是 `geo: [草原]`，而「牛草场」另有一个节点在用。
        //    第一版只产出了「牛草场」，于是全星球<b>没有任何部落能驯马</b>，
        //    horse_dom / horse_power 两个节点永久不可达 —— 与前面 geo 词表对不上是同一类错误。
        if (grass)
        {
            g.Add("草原");
            g.Add("牛草场");
            if (c.Domesticates.Contains("马")) g.Add("野马");
        }

        // ── 土壤：§7.8（A 层厚度 + 最冷月 + 降水 → 农业产能因子）──
        if (t.TopsoilM > 0.25 && t.AgricultureFactor > 0.30) g.Add("可耕地");

        // ── 水与盐：§7.6 距海距离 ──
        if (t.DistToSeaKm < 80.0) g.Add("海盐");
        if (dry && t.ElevationM > 400.0) g.Add("盐泉");
        if (t.AnnualRainMm < 150.0) g.Add("岩盐");

        // ── 黏土：河湖冲积与湿热风化 ──
        if (nearWater || forest || t.Biome.Contains("稀树草原")) g.Add("黏土");

        // ── 植物原料：按群系与气候分 ──
        if (forest) { g.Add("树皮"); g.Add("草木灰/碱"); }
        if (!polar && !desert) g.Add("麻");
        if (tropic) { g.Add("竹"); g.Add("甘蔗"); }
        if (temperate) g.Add("甜菜");

        // ── 非金属矿：按降水与地质 ──
        if (dry) { g.Add("硝土"); if (t.ElevationM > 600.0) g.Add("硝石矿"); }
        if (t.ElevationM > 800.0) g.Add("石英砂");
        if (t.ElevationM > 800.0 && nearWater) g.Add("美石河滩");

        // ── 深部矿体：§4.4 ──
        // 物理上"在那里"，但取不到 —— 可采性由 CapabilityGatedGeo 单独把关（未掌握
        // deep_mining_robot 时该词条视为不满足），这正是 §4.4 "4000 m 以深的储量恒为 0"。
        if (c.Minerals.Contains("稀有金属") || c.Minerals.Contains("铀矿") || c.Minerals.Contains("石油"))
            g.Add("深部矿体");

        return g;
    }

    /// <summary>
    /// §6.1 的矿物名 → <c>tech_tree.md</c> 的 <c>geo</c> 矿体名。
    ///
    /// <b>为什么必须翻译</b>：§6.1 列的是"这块大陆有什么"，用的是粗略的物名
    /// （<c>铜</c>、<c>铁</c>、<c>稀有金属</c>）；而 <c>tech_tree.md</c> 的门槛写的是
    /// <b>矿体</b>名（<c>铜矿</c>、<c>铁矿石</c>、<c>金银矿</c>）。
    /// 前者是后者的上位概念，但字符串不相等，而门槛判定是<b>精确匹配</b>。
    ///
    /// 「稀有金属」是唯一一个需要展开的：§6.1 只给了这一个粗类目（大陆 3），
    /// 而树里对应的具体矿体有金/银/铅银/锌/宝石五六个。此处全部展开 ——
    /// 否则大陆 3 的贵金属线（以及依赖它的电子、光学工业）
    /// <b>整条不可达</b>，而"赤道大陆只能出稀有金属"正是作者给它的立身之本。
    /// </summary>
    private static IEnumerable<string> MineralTerms(string mineral) => mineral switch
    {
        "铜"       => new[] { "铜矿" },
        "铁"       => new[] { "铁矿石" },
        "锡"       => new[] { "锡矿" },
        "煤"       => new[] { "煤矿" },
        "石油"     => new[] { "油田" },
        "铀矿"     => new[] { "铀矿" },
        "稀有金属" => new[] { "金矿", "银矿", "金银矿", "铅银矿", "锌矿", "宝石矿", "沙金", "硫磺" },
        _          => Array.Empty<string>(),
    };

    /// <summary>
    /// 树里出现的全部<b>金属矿体</b>名 —— 用于把类别词「金属」展开成可判定的条件。
    /// </summary>
    private static readonly string[] MetalOres =
    {
        "铜矿", "铁矿石", "锡矿", "锌矿", "金矿", "银矿", "金银矿", "铅银矿", "沙金", "铀矿",
    };

    // ══════════════════════════════════════════════════════════════════
    //  主循环
    // ══════════════════════════════════════════════════════════════════

    /// <summary>一直跑到 <paramref name="endYear"/>。</summary>
    public void RunTo(double endYear)
    {
        while (Year < endYear - 1e-9)
            Step(Math.Min(Cfg.StepYears, endYear - Year));
    }

    /// <summary>推进一个步长。</summary>
    public void Step(double dt)
    {
        if (dt <= 0) return;

        // §4.6：碳 → 全球升温。默认关闭（Q-A8），关闭时 GlobalTempOffset 恒为 0（INV-26）。
        if (Cfg.CarbonClimateCoupling) GlobalTempOffset = CarbonWarming(TotalCarbon);

        int yearBucket = (int)Math.Floor(Year);

        for (int i = 0; i < _tribes.Count; i++)
        {
            var t = _tribes[i];
            if (!t.Alive) continue;

            GrowSkills(t, dt);
            Discover(t, dt, yearBucket);
            Diffuse(t, dt, yearBucket);      // §1.8 P1a：从邻邦习得（在 RecomputeForces 之前，
                                             // 这样这一步学到的东西同步进入本步的力计算）
            RecomputeForces(t);
            GrowPopulation(t, dt);
            UpdateRetention(t, dt);
            UpdateCarbon(t, dt);
            ApplyShock(t, dt, yearBucket);

            if (t.Population < 1.0 && Cfg.AllowExtinction) Kill(t);
        }

        Fission(yearBucket);

        Year += dt;
    }

    // ══════════════════════════════════════════════════════════════════
    //  §1.3 发现引擎
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// §1.3：<c>P(第 t 年发现 T) = 1 − exp(−λ_T × N_practice × I_adjacency × E_env)</c>。
    ///
    /// 作者对这个式子的评价是"技术偶然的发现，却是必然被发现的结果"，并点出它的工程红利：
    /// <i>"指数形式可按任意步长<b>解析推进</b>。1−exp(−λNΔt) 对 Δt = 1 年或 25 年同样精确，
    /// 大步长不损失精度。这使得高速播放（100x 以上）在数学上是合法的，而非近似。"</i>
    /// 故此处直接照抄该形式，不做任何离散化近似。
    /// </summary>
    private void Discover(TribeState t, double dt, int yearBucket)
    {
        if (t.Frontier.Count == 0) return;

        for (int i = t.Frontier.Count - 1; i >= 0; i--)
        {
            string id = t.Frontier[i];
            if (t.Techs.Contains(id)) { t.Frontier.RemoveAt(i); continue; }

            var node = Tree[id];

            // 防御性再查一次前置。Frontier 的定义确实是"前置已齐的候选"，
            // 但那要靠"失传时绝不破坏前沿"这条纪律被处处守住才行 —— 漏一处，
            // 这里就会点亮一个前置不全的节点，VerifyInvariants 当场判死并拒绝落库。
            // 代价是 O(前置数)，通常 1~3 项，可以忽略。
            if (!RequiresMet(t, node)) continue;
            if (!SkillMet(t, node)) continue;
            if (!GeoMet(t, node)) continue;

            // ── 三个因子，逐项对应 §1.3 的公式 ──
            double lambda = TechEffects.LambdaOf(node);

            // N_practice：相关实践量（人·年）。人口越多、用越频繁，越大。以千人部落为 1 个单位。
            double practice = t.Population / 1000.0;

            // I_adjacency：前置邻接度。此处取"知识保持率"——
            //   候选技术的前置必然已全部满足（Frontier 的定义），故真正的变量是
            //   "这些前置知识<b>现在还记得多牢</b>"。§1.5 说过 P1 的隐藏作用正是"防倒退"。
            double adjacency = t.KnowledgeRetention;

            // E_env：环境赋能度。§1.3 原意是"本地有没有铜矿露头"，
            //   那部分已经由 GeoMet 做成硬门槛（有才有候选）；此处再按禀赋丰度给一个软加成。
            double env = 0.6 + 0.4 * Math.Min(1.0, t.Geo.Count / 8.0);

            double rate = lambda * practice * adjacency * env;
            double p = 1.0 - Math.Exp(-rate * dt);

            // 随机数由 (seed, 域, 部落, 节点序号, 年桶) 纯函数导出。
            // 年桶 = floor(年)，故 Δt=25 与 Δt=1 取到的是不同的数 —— 这是刻意的：
            // INV-18 只要求两种步长<b>统计上</b>接近（<2%），不要求逐位相同；
            // 而 INV-12 要求的逐位相同，针对的是"同一 seed 下换 LOD"，那时年桶不变，故必然成立。
            ulong h = Hashing.Hash64(Cfg.Seed, HashDomain.Discovery, t.Id, _index[id], yearBucket);
            if (Hashing.ToUnit(h) < p) Acquire(t, node);
        }
    }

    private static bool SkillMet(TribeState t, TechNode n)
    {
        foreach (var (domain, need) in n.Skills)
            if (t.GetSkill(domain) < need) return false;
        return true;
    }

    /// <summary>
    /// 地理门槛：节点要求的每个 <c>geo</c> 词条本部落都得有。
    ///
    /// 另有 §4.4 的一类"<b>物理上有、但现在取不到</b>"的词条 ——
    /// 深部矿体确实躺在地下，只是没有 <c>deep_mining_robot</c> 就挖不到。
    /// §4.4 原文：<i>"未掌握 deep_mining_robot → 4000 m 以深的储量恒为 0"</i>。
    /// 故这类词条查的是<b>能力</b>而不是禀赋。
    /// </summary>
    private static bool GeoMet(TribeState t, TechNode n)
    {
        foreach (string g in n.Geo)
        {
            // ⚠️ 查的是 EffectiveGeo（= 境内禀赋 ∪ 贸易换来的原料），不是原始 Geo。
            //    §6.1 的五块大陆各分一种矿产，就是为了让"铜 + 锌"这类组合必须靠贸易
            //    才能凑齐。用原始 Geo 会让整条电气主干永久断掉（实测卡在 69 项）。
            if (!t.EffectiveGeo.Contains(g)) return false;
            if (CapabilityGatedGeo.TryGetValue(g, out string? need) && !t.Techs.Contains(need))
                return false;
        }
        return true;
    }

    // ══════════════════════════════════════════════════════════════════
    //  贸易与扩散：让知识跨部落、跨大陆走
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// §1.7 的贸易阶梯（`trade_barter` → `market` → `money`，见树的 pre 链）。
    ///
    /// <b>⚠️ 一条已知的建模后果</b>：`market` 一旦掌握，该部落就能用上<b>全球</b>禀赋，
    /// 于是 §6.1 的"五大陆各分一种矿产"在那之后就基本不再约束技术路径 ——
    /// 即地理特化只在前市场时代成立。
    ///
    /// 这在建模上是"全球化"的合理结果（作者也确说赤道大陆「可能走贸易路线」），
    /// 但它确实<b>削弱了 §6.1 想制造的跨大陆差异</b>。若作者要保住差异，
    /// 可改成"贸易只能拿到伙伴大陆的禀赋"（需要贸易伙伴关系数据，当前没有）——
    /// 见 待裁 ⑫。
    /// </summary>
    private static readonly string[] TradeLocal  = { "trade_barter" };
    private static readonly string[] TradeGlobal = { "market", "money" };

    /// <summary>
    /// design.md 第 242 行：<i>「造纸 + 印刷 → 保持率 = 1，且<b>可跨大陆扩散</b>」</i>。
    /// 这行字里的"跨大陆"是个限定词，反过来说明<b>同大陆内的扩散更早就存在</b> ——
    /// 故本引擎的做法是：同大陆扩散始终开着（速率由 P1a 决定），跨大陆则要过这道门。
    /// </summary>
    private static bool CanCrossContinents(TribeState t)
        => t.Techs.Contains("paper") && (t.Techs.Contains("woodblock_print") || t.Techs.Contains("movable_type"));

    private static bool HasAny(TribeState t, string[] ids)
    {
        foreach (string id in ids) if (t.Techs.Contains(id)) return true;
        return false;
    }

    /// <summary>
    /// 按已掌握的贸易技术，重算这个部落实际能用上的原料集合（见 <see cref="TribeState.EffectiveGeo"/>）。
    /// 在播种时、以及每次掌握贸易类技术时调用 —— 禀赋池是固定的，
    /// 故这里只是"把池子并进来"，无需增量维护。
    /// </summary>
    private void CacheEffectiveGeo(TribeState t)
    {
        t.EffectiveGeo.Clear();
        t.EffectiveGeo.UnionWith(t.Geo);                       // 境内自有
        if (HasAny(t, TradeLocal))  t.EffectiveGeo.UnionWith(_continentGeo[t.ContinentId]);
        if (HasAny(t, TradeGlobal)) t.EffectiveGeo.UnionWith(_globalGeo);
    }

    /// <summary>
    /// 知识扩散：这个部落可能从邻居那里学到一项自己还没有的技术。
    ///
    /// <b>为什么必须有</b>：§6.1 给五块大陆分了不同矿产，本意是逼出分工与贸易
    /// （赤道大陆那行作者写得很直白：「可能走贸易路线」）。但若部落之间互不传播知识，
    /// 每块大陆都得独立重走一遍全链，而任何<b>需要两块大陆矿产</b>的节点
    /// （`battery` 要铜 + 锌）就<b>永久不可达</b> —— 实测全世界正好卡死在 69 项技术，
    /// 被堵住的正是电气/电信/计算机那 60 个节点。这不是树的错，是我漏了 §1.8。
    ///
    /// <b>速率挂在哪里</b>：<c>P1a</c>（<see cref="TribeState.InfoSpeed"/>，§1.8 定义的
    /// "信息传递效率 = 单位时间信息量 × 传播半径 × 保真度 × 留存率"）×
    /// <c>P1b</c>（<see cref="TribeState.KnowledgeRetention"/>，保真度与留存）。
    /// 于是"烽火 → 文字 → 造纸印刷 → 电信"这条链自然地把扩散速度拉开档次，
    /// 不需要另立一套参数。
    ///
    /// 抽样与发现引擎同构：先按纯函数掷一次"这一步有没有交流的机会"，
    /// 命中了再从技术池里挑一项。这样每步的代价是 O(部落数)，不是 O(部落数 × 池大小)。
    ///
    /// <b>⚠️ 与 INV-12 相容性（这一条不显然，故写明）</b>：扩散引入了<b>共享状态</b> ——
    /// 一个部落学到什么，取决于邻居们知道什么，于是它自己的轨迹不再只是自己 id 的函数。
    /// 粗看这像是在破坏 "同 seed 下无论焦点 LOD 如何切换结果完全一致"（INV-12）。
    ///
    /// 但不破坏，理由是 §3.1 的两条原文：
    /// ① <i>"部落级<b>永远</b>模拟，个体级按观测焦点降级"</i> —— 宏观层对<b>所有</b>部落
    ///    一律推进，技术池因此与焦点无关；
    /// ② 焦点升级是<i>"用聚合状态 + 种子随机<b>重建</b>个体队列"</i> ——
    ///    微观层是<b>派生视图</b>而不是驱动器，它不回流进技术池。
    ///
    /// 即：共享状态存在，但它的全部输入都来自"永远运行的宏观层"，故对 LOD 不变。
    /// <b>这条相容性依赖 §3.1 的那句"永远"；若哪天允许低 LOD 下跳过某些部落的更新，
    /// 扩散就会真的破坏 INV-12</b> —— 届时须改为"技术池由规范化的全量部落集合构建"。
    /// </summary>
    private void Diffuse(TribeState t, double dt, int yearBucket)
    {
        SortedSet<string> pool = CanCrossContinents(t) ? _globalTechs : _continentTechs[t.ContinentId];
        if (pool.Count == 0) return;

        double rate = Cfg.DiffusionBaseRate * (1.0 + t.InfoSpeed) * t.KnowledgeRetention;

        ulong hGate = Hashing.Hash64(Cfg.Seed, HashDomain.Diffusion, t.Id, 0, yearBucket);
        if (Hashing.ToUnit(hGate) >= 1.0 - Math.Exp(-rate * dt)) return;

        // 命中：把池里"尚未掌握、且门槛都够"的都收出来，再按哈希挑一项。
        // 用 SortedSet 遍历（键有序）保证与插入顺序无关 → INV-12/34。
        var cand = new List<string>();
        foreach (string id in pool)
        {
            if (t.Techs.Contains(id)) continue;
            if (!Tree.TryGet(id, out var n)) continue;
            if (!RequiresMet(t, n) || !SkillMet(t, n) || !GeoMet(t, n)) continue;
            cand.Add(id);
        }
        if (cand.Count == 0) return;

        ulong hPick = Hashing.Hash64(Cfg.Seed, HashDomain.Diffusion, t.Id, 1, yearBucket);
        var pick = Tree[cand[(int)(hPick % (ulong)cand.Count)]];
        Acquire(t, pick);
        Events.Add(new SimEvent(Year, t.Id, t.ContinentId, "diffuse", pick.Id, 1.0,
            $"{t.Code} 从邻邦习得 {pick.Name}"));
    }

    /// <summary>§4.4：这些 geo 词条不是"有没有"，而是"取不取得到"。</summary>
    private static readonly Dictionary<string, string> CapabilityGatedGeo = new(StringComparer.Ordinal)
    {
        ["深部矿体"] = TechPhysics.DeepMiningRobotTech,   // > 4000 m，人类硬极限
    };

    /// <summary>点亮一个技术节点。</summary>
    private void Acquire(TribeState t, TechNode n)
    {
        if (!t.Techs.Add(n.Id)) return;
        if (!t.AcquiredYear.ContainsKey(n.Id)) t.AcquiredYear[n.Id] = Year;   // 只记首次

        t.Frontier.Remove(n.Id);
        _frontierUnion.Add(n.Id);        // INV-23：并集只增不减
        Expose(t, n);                    // 掌握即"见过"该域

        // 进了本大陆的技术池（扩散的来源）。
        // ⚠️ 池是单调的：某个部落灭绝后它贡献过的技术仍留在池里。
        //    这偏向"知识不会随部落一起消失"，与 §1.5 的失传机制不是一回事，
        //    属于我的简化 —— 见 待裁。
        _continentTechs[t.ContinentId].Add(n.Id);
        _globalTechs.Add(n.Id);

        // 贸易类技术会改变"实际能用上哪些原料"，掌握时重算一次。
        if (HasAny(t, TradeLocal) || HasAny(t, TradeGlobal)) CacheEffectiveGeo(t);

        Events.Add(new SimEvent(Year, t.Id, t.ContinentId, "discover", n.Id, 1.0,
            $"{t.Code} 掌握 {n.Name}"));

        RefreshFrontier(t, n.Id);
    }

    /// <summary>
    /// 把"前置已全部满足但尚未掌握"的节点补进候选前沿。
    ///
    /// 走 <see cref="TechTree.DependentsOf"/> 反向索引而非全树扫描：只有刚掌握的那个技术的
    /// <b>直接后继</b>才可能因此变成候选，其余 170 多个节点不可能受影响。
    /// 全树扫描在 3000 部落 × 上万步下是无谓的十倍开销。
    /// </summary>
    private void RefreshFrontier(TribeState t, string? justAcquired = null)
    {
        if (justAcquired is null)
        {
            // 首次建库：全体扫一遍，把一个候选都还没有的部落补起来
            foreach (var n in Tree.Nodes)
                if (!t.Techs.Contains(n.Id) && RequiresMet(t, n) && !t.Frontier.Contains(n.Id))
                {
                    t.Frontier.Add(n.Id);
                    Expose(t, n);
                }
            return;
        }

        foreach (string dep in Tree.DependentsOf(justAcquired))
        {
            if (t.Techs.Contains(dep)) continue;
            if (!Tree.TryGet(dep, out var n)) continue;

            // 直接后继所属的域一律先"见过"（见 EvolutionConfig.ExposeSuccessors）。
            // ⚠️ 必须在 `continue` 之前做 —— 已经在前沿里的那些也要走到，
            //    否则"刚掌握→后继已在候选里"这条路径反而不会暴露它。
            if (Cfg.ExposeSuccessors) Expose(t, n);

            if (t.Frontier.Contains(dep)) continue;
            if (RequiresMet(t, n)) { t.Frontier.Add(dep); Expose(t, n); }
        }
    }

    /// <summary>
    /// 把这个节点涉及的技能域标为"已见过"（见 <see cref="TribeState.Exposed"/>）。
    /// 掌握的和只是进了候选前沿的都要标 —— 后者正是"机会摆在面前"。
    /// </summary>
    private static void Expose(TribeState t, TechNode n)
    {
        foreach (string d in n.Skills.Keys) t.Exposed.Add(d);
    }

    private static bool RequiresMet(TribeState t, TechNode n)
    {
        foreach (string p in n.Requires)
            if (!t.Techs.Contains(p)) return false;
        return true;
    }

    // ══════════════════════════════════════════════════════════════════
    //  技能增长
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 技能域按"练习"增长，logistic 趋于 100，闭式解故对步长不敏感：
    ///   <c>S(t+Δt) = 100 − (100 − S)·exp(−A·Δt/100)</c>，其中 <c>A = r₀·环境·(1+本域已掌握数)·人口因子</c>。
    ///
    /// <b>先决条件：这个域得"见过"（<see cref="TribeState.Exposed"/>）。</b>
    /// 没见过就恒为 0 —— 一个从没摸过矿石的族群，<c>ore</c> 技能不会自己长出来。
    ///
    /// ── 这一条是踩了两次坑才立的 ──
    ///
    /// <b>第一版</b>只数"本域已掌握的技术数"（<c>A = r₀·(1+owned)</c>）。后果是鸡生蛋：
    /// 那些技术<b>自己就还没被解锁</b>，所以 owned 恒为 0，于是除 <c>fire</c>（有环境加成）外
    /// 所有域在 10 万年里都停在 10 上下（实测 wood=9.8、fiber=9.1、clay=4.7），树走不动。
    ///
    /// <b>第二版</b>为解决它，加了一个与域无关的全局项 <c>0.5·技术总数</c>。走过头了：
    /// 该项对本域毫无分辨力，一部落掌握 70 项技术时它等于 35，把<b>每一个</b>域
    /// （包括 <c>cs</c>/<c>semi</c>/<c>aero</c>）一律推到 ~33。于是树里所有 ≤33 的技能门槛
    /// 全部免费通过 —— 其中 <c>lens</c> 的 <c>optics:30</c> 只要前置 <c>glass</c> 就能点，
    /// 整条光学链（→望远镜→…→激光→光纤）因此全线放行。
    /// 实测后果：<b>量子力学（前 176,475 年）、激光、光纤、量子通讯全部出现在炼铜之前</b>，
    /// 而全星球 393 个部落的技能向量几乎一模一样。世界史退化成一堆同质的骰子。
    ///
    /// 现在这一版：<b>分辨力交给"见过没见过"，增长率交给环境与本域积累</b>。
    /// 晚期域（<c>cs</c>/<c>semi</c>/<c>aero</c>…）在本域第一项技术进前沿之前恒为 0，
    /// 于是梯子重新立起来：得先有电，才谈得上电信；得先有电信，才谈得上计算机。
    /// </summary>
    private void GrowSkills(TribeState t, double dt)
    {
        double popFactor = Math.Min(1.0, t.Population / 1000.0);
        if (popFactor <= 0) return;

        // ⚠️ "本域已掌握几项"要<b>一次遍历算完</b>，不能塞进下面的域循环里逐个去数技术集合。
        //    那样是 O(域 × 技术数) 每部落每步（23 × 上百项），晚期 3000 个部落时
        //    光这一处就压掉整个后半程的算力 —— 实测跑到 49% 之后几乎不动，就是它。
        //    倒过来遍历是 O(技术数 × 每项涉及的域数)，实测快两个数量级。
        var ownedByDomain = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string id in t.Techs)
        {
            if (!Tree.TryGet(id, out var node)) continue;
            foreach (string d in node.Skills.Keys)
                ownedByDomain[d] = ownedByDomain.GetValueOrDefault(d) + 1;
        }

        foreach (string domain in Tree.SkillDomains())
        {
            if (!t.Exposed.Contains(domain)) continue;      // 没见过 → 不长

            double s = t.GetSkill(domain);
            if (s >= 99.99) continue;

            // 环境是"自发启蒙"的底线，本域积累是加速器。两者都在同一个域内起作用，
            // 所以不会出现"会做陶就等于懂半导体"这种跨域串味。
            //
            // 标定（这一段是本引擎最该被复核的地方，因为它决定整个世界史的节奏）。
            //
            // 由 s(t) = 100(1−e^{−at/100}) 反解，摸到门槛 g 所需时间
            //     t(g) = (100/a)·ln(100/(100−g))
            // 于是 g = 10 : 20 : 45 : 80 的到达时间之比恒为 1 : 2.1 : 5.7 : 15.3。
            // <b>注意这个比值只由 logistic 决定，与 a 无关</b> —— 也就是说单靠调 a
            // 永远无法既让"农业(20 分)落在公元前 1 万年"、又让"最难的门槛(80 分)
            // 落在公元前后"：两者的时间距离被死死钉在 7 倍上。
            //
            // 想让 300 ka 里有"史前漫长、近代加速"的形状，就必须让 a 本身随部落成长而变大，
            // 即 §1.6 那句"环境决定哪个技能先长起来"加上"干得越多长得越快"。
            // 这既是真实技术史的形状（收益递增），也正是 §4.5 能源锚点
            // （2000 → 20000 → 50000 → 300000 kWh）隐含的意思。
            //
            // 系数 0.6 与 SkillBaseRate=5e-5 是这样定出来的：
            //   · 典型部落（本域 1 项）a = 5e-5 × 1.6 = 8e-5 → 门槛 20 落在 27.9 万年 ≈ 公元前 2 万年
            //     （与原注释"农业在公元前 1 万年上下"的目标同量级）
            //   · 领先部落（本域 20 项）a = 5e-5 × 13  = 6.5e-4 → 门槛 80 落在 24.8 万年 ≈ 公元前 5 万年
            //   两者相差 8 倍，正好补上 logistic 缺的那段动态范围。
            //
            // ⚠️ 两个坑都踩过，留作警示：
            //    · (1 + 1.2×owned) + r=1e-3：20 项时加速器 25 倍，正反馈失控，
            //      全树 175 项在公元前 10 万年就点完 171 项，之后 10 万年毫无变化。
            //    · (1 + 0.35·log₂(1+owned)) + r=8e-5：压得太平，300 ka 跑完只有 31 项、
            //      <b>连铜都没炼出来</b>（材料等级全程 0），世界停在旧石器。
            double env = EnvAffinity(t, domain);
            double owned = ownedByDomain.GetValueOrDefault(domain);

            // 两级加速：本域深耕（owned）+ 整体富庶（t.Techs.Count）。
            // ⚠️ 两级缺一不可 —— 只用 owned 会掉进低技术均衡陷阱（树小时不启动），
            //    只用全局数则本域深耕的优势被抹平（先炼铜的与刚见铜的长得一样快）。
            //    见 EvolutionConfig.SkillOwnedAccel / SkillTechAccel 两处注释。
            double accel = env
                         * (1.0 + Cfg.SkillOwnedAccel * owned)
                         * (1.0 + Cfg.SkillTechAccel * t.Techs.Count);
            double a = Cfg.SkillBaseRate * accel * popFactor;
            if (a <= 0) continue;

            double next = 100.0 - (100.0 - s) * Math.Exp(-a * dt / 100.0);
            if (next > s) t.Skills[domain] = next;
        }
    }

    /// <summary>
    /// 环境对某技能域的助益。§1.3 的 E_env 在技能侧的对应物 ——
    /// 铜矿多的地方 <c>ore</c> 技能长得快，可耕地多的地方 <c>plant</c> 长得快。
    /// <b>⚠️ 具体倍率是我拟的</b>，但"环境决定哪个技能先长起来"这一条是 §1.6 的核心论断。
    /// </summary>
    private static double EnvAffinity(TribeState t, string domain)
    {
        bool Has(params string[] terms) { foreach (string s in terms) if (t.Geo.Contains(s)) return true; return false; }

        return domain switch
        {
            // 需要矿的域：本地有矿才长得快。§1.3 的 E_env 本意就是"本地有没有铜矿露头"。
            "ore"    => Has("铜矿", "铁矿石", "锡矿", "金矿", "银矿", "金银矿", "铅银矿", "锌矿", "宝石矿", "铀矿") ? 1.5 : 0.6,
            "metal"  => Has("铜矿", "铁矿石", "锡矿", "铅银矿") ? 1.4 : 0.7,
            "chem"   => Has("硝土", "硝石矿", "硫磺", "草木灰/碱", "油田") ? 1.5 : 0.7,
            "clay"   => Has("黏土") ? 1.4 : 0.6,
            "wood"   => Has("树皮", "竹") ? 1.3 : 0.7,
            "stone"  => Has("石英砂", "美石河滩", "深部矿体") ? 1.3 : 0.9,
            "fiber"  => Has("麻", "树皮", "竹") ? 1.4 : 0.7,
            // 农牧：§1.6「农业必须适配本地环境」——可耕地与草场是硬条件
            "plant"  => Has("可耕地") ? 0.5 + 1.0 * t.AgricultureFactor : 0.4,
            "animal" => Has("牛草场", "野马") || t.Geo.Contains("牛") || t.Geo.Contains("羊")
                            ? 0.6 + 0.8 * Math.Min(1.0, t.AnnualRainMm / 800.0) : 0.5,
            // 火：越冷越依赖（§6.1「寒带大陆取暖需大量燃料」→ 反而在能源技术上领先）
            "fire"   => 0.8 + 0.5 * Math.Min(1.0, Math.Abs(t.ColdestMonthC) / 25.0),
            "build"  => 0.7 + 0.6 * t.AgricultureFactor,
            _        => 1.0,
        };
    }

    // ══════════════════════════════════════════════════════════════════
    //  §4.2 六力演化方程
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 由"已掌握的技术集合"重算全部状态分量。
    ///
    /// ⚠️ <b>这里是 §4.5 的禁区</b>：本方法<b>绝不读</b> <see cref="CivilizationIndex"/>。
    /// 主轴只是 <c>EnergyPerCapita</c> 的一个视图，让视图反过来影响状态就会
    /// 违反 INV-27（"关闭主轴后模拟轨迹逐位一致"）。
    /// </summary>
    private void RecomputeForces(TribeState t)
    {
        double maxTemp = TechPhysics.MaxTempOf(t.Techs);
        t.MaxTemp = maxTemp;                                              // §4.2 ①
        t.MaterialTier = TechPhysics.MaterialTierOf(maxTemp, t.Geo);      // §4.2 ②（INV-13/14/20/21）

        double prod = 1.0, destruct = 1.0;                                // §4.2 ⑤
        double energy = TechPhysics.EnergyAnchors[0].KwhPerCapitaYear;    // §4.2 ① 地板 = 狩猎采集
        double retentionCeiling = 0.97;                                   // §1.5：纯口头 ≈0.97/年
        double infoLevel = 0, beasts = 0, machines = 0, shock = 0, capMult = 1.0;
        double lifeDelta = 0, carbonRate = 0, unrest = 0;

        foreach (string id in t.Techs)
        {
            if (!_index.TryGetValue(id, out int idx)) continue;
            var e = _effects[idx];                    // 缓存，见 _effects 的注释

            prod *= 1.0 + e.ProdDelta;
            destruct *= 1.0 + e.DestructDelta;
            if (e.EnergyAnchorKwh > energy) energy = e.EnergyAnchorKwh;
            if (e.RetentionCeiling > retentionCeiling) retentionCeiling = e.RetentionCeiling;
            if (e.InfoLevel > infoLevel) infoLevel = e.InfoLevel;
            beasts += e.Beasts;
            machines += e.Machines;
            shock += e.ShockDelta;
            capMult *= e.CapacityMultiplier;
            lifeDelta += e.LifeDelta;
            carbonRate += e.CostCarbon;
            unrest += e.CostUnrest;
        }

        // §4.4：掌握深地机器人后，P2 阶跃 → 连带拉动 P3（"极大的提高"）
        if (t.Techs.Contains(TechPhysics.DeepMiningRobotTech))
            energy *= TechPhysics.DeepMiningEnergyBoost;

        // §4.6：气变暖反噬（仅在开启耦合时非零）。§6.2 的冰期旋回叠加由调用方处理。
        if (Cfg.CarbonClimateCoupling && GlobalTempOffset > 0)
            energy *= 1.0 - 0.05 * Math.Min(1.0, GlobalTempOffset / 6.0);

        t.ProdMultiplier = prod;
        t.DestructMultiplier = destruct;
        t.EnergyPerCapita = energy;
        t.Beasts = beasts;
        t.Machines = machines;
        t.ShockAbsorption = Math.Clamp(shock, 0.0, TechPhysics.MaxShockAbsorption);   // INV-28
        t.CapacityMultiplier = capMult;
        t.CarbonRate = carbonRate;
        t.Unrest = unrest;

        // ── P1a：通信（横向）──
        t.InfoSpeed = infoLevel;
        t.TrustRadius = 150.0 * (1.0 + infoLevel);        // 邓巴数 150 起，制度与技术放大

        // ── P1b：知识保持率向天花板靠（真正的衰减在 UpdateRetention）──
        t.RetentionCeiling = retentionCeiling;
        if (t.KnowledgeRetention > retentionCeiling) t.KnowledgeRetention = retentionCeiling;

        // ── P4a：制度容量（§1.7：邓巴数 → 习俗 → 财产制 → 法律 → 货币 → 国家）──
        t.InstitutionCapacity = InstitutionCapacityOf(t);

        // ── P5：组织效率 = 1 − 内耗率（§1.7：内耗率 = f(规模 / 制度容量)）──
        // 系数 0.35 而非更高：§1.7 说的是内耗随规模比上升，不是"一超标就崩"。
        // 取 0.35 时，规模达到制度容量的 2.2 倍 → 组织效率 0.70，
        // 而 0.70 正是 §6.3 分裂判据的那个"内耗率阈值"—— 两个数是同一个数，不是巧合。
        double ratio = t.Population / Math.Max(1.0, t.InstitutionCapacity);
        t.OrgEfficiency = ratio <= 1.0 ? 1.0 : 1.0 / (1.0 + 0.35 * (ratio - 1.0));

        // ── P6：生活体验与寿命（§4.2 ⑧）──
        double lqTarget = LifeQualityTarget(t, lifeDelta);
        t.LifeQuality += (lqTarget - t.LifeQuality) * 0.1;   // 平滑，避免振荡
        t.Lifespan = 22.0 + 0.55 * t.LifeQuality;

        t.Capacity = CapacityOf(t);                          // §4.2 ④ 的承载上限
    }

    /// <summary>
    /// §1.7 的制度容量阶梯：<c>邓巴数(150) → 习俗 → 财产制 → 法律 → 货币 → 国家</c>。
    ///
    /// <b>基线为什么是 1000 而不是 150</b>：邓巴数 150 是<b>无制度</b>的游群上限，
    /// 而 §5.4 明写元年部落就是 <b>1000 人</b>。1000 人能作为一个部落活着，
    /// 说明它已经有"习俗"这一档制度了 —— §1.7 的链条里"习俗"本来就在邓巴数之后、
    /// 财产制之前，只是没给数。故此处把"习俗"这一档标定在 1000：
    /// <b>这个数不是我拟的，是从 §5.4 的初始条件反推的</b>。
    ///
    /// 第一次跑引擎时基线取的是 150，后果很直接：元年每个部落的 规模/制度容量 都是 6.7，
    /// 内耗爆表 → 生育率被压到 r &lt; 0 → 100 个部落集体衰减，弱的撞上冲击就灭绝，
    /// 4 万年里死剩 21 个。**一个数放错档位，整个史前史就没了。**
    ///
    /// 节点库里"法律"缺位（见 <see cref="TechEffects.MissingNodes"/>），
    /// 故这一档暂由 <c>writing</c> 代理 —— 记为待裁。
    /// </summary>
    private static double InstitutionCapacityOf(TribeState t)
    {
        double cap = 1_000.0;                                 // 习俗（= §5.4 的初始部落规模）
        if (t.Techs.Contains("trade_barter")) cap = Math.Max(cap, 2_500);
        if (t.Techs.Contains("property_private")) cap = Math.Max(cap, 5_000);
        if (t.Techs.Contains("money")) cap = Math.Max(cap, 30_000);
        if (t.Techs.Contains("writing")) cap = Math.Max(cap, 200_000);   // 代"法律"
        if (t.Techs.Contains("market")) cap = Math.Max(cap, 1_000_000);
        if (t.Techs.Contains("telegraph")) cap = Math.Max(cap, 20_000_000);
        if (t.Techs.Contains("internet")) cap = Math.Max(cap, 1_000_000_000);
        return cap;
    }

    /// <summary>
    /// §4.2 ④：<c>TerritoryKm2 ≤ TerritoryCapKm2</c>，且承载上限
    /// <c>K = 密度 × 疆域 × 农业因子 × 容量倍率 × 材料项 × 能源项</c>。
    ///
    /// <b>疆域项必须是线性的（指数 1.0），这一条不是可调参数。</b>
    /// 承载上限的物理含义是"这片地上的产出"，而分裂是把地<b>对半分</b>的。
    /// 若指数取 0.3，两块半地的容量之和是整块的 <c>2^0.7 = 1.62</c> 倍 ——
    /// 也就是说<b>每分一次家，世界上就凭空多出 62% 的粮食</b>。
    /// 第一版正是这么写的（本想用次线性压住"扩疆→容量涨→人口涨→再扩疆"的正反馈），
    /// 实测立刻跑飞：4 万年 414 个部落、74.6 万人且加速上升。
    /// 正确的做法是让指数守恒，另外用"组织效率不够就不许扩疆"去掐那个正反馈（见 <see cref="GrowPopulation"/>）。
    ///
    /// <c>AgricultureFactor</c> 出自 §7.8（A 层土壤厚度 + 最冷月 + 降水）；
    /// 材料项与能源项是"同一片地，技术越高养得越多"，即 §1.6 的"农业必须适配本地环境"
    /// 与 §4.2 ① 的能源锚点。
    /// </summary>
    private double CapacityOf(TribeState t)
    {
        double materialTerm = 1.0 + 0.6 * t.MaterialTier;
        double energyTerm = Math.Sqrt(Math.Max(t.EnergyPerCapita, 1.0) / 2_000.0);
        return Cfg.CapacityBase * Math.Max(t.TerritoryKm2, 1.0)
             * Math.Max(t.AgricultureFactor, 0.05) * t.CapacityMultiplier
             * materialTerm * energyTerm;
    }

    /// <summary>§4.2 ⑧：<c>LifeQuality ← f(产出/人, 寿命, 和平度, 文化技术)</c>。</summary>
    private static double LifeQualityTarget(TribeState t, double lifeDelta)
    {
        double energyBonus = 10.0 * Math.Log10(Math.Max(t.EnergyPerCapita, 1.0) / 2_000.0);
        double scarcity = t.Population > t.Capacity ? -12.0 : 0.0;
        double lq = 32.0 + energyBonus + lifeDelta * 0.6 - 20.0 * t.Unrest + scarcity;
        return Math.Clamp(lq, 0.0, 100.0);
    }

    // ══════════════════════════════════════════════════════════════════
    //  人口动力学（§4.3 / §4.2 ④）
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// §4.3：<c>生育率 = BaseRate × 生存压力因子 × (1 − LifeQuality/100 × β) × 制度因子</c>。
    ///
    /// 用 logistic 的<b>闭式解</b>推进：
    /// <c>P(t+Δt) = K / (1 + ((K−P)/P)·e^{−rΔt})</c>。
    /// 闭式解对任意 Δt 都精确，故 INV-18（Δt=25 vs Δt=1 差异 &lt;2%）是<b>结构性成立</b>的，
    /// 不是靠把步长调小凑出来的。
    ///
    /// <b>⚠️ 两处我动了 §4.3 的写法，都是为了让它真的能产生作者描述的那条曲线：</b>
    ///
    /// ① LQ 项用了 <c>(LQ/100)^1.2</c> 而非线性。§4.3 只给了线性形式和 β≈0.6，
    ///    但线性形式<b>做不到它自己声明的结论</b>——"工业时代生育率跌破更替水平"。
    ///    线性下死亡率降得比生育率快，r 反而更高。取 1.2 次幂后 LQ=100 时生育率因子压到 0.15，
    ///    低于死亡率下限 0.008，r 才会转负。**这是为了让结论成立而改的形式，不是标定。**
    ///
    /// ② 制度因子取 <c>0.85 + 0.15·OrgEfficiency</c>（温和），而非让内耗直接砍半生育率。
    ///    §1.7 的内耗真正该咬的是"有效协作人口"（体现在 <see cref="TribeState.Labor"/>）
    ///    与分裂判据，不是"一户人家生几个孩子"。第一版取 0.5+0.5·Org，后果见
    ///    <see cref="InstitutionCapacityOf"/> 的注释：整个史前史被压没了。
    /// </summary>
    private void GrowPopulation(TribeState t, double dt)
    {
        double lq = t.LifeQuality;

        // §4.3 的生育率：生活体验越高，生育率越低
        double lqPressure = 1.0 - Math.Pow(lq / 100.0, 1.2) * 0.85;
        double fertility = Cfg.BaseFertility
                         * Math.Max(0.0, lqPressure)
                         * (0.85 + 0.15 * t.OrgEfficiency);   // 制度因子（温和）

        // 死亡率：体验越高越低，但有下限（寿命再长也会死）
        double mortality = Math.Max(0.008, Cfg.BaseMortality - 0.00040 * lq);

        double r = fertility - mortality;
        double k = Math.Max(1.0, t.Capacity);
        double p0 = Math.Max(t.Population, 1e-6);

        // logistic 闭式解
        double p;
        if (Math.Abs(r) < 1e-9)
        {
            p = p0;
        }
        else
        {
            double ratio = (k - p0) / p0;
            // ratio 可能为负（人口已超载）—— 此时 e^{−rΔt} 仍给出合理解（人口向 K 回落）
            p = k / (1.0 + ratio * Math.Exp(-r * dt));
        }

        if (double.IsNaN(p) || double.IsInfinity(p)) p = p0;   // 兜底，绝不写 NaN 进库
        t.Population = Math.Max(0.0, p);

        // §4.2 ④：疆域只在人口逼近承载上限时扩张（"需要了才去占"），且不得超过硬上限（INV-25）。
        //
        // ⚠️ 多加的 `OrgEfficiency > 0.85` 这一条是必须的，否则会无限空转：
        //    分裂把人口和疆域<b>同时</b>减半 → 人口仍等于承载上限 → 又触发扩疆 →
        //    疆域涨回来 → 人口涨回来 → 再分裂。**每一轮都不产出任何东西，只是不停地分家。**
        //    加上组织效率门槛后，语义变成"管得住自己的族群才有余力去占地"——
        //    这也正是 §1.7 那句"人多力量大"真正成立的前提：人多，且组织得起来。
        if (t.OrgEfficiency > 0.85
            && t.Population > 0.85 * t.Capacity
            && t.TerritoryKm2 < t.TerritoryCapKm2)
            t.TerritoryKm2 = Math.Min(t.TerritoryCapKm2,
                                      t.TerritoryKm2 + Cfg.TerritoryExpansionRate * dt);

        // 疆域是硬上限，永远不许越过（INV-25）
        if (t.TerritoryKm2 > t.TerritoryCapKm2) t.TerritoryKm2 = t.TerritoryCapKm2;
    }

    // ══════════════════════════════════════════════════════════════════
    //  §1.5 知识失传（INV-16）
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// §1.5：<c>无（纯口头）~0.97/年，且人口崩溃时骤降至 0</c>。
    ///
    /// INV-16 要求 <c>0 ≤ KnowledgeRetention ≤ 1</c>，且<b>无文字时随人口崩溃单调衰减</b>。
    /// 故衰减速率里必须含一个人口项 —— 这正是"一次瘟疫让部落退回石器状态"的机制。
    /// </summary>
    private void UpdateRetention(TribeState t, double dt)
    {
        double ceiling = t.RetentionCeiling;

        // 有文字（ceiling ≈ 1）时几乎不衰减；纯口头时每年 ~3% 的缺口
        double annualDecay = (1.0 - ceiling) * 1.0;

        // 人口崩溃加速失传：低于承载能力的一半时，衰减急剧放大
        double load = t.Capacity > 0 ? t.Population / t.Capacity : 1.0;
        double collapse = load < 0.5 ? (0.5 - load) / 0.5 : 0.0;      // 0~1
        annualDecay *= 1.0 + 3.0 * collapse;

        double next = ceiling - (ceiling - t.KnowledgeRetention) * Math.Exp(-annualDecay * dt);
        // 人口崩溃时保持率被直接拉低（§1.5 的"骤降至 0"）
        next *= 1.0 - collapse * (1.0 - Math.Exp(-0.5 * dt));

        t.KnowledgeRetention = Math.Clamp(next, 0.0, 1.0);            // INV-16

        // 保持率跌破阈值 → 已掌握的技术真的会丢（§1.5 的"退回石器状态"）
        TryForget(t);
    }

    /// <summary>
    /// §1.5 的"知识会失传"落到具体技术：保持率越低，越可能丢掉<b>链条末端</b>的技术
    /// （前置还在、但它自己忘了）—— 于是文明"倒退"而不是"崩掉"。
    /// 丢掉的节点会重新回到候选前沿，将来可以再被发现一次。
    /// </summary>
    private void TryForget(TribeState t)
    {
        if (t.KnowledgeRetention > 0.75) return;

        // ⚠️ 只能忘"链末端"：若本部落还有别的技术靠它当前置，就<b>不许忘</b>。
        //    少了这一条就会出现"会烧木炭却不会生火"这种前置悬空的状态，
        //    实测全场跑出 53192 处 INV 违反、直接拒绝落库。
        //    这一条也正是 §1.5「退回石器状态」的本意：先丢最上面的，
        //    基础手艺最后才丢 —— 而不是把链条中间抽掉、让上面悬在空中。
        var needed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in t.Techs)
            if (Tree.TryGet(id, out var node))
                foreach (string p in node.Requires) needed.Add(p);

        // ⚠️ 候选前沿上"正等着被发现"的节点，它们的前置同样不能忘。
        //    第一版只护住了已掌握技术的前置，漏了这一半 —— 于是前沿里会留下一个
        //    "前置刚被忘掉"的候选，Discover 一掷中它就制造悬空。
        //    实测：修掉第一版后仍剩 15055 处违反，全部出自这一类
        //    （"掌握 glass 但缺前置 high_temp_kiln" 等）。
        foreach (string id in t.Frontier)
            if (Tree.TryGet(id, out var node))
                foreach (string p in node.Requires) needed.Add(p);

        double forgetChance = (0.75 - t.KnowledgeRetention) * 0.02;   // 每步
        bool any = false;
        foreach (string id in t.Techs.ToArray())
        {
            if (WorldConfig.InitialTechSet.Contains(id)) continue;    // 打石器与用火不会丢
            if (needed.Contains(id)) continue;                        // 还有后学靠它撑 → 不能忘
            if (!Tree.TryGet(id, out var n)) continue;

            ulong h = Hashing.Hash64(Cfg.Seed, HashDomain.Evolution, t.Id, _index[id], (int)Math.Floor(Year) + 7919);
            if (Hashing.ToUnit(h) >= forgetChance) continue;

            t.Techs.Remove(id);
            any = true;
            Events.Add(new SimEvent(Year, t.Id, t.ContinentId, "forget", id, 1.0,
                $"{t.Code} 失传 {n.Name}"));
        }

        // 一遍全扫比"每忘一项扫一次"省得多（晚期 3000 部落时是数量级差别）
        if (any) RefreshFrontier(t);
    }

    // ══════════════════════════════════════════════════════════════════
    //  §4.6 熵代价
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// §4.6：<c>化石燃料消耗 → CarbonLoad 累积 → GlobalTempOffset += CarbonWarming(CarbonLoad)</c>。
    /// INV-26 要求 <c>CarbonLoad</c> <b>单调不减</b> —— 故这里只有累加，没有衰减项。
    /// </summary>
    private void UpdateCarbon(TribeState t, double dt)
    {
        if (t.CarbonRate <= 0) return;
        // 碳排放 ∝ 人均能量通量 × 碳强度 × 人口
        double emitted = t.CarbonRate * (t.EnergyPerCapita / 2_000.0) * t.Population * dt / 1e6;
        t.CarbonLoad += emitted;
        TotalCarbon += emitted;
    }

    /// <summary>
    /// §4.6 的 <c>CarbonWarming</c>。碳-气候耦合<b>默认关闭</b>（Q-A8），
    /// 开启时 <c>GlobalTempOffset</c> 才非零，故 INV-26 的"关闭后与 v0.3 逐位一致"成立。
    ///
    /// 系数含义：累积碳量每 1000（本模型的任意单位）抬升约 1.5 ℃，并在高位非线性放大
    /// —— §4.6 说的"若越过临界值，触发失控温室分支"。
    /// <b>⚠️ 这两个系数是我拟的</b>，作者若要严肃对待碳-气候耦合应当另给标定。
    /// </summary>
    private static double CarbonWarming(double totalCarbon)
    {
        double linear = totalCarbon / 1000.0 * 1.5;
        double runaway = totalCarbon > 3000 ? Math.Pow((totalCarbon - 3000) / 1000.0, 1.5) : 0.0;
        return linear + runaway;
    }

    // ══════════════════════════════════════════════════════════════════
    //  §4.7 冲击
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// §4.7：<c>存活人口 = Pop × exp(−冲击强度 × (1 − ShockAbsorption))</c>，
    /// <c>知识保有率 = KR × (1 − 冲击强度 × (1 − SA) × 0.5)</c>。
    ///
    /// "抗逆力不改变进化方向，只决定谁出局"（§1.0）—— 故冲击<b>不改任何方向性分量</b>
    /// （ProdMultiplier / EnergyPerCapita / MaterialTier 一个都不动），只削人口与知识。
    /// </summary>
    private void ApplyShock(TribeState t, double dt, int yearBucket)
    {
        // 泊松式到达：期望每 ShockMeanIntervalYears 年一次
        double arrive = 1.0 - Math.Exp(-dt / Cfg.ShockMeanIntervalYears);
        ulong h = Hashing.Hash64(Cfg.Seed, HashDomain.Shock, t.Id, yearBucket, 0);
        if (Hashing.ToUnit(h) >= arrive) return;

        // 强度 ∈ [0.05, 0.9]，同样由哈希导出
        ulong h2 = Hashing.Hash64(Cfg.Seed, HashDomain.Shock, t.Id, yearBucket, 1);
        double intensity = Hashing.ToRange(h2, 0.05, 0.90);

        // 类型：饥荒 / 瘟疫 / 战争 / 气候突变
        ulong h3 = Hashing.Hash64(Cfg.Seed, HashDomain.Shock, t.Id, yearBucket, 2);
        string kind = (Hashing.ToUnit(h3) * 4.0) switch
        {
            < 1.0 => "famine",
            < 2.0 => "plague",
            < 3.0 => "war",
            _     => "climate",
        };

        double before = t.Population;
        t.Population = TechPhysics.SurvivingPopulation(t.Population, intensity, t.ShockAbsorption);
        t.KnowledgeRetention = Math.Clamp(
            TechPhysics.SurvivingRetention(t.KnowledgeRetention, intensity, t.ShockAbsorption), 0.0, 1.0);

        // 小灾不记档，但照常结算（见 ShockLogThreshold）
        if (intensity < Cfg.ShockLogThreshold) return;

        string absorb = t.ShockAbsorption <= 0
            ? "此时还毫无抗逆力（§4.7：储备/贸易/文字/驯化都还没出现）"
            : $"抗逆 {t.ShockAbsorption:0.00} 抵掉 {t.ShockAbsorption * 100:0}%";

        Events.Add(new SimEvent(Year, t.Id, t.ContinentId, $"shock:{kind}", null, intensity,
            $"{t.Code} 遭遇{ShockName(kind)}（强度 {intensity:0.00}）：" +
            $"{before:0} → {t.Population:0} 人 —— {absorb}"));
    }

    private static string ShockName(string kind) => kind switch
    {
        "famine" => "饥荒", "plague" => "瘟疫", "war" => "战乱", _ => "气候突变",
    };

    // ══════════════════════════════════════════════════════════════════
    //  §6.3 部落分裂
    // ══════════════════════════════════════════════════════════════════

    /// <summary>§6.3 的内耗率阈值：组织效率低于它就要分家。</summary>
    private const double FissionOrgThreshold = 0.70;

    /// <summary>低于这个疆域就不再分 —— 再分下去两块地都养不活人，那不是扩散是自杀。</summary>
    private const double MinViableTerritoryKm2 = 150.0;

    /// <summary>
    /// §6.3：<c>若 Pop &gt; Capacity 且 内耗率 &gt; 阈值 → 分裂</c>。
    /// 新部落继承母部落技术库（有损耗），分配新 ID。
    ///
    /// <b>判据的一处偏离，必须写明白</b>：原文写的是 <c>Pop &gt; Capacity</c>，
    /// 但本引擎的人口走的是 logistic，<b>它渐近于 K 而永不超过 K</b> ——
    /// 照字面实现的话这条判据一次都不会触发，100 个部落永远变不成数千个。
    /// 故改判"贴着上限"（<c>Pop ≥ 0.98·Capacity</c>），语义上正是原文要说的
    /// "地不够了"。**这是实现与原文的差异，不是标定，应当由作者过目。**
    ///
    /// 于是分裂真正由什么触发：<b>承载上限涨过了制度容量</b>。
    /// §1.7 的 <c>内耗率 = f(规模/制度容量)</c> 一旦让组织效率跌破 0.70，
    /// 就说明"这片地能养活的人已经多到这个部落的制度管不住了"→ 分家。
    /// 财产制（5000）、货币（30000）、文字（200000）每上一档，就把分裂线往上抬一次 ——
    /// 这正是 §5.2 表里"法律 → P5 OrgEfficiency↑↑ → 避免无效的内耗"那句话的落点。
    ///
    /// <b>人口与土地都是守恒地转移，不是凭空新增</b> —— 否则 INV-17
    /// （<c>Σ 各部落人口 == 全局人口</c>）立刻破。故子部落拿的是母部落的<b>一半</b>：
    /// 人口对半、疆域对半。这既守住了守恒，也正是现实中"分家"的样子。
    /// </summary>
    private void Fission(int yearBucket)
    {
        if (_tribes.Count >= Cfg.MaxTribes) return;

        int n = _tribes.Count;
        for (int i = 0; i < n; i++)
        {
            var parent = _tribes[i];
            if (!parent.Alive) continue;
            if (_tribes.Count >= Cfg.MaxTribes) return;

            // 条件一：人口贴着承载上限（= 原文的 Pop > Capacity，见上方注释）
            if (parent.Population < 0.98 * parent.Capacity) continue;

            // 条件二：内耗大（= 组织效率低）
            if (parent.OrgEfficiency >= FissionOrgThreshold) continue;

            // 地不够分就不再分
            if (parent.TerritoryKm2 < 2.0 * MinViableTerritoryKm2) continue;

            ulong h = Hashing.Hash64(Cfg.Seed, HashDomain.Fission, parent.Id, yearBucket, 0);

            // 越管不住越容易分：组织效率刚跌破阈值时 1%/步，跌到 0.2 时 10%/步
            double prob = Math.Min(0.5,
                0.10 * (FissionOrgThreshold - parent.OrgEfficiency) / FissionOrgThreshold);
            if (Hashing.ToUnit(h) >= prob) continue;

            Split(parent, h);
        }
    }

    private void Split(TribeState parent, ulong h)
    {
        double halfPop = parent.Population * 0.5;
        double halfLand = parent.TerritoryKm2 * 0.5;
        double halfCap = parent.TerritoryCapKm2 * 0.5;    // 上限也要分，见 TribeState.TerritoryCapKm2

        parent.Population -= halfPop;      // 守恒转移，不是复制
        parent.TerritoryKm2 -= halfLand;
        parent.TerritoryCapKm2 -= halfCap;

        int newId = _tribes.Count;
        string code = $"{parent.Code}.{_tribes.Count(t => t.ParentId == parent.Id) + 1}";

        var child = new TribeState
        {
            Id = newId,
            Code = code,
            ContinentId = parent.ContinentId,
            ParentId = parent.Id,
            FoundedYear = Year,
            Lat = parent.Lat + Hashing.ToRange(h, -1.5, 1.5),
            Lon = Sphere.WrapLon(parent.Lon + Hashing.ToRange(Hashing.Mix(h, 1), -1.5, 1.5)),
            ElevationM = parent.ElevationM,
            MeanTempC = parent.MeanTempC,
            AnnualRainMm = parent.AnnualRainMm,
            ColdestMonthC = parent.ColdestMonthC,
            DistToSeaKm = parent.DistToSeaKm,
            TopsoilM = parent.TopsoilM,
            AgricultureFactor = parent.AgricultureFactor,
            Biome = parent.Biome,
            TerritoryKm2 = halfLand,
            TerritoryCapKm2 = halfCap,
            Geo = parent.Geo,                 // 同处一域，禀赋相同
            Population = halfPop,
            // §6.3："继承母部落技术库（但有损耗，模拟传承损失）"
            KnowledgeRetention = parent.KnowledgeRetention * 0.97,
        };

        // 技术继承：逐条按保持率掷骰，然后补回前置闭包 ——
        // 于是"高级的丢了、基础的还在"，正是文明倒退的样子而非崩掉。
        foreach (string id in parent.Techs)
        {
            ulong hh = Hashing.Hash64(Cfg.Seed, HashDomain.Fission, newId, _index[id], 0);
            if (Hashing.ToUnit(hh) < parent.KnowledgeRetention) child.Techs.Add(id);
        }
        CloseUnderPrerequisites(child);

        // 继承技术的"首次掌握年份"随技术一起过来（历史是继承的，不是从分裂那年起算）
        foreach (string id in child.Techs)
            child.AcquiredYear[id] = parent.AcquiredYear.TryGetValue(id, out double y) ? y : Year;

        foreach (var (d, v) in parent.Skills)
            child.Skills[d] = v * 0.95;      // 技能也带一点损耗

        // 曝光是单调的、且是"知道世上有这门手艺"，与技艺水平无关，故原样继承（不打折）。
        // 打折会造出一个悖论：分家后孩子比父母更晚"想起"世上还有炼铁这回事。
        child.Exposed.UnionWith(parent.Exposed);

        // 贸易能力随技术库一起继承，故实际可用原料也要跟着重算
        // （否则孩子会"继承了货币却不能用货币买到的原料"）。
        CacheEffectiveGeo(child);

        child.TerritoryKm2 = Math.Min(child.TerritoryKm2, child.TerritoryCapKm2);
        _frontierUnion.UnionWith(child.Techs);
        RefreshFrontier(child);
        RecomputeForces(child);

        _tribes.Add(child);

        Events.Add(new SimEvent(Year, newId, child.ContinentId, "fission", null,
            child.Population,
            $"{parent.Code} 分裂出 {code}：{child.Population:0} 人、{child.TerritoryKm2:0} km²、" +
            $"继承 {child.Techs.Count}/{parent.Techs.Count} 项技术"));
    }

    /// <summary>把技术集合补成"前置闭合"——丢了高级的，就别把它的前置也留着悬空。</summary>
    private void CloseUnderPrerequisites(TribeState t)
    {
        var stack = new Stack<string>(t.Techs);
        while (stack.Count > 0)
        {
            string id = stack.Pop();
            if (!Tree.TryGet(id, out var n)) continue;
            foreach (string p in n.Requires)
                if (t.Techs.Add(p)) stack.Push(p);
        }
    }

    private void Kill(TribeState t)
    {
        t.ExtinctYear = Year;
        t.Population = 0;
        Events.Add(new SimEvent(Year, t.Id, t.ContinentId, "extinct", null, 1.0,
            $"{t.Code} 灭绝（曾掌握 {t.Techs.Count} 项技术；技术前沿并集不因此缩小，INV-23）"));
    }

    // ══════════════════════════════════════════════════════════════════
    //  §4.5 主轴（只读视图 —— 绝不参与状态推进，INV-27）
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// §4.5 的文明指数：<c>EnergyPerCapita × Π(其余各力归一化后的乘数)</c>。
    ///
    /// ⚠️ <b>本方法只能用于排序与着色。</b>任何状态推进读了它，INV-27 就破了
    /// （"关闭主轴后模拟轨迹必须逐位一致"）。故它被刻意写成<b>纯函数、无副作用</b>，
    /// 且全引擎除展示层外无人调用 —— 这与"能删掉整段而不影响轨迹"是同义的。
    /// </summary>
    public static double CivilizationIndex(TribeState t)
    {
        double info = 1.0 + Math.Log10(1.0 + t.InfoSpeed);          // P1a
        double retention = 0.5 + 0.5 * t.KnowledgeRetention;        // P1b
        double material = 1.0 + 0.5 * t.MaterialTier;               // P2
        double labor = Math.Log10(1.0 + Math.Max(t.Labor(), 1.0));  // P4
        double prod = t.ProdMultiplier;                             // P5a
        double org = 0.5 + 0.5 * t.OrgEfficiency;                   // P5
        double experience = 0.5 + t.LifeQuality / 100.0;            // P6
        return t.EnergyPerCapita * info * retention * material * labor * prod * org * experience;
    }

    /// <summary>全球总人口。INV-17 要求它等于 Σ 各部落人口 —— 由调用方校验。</summary>
    public double TotalPopulation => _tribes.Where(t => t.Alive).Sum(t => t.Population);

    // ══════════════════════════════════════════════════════════════════
    //  引擎侧不变量自检
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 跑完之后自检那些<b>演化过程本身可能跑破</b>的不变量。
    ///
    /// 与 <see cref="Invariants"/> 的分工：那边的 20 项判的是<b>世界生成</b>的产物
    /// （大陆连通性、部落间距、海拔……），这里判的是<b>三万年演化过程</b>的产物。
    /// M1 那套不变量在演化之前就已经全过了，它们<b>看不见</b>演化会不会把状态推出界。
    ///
    /// 返回空列表 = 全过。
    /// </summary>
    public List<string> VerifyInvariants()
    {
        var bad = new List<string>();

        foreach (var t in _tribes)
        {
            // INV-16：知识保持率 ∈ [0,1]
            if (t.KnowledgeRetention < 0 || t.KnowledgeRetention > 1)
                bad.Add($"INV-16 {t.Code} KnowledgeRetention={t.KnowledgeRetention:R}");

            // INV-25：疆域不得超过上限
            if (t.TerritoryKm2 > t.TerritoryCapKm2 + 1e-6)
                bad.Add($"INV-25 {t.Code} Territory={t.TerritoryKm2:R} > Cap={t.TerritoryCapKm2:R}");

            // INV-28：抗逆力 ∈ [0,1]
            if (t.ShockAbsorption < 0 || t.ShockAbsorption > 1)
                bad.Add($"INV-28 {t.Code} ShockAbsorption={t.ShockAbsorption:R}");

            // INV-26：碳排放单调不减 —— 由"只加不减"结构性保证，此处查非负
            if (t.CarbonLoad < 0)
                bad.Add($"INV-26 {t.Code} CarbonLoad={t.CarbonLoad:R}");

            // INV-13：材料等级必须与工艺温度自洽（能炼什么由温度决定，不由技术名决定）
            int tierByTemp = TechPhysics.MaterialTierOf(t.MaxTemp, t.Geo);
            if (Math.Abs(tierByTemp - t.MaterialTier) > 1e-9)
                bad.Add($"INV-13 {t.Code} MaterialTier={t.MaterialTier} 但按 MaxTemp={t.MaxTemp:R} 应为 {tierByTemp}");

            // 数值健康：NaN/Inf 一旦写进库就再也查不出来
            if (double.IsNaN(t.Population) || double.IsInfinity(t.Population) || t.Population < 0)
                bad.Add($"{t.Code} 人口非有限：{t.Population:R}");
            if (double.IsNaN(t.EnergyPerCapita) || double.IsInfinity(t.EnergyPerCapita))
                bad.Add($"{t.Code} 人均能耗非有限：{t.EnergyPerCapita:R}");
            if (double.IsNaN(t.Capacity) || double.IsInfinity(t.Capacity))
                bad.Add($"{t.Code} 承载上限非有限：{t.Capacity:R}");

            // 技术集合必须前置闭合：不然"会造铁但不会取火"这种状态会静默存在
            foreach (string id in t.Techs)
            {
                if (!Tree.TryGet(id, out var n)) { bad.Add($"{t.Code} 掌握未知节点 {id}"); continue; }
                foreach (string p in n.Requires)
                    if (!t.Techs.Contains(p))
                        bad.Add($"{t.Code} 掌握 {id} 但缺前置 {p}");
            }
        }

        // INV-23：技术前沿的并集不得缩小 —— 由"只增不减"结构性保证。
        // 此处查的是它<b>确实等于</b>所有部落（含已灭绝）掌握过的技术的并集。
        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in _tribes) union.UnionWith(t.Techs);
        if (!union.IsSubsetOf(_frontierUnion))
            bad.Add($"INV-23 前沿并集小于实际并集（差 {_frontierUnion.Count - union.Count} 项的记录丢失）");

        // 已灭绝的部落不得还有人
        foreach (var t in _tribes)
            if (!t.Alive && t.Population > 0)
                bad.Add($"{t.Code} 已灭绝（{WorldConfig.FormatYear(t.ExtinctYear!.Value)}）却还有 {t.Population:R} 人");

        return bad;
    }
}
