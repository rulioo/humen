namespace Humen.Core;

/// <summary>
/// 土壤分层 —— design.md §7.8（R10）。
///
/// <code>
/// O 层 腐殖质   0 ~ 0.1 m
/// A 层 表土     0.1 ~ 0.3 m   ← 有机质富集，农业依赖此层
/// B 层 心土     0.3 ~ 1.0 m
/// C 层 母质     1.0 ~ 3.0 m   ← 矿产来源
/// R 层 基岩     3.0 m +
/// </code>
///
/// §7.8 只给了厚度区间，没给成土函数。这里补一个：<b>厚薄由气候与坡度决定</b> ——
/// 成土是「生物把矿物碎屑改造成土壤」的过程，故需要
/// <b>够暖</b>（有生物活动）、<b>够湿</b>（有淋溶与搬运）但不至于把养分冲光、
/// 以及<b>够平</b>（陡坡上的土会被冲走、留不住）。三者缺一，A 层就薄。
///
/// 两个下游用途（§7.8 原文指定的）：
///   <list type="bullet">
///     <item><b>A 层厚度 → 农业产能因子</b>（<see cref="SoilProfile.AgricultureFactor"/>）。
///           这是 §6.1 农业禀赋真正落地的地方。</item>
///     <item><b>C / R 层 → 矿产可达性</b>（§4.4 的分层开采，M12 才接线）。</item>
///   </list>
///
/// ⚠️ 本类<b>不是</b> §6.1 那张表的替代品。§6.1 按大陆给的 ✅最优 / ⚠️受限 / ❌不可
/// 是作者的裁定（例如「大陆 3 赤道雨旱季 → 采集 → 早期农业受限」），
/// 这里是按<b>点位</b>算的实测值，两者应当同向；若不同向，是数据要复核，不是谁覆盖谁。
/// </summary>
public static class Soil
{
    // ── §7.8 表里的五个层位厚度区间（m）。基准值取中位，实际值由成土函数缩放 ──

    /// <summary>O 层（腐殖质）基准厚度。</summary>
    public const double BaseOrganicM = 0.10;

    /// <summary>A 层（表土）基准厚度。§7.8 给的是 0.1 ~ 0.3 m，取上沿 —— 那是「好土」的定义。</summary>
    public const double BaseTopsoilM = 0.30;

    /// <summary>B 层（心土）基准厚度。</summary>
    public const double BaseSubsoilM = 0.70;

    /// <summary>C 层（母质）基准厚度。</summary>
    public const double BaseParentM = 2.00;

    /// <summary>R 层（基岩）起点：§7.8 的 3.0 m。</summary>
    public const double BedrockTopM = 3.0;

    // ── 农业判据的阈值 ──

    /// <summary>表土厚度达到此值即视为「土层不再是限制因子」（m）。</summary>
    public const double TopsoilSaturatingM = 0.35;

    /// <summary>农业可行的最冷月气温下限（℃）：低于 −12 ℃ 越冬作物无法存活。</summary>
    public const double AgricultureFrostMinC = -12.0;

    /// <summary>农业可行的最冷月气温上限侧（℃）：到 +6 ℃ 已完全不受霜冻限制。</summary>
    public const double AgricultureFrostMaxC = 6.0;

    /// <summary>雨养农业的降水下沿（mm）：低于 150 mm 无灌溉就不能耕作。</summary>
    public const double AgricultureRainMinMm = 150.0;

    /// <summary>雨养农业的降水「够用」线（mm）。</summary>
    public const double AgricultureRainGoodMm = 450.0;

    /// <summary>土壤名称表（按 <see cref="SoilTypeName"/> 的分类）。</summary>
    public static string SoilTypeName(SoilKind kind) => kind switch
    {
        SoilKind.Bedrock => "裸岩",
        SoilKind.Permafrost => "永冻土",
        SoilKind.Podzol => "灰化土",
        SoilKind.Chernozem => "黑钙土",
        SoilKind.Kastanozem => "栗钙土",
        SoilKind.BrownEarth => "棕壤",
        SoilKind.Laterite => "砖红壤",
        SoilKind.RedEarth => "红壤",
        SoilKind.Desert => "漠土",
        _ => "未知",
    };

    /// <summary>
    /// 成土：由气候与坡度算出一份土壤剖面。
    /// </summary>
    /// <param name="tempC">年均温（℃）。</param>
    /// <param name="rainMm">年降水（mm）。</param>
    /// <param name="coldestMonthC">最冷月气温（℃）。农业因子要用它判霜冻。</param>
    /// <param name="slopeDeg">坡度（°）。</param>
    /// <param name="biome">群系码（<see cref="Climate"/>）。冰盖直接不出土。</param>
    public static SoilProfile Profile(
        double tempC, double rainMm, double coldestMonthC, double slopeDeg, byte biome)
    {
        // 冰盖：终年压在冰下，没有成土过程。宁可显式零掉，也别给一个「很少」的假数 ——
        // 否则 §6.1 的寒带大陆会算出一个非零的农业因子，与 INV-15 直接冲突。
        if (biome == Climate.IceCap || tempC < ClimateFrozenC)
            return new SoilProfile(0, 0, 0, 0, SoilKind.Bedrock, 0);

        double climateFactor = FormationFactor(tempC, rainMm);
        double slopeFactor = SlopeRetention(slopeDeg);

        double organic = Clamp(BaseOrganicM * climateFactor * slopeFactor, 0, 0.25);
        double topsoil = Clamp(BaseTopsoilM * climateFactor * slopeFactor, 0.005, 0.60);
        // B/C 层对气候没那么敏感：它们主要是母岩风化的产物，
        // 气候影响的是「风化了多少」，而这是随时间积累的，不会归零。
        double subsoil = Clamp(BaseSubsoilM * (0.35 + 0.65 * climateFactor) * slopeFactor, 0.03, 1.00);
        double parent = Clamp(BaseParentM * (0.45 + 0.55 * climateFactor), 0.20, 3.00);

        var kind = Classify(tempC, rainMm, biome);
        double agri = AgricultureFactor(topsoil, coldestMonthC, rainMm, kind);

        return new SoilProfile(organic, topsoil, subsoil, parent, kind, agri);
    }

    /// <summary>年均温低于此值视为永久冻土，成土停摆（℃）。</summary>
    public const double ClimateFrozenC = -8.0;

    /// <summary>
    /// 成土的气候因子 ∈ [0,1]：够暖 × 够湿 × 不被淋溶。
    ///
    /// 淋溶一项是热带的要害：降水超过 2000 mm 之后，可溶的盐基与腐殖质被冲走、
    /// 铁铝氧化物富集，土反而<b>贫瘠</b>（砖红壤）。所以气候因子不能只写成
    /// 「越暖越湿越好」的单调函数 —— 那条曲线会让热带雨林成为全球最肥的土，
    /// 而真实历史恰恰相反：农业起源于温带草原的黑钙土与河谷冲积土，
    /// 不是赤道雨林。这个反直觉的结果是有据可依的，别把它当 bug 修掉。
    /// </summary>
    public static double FormationFactor(double tempC, double rainMm)
    {
        double warm = Smooth01(tempC, -5.0, 12.0);
        double wet = Smooth01(rainMm, 80.0, 600.0);
        double leached = 1.0 - 0.45 * Smooth01(rainMm, 2000.0, 3600.0);
        return warm * wet * leached;
    }

    /// <summary>
    /// 坡度保土因子 ∈ [0,1]：缓坡留得住土，陡坡上的土被冲进河里。
    /// 8° 以内不罚，30° 以上基本留不住。
    /// </summary>
    public static double SlopeRetention(double slopeDeg)
        => 1.0 - 0.92 * Smooth01(slopeDeg, 8.0, 30.0);

    /// <summary>
    /// 农业产能因子 ∈ [0,1]。§7.8：<b>A 层厚度成为农业产能因子</b>。
    ///
    /// 三个乘子，各对应一条会一票否决的约束：
    ///   <list type="number">
    ///     <item><b>土层厚度</b> —— 没有表土就没有耕作；</item>
    ///     <item><b>最冷月气温</b> —— 这就是 INV-15「寒带大陆不得解锁任何农业变体」的
    ///           物理来源。判的是最冷月而不是年均温：年均温相同的两地，
    ///           海洋性气候的最冷月高得多、能越冬，大陆性的不行。</item>
    ///     <item><b>降水</b> —— 太干要灌溉（150 mm 以下雨养农业不存在），
    ///           太湿则淋溶贫瘠且难以开垦。</item>
    ///   </list>
    /// 相乘而不是取平均：任何一条不满足，农业都立不住，这是「与」不是「或」。
    /// </summary>
    public static double AgricultureFactor(
        double topsoilM, double coldestMonthC, double rainMm, SoilKind kind)
    {
        if (kind == SoilKind.Bedrock || kind == SoilKind.Permafrost) return 0.0;

        double depth = Math.Clamp(topsoilM / TopsoilSaturatingM, 0.0, 1.0);
        double frost = Smooth01(coldestMonthC, AgricultureFrostMinC, AgricultureFrostMaxC);
        double rain = Smooth01(rainMm, AgricultureRainMinMm, AgricultureRainGoodMm)
                    * (1.0 - 0.35 * Smooth01(rainMm, 2200.0, 3600.0));
        double soilBonus = kind == SoilKind.Chernozem ? 1.15 : 1.0;

        return Math.Clamp(depth * frost * rain * soilBonus, 0.0, 1.0);
    }

    /// <summary>土壤大类。名字见 <see cref="SoilTypeName"/>。</summary>
    public static SoilKind Classify(double tempC, double rainMm, byte biome) => biome switch
    {
        Climate.IceCap => SoilKind.Bedrock,
        Climate.PolarDesert => SoilKind.Permafrost,
        Climate.Tundra => SoilKind.Permafrost,
        Climate.Taiga => SoilKind.Podzol,
        Climate.Alpine => SoilKind.Bedrock,
        Climate.Desert => SoilKind.Desert,
        Climate.Steppe => SoilKind.Chernozem,      // 温带草原 = 黑钙土带（与北美大平原、乌克兰同型）
        Climate.Savanna => SoilKind.Kastanozem,
        Climate.TemperateForest or Climate.TemperateRainforest => SoilKind.BrownEarth,
        Climate.Mediterranean => SoilKind.Kastanozem,
        Climate.Rainforest or Climate.MonsoonForest => SoilKind.Laterite,
        Climate.MontaneRainforest or Climate.TropicalWoodland => SoilKind.RedEarth,
        _ => rainMm > 1600 ? SoilKind.Laterite : SoilKind.BrownEarth,
    };

    private static double Smooth01(double x, double a, double b)
    {
        double u = Math.Clamp((x - a) / (b - a), 0.0, 1.0);
        return u * u * (3.0 - 2.0 * u);
    }

    private static double Clamp(double v, double lo, double hi) => Math.Clamp(v, lo, hi);
}

/// <summary>土壤大类。名称见 <see cref="Soil.SoilTypeName"/>。</summary>
public enum SoilKind
{
    Bedrock = 0,      // 裸岩（冰盖 / 高山）
    Permafrost = 1,   // 永冻土
    Podzol = 2,       // 灰化土（针叶林）
    Chernozem = 3,    // 黑钙土（温带草原）—— 全球最宜农
    Kastanozem = 4,   // 栗钙土（干草原 / 地中海）
    BrownEarth = 5,   // 棕壤（温带落叶林）
    Laterite = 6,     // 砖红壤（热带雨林，淋溶贫瘠）
    RedEarth = 7,     // 红壤（热带山地 / 疏林）
    Desert = 8,       // 漠土
}

/// <summary>一份土壤剖面。厚度单位 m，<see cref="AgricultureFactor"/> ∈ [0,1]。</summary>
public sealed record SoilProfile(
    double OrganicM,        // O 层 腐殖质
    double TopsoilM,        // A 层 表土 ← 农业依赖此层
    double SubsoilM,        // B 层 心土
    double ParentM,         // C 层 母质 ← 矿产来源
    SoilKind Kind,
    double AgricultureFactor)
{
    /// <summary>土层名称（中文）。</summary>
    public string TypeName => Soil.SoilTypeName(Kind);

    /// <summary>剖面上界厚度：O + A + B + C 之后即 R 层基岩。</summary>
    public double ProfileDepthM => OrganicM + TopsoilM + SubsoilM + ParentM;
}
