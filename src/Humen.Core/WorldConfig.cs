namespace Humen.Core;

/// <summary>
/// 全局常量与可调参数。所有"魔数"集中在此，禁止散落到其它文件。
///
/// 依据：
///   design.md §9.1  时间轴（起点/终点/显示约定）
///   design.md §8.2  性别与年龄
///   design.md §7.2  大陆布局
///   design.md §5.4  初始技术集
///   id.md     §0    ID 发放窗口
///   req.txt  第 2~5、15 行（世界规模与起点，作者原文）
/// </summary>
public static class WorldConfig
{
    // ══════════════════════════════════════════════════════════════
    //  时间轴（design.md §9.1）
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 模拟起点 = 公元前 30 万年。<c>req.txt</c> 第 15 行原文，
    /// 对齐 Jebel Irhoud（摩洛哥，约 31.5 万年前，最早的智人化石）。
    /// </summary>
    public const double StartYear = -300_000.0;

    /// <summary>
    /// 模拟终点。⚠️ 注意 <b>显示约定</b>：
    /// 内部年 <c>y ≥ 0</c> → 显示「公元 (y+1) 年」；<c>y &lt; 0</c> → 显示「公元前 (-y) 年」。
    /// 故 <c>2125.0</c> 显示为「<b>公元 2126 年</b>」——写成 2126.0 会显示成公元 2127 年。
    /// 作者 2026-10-01 定案：「模拟就跑到 2126 年吧，够了，再远没意义了」。
    /// </summary>
    public const double EndYear = 2_125.0;

    /// <summary>全程跨度（年）。= EndYear − StartYear = 302,125</summary>
    public const double TotalSpanYears = EndYear - StartYear;

    // ══════════════════════════════════════════════════════════════
    //  世界规模（req.txt 第 2~4 行）
    // ══════════════════════════════════════════════════════════════

    public const int ContinentCount = 5;

    /// <summary>每大陆一级支流数（req.txt 第 1 行"每个河流有20个分支部落"的澄清结果，design.md §2.2）</summary>
    public const int TributariesPerRiver = 5;

    /// <summary>每条支流沿岸的部落数</summary>
    public const int TribesPerTributary = 4;

    /// <summary>每大陆部落数 = 5 × 4 = 20</summary>
    public const int TribesPerContinent = TributariesPerRiver * TribesPerTributary;

    /// <summary>全球部落数 = 5 × 20 = 100</summary>
    public const int TribeCount = ContinentCount * TribesPerContinent;

    /// <summary>每部落成员数（req.txt 第 4 行）</summary>
    public const int MembersPerTribe = 1000;

    /// <summary>元年成员总数 = 100 × 1000 = 100,000（req.txt 第 4 行）</summary>
    public const int GenesisMemberCount = TribeCount * MembersPerTribe;

    // ══════════════════════════════════════════════════════════════
    //  性别与年龄（req.txt 第 5 行；INV-3 / INV-6）
    // ══════════════════════════════════════════════════════════════

    /// <summary>每部落男性数。1000 × 1.05/2.05 = 512.19 → 512（INV-3 要求误差 0）</summary>
    public const int MalePerTribe = 512;

    /// <summary>每部落女性数 = 1000 − 512 = 488</summary>
    public const int FemalePerTribe = MembersPerTribe - MalePerTribe;

    public const int MinAge = 13;
    public const int MaxAge = 17;   // 含

    // ══════════════════════════════════════════════════════════════
    //  ID 发放窗口（id.md §0）
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 名义窗口起点 = 公元前 3000 年（作者定案）。
    /// ⚠️ 实际"某大陆第一个有档案的人"出现在该大陆 <c>writing</c> 节点首次被发明那一年，
    /// 见 <see cref="IdWindowNote"/>。窗口本身是硬编码的日历事实。
    /// </summary>
    public const double IdWindowStart = -3_000.0;

    /// <summary>窗口终点 = 模拟终点（v0.9 起两者重合，不存在"2126 年之后"）</summary>
    public const double IdWindowEnd = EndYear;

    /// <summary>
    /// L2 程序层派生性别时的偏置：P(男) = 512/1000 = 0.512 ≈ 1.05 : 1。
    /// ⚠️ 仅用于 L2 派生。<b>元年 10 万成员走配额路径</b>（严格 512/488，INV-3 误差 0）。
    /// </summary>
    public const int GenderBiasMilli = 512;

    /// <summary>供 UI/文档引用的说明文本</summary>
    public const string IdWindowNote =
        "窗口 [−3000, 2125] 硬编码；各大陆首个建档者出现在该大陆 writing 首次被发明之年。";

    // ══════════════════════════════════════════════════════════════
    //  初始技术集（design.md §5.4）
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 开局已掌握的技术（30 万年前人类已有）。
    /// <c>fire_making</c> <b>不在其中</b> —— 它是元年的第一个大发现，由发现引擎（§1.3）自发解锁。
    ///
    /// 规则：起点越早，本清单只会越长或不变，不会变短。
    /// 只有把起点前移到 <b>260 万年以前</b>（奥杜韦石器出现之前）才能置空。
    /// </summary>
    public static readonly string[] InitialTechSet = { "stone_flake", "fire_use" };

    // ══════════════════════════════════════════════════════════════
    //  默认种子
    // ══════════════════════════════════════════════════════════════

    /// <summary>默认世界种子。换种子 = 换一个蓝星，但同种子必须逐位可复现（INV-34/35）。</summary>
    public const long DefaultSeed = 20261001L;

    /// <summary>
    /// 「当代」= 公元 2025 年，本书叙事里的「今天」。
    /// 气候验收（§13）拿它当基准态：冰量 ≈ 0、海平面偏移 ≈ 0、全球温度偏移 ≈ 0。
    /// </summary>
    public const double PresentYear = 2025.0;

    // ══════════════════════════════════════════════════════════════
    //  派生哈希的子域标签（避免不同用途的哈希互相干扰）
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 把内部年转成人类可读纪年。<b>无公元 0 年</b>。
    ///
    /// ⚠️ 必须先挡 NaN/∞：L2 程序层在人口史尚未跑出来时（M1）出生年是 <c>NaN</c>，
    /// 而 <c>(long)Math.Floor(NaN)</c> 在 C# 里是未定义转换，会静默变成
    /// <c>long.MinValue</c> —— 显示成「公元前 -9,223,372,036,854,775,808 年」。
    /// </summary>
    public static string FormatYear(double year)
    {
        if (double.IsNaN(year) || double.IsInfinity(year)) return "年代未知";
        long y = (long)Math.Floor(year);
        return y >= 0 ? $"公元 {y + 1:N0} 年" : $"公元前 {-y:N0} 年";
    }
}
