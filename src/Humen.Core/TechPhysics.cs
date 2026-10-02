namespace Humen.Core;

/// <summary>
/// 进化引擎的<b>物理常量表</b>：把 <c>design.md</c> 里已经写死的定量约束搬成代码。
///
/// 本类里每一张表都能指到设计文档的某一段，<b>没有一处是我编的</b>：
///   §1.4  热源可达温度 与 材料熔点/工艺温度（这就是"为什么青铜时代早于铁器时代"的物理根据）
///   §4.4  资源开采深度分层（4000 m 是人类硬极限）
///   §4.5  P3 主轴的半定量标度（人均年能量通量）
///   §6.1  大陆环境禀赋
///
/// ⚠️ 与之相对，<b>每个技术节点对各力的"效果"在 <c>tech_tree.md</c> 里根本不存在</b> ——
/// 175 个节点只有 <c>pre/skill/geo/tag/adopt/cost</c>，没有 <c>effects</c>。
/// §5.1 的 <c>TechNode</c> 定义了 <c>Effect[] Effects</c> 字段，但节点库里没有对应数据。
/// 这个缺口的桥接规则集中在 <see cref="TechEffects"/>，那里会明确标出哪些是作者的、哪些是我拟的。
/// </summary>
public static class TechPhysics
{
    // ══════════════════════════════════════════════════════════════════
    //  §1.4 热源：可达工艺温度
    // ══════════════════════════════════════════════════════════════════

    /// <param name="TechId">解锁它的技术节点 id。取"已掌握的热源里温度最高的那个"即 MaxTemp。</param>
    public sealed record HeatSource(string TechId, string Name, double TempC, string Note);

    /// <summary>
    /// §1.4 的热源表，按可达温度升序。
    ///
    /// ⚠️ <c>coke</c> 那一行的 <c>TechId</c> 是 <b>null</b>：§1.4 说焦炭的解锁条件是
    /// "煤 + 干馏技术"，但 <c>tech_tree.md</c> 里<b>没有干馏节点</b>（实测 grep 无匹配）。
    /// 故焦炭在当前节点库下<b>不可达</b> —— 保留这一行是为了让缺口可见，
    /// 而不是假装 1600 °C 这条线已经通了。
    /// </summary>
    public static readonly HeatSource[] HeatSources =
    {
        new("fire_use",  "柴火（直接烧木）",  700, "掌握火即可"),
        new("charcoal",  "木炭",            1200, "不完全燃烧 —— 作者明说是「纯意外发现」，故 λ 低"),
        new("coal_fuel", "煤炭",            1400, "P2 采矿解锁"),
        new(null!,       "焦炭",            1600, "⚠️ §1.4 要求「煤 + 干馏」，但干馏节点尚未入库 → 当前不可达"),
    };

    /// <summary>已掌握的热源里最高的那个温度。没有任何热源时为 0（＝没有火，只能吃生的）。</summary>
    public static double MaxTempOf(IReadOnlyCollection<string> ownedTechs)
    {
        double best = 0;
        foreach (var h in HeatSources)
            if (h.TechId != null && ownedTechs.Contains(h.TechId) && h.TempC > best)
                best = h.TempC;
        return best;
    }

    // ══════════════════════════════════════════════════════════════════
    //  §1.4 材料：熔点 与 工艺温度
    // ══════════════════════════════════════════════════════════════════

    /// <param name="Tier">
    /// §4.1 的 <c>MaterialTier</c>：0=木石, 1=陶, 2=铜, 3=青铜, 4=铁。
    /// 铁之后（钢/合金/半导体…）本表暂不收 —— 那些由 <c>tech_tree.md</c> 的节点而非熔点决定。
    /// </param>
    public sealed record Material(
        int Tier, string Name,
        double MeltPointC,   // 物理熔点
        double ProcessTempC, // 实际工艺所需温度（见下方 ⚠️）
        string[] OreGeo);    // 需要境内有的矿产（§5.1 EnvRequirement）

    /// <summary>
    /// §1.4 的材料表。
    ///
    /// <b>⚠️⚠️ 这里有一处设计文档内部的真矛盾，必须说清楚（不是我偷改数）：</b>
    ///
    /// §1.4 有<b>两张</b>表，放在一起读会自相矛盾：
    ///   · 材料表说：铁（<b>1538 °C</b>）用<b>木炭即可</b> ✅
    ///   · 热源表说：木炭只能到 <b>~1200 °C</b>
    /// 1200 &lt; 1538 —— 于是按字面读，<b>铁永远炼不出来</b>，
    /// INV-13（<c>MaterialTier ≤ max{m : 熔点(m) ≤ MaxTemp}</c>）也就<b>永远无法满足</b>。
    ///
    /// <b>成因</b>：§1.4 那张表的表头写的正是「熔点/<b>工艺温度</b>」—— 它混用了两个概念。
    /// 现实中的块炼铁（bloomery）<b>本来就不把铁熔化成水</b>：它在 ~1200 °C 还原出海绵状的
    /// 固体"坯铁"，再反复锻打挤出渣。所以炼铁<b>不需要</b> 1538 °C，只需要约 1200 °C。
    ///
    /// <b>处理</b>：本表把两个数<b>分开存</b>（<see cref="MeltPointC"/> / <see cref="ProcessTempC"/>），
    /// 门槛判定一律走 <see cref="ProcessTempC"/>，熔点只作展示。
    /// 这样既守住了"木炭能炼铁"这条作者要的因果（铁的 <c>ProcessTempC</c> = 1200 ≤ 木炭的 1200），
    /// 也不用把 1538 这个真实的物理常数改掉。
    ///
    /// <b>连带的措辞问题（待裁）</b>：INV-13 与 §4.2 ② 写的是"熔点(m) ≤ MaxTemp"，
    /// 按字面用熔点判定则铁不可达。<b>建议把两处措辞改为"工艺温度"</b>，留作者裁定。
    /// </summary>
    public static readonly Material[] Materials =
    {
        // tier  名称    熔点    工艺温度   所需矿产
        new(0, "木石",      0,    0,   Array.Empty<string>()),
        new(1, "陶器",    900,  900,   new[] { "黏土" }),
        new(2, "铜",     1085, 1085,   new[] { "铜矿" }),
        new(3, "青铜",    950,  950,   new[] { "铜矿", "锡矿" }),   // 青铜熔点比铜低 → 故青铜时代早于铁器时代
        new(4, "铁",     1538, 1200,   new[] { "铁矿石" }),        // ← 1200 见上方长注释：块炼铁不熔融
    };

    /// <summary>
    /// §4.2 ②：<c>MaterialTier ← max{ m : 工艺温度(m) ≤ MaxTemp 且 可采储量(m) &gt; 0 }</c>。
    /// </summary>
    /// <param name="maxTempC">当前可达工艺温度（<see cref="MaxTempOf"/>）。</param>
    /// <param name="availableGeo">该部落境内实有的矿产/原料。</param>
    public static int MaterialTierOf(double maxTempC, IReadOnlyCollection<string> availableGeo)
    {
        int best = 0;
        foreach (var m in Materials)
        {
            if (m.ProcessTempC > maxTempC) continue;
            if (!m.OreGeo.All(availableGeo.Contains)) continue;   // 本地没这矿，炼不出来
            if (m.Tier > best) best = m.Tier;
        }
        return best;
    }

    /// <summary>某材料等级的展示名（0→木石 … 4→铁）。</summary>
    public static string MaterialName(int tier)
    {
        foreach (var m in Materials) if (m.Tier == tier) return m.Name;
        return $"tier{tier}";
    }

    // ══════════════════════════════════════════════════════════════════
    //  §4.4 资源开采深度分层：4000 m 是人类硬极限
    // ══════════════════════════════════════════════════════════════════

    /// <param name="HumanFactor">人类作业时的可采系数（0＝挖不了）。</param>
    /// <param name="RobotFactor">机器人作业时的可采系数。</param>
    /// <param name="RockTempC">该深度带的岩石温度（按 ~25 °C/km 地温梯度估）。</param>
    public sealed record DepthBand(string Name, int DepthFromM, int DepthToM, double HumanFactor, double RobotFactor);

    /// <summary>
    /// §4.4 的深度分层表。<b>"4000 米"是作者点名的硬极限</b> ——
    /// 那里岩石温度 ≈ 地表 + 100 °C，叠加岩爆、突水、有毒气体、缺氧。
    /// 掌握 <c>deep_mining_robot</c> 前，4000 m 以深的可采系数<b>恒为 0</b>（INV-20）；
    /// 掌握之后该上限消失，可采储量出现阶跃上升（INV-21，Δ ≥ 50%）。
    /// </summary>
    public static readonly DepthBand[] DepthBands =
    {
        new("露天",   0,    50, 1.00, 1.00),
        new("浅井",   50,   500, 0.90, 1.00),
        new("深井",   500,  2000, 0.60, 1.00),
        new("超深",   2000, 4000, 0.25, 1.00),
        new("极深",   4000, int.MaxValue, 0.00, 1.00),   // ← 人类硬极限
    };

    /// <summary>§4.4：4000 m。人类作业的硬极限深度。</summary>
    public const int HumanDepthLimitM = 4000;

    /// <summary>解锁深地机器人的节点 id（§4.4 / INV-20/21/22）。</summary>
    public const string DeepMiningRobotTech = "deep_mining_robot";

    /// <summary>
    /// 给定深度与"是否掌握机器人采矿"，返回可采系数。
    /// 这就是 §4.4 那句 <c>可采系数(depth, 技术)</c>。
    /// </summary>
    public static double ExtractabilityOf(int depthM, bool hasRobot)
    {
        foreach (var b in DepthBands)
            if (depthM >= b.DepthFromM && depthM < b.DepthToM)
                return hasRobot ? b.RobotFactor : b.HumanFactor;
        return 0;
    }

    // ══════════════════════════════════════════════════════════════════
    //  §4.5 P3 主轴：人均年能量通量的半定量标度
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 文明阶段的能量锚点（kWh/人·年，含分摊的间接能耗）。
    ///
    /// ⚠️ 作者在 §4.5 特意澄清过：主轴量的是<b>支配</b>而不是<b>消耗</b> ——
    /// "马拉松传讯耗 3 kWh、短信耗 ≈0"恰恰是支配能量上升的<b>结果</b>，不是反例。
    /// 故 <c>EnergyPerCapita</c> 必须记该部落可支配的全部能量通量
    /// （自身代谢 + 畜力 + 燃料 + 电力 + 分摊的基础设施）。
    ///
    /// 跨度约 45 倍且在对数轴上近乎等距，故 §10 的着色与排行榜用 log₁₀ 刻度。
    /// </summary>
    public sealed record EnergyAnchor(string Stage, double KwhPerCapitaYear);

    public static readonly EnergyAnchor[] EnergyAnchors =
    {
        new("狩猎采集",     2_000),
        new("原始农业",     4_000),
        new("传统帝国",     8_000),
        new("早期工业",    20_000),
        new("工业社会",    50_000),
        new("现代信息社会", 90_000),
    };

    /// <summary>核能（§5.2 T6："P3 → ∞"，没有日照也能产能）—— 越过信息社会锚点的下一档。</summary>
    public const double NuclearEnergyAnchor = 300_000;

    /// <summary>深地机器人（§4.4）对能源的连带拉动：P2 阶跃 → 连带拉动 P3。</summary>
    public const double DeepMiningEnergyBoost = 1.25;

    /// <summary>§10 的着色建议：log₁₀ 刻度，免得"工业→信息"这一段被压扁。</summary>
    public static double LogScore(double energyPerCapita)
        => Math.Log10(Math.Max(energyPerCapita, 1.0));

    // ══════════════════════════════════════════════════════════════════
    //  §4.3 P6 → P4 负反馈
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// §4.3：<c>生育率 = BaseRate × 生存压力因子 × (1 − LifeQuality/100 × β) × 制度因子</c>。
    /// 生活体验越高，生育率越低 —— 这条反直觉动态是作者自己点出的，
    /// 也是"文明自然走向机器替代人力"的那条链。
    /// </summary>
    public const double FertilityFeedbackBeta = 0.6;

    /// <summary>更替水平（每个女性一生生育数）。低于它人口长期萎缩。</summary>
    public const double ReplacementFertility = 2.1;

    // ══════════════════════════════════════════════════════════════════
    //  §4.7 抗逆力
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 提升抗逆力的节点（§4.7 表）。§4.7 特意说过这本是"已经在 tech_tree.md 里躺着的"，
    /// 故是白送的 —— 不需要为它新增任何节点。
    ///
    /// ⚠️ <b>但 §4.7 那张表有两行在节点库里找不到，必须记一笔</b>：
    ///   · 「<c>medicine</c> 医疗 → 抵御瘟疫」—— <c>tech_tree.md</c> <b>没有医疗节点</b>
    ///     （实测 grep "cn: .*医" 无匹配）。故瘟疫这一路抗逆力<b>当前无来源</b>。
    ///   · 「货币」在节点库里叫 <c>money</c> 而不是 <c>currency</c>，此处已按实名引用。
    /// 前者是缺口而非笔误，与 <see cref="TechEffects.MissingNodes"/> 记的是同一件事。
    /// </summary>
    public static readonly (string TechId, double Delta, string Why)[] ShockAbsorptionSources =
    {
        ("granary",     0.12, "公仓：粮食储备 → 抵御饥荒"),
        ("cured_food",  0.08, "腌制防腐：同上"),
        ("trade_barter",0.06, "以物易物：贸易伙伴数 = 风险分散度"),
        ("money",       0.10, "货币：贸易半径进一步扩大"),
        ("market",      0.05, "集市：同上"),
        ("writing",     0.05, "文字：知识不随人死而失，灾后重建更快"),
        ("mudbrick",    0.03, "土坯房：比窝棚更抗风雪"),
        ("animal_herd", 0.06, "驯化物种多样性：§4.7「单作物歉收不致命」—— 牧群是活的风险分散"),
        // ⚠️ 缺 medicine —— 见上方注释。§4.7 的"抵御瘟疫"一项目前落不了地。
    };

    /// <summary>抗逆力上限（INV-28 要求 ∈ [0,1]）。</summary>
    public const double MaxShockAbsorption = 0.85;

    /// <summary>§4.7：<c>存活人口 = Pop × exp(−冲击强度 × (1 − ShockAbsorption))</c>。</summary>
    public static double SurvivingPopulation(double pop, double shockIntensity, double shockAbsorption)
        => pop * Math.Exp(-shockIntensity * (1.0 - shockAbsorption));

    /// <summary>§4.7：<c>知识保有率 = KR × (1 − 冲击强度 × (1 − SA) × 0.5)</c>。</summary>
    public static double SurvivingRetention(double retention, double shockIntensity, double shockAbsorption)
        => retention * Math.Max(0.0, 1.0 - shockIntensity * (1.0 - shockAbsorption) * 0.5);
}
