namespace Humen.Core;

/// <summary>
/// 一个部落的完整状态向量。字段与 <c>design.md</c> §4.1 的 <c>TribeState</c> <b>一一对应</b>，
/// 顺序、含义、单位都照抄，不增不减 —— 因为 §4.5 末尾特意交代过：
///
/// <blockquote>"若 P3 主轴掩盖了由 P1/P6 主导的文明差异，退回多维视图即可 ——
/// 故 <c>TribeState</c> 中<b>所有分量全部保留</b>。"</blockquote>
///
/// 也就是说这个结构体本身就是"退路"：主轴（<see cref="EnergyPerCapita"/>）只是个视图，
/// 只要任一时刻想退回六力并排看，数据都还在。
/// </summary>
public sealed class TribeState
{
    // ══════════════════════════════════════════════════════════════════
    //  身份（不随演化改变；分裂时由母部落派生）
    // ══════════════════════════════════════════════════════════════════

    /// <summary>全局唯一序号。元年 100 个为 0..99，分裂产生的新部落从这里往后发。</summary>
    public int Id { get; init; }

    /// <summary>req.txt 的「大陆.部落」写法，如 "1.1"。分裂后为 "1.1.1" 形式（§6.3）。</summary>
    public string Code { get; set; } = "";

    public int ContinentId { get; init; }
    public int ParentId { get; init; } = -1;
    public double FoundedYear { get; init; }

    /// <summary>灭绝年份（<c>null</c> = 还活着）。§1.0 允许部落灭绝，但技术前沿的并集不许缩小（INV-23）。</summary>
    public double? ExtinctYear { get; set; }

    public bool Alive => ExtinctYear is null;

    // ══════════════════════════════════════════════════════════════════
    //  地理与禀赋（固定不变 —— 部落不会搬家；分裂出的新部落才换地方）
    // ══════════════════════════════════════════════════════════════════

    public double Lat { get; init; }
    public double Lon { get; init; }
    public double ElevationM { get; init; }
    public double MeanTempC { get; init; }
    public double AnnualRainMm { get; init; }
    public double ColdestMonthC { get; init; }      // INV-15 判的是它，不是年均温
    public double DistToSeaKm { get; init; }
    public double TopsoilM { get; init; }
    /// <summary>§7.8：A 层厚度 + 最冷月 + 降水算出的农业产能因子 ∈ [0,1]。</summary>
    public double AgricultureFactor { get; init; }
    public string Biome { get; init; } = "";

    /// <summary>
    /// 本部落可扩张上限（§1.10）—— 它在<b>大陆土地里占的那一份</b>。
    ///
    /// INV-25 原文写的是"由大陆面积给出且<b>全程恒定</b>"。本引擎在此处<b>偏离了它的字面</b>，
    /// 必须记明白：分裂时母部落的这一份要<b>对半分给子部落</b>（母部落自己减半）。
    /// 理由是 §1.10 的立论本身：<b>"土地不可再生，零和"</b>、<b>"疆域天花板锁死 →
    /// 人口天花板锁死"</b>。若分裂时子部落凭空多拿一份，则 Σ 各部落上限会随分裂翻倍，
    /// 几次之后就远超大陆面积，"疆域锁死"整个结论就没了 ——
    /// 实测第一次跑：4 万年里分裂出 414 个部落，总人口从 10 万涨到 74.6 万且仍在加速，
    /// 原因正是子部落的上限没从母部落划走。
    /// 故取"每个部落的上限在<b>其存续期内</b>恒定，新部落的上限自母部落划出"这一读法。
    /// <b>这一条与 INV-25 的字面冲突，待作者裁定。</b>
    /// </summary>
    public double TerritoryCapKm2 { get; set; }

    /// <summary>境内实有的矿产/原料（来自 §6.1 大陆禀赋 × 本地地质）。</summary>
    public HashSet<string> Geo { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// <b>算上贸易之后</b>实际能用上的原料集合 —— <see cref="Geo"/> 是"境内有什么"，
    /// 这个才是"门槛判定时算你有什么"。技术节点的 <c>geo</c> 门槛查的是这一个。
    ///
    /// §6.1 给五块大陆各分了不同矿产，本意就是<b>逼出贸易</b>
    /// （赤道大陆那行作者写得很直白：「采集 → 早期农业受限 → <b>可能走贸易路线</b>」）。
    /// 没有这一层，`battery` 要的铜（1/2 号大陆）与锌（3 号大陆）永远不可能同时到手，
    /// 整条电气主干就永久断在 69 项技术那里 —— 实测就是如此。
    ///
    /// 由 <c>EvolutionEngine</c> 在部落掌握贸易类技术时重算并缓存；
    /// 原始 <see cref="Geo"/> 保持不动，因为技能增长看的是<b>本地环境</b>（§1.6）。
    /// </summary>
    public HashSet<string> EffectiveGeo { get; } = new(StringComparer.Ordinal);

    // ══════════════════════════════════════════════════════════════════
    //  §4.1 状态向量
    // ══════════════════════════════════════════════════════════════════

    // ── P1a 通信（横向：跨空间）──
    /// <summary>信息传递速率（相对值）。§4.2 ⑦：由信息技术最高层级给出。</summary>
    public double InfoSpeed { get; set; }

    /// <summary>信任半径（可协作人数上限）。§4.2 ⑦：<c>f(InfoSpeed, 制度技术)</c>。</summary>
    public double TrustRadius { get; set; }

    // ── P1b 记录（纵向：跨时间）──
    /// <summary>知识保持率 ∈ [0,1]（INV-16）。无文字时随人口崩溃单调衰减。</summary>
    public double KnowledgeRetention { get; set; } = 1.0;

    // ── P2 材料 ──
    /// <summary>可加工材料等级：0=木石, 1=陶, 2=铜, 3=青铜, 4=铁（§1.4 / INV-13）。</summary>
    public double MaterialTier { get; set; }

    // ── P3 能源【主轴 §4.5】──
    /// <summary>
    /// 人均年能耗（kWh）—— 文明等级硬指标、统一纵轴。
    /// ⚠️ §4.5 澄清过：量的<b>不是消耗，是支配</b>，故必须计入畜力/燃料/电力/分摊的基础设施。
    /// </summary>
    public double EnergyPerCapita { get; set; } = 2_000;

    /// <summary>当前可达工艺温度（℃）。由 §1.4 的热源表给出，决定能炼什么。</summary>
    public double MaxTemp { get; set; }

    /// <summary>累积碳排放（§4.6 熵代价，INV-26 要求单调不减）。</summary>
    public double CarbonLoad { get; set; }

    // ── P4a 劳动力 ──
    /// <summary>总人口。INV-17：Σ 各部落人口 == 全局人口。</summary>
    public double Population { get; set; }

    public double Slaves { get; set; }

    /// <summary>畜力单位（§4.2 ③：1 畜 = 8 个劳动力）。</summary>
    public double Beasts { get; set; }

    /// <summary>机械单位（§4.2 ③：1 机械 = 50 个劳动力）。</summary>
    public double Machines { get; set; }

    // ── P4b 疆域（§1.10）──
    /// <summary>实控面积。INV-25：<c>TerritoryKm2 ≤ TerritoryCapKm2</c>。</summary>
    public double TerritoryKm2 { get; set; }

    // ── P5a 生产 / P5b 破坏（§1.9）──
    /// <summary>生产力放大倍数（拳脚 = 1）。INV-24：生产结算<b>只读</b>它。</summary>
    public double ProdMultiplier { get; set; } = 1.0;

    /// <summary>破坏力放大倍数（拳脚 = 1）。INV-24：战争结算<b>只读</b>它，与 P5a 独立演进。</summary>
    public double DestructMultiplier { get; set; } = 1.0;

    /// <summary>组织效率 = 1 − 内耗率（§1.7：<c>内耗率 = f(规模 / 制度容量)</c>）。</summary>
    public double OrgEfficiency { get; set; } = 1.0;

    // ── P6 体验 ──
    /// <summary>生活体验 0~100。§4.3 的生育率通过它反噬人口增长（β ≈ 0.6）。</summary>
    public double LifeQuality { get; set; } = 50.0;

    /// <summary>预期寿命（年）。</summary>
    public double Lifespan { get; set; } = 30.0;

    // ── 非方向性 · 载体筛选器（§1.0 / §4.7）──
    /// <summary>抗逆力 ∈ [0,1]（INV-28）。不进 power.txt 的力的列表，只决定"谁出局"。</summary>
    public double ShockAbsorption { get; set; }

    // ══════════════════════════════════════════════════════════════════
    //  支撑数据（不是 §4.1 的分量，但没有它们算不动）
    // ══════════════════════════════════════════════════════════════════

    /// <summary>已掌握的技术节点 id。</summary>
    public HashSet<string> Techs { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 技术 id → <b>首次</b>掌握年份。落进 <c>tech_state.acquired_year</c>。
    /// 记"首次"而非"最近一次"：§1.5 允许失传后重新发现，但库里那一行只该有一份，
    /// 而"这个族群什么时候第一次会做这件事"才是有历史意义的那个数。
    /// </summary>
    public Dictionary<string, double> AcquiredYear { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 23 个技能域的当前水平，0~100。判定技术节点的 <c>skill</c> 门槛用。
    /// 只记录非零域，缺键即 0。
    /// </summary>
    public Dictionary<string, double> Skills { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// <b>这个部落"见过"的技能域</b> —— 只在这些域上练习，其余域恒为 0。
    ///
    /// 一个域何时算"见过"：本部落<b>掌握</b>了该域的某项技术，或者该域的某项技术
    /// <b>进了候选前沿</b>（前置已齐、机会就摆在面前）。
    ///
    /// <b>为什么必须有这一层</b>：没有它，技能就只能靠一个与域无关的全局项来增长，
    /// 而全局项对本域毫无分辨力 —— 实测一个连铜矿都没见过的部落会把
    /// <c>cs</c>（计算机）、<c>semi</c>（半导体）、<c>aero</c>（航空）全部推到 33.2，
    /// 于是树里所有 ≤33 的门槛（<c>lens</c> 的 <c>optics:30</c> 是最要命的一个）全部免费通过，
    /// 175 个节点的难度梯度整个塌平，发现顺序退化成纯掷骰 ——
    /// 后果是<b>量子力学、激光、光纤在炼铜之前就出现了</b>。
    ///
    /// 这一层也是"环境决定哪个技能先长起来"（§1.6）真正的落点：
    /// 有铜矿露头的部落才会去想冶炼，草原上的部落才会去想驯马。
    /// 单调不减 —— 见过一次就永远见过，因为手艺这东西一旦知道存在就不会重新变回"不知道"。
    /// </summary>
    public HashSet<string> Exposed { get; } = new(StringComparer.Ordinal);

    /// <summary>本部落的承载上限（人）。由农业因子 × 疆域 × 容量倍率算出。</summary>
    public double Capacity { get; set; }

    /// <summary>制度容量（§1.7：邓巴数 → 习俗 → 财产制 → 法律 → 货币 → 国家）。</summary>
    public double InstitutionCapacity { get; set; } = 150.0;

    /// <summary>发现引擎的候选前沿：前置已满足、但尚未掌握的技术。避免每步扫全树。</summary>
    public List<string> Frontier { get; } = new();

    // ── 每步由 RecomputeForces 重算的中间量 ──
    // 它们不是 §4.1 的分量，而是"从已掌握技术集合折叠出来"的聚合值，
    // 供同一步内的后续子系统（人口/失传/碳）取用，避免重复遍历技术集合。

    /// <summary>容量倍率 ∏(1+CapacityMultiplier)，§4.2 ④ 的承载上限乘数。</summary>
    public double CapacityMultiplier { get; set; } = 1.0;

    /// <summary>碳强度 ΣCostCarbon，§4.6 的排放速率。</summary>
    public double CarbonRate { get; set; }

    /// <summary>内耗 ΣCostUnrest，§1.7 的负面项。</summary>
    public double Unrest { get; set; }

    /// <summary>知识保持率天花板（§1.5：纯口头 ≈0.97，有文字 ≈1.0）。</summary>
    public double RetentionCeiling { get; set; } = 0.97;

    public double GetSkill(string domain) => Skills.TryGetValue(domain, out double v) ? v : 0.0;

    /// <summary>§4.2 ③：<c>Labor = Pop × AgeFactor + Slaves + Beasts×8 + Machines×50</c>。</summary>
    public double Labor(double ageFactor = 0.55)
        => Population * ageFactor + Slaves + Beasts * 8.0 + Machines * 50.0;

    /// <summary>每千人每年可支配的能量（kWh）—— §4.5 主轴的绝对量。</summary>
    public double EnergyFlux => EnergyPerCapita * Population;

    /// <summary>§4.5 的 log₁₀ 刻度，供排行榜与星球着色。</summary>
    public double LogEnergyScore => TechPhysics.LogScore(EnergyPerCapita);

    public override string ToString()
        => $"{Code} pop={Population:0} E={EnergyPerCapita:0} techs={Techs.Count}";
}
