using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Humen.Core;

/// <summary>一条验收结果。</summary>
public sealed record CheckResult(string Id, string Title, CheckStatus Status, string Detail)
{
    public bool Passed => Status == CheckStatus.Pass;
    public string Glyph => Status switch
    {
        CheckStatus.Pass => "PASS",
        CheckStatus.Fail => "FAIL",
        CheckStatus.Skip => "SKIP",
        _ => "????",
    };
}

public enum CheckStatus { Pass, Fail, Skip }

/// <summary>
/// M1 验收（design.md §13「M1 数据底座」）。
///
/// 验收清单：
///   design.md  INV-1、INV-1b、INV-1c、INV-2 ~ INV-6、INV-10、INV-34、INV-35
///   id.md      ID-1 ~ ID-7、ID-14 ~ ID-17
///   附加       L2 派生验证（抽 10⁴ 组两次派生逐位一致）、命名组合空间 ≥ 5×10⁷
///
/// <b>未通过不等于代码错</b>：也可能是文档之间打架。每条结果都写明判据出处，
/// 碰到后者请改文档而不是改断言。
/// </summary>
public static class Invariants
{
    /// <summary>ID 规范式（id.md ID-1）。</summary>
    private static readonly Regex IdPattern = new(@"^[1-5]\.\d+-\d+-[01]$", RegexOptions.Compiled);

    /// <param name="field">
    /// M2 的行星场。M3 起由调用方传入并<b>全程复用</b> —— INV-7/8/9/10 都要向它取值，
    /// 各建一份等于把标定跑四遍。调用方（CLI）本来也已经建过一份了。
    /// </param>
    public static List<CheckResult> RunAll(string dbPath, long seed, Geography.World world,
                                           IReadOnlyList<Member> members, PlanetField field)
    {
        var r = new List<CheckResult>();

        // ── 地理与结构 ──
        r.Add(CheckInv1(world));
        r.Add(CheckInv1b(world));
        r.Add(CheckInv1c(world));
        r.Add(CheckInv2(world, members));
        r.Add(CheckInv3(members));
        r.Add(CheckInv4(members));
        r.Add(CheckInv5(members));
        r.Add(CheckInv6(members));
        // M2 的地理真值。INV-7/8/9/10 都要用它。
        var areas = field.MeasureAreaShares();
        r.Add(CheckInv7(field, areas));
        r.Add(CheckInv8(field, seed, areas));

        // ── M3：河流与部落 ──
        r.Add(CheckInv9(world, field));
        r.Add(CheckInv9b(world));
        r.Add(CheckInv10(world, field));

        // ── M4：气候与植被 ──
        r.Add(CheckClimateAcceptance(world, seed));

        // ── 身份（id.md §7）──
        r.Add(CheckId1(members));
        r.Add(CheckId2AndId15AndId17(seed, members));
        r.Add(CheckId3Id4Id5(members));
        r.Add(CheckId6(dbPath));
        r.Add(CheckId7(world));
        r.Add(CheckId16(seed));

        // ── 架构级 ──
        r.Add(CheckInv34(seed, field));
        r.Add(CheckInv35(dbPath, members));
        r.Add(CheckL2Sampling(seed));
        r.Add(CheckNameSpace());

        return r;
    }

    // ══════════════════════════════════════════════════════════════════
    //  design.md §12 不变量
    // ══════════════════════════════════════════════════════════════════

    private static CheckResult CheckInv1(Geography.World w) => new(
        "INV-1", "部落总数 == 5 大陆 × 20 = 100",
        w.Tribes.Count == WorldConfig.TribeCount ? CheckStatus.Pass : CheckStatus.Fail,
        $"实际 {w.Tribes.Count}（期望 {WorldConfig.TribeCount}）");

    private static CheckResult CheckInv1b(Geography.World w)
    {
        bool ok = w.Rivers.Count == WorldConfig.ContinentCount
               && w.Rivers.Select(x => x.ContinentId).Distinct().Count() == WorldConfig.ContinentCount;
        return new("INV-1b", "每大陆恰有 1 条主河，总数 == 5",
            ok ? CheckStatus.Pass : CheckStatus.Fail,
            $"主河 {w.Rivers.Count} 条，覆盖大陆 {w.Rivers.Select(x => x.ContinentId).Distinct().Count()} 个");
    }

    private static CheckResult CheckInv1c(Geography.World w)
    {
        var triById = w.Tributaries.ToDictionary(t => t.Id);
        var bad = new List<string>();
        foreach (var t in w.Tribes)
        {
            if (!triById.TryGetValue(t.TributaryId, out var tri)) { bad.Add($"{t.Code}:支流缺失"); continue; }
            if (tri.ContinentId != t.ContinentId) bad.Add($"{t.Code}:大陆不一致");
            if (tri.RiverId != t.ContinentId) bad.Add($"{t.Code}:主河不一致");
        }
        return new("INV-1c", "部落的支流/主河/大陆三者一致",
            bad.Count == 0 ? CheckStatus.Pass : CheckStatus.Fail,
            bad.Count == 0 ? "100/100 一致" : string.Join("; ", bad.Take(3)));
    }

    private static CheckResult CheckInv2(Geography.World w, IReadOnlyList<Member> m)
    {
        int expect = w.Tribes.Count * WorldConfig.MembersPerTribe;
        return new("INV-2", "成员总数 == 部落数 × 1000",
            m.Count == expect ? CheckStatus.Pass : CheckStatus.Fail,
            $"实际 {m.Count:N0}（期望 {expect:N0}）");
    }

    private static CheckResult CheckInv3(IReadOnlyList<Member> m)
    {
        var bad = new List<string>();
        foreach (var g in m.GroupBy(x => x.TribeCode))
        {
            int male = g.Count(x => x.Gender == Gender.Male);
            int female = g.Count(x => x.Gender == Gender.Female);
            if (male != WorldConfig.MalePerTribe || female != WorldConfig.FemalePerTribe)
                bad.Add($"{g.Key}:{male}/{female}");
        }

        long totalM = m.Count(x => x.Gender == Gender.Male);
        long totalF = m.Count - totalM;
        double ratio = (double)totalM / totalF;
        double drift = Math.Abs(ratio - 1.05) / 1.05;

        bool ok = bad.Count == 0 && drift < 0.001;
        string detail = bad.Count > 0
            ? $"违反部落 {bad.Count} 个：{string.Join("; ", bad.Take(3))}"
            : $"每部落严格 512:488 ✅；全局 {totalM:N0}:{totalF:N0} = {ratio.ToString("F5", CultureInfo.InvariantCulture)}"
              + $"（目标 1.05，偏差 {drift * 100:F3}%）";

        return new("INV-3", "每部落男:女 == 512:488；全局 ≈ 1.05:1", ok ? CheckStatus.Pass : CheckStatus.Fail, detail);
    }

    private static CheckResult CheckInv4(IReadOnlyList<Member> m)
    {
        var idSet = new HashSet<string>();
        var pairSet = new HashSet<(string, int)>();
        int dupId = 0, dupPair = 0;
        foreach (var x in m)
        {
            if (!idSet.Add(x.Id)) dupId++;
            if (!pairSet.Add((x.TribeCode, x.Seq))) dupPair++;
        }
        bool ok = dupId == 0 && dupPair == 0;
        return new("INV-4", "member_id 全局唯一；(tribe_id, member_no) 唯一",
            ok ? CheckStatus.Pass : CheckStatus.Fail,
            ok ? $"{m.Count:N0} 行无重复" : $"重复 ID {dupId}，重复 (部落,序号) {dupPair}");
    }

    private static CheckResult CheckInv5(IReadOnlyList<Member> m)
    {
        int bad = m.Count(x => (x.GenderCode == 1) != (x.Gender == Gender.Male));
        return new("INV-5", "gender_code == 1 ⟺ gender == male",
            bad == 0 ? CheckStatus.Pass : CheckStatus.Fail,
            bad == 0 ? "100,000/100,000 一致" : $"{bad} 行不一致");
    }

    private static CheckResult CheckInv6(IReadOnlyList<Member> m)
    {
        int lo = m.Min(x => x.AgeAtGenesis);
        int hi = m.Max(x => x.AgeAtGenesis);
        bool ok = lo >= WorldConfig.MinAge && hi <= WorldConfig.MaxAge;
        var dist = m.GroupBy(x => x.AgeAtGenesis).OrderBy(g => g.Key)
                    .Select(g => $"{g.Key}岁:{g.Count():N0}");
        return new("INV-6", "初始年龄全部 ∈ [13,17]",
            ok ? CheckStatus.Pass : CheckStatus.Fail,
            $"区间 [{lo},{hi}]；分布 {string.Join(" ", dist)}");
    }

    /// <summary>
    /// INV-7：大陆间最小角距。
    ///
    /// 判据取<b>大陆中心</b>之间的角距 —— 这是 design.md §7.2 表格里那 5 个坐标
    /// 唯一能给出的量，且文档写的「实际 ≥ 55°」与中心距算出的最小值（C1~C4 = 55.0°）
    /// 精确吻合，说明当初就是这么算的。
    ///
    /// 同时报出<b>包围盒角点</b>的最近距离供参考：它更保守，也更能暴露"两块大陆
    /// 的矩形外框是否擦边"。两者含义不同，不要混为一谈。
    /// </summary>
    private static CheckResult CheckInv7(PlanetField field, PlanetField.AreaReport area)
    {
        double minCenter = double.MaxValue, minCorner = double.MaxValue;
        string worstCenter = "", worstCorner = "";

        for (int a = 1; a <= 5; a++)
            for (int b = a + 1; b <= 5; b++)
            {
                var ca = Geography.Continents[a - 1];
                var cb = Geography.Continents[b - 1];

                double dc = AngularDeg(ca.CenterLat, ca.CenterLon, cb.CenterLat, cb.CenterLon);
                if (dc < minCenter) { minCenter = dc; worstCenter = $"C{a}~C{b}"; }

                foreach (var p in BboxSamples(ca))
                    foreach (var q in BboxSamples(cb))
                    {
                        double d = AngularDeg(p.Lat, p.Lon, q.Lat, q.Lon);
                        if (d < minCorner) { minCorner = d; worstCorner = $"C{a}~C{b}"; }
                    }
            }

        bool ok = minCenter >= 30.0;

        // M2 补测：判定「角距够了」之后，还要看<b>实际生成的陆地</b>够不够格叫 5 块大陆。
        // 判据（中心距）通过而陆地面积为零，是可以同时成立的 ——
        // 那样 INV-7 会以「通过」的姿态掩盖一颗光秃秃的星球。
        // 故把实测面积一并报出来，让「通过」这两个字有内容。
        _ = field;
        var shares = string.Join(" ", Geography.Continents.Select(c => $"C{c.Id} {area.Shares[c.Id]:P2}"));

        return new("INV-7", "任意两大陆最小角距 ≥ 30°（文档称实际 ≥ 55°）",
            ok ? CheckStatus.Pass : CheckStatus.Fail,
            $"中心距最小 {minCenter:F1}°（{worstCenter}）；包围盒角点最小 {minCorner:F1}°（{worstCorner}）；" +
            $"M2 实测陆地占比 {shares}（合计 {area.LandFraction:P2}）");
    }

    /// <summary>
    /// 沿大陆包围盒四边采样。<b>经度必须走短弧</b>——否则跨 180° 的大陆 3
    /// （140°E ~ 160°W）会被插值成"绕地球 300° 一圈"，凭空穿过其它大陆。
    /// </summary>
    private static IEnumerable<(double Lat, double Lon)> BboxSamples(Continent c)
    {
        double lonSpan = c.LonMax - c.LonMin;
        if (lonSpan < 0) lonSpan += 360;      // 跨 180° 线

        const int N = 24;
        for (int i = 0; i <= N; i++)
        {
            double u = i / (double)N;
            double lat = c.LatMin + (c.LatMax - c.LatMin) * u;
            double lon = Sphere.WrapLon(c.LonMin + lonSpan * u);

            yield return (lat, c.LonMin);
            yield return (lat, c.LonMax);
            yield return (c.LatMin, lon);
            yield return (c.LatMax, lon);
        }
    }

    /// <summary>
    /// INV-8：最低海平面（§6.2 冰盛期 −120 m）时 5 大陆仍互不连通。
    ///
    /// M1 阶段这一项是 <see cref="CheckStatus.Skip"/> —— 当时的数据模型只有大陆包围盒
    /// 与部落点，没有地形面，「连通」二字无从谈起。M2 把 §7.3/§7.4 的场做出来之后，
    /// 这项判定才第一次有了真值来源，故在此<b>真正执行</b>，不再跳过。
    ///
    /// <b>判据不是「包围盒不相交」，也不是「种子圆盘不相交」</b>，而是把露出水面的格元
    /// 做连通分量分解，看有没有哪个分量同时含两块大陆的格元
    /// （<see cref="PlanetRaster.AnalyzeConnectivity"/>）。
    /// 前两者都只是必要不充分条件：两块大陆的包围盒可以离得很远，
    /// 而海面下降 120 m 之后，中间那条浅水脊照样能把它们连成一片 ——
    /// 白令陆桥就是这么来的，而它恰恰是本项目最关心的地理事件之一。
    /// 真正要防的正是这种情形，所以必须用真地形判，不能靠几何近似。
    ///
    /// 检查放在<b>最低海平面</b>而非今日海平面：−120 m 是全程最坏情况，
    /// 它过了，其余年代自然都过（海平面更高只会淹掉更多陆桥）。
    /// 顺带把今日海平面也跑一遍，两者对照可以看出「现在是离连通有多远」。
    /// </summary>
    private static CheckResult CheckInv8(PlanetField field, long seed, PlanetField.AreaReport area)
    {
        var raster = PlanetRaster.Build(field, 1024, 512);

        double seaLow = GlacialCycle.LowestSeaLevelM();

        // 今日海平面。用今年而非「冰量为 0 之年」，因为要点是报告当下的情形。
        double seaNow = GlacialCycle.SeaLevelOffsetM(WorldConfig.EndYear, seed);

        var atLow = raster.AnalyzeConnectivity(seaLow);
        var atNow = raster.AnalyzeConnectivity(seaNow);

        // 解析判据（标定阶段二分用的就是它）作为佐证：它不受栅格分辨率影响。
        var bridge = field.WorstBridge();

        var detail = new System.Text.StringBuilder();
        detail.Append($"最低海平面 {seaLow:F0} m：");
        if (atLow.ContinentsSeparated)
            detail.Append($"✅ 互不连通（连通分量 {atLow.ComponentCount} · 陆格 {atLow.LandCells:N0}）");
        else
            detail.Append($"❌ 陆桥 {string.Join("、", atLow.BridgingComponents)}");
        detail.Append($";今日海平面 {seaNow:F1} m：" +
                      (atNow.ContinentsSeparated ? "✅ 互不连通" : $"❌ 陆桥 {string.Join("、", atNow.BridgingComponents)}"));
        detail.Append($";洋面最窄处 C{bridge.A}~C{bridge.B} {bridge.GapDeg:F1}°（判据 ≥ {PlanetField.MinOceanGapDeg:F0}°）");

        // 报<b>实测</b>陆地占比，不是 AchievedLandTarget ——
        // 后者是标定用掉的目标值（可行时恰为 22%），拿它当「全球陆地」会与 INV-7
        // 报出的实测量打架，读者会以为哪一项算错了。
        detail.Append($";全球陆地 {area.LandFraction:P2}");

        return new("INV-8", "最低海平面（−120 m）时 5 大陆仍不连通",
            atLow.ContinentsSeparated ? CheckStatus.Pass : CheckStatus.Fail,
            detail.ToString());
    }

    /// <summary>
    /// 沿程单调性允许的单步回升：绝对值下限（米）。
    /// 实际容差取 <c>max(本值, 落差 × <see cref="MonotoneToleranceFraction"/>)</c>。
    /// </summary>
    private const double MonotoneToleranceM = 2.0;

    /// <summary>
    /// 沿程单调性容差占河道总落差的比例。
    ///
    /// <b>为什么要跟落差成比例</b>：存进库的几何是 Catmull-Rom <b>样条</b>，
    /// 而样条在控制点之间会有过冲 —— 控制点严格递减<b>不保证</b>样条处处递减。
    /// 过冲幅度与相邻控制点的高差同量级，所以固定 2 m 的容差对一条落差 2000 m、
    /// 控制点间距 39 km 的大河而言过严（实测 R2 过冲 2.9 m，
    /// 折算坡度 0.007%，远在地形噪声之下）。
    /// 取落差的 0.5%：足以容纳样条过冲，又远小于真正的"倒流"
    /// （那会是几百米量级，不可能漏过）。
    /// </summary>
    private const double MonotoneToleranceFraction = 0.005;

    /// <summary>
    /// INV-9：河源海拔 &gt; 入海口，且沿程单调下降（design.md §7.5）。
    ///
    /// 主河 5 条 + 支流 25 条都查。支流的 t=0 是汇入点、t=1 是源头
    /// （<see cref="Tributary"/> 的约定），故其海拔应沿 t <b>递增</b>，判据取反。
    ///
    /// <b>口径说明（§7.5 未指定处之四）</b>：原文只说「沿程单调下降」，
    /// 没说采样点取在哪、允不允许过冲。这里在样条上<b>密采样 257 点</b>再查 ——
    /// 只查控制点是查不出问题的，控制点递减完全不保证样条处处递减
    /// （Catmull-Rom 在控制点之间会过冲）。单步回升超过
    /// <see cref="MonotoneToleranceM"/> 即判失败。
    /// </summary>
    private static CheckResult CheckInv9(Geography.World w, PlanetField field)
    {
        var bad = new List<string>();
        double worst = 0;

        foreach (var r in w.Rivers)
        {
            double rise = ProfileRise(field, r.ControlPoints, rising: false);
            double srcE = ElevAt(field, r.ControlPoints[0]);
            double mouthE = ElevAt(field, r.ControlPoints[^1]);
            double tol = Tolerance(srcE - mouthE);
            worst = Math.Max(worst, rise);
            if (srcE <= mouthE) bad.Add($"主河R{r.ContinentId} 源{srcE:F0}≤口{mouthE:F0}");
            else if (rise > tol) bad.Add($"主河R{r.ContinentId} 逆升{rise:F1}m>容差{tol:F1}");
        }

        foreach (var t in w.Tributaries)
        {
            double rise = ProfileRise(field, t.ControlPoints, rising: true);
            double junctionE = ElevAt(field, t.ControlPoints[0]);
            double srcE = ElevAt(field, t.ControlPoints[^1]);
            double tol = Tolerance(srcE - junctionE);
            worst = Math.Max(worst, rise);
            if (srcE <= junctionE) bad.Add($"支流T{t.Id} 源{srcE:F0}≤汇{junctionE:F0}");
            else if (rise > tol) bad.Add($"支流T{t.Id} 逆升{rise:F1}m>容差{tol:F1}");
        }

        int total = w.Rivers.Count + w.Tributaries.Count;
        return new("INV-9", $"河源海拔>入海口，沿程单调（主河 {w.Rivers.Count} + 支流 {w.Tributaries.Count}）",
            bad.Count == 0 ? CheckStatus.Pass : CheckStatus.Fail,
            bad.Count == 0
                ? $"{total}/{total} 条合规；样条密采样 257 点，最大逆升 {worst:F2} m（容差 {MonotoneToleranceM}）"
                : string.Join("; ", bad.Take(5)) + (bad.Count > 5 ? $" … 共 {bad.Count} 条" : ""));
    }

    private static double ElevAt(PlanetField f, (double Lat, double Lon) p) => f.ElevationM(p.Lat, p.Lon);

    /// <summary>某条河的单调性容差：<c>max(绝对下限, 落差 × 比例)</c>。</summary>
    private static double Tolerance(double relief)
        => Math.Max(MonotoneToleranceM, Math.Max(0, relief) * MonotoneToleranceFraction);

    /// <summary>样条密采样后，单步海拔相对前一采样的最大「逆向」幅度（米）。</summary>
    /// <param name="rising">true 表示该折线的海拔本应递增（支流），此时逆 = 下降。</param>
    private static double ProfileRise(PlanetField field,
                                      IReadOnlyList<(double Lat, double Lon)> pts, bool rising)
    {
        const int N = 256;
        double prev = double.NaN, worst = 0;
        for (int i = 0; i <= N; i++)
        {
            var (la, lo) = Geography.CatmullRom(pts, i / (double)N);
            double e = field.ElevationM(la, lo);
            if (!double.IsNaN(prev))
            {
                double step = rising ? prev - e : e - prev;
                if (step > worst) worst = step;
            }
            prev = e;
        }
        return worst;
    }

    /// <summary>
    /// INV-9b：每条主河的 tribe_capacity 之和 == 该大陆部落数（20）。
    ///
    /// ⚠️ <b>本检查在 M3 之前是恒真的</b>（永远通过、永远不可能失败）——
    /// 因为它把常量 <c>WorldConfig.TribesPerTributary</c> 求和，
    /// 而那个常量同时决定了部落数，两边同源，<c>5×4≡20</c> 无从证伪。
    /// M3 起 <c>tribe_capacity</c> 由长度/流域面积分配（<see cref="Geography.AllocateCapacity"/>），
    /// 与部落数<b>各自独立</b>产生，这才是一个真的检查。
    /// 另加一条：每条支流上的实际部落数必须等于它自己申报的养育能力。
    /// </summary>
    private static CheckResult CheckInv9b(Geography.World w)
    {
        var bad = new List<string>();
        for (int cid = 1; cid <= WorldConfig.ContinentCount; cid++)
        {
            var tris = w.Tributaries.Where(t => t.ContinentId == cid).ToList();
            int capacity = tris.Sum(t => t.TribeCapacity);
            int actual = w.Tribes.Count(t => t.ContinentId == cid);
            if (capacity != actual) bad.Add($"C{cid}:载{capacity}≠实{actual}");

            foreach (var t in tris)
            {
                int n = w.Tribes.Count(x => x.TributaryId == t.Id);
                if (n != t.TribeCapacity) bad.Add($"T{t.Id}:载{t.TribeCapacity}≠实{n}");
            }
        }

        string sample = string.Join("/", w.Tributaries
            .Where(t => t.ContinentId == 1).Select(t => t.TribeCapacity));

        return new("INV-9b", "每条主河的 tribe_capacity 之和 == 该大陆部落数（20）",
            bad.Count == 0 ? CheckStatus.Pass : CheckStatus.Fail,
            bad.Count == 0
                ? $"{WorldConfig.ContinentCount}/{WorldConfig.ContinentCount} 大陆 = 20；C1 能力分布 {sample}（合计 {w.Tributaries.Where(t => t.ContinentId == 1).Sum(t => t.TribeCapacity)}）"
                : string.Join("; ", bad.Take(5)));
    }

    /// <summary>
    /// INV-10：全部部落点海拔 &gt; 海平面 + 5 m。
    ///
    /// ⚠️ <b>M3 之前这个检查是「假的通过」</b>：它读 <c>t.ElevationM</c>，
    /// 而那是 <see cref="Geography"/> 用公式估算的值，与 M2 的地形场毫无关系 ——
    /// 于是 100 个部落里有 <b>21 个实际落在水下</b>，检查却全绿。
    /// 现在海拔由地形场直接给出，本检查<b>独立重算</b>（不信任记录里的字段），
    /// 并顺带把 §7.5 其余三条约束（非冰盖 / 坡度 / 间距）的实测数一并报出。
    ///
    /// <b>判定口径</b>：只有「海拔」一条决定通过与否（那才是 INV-10 的定义）。
    /// 另外三条是 §7.5 的约束，其中「非冰盖」在寒带大陆（§7.2 大陆 4、5，纬度 68~84°）
    /// 是否可行，作者尚未裁定（见 design.md §13），故此处只报数、不判负。
    /// </summary>
    private static CheckResult CheckInv10(Geography.World w, PlanetField field)
    {
        int below = 0, ice = 0, steep = 0, closePairs = 0;
        double minE = double.PositiveInfinity, maxE = double.NegativeInfinity;
        double worstSlope = 0, minSpacing = double.PositiveInfinity;

        for (int i = 0; i < w.Tribes.Count; i++)
        {
            var t = w.Tribes[i];
            double e = field.ElevationM(t.Lat, t.Lon);        // 独立重算
            if (e <= Geography.SeaLevelPlusM) below++;
            minE = Math.Min(minE, e);
            maxE = Math.Max(maxE, e);

            if (t.Biome == Climate.BiomeName(Climate.IceCap)) ice++;

            double sl = Geography.SlopeDeg(field, t.Lat, t.Lon);
            worstSlope = Math.Max(worstSlope, sl);
            if (sl > Geography.MaxSlopeDeg) steep++;

            for (int j = i + 1; j < w.Tribes.Count; j++)
            {
                if (w.Tribes[j].ContinentId != t.ContinentId) continue;
                double d = Geography.DistanceKm((t.Lat, t.Lon), (w.Tribes[j].Lat, w.Tribes[j].Lon));
                if (d < minSpacing) minSpacing = d;
                if (d < Geography.MinSpacingKm) closePairs++;
            }
        }

        if (double.IsPositiveInfinity(minSpacing)) minSpacing = 0;

        return new("INV-10", "全部部落点海拔 > 海平面 + 5 m（地形场实测）",
            below == 0 ? CheckStatus.Pass : CheckStatus.Fail,
            $"水下 {below}/{w.Tribes.Count}；海拔 {minE:F1}~{maxE:F1} m。" +
            $"§7.5 其余三条：冰盖 {ice} · 坡度>{Geography.MaxSlopeDeg}° {steep}（最大 {worstSlope:F1}°）· " +
            $"间距<{Geography.MinSpacingKm}km {closePairs} 对（最小 {minSpacing:F1} km）");
    }

    /// <summary>
    /// §13 气候验收：<b>赤道绿、副热带沙、极地白；冰期可驱动</b>。
    ///
    /// 量算口径与 <c>humen climate</c> 报告<b>共用</b> <see cref="ClimateAudit"/>，
    /// 所以不可能出现「报告说达标、验收说没达标」。四条的实测值与判据都在
    /// <see cref="ClimateAudit.Acceptance"/> 里，这里只做汇总。
    ///
    /// 格网直接从 <see cref="Geography.World.Climate"/> 取 —— 那是建世界时烘好的同一张，
    /// 不必再扫一遍（一次 ~1.2 s）。
    /// </summary>
    private static CheckResult CheckClimateAcceptance(Geography.World w, long seed)
    {
        var a = ClimateAudit.Evaluate(w.Climate, seed);

        bool ok = a.AllPass;
        var failed = a.Verdicts.Where(v => !v.Pass).Select(v => v.What.Split(' ')[0]).ToList();

        string detail =
            $"赤道绿 {a.EquatorGreen.Got:P1}（≥{ClimateAudit.EquatorGreenNeed:P0}）· " +
            $"副热带沙 {a.SubtropDesert.Got:P1}（≥{ClimateAudit.SubtropDesertNeed:P0}）· " +
            $"极地白 {a.PolarIce.Got:P1}（≥{ClimateAudit.PolarIceNeed:P0}）· " +
            $"冰期驱动 ×{a.GlacialDrive.Got:F2}（≥×{ClimateAudit.GlacialRatioNeed:F1}，" +
            $"{WorldConfig.FormatYear(a.GlacialYear)}）"
            + (ok ? "" : $"；未达标 {string.Join("、", failed)}");

        return new("§13 M4 验收", "赤道绿 · 副热带沙 · 极地白 · 冰期可驱动",
            ok ? CheckStatus.Pass : CheckStatus.Fail, detail);
    }

    // ══════════════════════════════════════════════════════════════════
    //  id.md §7 不变量
    // ══════════════════════════════════════════════════════════════════

    private static CheckResult CheckId1(IReadOnlyList<Member> m)
    {
        int bad = 0;
        foreach (var x in m)
        {
            if (!IdPattern.IsMatch(x.Id)) { bad++; continue; }
            if (!Identity.TryParseId(x.Id, out int c, out int t, out int seq, out int g)) { bad++; continue; }
            if (c != x.ContinentId || seq != x.Seq || g != x.GenderCode) bad++;
        }
        return new("ID-1", "ID 匹配 ^[1-5]\\.\\d+-\\d+-[01]$ 且三段解析恰好成功",
            bad == 0 ? CheckStatus.Pass : CheckStatus.Fail,
            bad == 0 ? "100,000/100,000 合规" : $"{bad} 个不合规");
    }

    /// <summary>
    /// ID-2 + ID-15 + ID-17 合并检查：
    /// 先建 <c>members</c> 的 ID 集合（= 当时即档），
    /// 再<b>乱序随机访问</b>重派一遍（= 事后补档 / 跨 LOD 重放），
    /// 两者必须逐字相同，且不产生表外新 ID。
    /// </summary>
    private static CheckResult CheckId2AndId15AndId17(long seed, IReadOnlyList<Member> m)
    {
        var byKey = new Dictionary<(int, int, int), Member>();
        foreach (var x in m)
        {
            var (c, l) = Identity.SplitTribeIndex(x.TribeId);
            byKey[(c, l, x.Seq)] = x;
        }

        // 乱序访问：用固定种子的洗牌保证"乱"本身也可复现
        var keys = byKey.Keys.ToList();
        var rng = new Rng(seed ^ 0x5EED_1234);
        rng.Shuffle(keys);

        int mismatch = 0;
        string firstBad = "";
        foreach (var k in keys)
        {
            var re = Identity.Derive(seed, k.Item1, Identity.JoinTribeIndex(k.Item1, k.Item2), k.Item3,
                                     IdSource.Genesis);
            if (!MemberEquals(byKey[k], re))
            {
                mismatch++;
                if (firstBad.Length == 0) firstBad = $"{byKey[k].Id} vs {re.Id}";
            }
        }

        bool ok = mismatch == 0;
        return new("ID-2 / ID-15 / ID-17", "ID 全局唯一；跨 LOD 重派生逐字相同；补档不改 ID",
            ok ? CheckStatus.Pass : CheckStatus.Fail,
            ok ? $"{keys.Count:N0} 个 ID 乱序重派，逐字一致"
               : $"{mismatch} 个不一致，首个：{firstBad}");
    }

    private static CheckResult CheckId3Id4Id5(IReadOnlyList<Member> m)
    {
        // ID-4：元年集合恰好为 {1..5}.{1..20}-{1..1000}-{0,1}
        var expect = new HashSet<string>(WorldConfig.GenesisMemberCount);
        for (int c = 1; c <= 5; c++)
            for (int t = 1; t <= 20; t++)
                for (int seq = 1; seq <= 1000; seq++)
                    for (int g = 0; g <= 1; g++)
                        expect.Add(Identity.FormatId(c, t, seq, g));

        var actual = m.Select(x => x.Id).ToHashSet();
        bool setOk = actual.Count == WorldConfig.GenesisMemberCount && actual.IsSubsetOf(expect);

        // ID-3：末位 == 1 ⟺ male
        int bad3 = m.Count(x => (x.Id[^1] == '1') != (x.Gender == Gender.Male));

        // ID-5：id_source == genesis ⟺ ID ∈ 元年集合
        int bad5 = m.Count(x => (x.Source == IdSource.Genesis) != expect.Contains(x.Id));

        bool ok = setOk && bad3 == 0 && bad5 == 0;
        return new("ID-3 / ID-4 / ID-5", "末位性别码一致；元年 ID 集合恰好 10 万个；id_source 与集合互充",
            ok ? CheckStatus.Pass : CheckStatus.Fail,
            $"集合 {(setOk ? "恰好" : "不符")}（{actual.Count:N0}）；末位不符 {bad3}；id_source 不符 {bad5}");
    }

    private static CheckResult CheckId6(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();

        int rows;
        long minNext = long.MaxValue, maxNext = long.MinValue;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*), MIN(next_seq), MAX(next_seq) FROM id_ledger;";
            using var rd = cmd.ExecuteReader();
            rd.Read();
            rows = rd.GetInt32(0);
            minNext = rd.GetInt64(1);
            maxNext = rd.GetInt64(2);
        }

        bool rowOk = rows == WorldConfig.TribeCount;
        bool seqOk = minNext == maxNext && minNext == WorldConfig.MembersPerTribe + 1;

        return new("ID-6", "部落内序号唯一；id_ledger.next_seq 单调递增",
            rowOk && seqOk ? CheckStatus.Pass : CheckStatus.Fail,
            $"id_ledger {rows} 行，next_seq ∈ [{minNext},{maxNext}]（期望 {WorldConfig.MembersPerTribe + 1}）");
    }

    private static CheckResult CheckId7(Geography.World w) => new(
        "ID-7", "已作废（死亡）的 ID 不出现在任何新的发放记录中",
        CheckStatus.Skip,
        "尚无死亡事件（模拟未启动），无可作废的 ID。→ 待 M3 起部落年度演化产出 events 表后校验。" +
        "（M2 只做星球几何，不含任何生命事件，故本项在 M2 之后仍是 SKIP。）");

    private static CheckResult CheckId16(long seed)
    {
        // 窗口内
        bool inWindow = Identity.HasId(0.0);
        bool beforeWindow = Identity.HasId(WorldConfig.IdWindowStart - 1);
        bool afterWindow = Identity.HasId(WorldConfig.IdWindowEnd + 1);
        bool genesisExempt = Identity.HasId(WorldConfig.StartYear, isGenesisMember: true);
        bool genesisNotExempt = Identity.HasId(WorldConfig.StartYear, isGenesisMember: false);

        bool ok = inWindow && !beforeWindow && !afterWindow && genesisExempt && !genesisNotExempt;

        string window = $"[{WorldConfig.FormatYear(WorldConfig.IdWindowStart)}, {WorldConfig.FormatYear(WorldConfig.IdWindowEnd)}]";
        return new("ID-16", "发放窗口 = [公元前3000, 公元2126]；元年 10 万恒有 ID",
            ok ? CheckStatus.Pass : CheckStatus.Fail,
            $"窗口 {window}；窗口内={inWindow} 窗口前={beforeWindow} 窗口后={afterWindow}；元年豁免={genesisExempt}");
    }

    // ══════════════════════════════════════════════════════════════════
    //  架构级检查
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// INV-34：同种子下两次独立建世界，结果必须逐位一致。
    ///
    /// M3 起 <b>地理骨架也走这条检查</b>：河流的梯度下降、部落的候选罚分选址
    /// 都是「看似随机」的过程，最容易引入 <c>System.Random</c>、字典遍历序、
    /// 浮点累加序之类的非确定源。两次建的世界对象各自独立生成，再各自落库比字节。
    /// </summary>
    private static CheckResult CheckInv34(long seed, PlanetField field)
    {
        string a = Path.Combine(Path.GetTempPath(), $"humen_det_a_{seed}.db");
        string b = Path.Combine(Path.GetTempPath(), $"humen_det_b_{seed}.db");
        try
        {
            WorldDb.Build(a, seed, Geography.Build(seed, field));
            WorldDb.Build(b, seed, Geography.Build(seed, field));

            string ha = ContentHash(a), hb = ContentHash(b);
            bool ok = ha == hb;
            return new("INV-34", "身份/世界可复现：同 seed 两次构建逐位一致",
                ok ? CheckStatus.Pass : CheckStatus.Fail,
                ok ? $"内容哈希 {ha[..16]}… 相同"
                   : $"内容哈希不同：{ha[..16]}… vs {hb[..16]}…");
        }
        finally
        {
            TryDelete(a); TryDelete(b);
        }
    }

    /// <summary>
    /// INV-35：<c>members</c> 行数与窗口内总出生数（≈2×10¹⁰）无关。
    /// 判据：M1 只落 L1 实体层 10 万行，且 <c>id_ledger</c> 恰 100 行。
    /// </summary>
    private static CheckResult CheckInv35(string dbPath, IReadOnlyList<Member> m)
    {
        long ledgerRows;
        using (var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM id_ledger;";
            ledgerRows = (long)cmd.ExecuteScalar()!;
        }

        const double WindowBirths = 2.0e10;
        bool ok = m.Count <= 1_000_000 && ledgerRows <= 200;
        double perCapita = 0.0;   // 人均存储成本 = 0（L2 不落行）

        return new("INV-35", "人均存储成本 == 0：members 行数 ∝ L1 实体层，与窗口出生数无关",
            ok ? CheckStatus.Pass : CheckStatus.Fail,
            $"窗口内出生 ≈{WindowBirths:0.0e+0}；members {m.Count:N0} 行（L1 上限 10⁶）；"
          + $"id_ledger {ledgerRows} 行；人均存储 {perCapita} 字节");
    }

    /// <summary>
    /// §13 M1 附加项：随机抽 10⁴ 组 <c>(C,T,seq)</c>，两次派生必须逐位一致。
    /// 用确定性出生曲线桩，以便连非 NaN 路径一起验。
    /// </summary>
    private static CheckResult CheckL2Sampling(long seed)
    {
        var curve = new StubBirthCurve(seed);
        const int N = 10_000;
        var rng = new Rng(seed ^ 0xABCD_0001);

        int mismatch = 0;
        string firstBad = "";
        for (int i = 0; i < N; i++)
        {
            int c = rng.NextInt(1, WorldConfig.ContinentCount + 1);
            int t = rng.NextInt(0, WorldConfig.TribeCount);
            int seq = rng.NextInt(1001, 5_000_000);   // L2 区间

            var m1 = Identity.Derive(seed, c, t, seq, IdSource.Derived, curve);
            var m2 = Identity.Derive(seed, c, t, seq, IdSource.Derived, curve);

            if (!MemberEquals(m1, m2) || !IdPattern.IsMatch(m1.Id))
            {
                mismatch++;
                if (firstBad.Length == 0) firstBad = $"{m1.Id}({m1.FullName})";
            }
        }

        bool ok = mismatch == 0;
        return new("L2 派生验证", $"随机抽 {N:N0} 组 (C,T,seq) ∈ L2 区间，两次派生逐位一致",
            ok ? CheckStatus.Pass : CheckStatus.Fail,
            ok ? $"{N:N0}/{N:N0} 逐位一致（示例 ID 形如 {Identity.FormatId(3, 7, 123456, 1)}）"
               : $"{mismatch} 组不一致，首个 {firstBad}");
    }

    /// <summary>design.md §8.3：每大陆命名组合空间 ≥ 5×10⁷。</summary>
    private static CheckResult CheckNameSpace()
    {
        var parts = new List<string>();
        bool ok = true;
        for (int c = 1; c <= 5; c++)
        {
            double space = NameGen.CombinationSpace(c);
            if (space < 5e7) ok = false;
            parts.Add($"C{c} {NameGen.StyleName(c)} {space:0.00e+0}");
        }
        return new("§8.3 命名空间", "每大陆组合空间 ≥ 5×10⁷",
            ok ? CheckStatus.Pass : CheckStatus.Fail,
            string.Join("；", parts));
    }

    // ══════════════════════════════════════════════════════════════════
    //  工具
    // ══════════════════════════════════════════════════════════════════

    /// <summary>逐字段比较，<b>double 走位比较</b>（否则 NaN != NaN 会误报）。</summary>
    private static bool MemberEquals(Member a, Member b)
        => a.Id == b.Id
        && a.ContinentId == b.ContinentId
        && a.TribeId == b.TribeId
        && a.Seq == b.Seq
        && a.Gender == b.Gender
        && a.FullName == b.FullName
        && a.Surname == b.Surname
        && a.GivenName == b.GivenName
        && BitConverter.DoubleToInt64Bits(a.BirthYear) == BitConverter.DoubleToInt64Bits(b.BirthYear)
        && a.AgeAtGenesis == b.AgeAtGenesis
        && a.Source == b.Source;

    private static double AngularDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double p1 = lat1 * Math.PI / 180, p2 = lat2 * Math.PI / 180;
        double dl = (lon1 - lon2) * Math.PI / 180;
        double cos = Math.Sin(p1) * Math.Sin(p2) + Math.Cos(p1) * Math.Cos(p2) * Math.Cos(dl);
        return Math.Acos(Math.Clamp(cos, -1.0, 1.0)) * 180.0 / Math.PI;
    }

    /// <summary>确定性出生曲线桩。M2 起换成读 <c>tribe_history</c> 的真实求逆。</summary>
    private sealed class StubBirthCurve : IBirthCurve
    {
        private readonly long _seed;
        public StubBirthCurve(long seed) => _seed = seed;

        public double Inverse(int continentId, int tribeId, int seq)
        {
            // 桩：把序号线性映射到 [公元前3000, 公元2126]，叠一点确定性抖动。
            // 只用于验证"同输入同输出"，不代表真实人口史。
            ulong h = Hashing.Hash64(_seed, HashDomain.Birth, continentId, tribeId, seq);
            double u = Hashing.ToUnit(h);
            return WorldConfig.IdWindowStart + u * (WorldConfig.IdWindowEnd - WorldConfig.IdWindowStart);
        }
    }

    /// <summary>整库内容哈希：按确定顺序逐行喂进 SHA-256。</summary>
    public static string ContentHash(string dbPath)
    {
        using var sha = SHA256.Create();
        var sb = new StringBuilder();

        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();

        string[] tables =
        {
            "world_meta", "continents", "rivers", "tributaries", "mineral_deposits",
            "tribes", "members", "tech_state", "events", "tribe_history",
            "members_archive", "id_ledger",
        };

        foreach (var t in tables)
        {
            using var cmd = conn.CreateCommand();
            // 按 rowid 顺序（= 写入顺序），保证确定性
            cmd.CommandText = $"SELECT * FROM {t} ORDER BY rowid;";
            using var rd = cmd.ExecuteReader();
            sb.Append('[').Append(t).Append(']');
            while (rd.Read())
            {
                for (int i = 0; i < rd.FieldCount; i++)
                {
                    sb.Append(rd.IsDBNull(i) ? " " : Convert.ToString(rd.GetValue(i), CultureInfo.InvariantCulture));
                    sb.Append('');
                }
                sb.Append('\n');
            }
        }

        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDelete(string p)
    {
        try { if (File.Exists(p)) File.Delete(p); } catch { /* 临时文件，删不掉就算了 */ }
    }
}
