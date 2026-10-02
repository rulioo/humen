namespace Humen.Core;

/// <summary>
/// 冰川旋回 —— design.md §6.2。M2 只需要其中的<b>海平面项</b>
/// （球体要把海水画出来就必须知道当年海平面在哪），
/// 完整的温度/植被耦合属 M4，碳-气候耦合（§4.6）属 M7 之后。
///
/// <code>
/// IceVolume(year)        = 偏斜波（慢积累 / 快消融）+ 混沌噪声      ← 见下方 ⚠️
/// GlobalTempOffset(year) = −8°C × IceVolume(year) + CarbonWarming(Σ CarbonLoad)
/// SeaLevelOffset(year)   = −120 m × IceVolume(year) + 热膨胀项
/// </code>
///
/// ⚠️ <b>波形用偏斜而非 §6.2 原文的正弦，这是 M2 的一处刻意改动，理由可验证。</b>
///
/// 纯正弦做不到本项目最需要的一件事：<b>让「今天」落在间冰期里</b>。
/// 原因不是参数没调好，而是几何上的：正弦的冷暖峰恒隔半个周期（5 万年），
/// 而真实的末次冰盛期（−21 000）与全新世暖期起点（−11 700）只隔 <b>9 300 年</b> ——
/// 差了 5 倍多，任何对称波形都摆不下。照正弦走，模型会把末次冰盛期
/// <b>放到公元 25000 年</b>（未来），当天海平面 −68 m、全球降温 4.5 ℃：
/// 这在「按真实历史做预测外推」的项目里是致命的 —— 白令陆桥、农业起源、海岸线全错位。
///
/// 真实的冰期旋回本就是锯齿形：冰盖用约 10 万年缓慢积累、再用约 1 万年快速崩解
/// （termination）。故 <see cref="IceVolume"/> 改为「smoothstep 慢积累 + smoothstep 快消融」，
/// 并把三个真实年代钉死为锚点：
///
/// | 锚点 | 年代 | 模型冰量 |
/// |---|---|---|
/// | 末次冰盛期 LGM | 公元前 21 000 | 1.000 |
/// | 全新世暖期起点 | 公元前 11 700 | 0.000 |
/// | Eemian 间冰期最暖 | 公元前 125 000 | 0.000 |
/// | 今天 | 公元 2025 | 0.051 |
///
/// 周期随之由 10 万改为 <b>113 300</b>（末次两个暖峰的真实间隔），
/// 详见 <see cref="Period"/> —— 用 10 万的取整值会让 Eemian 落到冷峰上，与 §6.2 自己的
/// 「Eemian 最暖」打架。
///
/// <b>代价（如实记录）</b>：钉住近代之后，§6.2 正文那四个古期次的拟合变差
/// （MIS 6/7/8 漂 20 余千年）。但那份清单本身就自相矛盾：冷期隔 11 万年、暖期隔 9 万年，
/// 而任何定周期波形的冷暖峰间距恒定，不可能同时命中。取舍的理由见 <see cref="Period"/>。
/// 作者要改回正弦：把 <see cref="IceVolume"/> 换成 <see cref="IceVolumeSinusoid"/> 即可，
/// 海平面与温度自动跟随。两条曲线的锚点对照由 <see cref="CompareWaveforms"/> 打印。
/// </summary>
public static class GlacialCycle
{
    /// <summary>
    /// 冰期周期（年）。<b>113 300</b> —— 由两个真实冰消期定出，不是 §6.2 的 100 000。
    ///
    /// §6.2 写的是「地球冰期周期 ≈ 10 万年」；本类注释的四个期次拟合当初也是按 10 万写的。
    /// 但 10 万这个取整值<b>会让 Eemian 落到冷峰上</b>：以全新世暖峰（−11 700）为 0 往前推，
    /// 10 万年的整数倍依次是 −111 700、−211 700，而 −125 000 距 −111 700 有 13 300 年 ——
    /// 恰好越过冷峰。于是模型会宣称「Eemian 是最冷的」，与 §6.2 正文
    /// 「Eemian 间冰期（约 12.5 万年前）最暖」直接打脸。
    ///
    /// 真实的末次两个暖峰相隔就是 113 300 年（125 000 − 11 700）。
    /// 用它，则 Eemian 与全新世两个锚点<b>同时精确命中</b>，末次冰盛期也随之精确到 −21 000。
    /// 代价是更早的期次（MIS 6/7/8）会漂 20 余千年 —— 但这几期在真实地质记录里
    /// 本就长短不一（MIS 8→7 只隔 6.5 万年），任何定周期波形都拟合不了；
    /// 而对本项目（3 亿年跨度里只需要知道海平面与陆桥何时在哪）而言，
    /// 最近两个冰消期精确远比 MIS 6 的绝对值重要。
    /// </summary>
    public const double Period = 113_300.0;

    /// <summary>末次冰盛期（LGM）：公元前 21 000 年。真实值，也是白令陆桥存在的年代。</summary>
    public const double LastGlacialMaxYear = -21_000.0;

    /// <summary>Eemian 间冰期最暖：公元前 125 000 年。§6.2 正文指定的「最暖」锚点。</summary>
    public const double EemianYear = -125_000.0;

    /// <summary>由「Eemian 最暖」反解出的相位，见类注释。仅供 <see cref="IceVolumeSinusoid"/> 使用。</summary>
    public const double Phase = -100_000.0;

    /// <summary>
    /// 最近一次消融结束、即全新世暖期起点：<b>公元前 11700 年</b>（真实值）。
    /// 冰量的零点就定在这里。
    /// </summary>
    public const double TerminationYear = -11_700.0;

    /// <summary>
    /// 一个周期里用于「快速消融」的比例 = 9 300 / 113 300。
    /// 分子由「末次冰盛期 −21 000、全新世起点 −11 700」两个真实年代之差定出（21 000 − 11 700），
    /// 不是调出来的。它同时保证冷峰精确落在 −21 000。
    /// </summary>
    public const double MeltFraction = 9_300.0 / Period;

    /// <summary>冰盛期海平面下降量（米）。§6.2。</summary>
    public const double MaxSeaLevelDropM = 120.0;

    /// <summary>冰盛期全球降温幅度（℃）。§6.2。</summary>
    public const double MaxTempDropC = 8.0;

    /// <summary>混沌扰动幅度上限（叠加在 [0,1] 的 IceVolume 上）。</summary>
    private const double ChaosAmp = 0.06;

    /// <summary>
    /// §6.2 的「混沌噪声」。<b>不能用白噪声</b> —— 海平面逐年跳变会让
    /// 海岸线闪烁、部落海拔判定抖动。故取三条真实<b>米兰科维奇</b>边频
    /// （黄赤交角 41 ka、岁差 23 ka / 19 ka），它们与主周期 100 ka 不可通约，
    /// 叠加后曲线上每个旋回都不完全相同 —— 这正是「混沌」想要的效果，
    /// 却仍然是 <c>year</c> 的光滑函数，且完全确定（不含随机数）。
    /// </summary>
    private static double Chaos(double year, long seed)
    {
        Span<double> periods = stackalloc double[] { 41_000, 23_000, 19_000 };

        double sum = 0;
        for (int k = 0; k < periods.Length; k++)
        {
            // 相位由种子决定：换种子 = 换一颗蓝星，但同一颗星上各旋回形态固定
            double phase = Hashing.ToRange(Hashing.Hash64(seed, HashDomain.Birth, 900 + k, 0, 0), 0, Math.Tau);
            sum += Math.Sin(Math.Tau * year / periods[k] + phase) / (k + 1.0);
        }
        return ChaosAmp * sum * (1.0 / (1.0 + 1.0 / 2 + 1.0 / 3));
    }

    /// <summary>
    /// 冰量，[0, 1]。<b>0 = 今日（全新世暖期平台）</b>，1 = 冰盛期（海平面 −120 m）。
    ///
    /// ⚠️ <b>波形是偏斜的（慢积累 / 快消融），不是正弦 —— 这是刻意的改动，理由是可验证的。</b>
    ///
    /// §6.2 的纯正弦把相位钉在「Eemian 最暖」上，于是最近的一个冷峰落在
    /// <c>year = +25 000</c>：<b>末次冰盛期被放到了未来</b>。而真实的末次冰盛期在
    /// <b>21 000 年前</b>（正是白令陆桥存在、海平面 −120 m 的那个时候），
    /// 全新世暖期则始于 <b>11 700 年前</b>。二者只隔 9 300 年 —— 远小于半个周期，
    /// <b>任何 100 ka 的对称波形都做不到</b>：对称波形里冷暖峰恒隔 50 ka。
    ///
    /// 真实的冰期旋回本就是锯齿形：冰盖用约 9 万年缓慢积累，再用约 1 万年快速崩解
    /// （这就是「termination」一词的由来）。故这里改用偏斜波形，
    /// 并把两个真正的锚点钉死：<b>消融结束（暖峰）= 公元前 11700 年</b>、
    /// <b>冰盛期 = 公元前 21000 年</b>（由 <see cref="MeltFraction"/> = 9300/100000 自动导出）。
    ///
    /// <b>代价（如实记录）</b>：钉住近代之后，§6.2 正文那四个古期次的拟合会变差 ——
    /// 因为那四个数字彼此就不自洽（冷期隔 110 ka、暖期隔 90 ka，而定周期波形的冷暖峰间距恒定）。
    /// 用 <see cref="CompareWaveforms"/> 可以两条曲线的偏差表一起打出来对比。
    /// 对本项目而言这个取舍是划算的：模拟的主线是「30 万年前 → 2126 年」，
    /// 末次冰盛期与全新世的位置直接决定海平面、陆桥与农业起源的年代，
    /// 而 MIS 6 / MIS 8 的具体年代对文明演化几乎无影响。
    /// 作者若要改回正弦，把本方法的实现换成 <see cref="IceVolumeSinusoid"/> 即可，其余全自动跟随。
    /// </summary>
    public static double IceVolume(double year, long seed)
    {
        // u：以「消融结束（暖峰）」为 0 的周期内位置
        double u = Frac((year - TerminationYear) / Period);

        // 冰盖用 1 − MeltFraction 的时间缓慢积累，再用 MeltFraction 的时间快速崩解。
        // 两段都用 smoothstep：端部导数为 0，故暖峰与冷峰都是圆滑的平台而非尖角
        // （尖角会让海平面在峰值附近逐年抖动，海岸线跟着闪）。
        double ice = u < 1.0 - MeltFraction
            ? Smoothstep(u / (1.0 - MeltFraction))                        // 缓慢积累 0 → 1
            : 1.0 - Smoothstep((u - (1.0 - MeltFraction)) / MeltFraction); // 快速消融 1 → 0

        return Math.Clamp(ice + Chaos(year, seed), 0.0, 1.0);
    }

    /// <summary>
    /// §6.2 原文的纯正弦版本，保留供对照（见 <see cref="IceVolume"/> 注释）。
    /// 用<b>它自己的</b> <see cref="SinusoidPeriod"/> 而不是 <see cref="Period"/> ——
    /// 否则改了偏斜波形的周期，这条对照曲线会跟着变，「对照」就失去意义了。
    /// </summary>
    public static double IceVolumeSinusoid(double year, long seed)
    {
        double v = 0.5 + 0.5 * Math.Sin(Math.Tau * (year - Phase) / SinusoidPeriod) + Chaos(year, seed);
        return Math.Clamp(v, 0.0, 1.0);
    }

    /// <summary>§6.2 原文写的周期：10 万年。仅用于 <see cref="IceVolumeSinusoid"/>。</summary>
    public const double SinusoidPeriod = 100_000.0;

    private static double Smoothstep(double x)
    {
        x = Math.Clamp(x, 0.0, 1.0);
        return x * x * (3.0 - 2.0 * x);
    }

    private static double Frac(double x) => x - Math.Floor(x);

    /// <summary>
    /// 海平面相对今日的偏移（米），恒 ≤ 0。
    /// <paramref name="thermalExpansionM"/> 是 §6.2 的「热膨胀项」，
    /// 由 §4.6 的 <c>CarbonWarming</c> 驱动；M2 阶段恒为 0（碳模型尚未接入）。
    /// </summary>
    public static double SeaLevelOffsetM(double year, long seed, double thermalExpansionM = 0.0)
        => -MaxSeaLevelDropM * IceVolume(year, seed) + thermalExpansionM;

    /// <summary>
    /// 全球温度偏移（℃）。<paramref name="carbonWarmingC"/> 来自 §4.6 的
    /// <c>CarbonWarming(Σ CarbonLoad)</c>，M2 阶段为 0。
    /// </summary>
    public static double GlobalTempOffsetC(double year, long seed, double carbonWarmingC = 0.0)
        => -MaxTempDropC * IceVolume(year, seed) + carbonWarmingC;

    /// <summary>全程最低海平面（米）—— 即 INV-8 要检验的最坏情况。</summary>
    public static double LowestSeaLevelM() => -MaxSeaLevelDropM;

    /// <summary>§6.2 正文列出的四个古期次（真实年代，单位：年）。</summary>
    public static readonly (string Era, double Year, bool Cold)[] DocEras =
    {
        ("MIS 9/8 冰期",  -270_000, true),
        ("MIS 7 间冰期",  -215_000, false),
        ("MIS 6 冰期",    -160_000, true),
        ("Eemian 间冰期", -125_000, false),
    };

    /// <summary>
    /// 自检：把模型反解出的冷暖峰与 §6.2 正文列出的实际年代对表，
    /// 返回 (期次, 模型年, 文档年, 偏差年)。供验收打印，不参与模拟。
    /// </summary>
    public static List<(string Era, double ModelYear, double DocYear, double DeltaKy)>
        VerifyAgainstDoc(long seed, bool sinusoid = false)
    {
        var res = new List<(string, double, double, double)>();

        if (sinusoid)
        {
            // 正弦：sin = ±1 ⇒ year = phase ± 25000 + 100000·k
            for (int k = -3; k <= 0; k++)
            {
                res.Add(("冷峰", Phase + SinusoidPeriod / 4 + SinusoidPeriod * k, 0, 0));
                res.Add(("暖峰", Phase - SinusoidPeriod / 4 + SinusoidPeriod * k, 0, 0));
            }
        }
        else
        {
            // 偏斜：暖峰在消融结束处，冷峰在其后 (1 − MeltFraction) 个周期
            for (int k = -3; k <= 0; k++)
            {
                res.Add(("暖峰", TerminationYear + Period * k, 0, 0));
                res.Add(("冷峰", TerminationYear + (1.0 - MeltFraction) * Period + Period * k, 0, 0));
            }
        }

        var outp = new List<(string, double, double, double)>();
        foreach (var d in DocEras)
        {
            double best = double.MaxValue;
            foreach (var r in res)
            {
                if ((r.Item1 == "冷峰") != d.Cold) continue;
                if (Math.Abs(r.Item2 - d.Year) < Math.Abs(best)) best = r.Item2 - d.Year;
            }
            if (best != double.MaxValue) outp.Add((d.Era, d.Year + best, d.Year, best / 1000.0));
        }
        return outp;
    }

    /// <summary>
    /// 两条波形在几个关键锚点上的对照，供作者权衡（见 <see cref="IceVolume"/> 注释）。
    /// 返回 (锚点, 偏斜模型冰量, 正弦模型冰量, 真实值)。
    /// </summary>
    public static List<(string Anchor, double Skewed, double Sinusoid, string Real)>
        CompareWaveforms(long seed)
    {
        var anchors = new (string Name, double Year, string Real)[]
        {
            ("末次冰盛期 −21000", -21_000, "冰量 ≈ 1（海平面 −120 m）"),
            ("全新世起点 −11700", -11_700, "冰量 ≈ 0（暖期平台起点）"),
            ("Eemian 最暖 −125000", -125_000, "冰量 ≈ 0（间冰期）"),
            ("今天 +2025", 2025, "冰量 ≈ 0（仍在间冰期）"),
        };

        var outp = new List<(string, double, double, string)>();
        foreach (var a in anchors)
            outp.Add((a.Name, IceVolume(a.Year, seed), IceVolumeSinusoid(a.Year, seed), a.Real));
        return outp;
    }
}
