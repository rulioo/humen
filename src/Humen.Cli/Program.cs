using System.Globalization;
using System.Text;
using System.Text.Json;
using Humen.Core;

namespace Humen.Cli;

/// <summary>
/// 命令行入口：M1 数据底座 + M2 星球与大陆 + M3 河流与部落 + M4 气候与植被。
///
/// 之所以先做 CLI 而不是 Unity 菜单（design.md §8.5 的「Humen → Build World Database」）：
/// §8.5 明确要求「<b>生成器与 UnityEngine 解耦（纯 C#），可在 CI 中复现验证</b>」。
/// CLI 就是那个可复现的形态；Unity 菜单只是一层薄壳，等 Editor 装好后再包。
/// </summary>
public static class Program
{
    private const string DefaultOut = "world.db";

    public static int Main(string[] args)
    {
        // ★ 先把控制台<b>原本</b>的代码页记下来。下面设成 UTF-8 之后，
        // 控制台的代码页会<b>留在</b> 65001 —— 那是进程级共享状态，退出时
        // 不会自动还原，等于把调用方（比如启动脚本）的环境改坏了。
        //
        // 实测后果：从 .cmd 里跑完本程序后，批处理自己的 GBK 中文全变乱码；
        // 而在批处理里用 `chcp 936` 去补救，会让 cmd 丢弃<b>重定向的标准输入</b>，
        // 启动脚本的 set /p 就全部读空（三个探针实测确认，见 design.md §13.2）。
        // 两边都不讨好，所以只能在这里还原。
        uint originalCp = OperatingSystem.IsWindows() ? GetConsoleOutputCP() : 0;

        Console.OutputEncoding = Encoding.UTF8;

        int code;
        try
        {
            code = Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误：{ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            code = 1;
        }

        // ★ 必须在<b>所有</b>出口暂停，成功和失败都要 —— 否则双击运行时
        // 报错信息会跟着窗口一起消失，只剩「闪退」两个字，无从排查。
        PauseIfSoleConsoleOwner(args);

        // 还原放在暂停<b>之后</b>：那句「按回车键退出」本身也是中文，
        // 得让它在 UTF-8 的控制台下打出来才正常。
        if (originalCp != 0) SetConsoleOutputCP(originalCp);

        return code;
    }

    private static int Run(string[] args)
    {
        // --help / -h 必须在<b>任何位置</b>都认。
        // 曾经只认 args[0]，于是 `humen planet --help` 会静默忽略这个参数、
        // 按默认值跑一次完整生成（本机实测：把 --size 900 的产物覆盖成了 1600 的，
        // 排查了半天「同样的参数为何哈希变了」）。`humen build-world --help` 更糟 ——
        // 不打印帮助，反而写出一个 12 MB 的 world.db。
        bool wantsHelp = args.Length == 0 || args[0] is "help";
        foreach (string a in args)
            if (a is "-h" or "--help") wantsHelp = true;

        if (wantsHelp)
        {
            PrintHelp();
            return 0;
        }

        return args[0] switch
        {
            "build-world" => BuildWorld(args[1..]),
            "planet" => Planet(args[1..]),
            "climate" => ClimateReport(args[1..]),
            "terrain" => TerrainReport(args[1..]),
            "tech" => TechReport(args[1..]),
            "evolve" => Evolve(args[1..]),
            "emit-names" => EmitNames(args[1..]),
            "derive" => Derive(args[1..]),
            "verify" => Verify(args[1..]),
            "inspect" => Inspect(args[1..]),
            "export-world" => ExportWorld(args[1..]),
            _ => Unknown(args[0]),
        };
    }

    // ══════════════════════════════════════════════════════════════════
    //  双击运行时不闪退
    // ══════════════════════════════════════════════════════════════════

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList(uint[] processList, uint count);

    /// <summary>当前控制台的输出代码页；<b>没有控制台时返回 0</b>（故调用方必须判 0）。</summary>
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleOutputCP();

    /// <summary>设置控制台输出代码页。见 <see cref="Main"/> 里为什么要还原。</summary>
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetConsoleOutputCP(uint codePage);

    /// <summary>
    /// 本进程是不是这个控制台的<b>唯一</b>持有者。
    ///
    /// 双击 exe 时 Windows 会为本进程单独开一个控制台，里面只有它自己，
    /// 于是进程一退出窗口立刻消失 —— 用户看到的就是「闪退」。
    /// 其实程序正常跑完了（或正常报了错），只是没人来得及看。
    ///
    /// 而在 PowerShell / Windows Terminal 里运行，控制台里还有那个 shell，
    /// 进程数 &gt; 1，窗口不会关，就不该暂停；输出被重定向时根本没有控制台，
    /// 返回 0，更不能暂停（否则 CI 会一直挂着等回车）。
    /// </summary>
    private static bool IsSoleConsoleOwner()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var list = new uint[8];
            return GetConsoleProcessList(list, (uint)list.Length) == 1;
        }
        catch
        {
            // 拿不到就当作「不是」：宁可不暂停，也不能把脚本挂死。
            return false;
        }
    }

    private static void PauseIfSoleConsoleOwner(string[] args)
    {
        if (args.Contains("--no-pause")) return;
        if (!args.Contains("--pause") && !IsSoleConsoleOwner()) return;

        Console.WriteLine();
        Console.Write("按回车键退出…");
        Console.ReadLine();
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"未知命令：{cmd}");
        PrintHelp();
        return 2;
    }

    /// <summary>
    /// 参数越界。退出码 2 与 <see cref="Unknown"/> 一致，都是「用法错误」，
    /// 以区别于「运行失败」（1）。脚本据此可分辨该改命令行还是该查环境。
    /// </summary>
    private static int UsageError(string msg)
    {
        Console.Error.WriteLine($"参数错误：{msg}");
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            humen —— 蓝星（Humen）世界生成器 · M4

            用法：
              humen build-world [--seed N] [--out PATH] [--no-check]
                  生成 world.db（5 大陆 → 5 主河 → 25 支流 → 100 部落 → 10 万成员），
                  随后跑不变式验收（INV 全集），并写出 world_summary.json。默认 out = world.db

              humen planet [--seed N] [--out DIR] [--size N] [--res N]
                           [--year Y] [--no-clouds] [--no-stars] [--no-atmosphere]
                           [--cam-lat A] [--cam-lon A] [--sun-lat A] [--sun-lon A]
                           [--mesh-res N] [--z-exag X] [--no-mesh] [--no-shells]
                           [--no-graticule]
                  绘制蓝星（M2）：球体 + 5 大陆 + 海水 + 大气。
                  产出球面视图 PNG、等距圆柱地图 PNG、球体网格 OBJ+MTL，
                  并判定 INV-8（5 大陆连通性）。
                  默认 out = renders，size = 1600（成图边长），res = 2048（栅格宽），
                  mesh-res = 256（网格分段），z-exag = 20（垂直放大，仅供观看）
                  --no-graticule：等距圆柱图不画经纬网。贴到球面上当贴图用时必须加，
                  否则网格线会一起贴上去（M4b 的 Unity 侧即如此导出）。

              humen terrain [--seed N]
                  地形体检（M2）：陆地高程分位、各高度以下占比、mask 分布、分大陆明细。
                  量的是 §13.3 那条「陆地是一整块高原」——改高程映射前后都该跑它。

              humen climate [--seed N] [--year Y]
                  气候体检（M4，design.md §7.6 / §7.7 / §7.8）：按 1° 格网扫全星球，
                  打印各群系占比、纬度带 × 群系分布、气候分项贡献，
                  并逐条判定 §13 的验收口径「赤道绿、副热带沙、极地白、冰期可驱动」。
                  每一栏都给<b>两列</b>：M1 降维版（只按纬度）与 M4 完整版 ——
                  一眼看出「副热带沙」是新增的哪几项做出来的。
                  默认 year = 2025（今天）。
                  末尾自动对照末次冰盛期（公元前 21000），检查冰川旋回是否真的驱动了群系。

              humen verify [--db PATH]
                  只对已有的库跑验收。

              humen tech [--file PATH] [--node ID]
                  技术树体检：解析 tech_tree.md，打印层分布、类别→力折算、
                  技能域清单、入度为零的起点节点，并逐条判定 INV-11（无环 / 前置可达 / 有出处）。
                  默认 file = tech_tree.md
                  --node ID：打印该节点的完整字段与它的直接后继。

              humen evolve [--seed N] [--db PATH] [--years A..B] [--step N]
                           [--sample N] [--max-tribes N] [--carbon] [--no-write]
                           [--skill-rate R] [--owned-accel K1] [--tech-accel K2]
                           [--expose-successors | --no-expose-successors]
                  演化引擎（§1.3 发现 / §4.2 六力 / §4.3 人口 / §4.7 冲击 / §6.3 分裂）：
                  让元年 100 个部落从公元前 30 万年走到公元 2126 年，
                  把技术发现、事件、人口曲线写回 world.db。
                  默认 seed = 20261001，db = world.db，step = 25 年，sample = 2500 年。
                  --years A..B：只跑一段（调试用，如 --years -12000..-8000）。
                  --carbon：打开 §4.6 碳-气候耦合（默认关闭，Q-A8）。
                  --no-write：只跑不落库，用来测速与比对确定性。
                  --skill-rate / --owned-accel / --tech-accel：技能增长标定的三个旋钮
                    （§1.3 两级加速；不给则用 EvolutionConfig 的默认值）。
                  --expose-successors / --no-expose-successors：直接后继是否顺带曝光其所属域
                    （不给则用 EvolutionConfig 的默认值，即开）。实测这一项决定
                    技术前沿停在 104 还是走到 130，是全套参数里影响最大的单开关。

              humen export-world [--db PATH] [--out PATH] [--pretty]
                  把演化结果倒成 Unity 能读的世界快照（M4b-2 / M4b-3 的前置）。
                  Core 与 Unity 之间只能靠数据文件通信，此前 Unity 侧只读得到一张
                  map_biome.png —— 部落、人口、技术、河流一个都拿不到。
                  导出内容：大陆 / 河流与支流折线 / 每部落的经纬海拔群系土壤 /
                  逐年人口·能耗·材料等级（时间轴）/ 每项技术的习得年份。
                  默认 out = unity/Assets/StreamingAssets/World/world_view.json。
                  输出贴 Unity 的 JsonUtility（不支持嵌套容器与 Dictionary，
                  故折线点写成 {"lat":..,"lon":..} 对象数组）。
                  --pretty：缩进输出，便于肉眼看；默认紧凑，体积小得多。

              humen emit-names [--out DIR]
                  导出 Assets/Data/Names/naming_{1..5}.json（design.md §8.3）。

              humen derive [--seed N] [--c C] [--t T] [--seq N]
                  演示 L2 程序层派生：不建库、不落盘，凭空算出一个人的完整身份。
                  默认 c = 1，t = 1，seq = 1001。c ∈ [1,5] 大陆号；
                  t ∈ [1,20] 为大陆<b>内部</b>的部落号（从 1 数起）；seq ≥ 1。
                  连续打印 3 个人，用来看同一部落内 ID 如何递增。

              humen inspect [--db PATH]
                  抽样打印库里的内容，肉眼核对。
            """);
    }

    // ══════════════════════════════════════════════════════════════════
    //  build-world
    // ══════════════════════════════════════════════════════════════════

    private static int BuildWorld(string[] args)
    {
        long seed = ArgLong(args, "--seed", WorldConfig.DefaultSeed);
        string outPath = ArgStr(args, "--out", DefaultOut);
        bool noCheck = args.Contains("--no-check");

        Banner();

        Console.WriteLine($"种子               : {seed}");
        Console.WriteLine($"时间轴             : {WorldConfig.FormatYear(WorldConfig.StartYear)} → {WorldConfig.FormatYear(WorldConfig.EndYear)}");
        Console.WriteLine($"全程               : {WorldConfig.TotalSpanYears:N0} 年");
        Console.WriteLine($"ID 发放窗口        : {WorldConfig.FormatYear(WorldConfig.IdWindowStart)} → {WorldConfig.FormatYear(WorldConfig.IdWindowEnd)}");
        Console.WriteLine();

        var sw = System.Diagnostics.Stopwatch.StartNew();

        Step(1, 5, "地理骨架（大陆 / 主河 / 支流 / 部落）");
        var field = PlanetField.Build(seed);
        var world = Geography.Build(seed, field);
        Console.WriteLine($"      大陆 {Geography.Continents.Length} · 主河 {world.Rivers.Count} · 支流 {world.Tributaries.Count} · 部落 {world.Tribes.Count}");
        foreach (var r in world.Rivers)
        {
            var src = r.ControlPoints[0];
            var mouth = r.ControlPoints[^1];
            Console.WriteLine(
                $"        R{r.ContinentId} 长 {Geography.PolylineLengthKm(r.ControlPoints),6:F0} km · " +
                $"控制点 {r.ControlPoints.Count,2} · " +
                $"源 {src.Lat,6:F1},{src.Lon,7:F1} @ {field.ElevationM(src.Lat, src.Lon),6:F0} m · " +
                $"口 {mouth.Lat,6:F1},{mouth.Lon,7:F1} @ {field.ElevationM(mouth.Lat, mouth.Lon),6:F0} m");
        }
        PrintPlacement(world.Placement);

        Step(2, 5, $"元年成员（{WorldConfig.GenesisMemberCount:N0} 人 = 100 部落 × 1000）");
        var members = MemberBuilder.BuildGenesis(seed, world.Tribes);
        Console.WriteLine($"      已派生 {members.Count:N0} 个身份（L1 实体层）");

        Step(3, 5, "建库与写入");
        var stats = WorldDb.Build(outPath, seed, world);
        Console.WriteLine($"      {outPath}  {stats.SizeBytes / 1024.0 / 1024.0:F2} MB");

        Step(4, 5, "写 world_summary.json");
        string summaryPath = Path.ChangeExtension(outPath, ".summary.json");
        File.WriteAllText(summaryPath, BuildSummaryJson(seed, world, members, stats), Encoding.UTF8);
        Console.WriteLine($"      {summaryPath}");

        Step(5, 5, "不变式验收（INV 全集）");
        Console.WriteLine();

        var results = noCheck
            ? new List<CheckResult>()
            : Invariants.RunAll(outPath, seed, world, members, field);

        if (!noCheck) PrintResults(results);

        sw.Stop();
        Console.WriteLine();
        Console.WriteLine($"耗时 {sw.Elapsed.TotalSeconds:F2} 秒");

        if (noCheck) return 0;

        int failed = results.Count(x => x.Status == CheckStatus.Fail);
        Console.WriteLine(failed == 0
            ? "不变式验收：全部通过 ✅"
            : $"不变式验收：{failed} 项未通过 ❌");
        return failed == 0 ? 0 : 1;
    }

    // ══════════════════════════════════════════════════════════════════
    //  planet —— M2：把蓝星画出来
    // ══════════════════════════════════════════════════════════════════

    private static int Planet(string[] args)
    {
        long seed = ArgLong(args, "--seed", WorldConfig.DefaultSeed);
        string outDir = ArgStr(args, "--out", "renders");
        int size = (int)ArgLong(args, "--size", 1600);
        int res = (int)ArgLong(args, "--res", 2048);
        double year = ArgDouble(args, "--year", 2025.0);

        double camLat = ArgDouble(args, "--cam-lat", 22);
        double camLon = ArgDouble(args, "--cam-lon", -35);
        double sunLat = ArgDouble(args, "--sun-lat", 15);
        // ★ 太阳默认经度必须由 camLon 推出，不能写成常量 -35 + 40。
        // 太阳相对相机偏 40° 是「侧光」这个意图，而意图要跟着相机走：
        // 写死常量后，只有默认视角是侧光，--cam-lon 一旦换到别的经度，
        // 相机就转到夜半球去了 —— 渲染出来的是一团黑，
        // 却被当成「这块大陆没生成」。（本行曾如此，view_c3 全黑即此故。）
        double sunLon = ArgDouble(args, "--sun-lon", camLon + 40);

        bool clouds = !args.Contains("--no-clouds");
        bool stars = !args.Contains("--no-stars");
        bool atmo = !args.Contains("--no-atmosphere");

        int meshRes = (int)ArgLong(args, "--mesh-res", 256);
        double zExag = ArgDouble(args, "--z-exag", 20.0);
        bool mesh = !args.Contains("--no-mesh");
        bool shells = !args.Contains("--no-shells");

        // 经纬网是「地图」的标注，默认画 —— 看地图的人要靠它定位。
        // 但 map_biome.png 同时也是 <b>球面贴图</b>（Unity 侧直接贴到球上），
        // 网格线会跟着贴上去，在球面上变成一圈圈孤零零的细线。导出贴图时用
        // --no-graticule 关掉。默认值保持 true 不变，免得已有的图悄悄变了样。
        bool graticule = !args.Contains("--no-graticule");

        // M4：默认走 §7.6 的完整气候（洋流/大陆度/迎风坡/季风）。
        // --no-climate 退回 M1 的降维版（只按纬度分带），出图用于对照 ——
        // 「副热带沙」这类验收要能一眼看出是新增的那几项做出来的，不是本来就有的。
        bool useClimate = !args.Contains("--no-climate");

        Directory.CreateDirectory(outDir);

        Banner();
        Console.WriteLine($"种子               : {seed}");
        Console.WriteLine($"年代               : {WorldConfig.FormatYear(year)}");
        Console.WriteLine($"星球半径           : {Sphere.Radius:N0} units（1 unit ≈ 6.371 km，§7.1）");
        Console.WriteLine();

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // ── 1. 大陆场 ──
        Step(1, 7, "大陆场（§7.3 球面 metaball 距离场 + Simplex 噪声）");
        var field = PlanetField.Build(seed);
        int posSeeds = field.Seeds.Count(s => !s.IsNegative);
        int negSeeds = field.Seeds.Count(s => s.IsNegative);
        Console.WriteLine($"      种子圆盘 {field.Seeds.Count}（正权 {posSeeds} · 负权 {negSeeds}）");

        // §7.6 的洋流/大陆度两项要海陆分布。M4 起渲染、网格、部落落库共用这一张格网。
        ClimateGrid? climate = useClimate ? ClimateGrid.Build(field) : null;
        if (climate is not null)
            Console.WriteLine($"      气候格网 {ClimateGrid.Nx}×{ClimateGrid.Ny}（1°）" +
                              $" · 陆地格 {climate.LandCells:N0}" +
                              $"（{climate.LandCells / (double)(ClimateGrid.Nx * ClimateGrid.Ny):P1}）");

        var area = field.MeasureAreaShares();
        Console.Write("      面积占比（占整球）");
        foreach (var c in Geography.Continents)
            Console.Write($"  C{c.Id} {area.Shares[c.Id]:P2}/{area.TargetShares[c.Id]:P2}");
        Console.WriteLine();
        double targetSum = area.TargetShares.Values.Sum();
        // ⚠️ 报的是**被收缩之前的请求值**（TargetLandFraction），不是收缩之后的结果。
        //    曾经这里印的是 AchievedLandTarget —— 那正是收缩的**结果**，
        //    于是输出成了一句自我矛盾的"收缩自 18.09%"（收缩自 18.09% 到 18.09%），
        //    看着像没收缩，实际上是从 30% 一路降下来的。诊断信息必须指向**因**，不是果。
        Console.WriteLine($"      全球陆地 {area.LandFraction:P2}（有效目标 {targetSum:P2}" +
                          (field.TargetWasFeasible ? "" : $"，受 INV-8 约束收缩自 {PlanetField.TargetLandFraction:P2}") +
                          "）");

        var bbox = PlanetField.BBoxFractions();
        Console.Write("      §7.2 包围盒占球面 ");
        foreach (var c in Geography.Continents)
            Console.Write($"  C{c.Id} {bbox[c.Id]:P2}");
        Console.WriteLine($"（合计 {bbox.Values.Sum():P2}）");
        Console.Write("      落在 §7.2 包围盒内 ");
        foreach (var c in Geography.Continents)
            Console.Write($"  C{c.Id} {area.InBoxShare[c.Id]:P0}");
        Console.WriteLine();
        // 权重表从 Geography.Continents 现取，**不写字面量** ——
        // 写死的话改了权重、这里还印旧比值，输出会安静地撒谎（已经发生过一次）。
        var w = Geography.Continents.Select(c => (c.Id, c.LandFraction)).ToArray();
        Console.WriteLine($"      权重 {string.Join(":", w.Select(x => x.LandFraction.ToString("P0")))}");
        Console.WriteLine("         面积按上面的权重分摊，**每块各自不得超过自身 §7.2 包围盒**");
        Console.WriteLine("         （BoxOvergrowthLimit）—— 包围盒才是陆地总量的硬上限，不是权重。");
        Console.WriteLine();

        // ── 2. 栅格 ──
        Step(2, 7, $"等距圆柱栅格 {res} × {res / 2}");
        var raster = PlanetRaster.Build(field, res, res / 2);
        int landCells = raster.IsLand.Count(b => b);
        Console.WriteLine($"      判定为陆地的格元 {landCells:N0} / {raster.IsLand.Length:N0}" +
                          $"（{landCells / (double)raster.IsLand.Length:P1}）");
        Console.WriteLine();

        // ── 3. 海平面与连通性 ──
        Step(3, 7, "海平面（§6.2 冰川旋回）与 INV-8 连通性");
        double ice = GlacialCycle.IceVolume(year, seed);
        double seaNow = GlacialCycle.SeaLevelOffsetM(year, seed);
        double tempOff = GlacialCycle.GlobalTempOffsetC(year, seed);
        double seaLow = GlacialCycle.LowestSeaLevelM();

        Console.WriteLine($"      冰量 {ice:F3} · 海平面 {seaNow:F1} m · 全球温度偏移 {tempOff:F2} ℃");
        Console.WriteLine();

        // §6.2 的波形取舍：偏斜（默认）vs 纯正弦。两条都打出来，偏差一目了然。
        Console.WriteLine("      波形锚点对照（偏斜 · 正弦 · 真实）：");
        foreach (var (anchor, skew, sin, real) in GlacialCycle.CompareWaveforms(seed))
            Console.WriteLine($"        {anchor,-22} {skew:F3} · {sin:F3} · {real}");
        Console.WriteLine();

        var atNow = raster.AnalyzeConnectivity(seaNow);
        PrintSeparation($"当前海平面 {seaNow,7:F1} m", atNow);

        // INV-8 的判据就在这一行：最低海平面（−120 m）下仍不得连通
        var atLow = raster.AnalyzeConnectivity(seaLow);
        PrintSeparation($"最低海平面 {seaLow,7:F1} m", atLow);

        // 陆桥的解析判据（标定阶段二分用的就是它），与上面的栅格连通性互为印证
        var br = field.WorstBridge();
        Console.WriteLine($"      陆桥判据：C{br.A} ↔ C{br.B} 洋面宽 {br.GapDeg:F1}°" +
                          $"（需 ≥ {PlanetField.MinOceanGapDeg:F0}°，中点场值 {br.MidField:F3}）" +
                          $" → {(field.HasBridge() ? "❌ 太窄" : "✅ 充足")}");
        Console.WriteLine();

        // ── 4. 渲染 ──
        Step(4, 7, "光线投射渲染（星球主视图）");
        var opt = new RenderOptions
        {
            Width = size,
            Height = size,
            CameraLat = camLat,
            CameraLon = camLon,
            SunLat = sunLat,
            SunLon = sunLon,
            SeaLevelM = seaNow,
            GlobalTempOffsetC = tempOff,
            Climate = climate,
            Clouds = clouds,
            Stars = stars,
            Atmosphere = atmo,
            YearLabel = WorldConfig.FormatYear(year),
        };
        Console.WriteLine($"      相机 {camLat:F0}°/{camLon:F0}° · 太阳 {sunLat:F0}°/{sunLon:F0}°");

        var t0 = sw.Elapsed;
        byte[] sphere = PlanetRenderer.Render(raster, seed, opt);
        string spherePath = Path.Combine(outDir, "planet.png");
        PngWriter.Write(spherePath, size, size, sphere);
        Console.WriteLine($"      {spherePath}  {size}×{size}  " +
                          $"({(sw.Elapsed - t0).TotalSeconds:F1} 秒，{opt.SuperSample}× 超采样)");
        Console.WriteLine();

        // ── 5. 等距圆柱地图 ──
        Step(5, 7, "等距圆柱地图（核对与贴图用）");
        WriteMap(outDir, "map_biome.png", raster, opt with { Mode = PlanetColorMode.Biome }, res, graticule);
        WriteMap(outDir, "map_elevation.png", raster, opt with { Mode = PlanetColorMode.Elevation }, res, graticule);
        WriteMap(outDir, "map_landsea_low.png", raster,
                 opt with { Mode = PlanetColorMode.LandSea, SeaLevelM = seaLow }, res, graticule);
        Console.WriteLine();

        // ── 6. 球体网格与 OBJ 导出（M2-E，交给 Unity 的东西）──
        if (mesh)
        {
            Step(6, 7, "球体网格与 OBJ 导出（M2-E）");
            var mopt = new MeshOptions
            {
                Segments = meshRes,
                Rings = Math.Max(2, meshRes / 2),
                ShellSegments = Math.Max(32, meshRes / 2),
                ShellRings = Math.Max(16, meshRes / 4),
                SeaLevelM = seaNow,
                GlobalTempOffsetC = tempOff,
                Climate = climate,
                VerticalExaggeration = zExag,
                OceanShell = shells,
                AtmosphereShell = shells && atmo,
                CloudShell = shells && clouds,
            };

            var t1 = sw.Elapsed;
            var pm = PlanetMesh.Build(raster, mopt);
            var st = pm.Stats;

            Console.WriteLine($"      分段 {mopt.Segments} × {mopt.Rings}（壳层 {mopt.ShellSegments} × {mopt.ShellRings}）" +
                              $" · 垂直放大 {zExag:0.##}×");
            Console.Write("      面数");
            foreach (var kv in st.FacesPerObject) Console.Write($"  {kv.Key} {kv.Value:N0}");
            Console.WriteLine();
            Console.WriteLine($"      顶点 {st.VertexCount:N0} · 面 {st.FaceCount:N0} · 三角 {st.TriangleCount:N0}");
            Console.WriteLine($"      半径范围 {st.MinRadius:F2} → {st.MaxRadius:F2} units" +
                              $"（真实比例下为 {Sphere.Radius + (st.MinElevationM - seaNow) / PlanetMesh.MetresPerUnit:F2}" +
                              $" → {Sphere.Radius + (st.MaxElevationM - seaNow) / PlanetMesh.MetresPerUnit:F2}）");
            Console.WriteLine($"      高程范围 {st.MinElevationM:N0} → {st.MaxElevationM:N0} m");

            // 网格面积 vs 栅格面积：两者若对不上，说明顶点采样或绕序写错了。
            // 这是 M2-E 唯一的实质性自检，比「文件生成了没有」有意义得多。
            Console.Write("      网格面积占比（按面片积分）");
            foreach (var c in Geography.Continents)
                Console.Write($"  C{c.Id} {st.SharesByContinent[c.Id]:P2}/{area.Shares[c.Id]:P2}");
            Console.WriteLine();
            // ★ 两边必须<b>在同一个海平面下</b>比。网格是按 seaNow 建的
            //（--year -21000 时是 −120 m），而 field.MeasureAreaShares() 量的是
            // 「metaball 场 > 0.5」这个与海平面无关的大陆掩膜，标定在 0 m 上。
            // 早先拿这两者相减，等于拿 −120 m 的网格去比 0 m 的场：
            // 平年只差 0.03 个百分点（看着 ✅），末次冰盛期报 0.238 —— 那 0.2 不是
            // 网格错了，是大陆架真的露出来了。判据本身在骗人，故改为同海平面比栅格。
            double refLand = raster.LandFractionAt(seaNow);
            double dLand = Math.Abs(st.LandFraction - refLand);
            Console.WriteLine($"      网格全球陆地 {st.LandFraction:P2} · 栅格 {refLand:P2}" +
                              $"（同为 {seaNow:F1} m）" +
                              $" · 差 {(dLand * 100):F3} 个百分点 → {(dLand < 0.01 ? "✅ 一致" : "❌ 网格未复现栅格")}");
            Console.WriteLine($"      绕序朝外 {st.OutwardRatio:P3}（反向三角 {st.InwardFacingTriangles:N0} / {st.TriangleCount:N0}）" +
                              $" → {(st.WindingIsOutward ? "✅" : "❌ 模型会内翻，检查 BuildSphere 的顶点顺序")}");
            foreach (var kv in st.TrianglesPerObject)
                Console.WriteLine($"        {kv.Key,-12} 反向 {st.InwardPerObject.GetValueOrDefault(kv.Key):N0} / {kv.Value:N0}");
            Console.WriteLine($"        最差面 {st.WorstFacingObject}：法线与径向夹角的余弦 {st.WorstFacingDot:F3}" +
                              $"（+1 = 完全朝外，0 = 与球面相切，负 = 反向）");
            Console.WriteLine($"      ⚠️ 网格按 {zExag:0.##}× 垂直放大导出（§7.4 的真实比例下地形不可见）。");
            Console.WriteLine("         放大是径向单调变换，不改变任何拓扑，INV-8 结论不受影响；Unity 运行时取 1.0。");

            foreach (var f in PlanetMesh.WriteObj(outDir, pm))
                Console.WriteLine($"      {f}  {new FileInfo(f).Length / 1024.0 / 1024.0:F2} MB");
            Console.WriteLine($"      （{(sw.Elapsed - t1).TotalSeconds:F1} 秒）");
            Console.WriteLine();
        }

        // ── 7. 部落落位与地形场的一致性（M3 回归检查） ──
        Step(7, 7, "部落落位 vs 地表高程（M3 回归，不参与判定）");
        var world = Geography.Build(seed, field);
        int drowned = 0, offshore = 0;
        double maxDelta = 0, sumDelta = 0;
        foreach (var t in world.Tribes)
        {
            double h = raster.SampleElevationM(t.Lat, t.Lon);
            if (h <= seaNow) { drowned++; if (raster.ContinentAt(t.Lat, t.Lon) == 0) offshore++; }
            double d = Math.Abs(h - t.ElevationM);
            sumDelta += d;
            if (d > maxDelta) maxDelta = d;
        }
        Console.WriteLine($"      部落点落在此海平面之下的：{drowned} / {world.Tribes.Count}");
        Console.WriteLine($"        其中不在任何大陆上的：{offshore}");
        Console.WriteLine($"      部落海拔（落位值）与地表高程（栅格采样）之差：" +
                          $"均值 {sumDelta / world.Tribes.Count:N0} m · 最大 {maxDelta:N0} m");
        Console.WriteLine("      ↑ M3 起部落海拔直接取自同一地形场，故此差应只剩栅格离散误差；");
        Console.WriteLine("        M1 时代这里读的是另一个模型的公式值，是 1883 m 的系统性偏差。");
        Console.WriteLine();
        PrintPlacement(world.Placement);

        sw.Stop();
        Console.WriteLine($"耗时 {sw.Elapsed.TotalSeconds:F2} 秒");

        bool ok = atLow.ContinentsSeparated;
        Console.WriteLine(ok
            ? "M2 验收：5 大陆在最低海平面下仍互不连通 ✅"
            : "M2 验收：出现跨大陆陆桥 ❌");
        return ok ? 0 : 1;
    }

    private static void PrintSeparation(string label, PlanetRaster.ConnectivityResult r)
    {
        string verdict = r.ContinentsSeparated ? "✅ 互不连通" : "❌ 出现跨大陆陆桥";
        Console.WriteLine($"      {label} → {verdict}（连通分量 {r.ComponentCount} · 陆格 {r.LandCells:N0}）");
        foreach (var b in r.BridgingComponents)
            Console.WriteLine($"        跨陆分量：{b}");
    }

    private static void WriteMap(string dir, string name, PlanetRaster raster,
                                 RenderOptions opt, int width, bool graticule = true)
    {
        int height = width / 2;
        var buf = PlanetRenderer.RenderEquirectMap(raster, opt, width, height, graticule);
        string path = Path.Combine(dir, name);
        PngWriter.Write(path, width, height, buf);
        Console.WriteLine($"      {path}  {width}×{height}");
    }

    // ══════════════════════════════════════════════════════════════════
    //  verify
    // ══════════════════════════════════════════════════════════════════

    private static int Verify(string[] args)
    {
        string dbPath = ArgStr(args, "--db", DefaultOut);
        long seed = ArgLong(args, "--seed", WorldConfig.DefaultSeed);

        if (!File.Exists(dbPath))
        {
            Console.Error.WriteLine($"找不到 {dbPath}，先跑 build-world。");
            return 1;
        }

        Banner();
        var field = PlanetField.Build(seed);
        var world = Geography.Build(seed, field);
        var members = MemberBuilder.BuildGenesis(seed, world.Tribes);
        var results = Invariants.RunAll(dbPath, seed, world, members, field);
        PrintResults(results);

        int failed = results.Count(x => x.Status == CheckStatus.Fail);
        return failed == 0 ? 0 : 1;
    }

    // ══════════════════════════════════════════════════════════════════
    //  emit-names
    // ══════════════════════════════════════════════════════════════════

    private static int EmitNames(string[] args)
    {
        string dir = ArgStr(args, "--out", Path.Combine("Assets", "Data", "Names"));
        Directory.CreateDirectory(dir);

        for (int c = 1; c <= 5; c++)
        {
            string path = Path.Combine(dir, $"naming_{c}.json");
            File.WriteAllText(path, NameGen.EmitJson(c), Encoding.UTF8);
            Console.WriteLine($"{path}  ({NameGen.StyleName(c)}，组合空间 {NameGen.CombinationSpace(c):0.00e+0})");
        }
        return 0;
    }

    // ══════════════════════════════════════════════════════════════════
    //  derive —— L2 程序层的现场演示
    // ══════════════════════════════════════════════════════════════════

    private static int Derive(string[] args)
    {
        long seed = ArgLong(args, "--seed", WorldConfig.DefaultSeed);
        int c = (int)ArgLong(args, "--c", 1);
        // 默认必须是 1，不能是 0：--t 是<b>从 1 数起</b>的大陆内部部落号，
        // JoinTribeIndex(1, 0) = -1，会直接撞进 Identity.Derive 的范围校验。
        // 也就是说 `humen derive`（不带任何参数）曾经是必崩的。
        int t = (int)ArgLong(args, "--t", 1);
        int seq = (int)ArgLong(args, "--seq", 1001);

        // 使用者填错区间是<b>用法错误</b>，不是程序异常：给一句能照着改的话，
        // 别把 .NET 的堆栈倒出来 —— 那既没说是哪个参数，也没说该填几。
        if (c < 1 || c > WorldConfig.ContinentCount)
            return UsageError($"--c 大陆号须在 1~{WorldConfig.ContinentCount}，收到 {c}。");
        if (t < 1 || t > WorldConfig.TribesPerContinent)
            return UsageError($"--t 部落号须在 1~{WorldConfig.TribesPerContinent}" +
                              $"（每大陆 {WorldConfig.TribesPerContinent} 个部落），收到 {t}。");
        if (seq < 1)
            return UsageError($"--seq 部落内序号须 ≥ 1，收到 {seq}。");

        Banner();
        Console.WriteLine("L2 程序层派生 —— 不建库、不落盘、零存储。");
        Console.WriteLine();

        foreach (int s in new[] { seq, seq + 1, seq + 2 })
        {
            var m = Identity.Derive(seed, c, Identity.JoinTribeIndex(c, t), s,
                                    IdSource.Derived, IBirthCurve.Unavailable);

            Console.WriteLine($"  {m.Id,-22} {m.FullName,-24} {m.Gender,-7} " +
                              $"生于 {WorldConfig.FormatYear(m.BirthYear)}   " +
                              $"有档案={Identity.HasId(m)}");
        }

        Console.WriteLine();
        Console.WriteLine("  ↑ 这些人是真实存在的：ID、姓名、性别都唯一确定，");
        Console.WriteLine("    但 members 表里没有他们的任何一行 —— 需要时重算即可（INV-35）。");
        Console.WriteLine();
        Console.WriteLine("  注：出生年显示「年代未知」，因为还没有人口史（tribe_history 为空）。");
        Console.WriteLine("      L2 的出生年靠累积出生曲线求逆，等 M8 跑出人口曲线后即可填上。");
        return 0;
    }

    // ══════════════════════════════════════════════════════════════════
    //  inspect
    // ══════════════════════════════════════════════════════════════════

    private static int Inspect(string[] args)
    {
        string dbPath = ArgStr(args, "--db", DefaultOut);
        if (!File.Exists(dbPath))
        {
            Console.Error.WriteLine($"找不到 {dbPath}");
            return 1;
        }

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();

        Banner();
        Console.WriteLine($"库：{dbPath}  ({new FileInfo(dbPath).Length / 1024.0 / 1024.0:F2} MB)");
        Console.WriteLine();

        Section(conn, "各表行数", """
            SELECT 'continents',      COUNT(*) FROM continents
            UNION ALL SELECT 'rivers',        COUNT(*) FROM rivers
            UNION ALL SELECT 'tributaries',   COUNT(*) FROM tributaries
            UNION ALL SELECT 'mineral_deposits', COUNT(*) FROM mineral_deposits
            UNION ALL SELECT 'tribes',        COUNT(*) FROM tribes
            UNION ALL SELECT 'members',       COUNT(*) FROM members
            UNION ALL SELECT 'tech_state',    COUNT(*) FROM tech_state
            UNION ALL SELECT 'id_ledger',     COUNT(*) FROM id_ledger
            UNION ALL SELECT 'events',        COUNT(*) FROM events
            UNION ALL SELECT 'tribe_history', COUNT(*) FROM tribe_history
            """, "表", "行数");

        Section(conn, "部落（前 12 个）", """
            SELECT tribe_id, continent_id, substr(name,1,12),
                   printf('%.2f,%.2f', latitude, longitude),
                   printf('%.0fm', elevation_m), substr(biome,1,10),
                   printf('%.1fC', mean_temp_c), printf('%.0fmm', annual_rain_mm)
            FROM tribes ORDER BY continent_id, CAST(substr(tribe_id,3) AS INTEGER) LIMIT 12;
            """, "代号", "大陆", "名称", "经纬", "海拔", "群系", "均温", "降水");

        Section(conn, "members 抽样（每大陆第 1 个部落的 1/300/500/900 号）", """
            SELECT member_id, full_name, gender, age, printf('%.0f', birth_year), id_source
            FROM members
            WHERE member_no IN (1,300,500,900)
            ORDER BY member_id LIMIT 12;
            """, "ID", "姓名", "性别", "年龄", "出生年", "来源");

        Section(conn, "id_ledger 抽样", """
            SELECT continent, tribe, next_seq, minted_total FROM id_ledger
            ORDER BY continent, tribe LIMIT 6;
            """, "大陆", "部落", "next_seq", "已发");

        Section(conn, "矿产深度分带（大陆1）", """
            SELECT material, depth_band, depth_m, printf('%.3f', richness), human_extractable
            FROM mineral_deposits WHERE continent_id = 1 ORDER BY depth_m, material;
            """, "矿种", "分带", "深度m", "丰度", "可采");

        return 0;
    }

    // ══════════════════════════════════════════════════════════════════
    //  export-world —— 把演化结果倒成 Unity 能读的世界快照
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// M4b-2 / M4b-3 的前置。Core 与 Unity 之间只能靠<b>数据文件</b>通信
    /// （见 <c>PlanetBootstrap</c> 的依赖方向说明：net8.0 的程序集 Unity 加载不了），
    /// 而在此之前 Unity 侧只读得到一张 <c>map_biome.png</c> ——
    /// 部落、人口、技术、河流一个都拿不到，于是作者要的"活生生的世界"无从画起。
    ///
    /// 库里这些表本来就够了，故本命令<b>只读，不建库也不改库</b>：
    /// <list type="bullet">
    /// <item><c>tribes</c> —— 经纬 / 海拔 / 群系 / 土壤 / 距海（画标记与农田）</item>
    /// <item><c>tribe_history</c> —— 逐年人口 / 人均能耗 / 生活品质 / 材料等级（<b>时间轴</b>）</item>
    /// <item><c>tech_state</c> —— 每项技术的习得年份（阶段特征）</item>
    /// <item><c>rivers</c> / <c>tributaries</c> —— spline_json 折线</item>
    /// <item><c>continents</c> —— 用于视角与配色</item>
    /// </list>
    ///
    /// 输出格式刻意贴着 Unity 的 <c>JsonUtility</c>：它<b>不支持嵌套容器</b>
    /// （<c>List&lt;List&lt;float&gt;&gt;</c>）也不支持 <c>Dictionary</c>，
    /// 故折线点写成 <c>{"lat":..,"lon":..}</c> 对象数组，全树只用
    /// "对象 + 对象数组 + 基本类型" —— Unity 侧定义几个 <c>[Serializable]</c> 类即可直接反序列化，
    /// 不需要引入 Newtonsoft。
    /// </summary>
    private static int ExportWorld(string[] args)
    {
        string dbPath = ArgStr(args, "--db", DefaultOut);
        string outPath = ArgStr(args, "--out",
            Path.Combine("unity", "Assets", "StreamingAssets", "World", "world_view.json"));
        bool pretty = args.Contains("--pretty");

        if (!File.Exists(dbPath))
        {
            Console.Error.WriteLine($"找不到 {dbPath}");
            return 1;
        }

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();

        var meta = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT key, value FROM world_meta;";
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) meta[rd.GetString(0)] = rd.GetString(1);
        }

        // 只有地理骨架的库也能导出 —— 但导出来会是一片"没有人的世界"，
        // 3D 视图上什么都看不到，排查起来会以为是 Unity 侧的 bug。故直接挡在这里。
        if (meta.GetValueOrDefault("evolved") != "1")
        {
            Console.Error.WriteLine($"✗ {dbPath} 还没跑过演化（world_meta.evolved ≠ 1）。");
            Console.Error.WriteLine("  先跑 `humen evolve`，再导出 —— 否则库里只有地理骨架，没有部落历史可画。");
            return 1;
        }

        double MetaNum(string k) =>
            double.TryParse(meta.GetValueOrDefault(k, "0"), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double v) ? v : 0;

        double startYear = MetaNum("start_year"), endYear = MetaNum("end_year");

        // ── 大陆 ──
        var continents = new List<object>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT continent_id, name, climate_zone, center_lat, center_lon,
                       area_ratio, agriculture_feasible, domesticable_species, mineral_richness
                FROM continents ORDER BY continent_id;
                """;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                continents.Add(new
                {
                    id = rd.GetInt32(0),
                    name = rd.IsDBNull(1) ? null : rd.GetString(1),
                    climateZone = rd.IsDBNull(2) ? null : rd.GetString(2),
                    centerLat = rd.IsDBNull(3) ? 0 : Math.Round(rd.GetDouble(3), 5),
                    centerLon = rd.IsDBNull(4) ? 0 : Math.Round(rd.GetDouble(4), 5),
                    areaRatio = rd.IsDBNull(5) ? 0 : Math.Round(rd.GetDouble(5), 5),
                    agricultureFeasible = !rd.IsDBNull(6) && rd.GetInt32(6) != 0,
                    domesticableSpecies = rd.IsDBNull(7) ? 0 : rd.GetInt32(7),
                    mineralRichness = rd.IsDBNull(8) ? null : rd.GetString(8),
                });
        }

        // ── 河流与支流 ──（折线由 spline_json 的 [[lat,lon],…] 转成对象数组）
        var rivers = new List<object>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT river_id, continent_id, name, source_lat, source_lon,
                       mouth_lat, mouth_lon, spline_json, length_km, avg_discharge_m3s
                FROM rivers ORDER BY river_id;
                """;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                rivers.Add(new
                {
                    id = rd.GetString(0),
                    continentId = rd.IsDBNull(1) ? 0 : rd.GetInt32(1),
                    name = rd.IsDBNull(2) ? null : rd.GetString(2),
                    sourceLat = rd.IsDBNull(3) ? 0 : Math.Round(rd.GetDouble(3), 5),
                    sourceLon = rd.IsDBNull(4) ? 0 : Math.Round(rd.GetDouble(4), 5),
                    mouthLat = rd.IsDBNull(5) ? 0 : Math.Round(rd.GetDouble(5), 5),
                    mouthLon = rd.IsDBNull(6) ? 0 : Math.Round(rd.GetDouble(6), 5),
                    lengthKm = rd.IsDBNull(8) ? 0 : Math.Round(rd.GetDouble(8), 3),
                    dischargeM3s = rd.IsDBNull(9) ? 0 : Math.Round(rd.GetDouble(9), 3),
                    points = ParseSpline(rd.IsDBNull(7) ? null : rd.GetString(7)),
                });
        }

        var tributaries = new List<object>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT tributary_id, river_id, name, confluence_t, spline_json,
                       length_km, basin_area_km2, tribe_capacity
                FROM tributaries ORDER BY tributary_id;
                """;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                tributaries.Add(new
                {
                    id = rd.GetString(0),
                    riverId = rd.IsDBNull(1) ? null : rd.GetString(1),
                    name = rd.IsDBNull(2) ? null : rd.GetString(2),
                    confluenceT = rd.IsDBNull(3) ? 0 : Math.Round(rd.GetDouble(3), 5),
                    lengthKm = rd.IsDBNull(5) ? 0 : Math.Round(rd.GetDouble(5), 3),
                    basinAreaKm2 = rd.IsDBNull(6) ? 0 : Math.Round(rd.GetDouble(6), 3),
                    tribeCapacity = rd.IsDBNull(7) ? 0 : rd.GetInt32(7),
                    points = ParseSpline(rd.IsDBNull(4) ? null : rd.GetString(4)),
                });
        }

        // ── 部落逐年曲线（时间轴）──
        var history = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        var lastPop = new Dictionary<string, long>(StringComparer.Ordinal);   // 曲线末端人口
        int stepGuess = 0;
        string prevTribe = "";
        double prevYear = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT tribe_id, year, population, energy_per_capita, life_quality, material_tier
                FROM tribe_history ORDER BY tribe_id, year;
                """;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                string id = rd.GetString(0);
                if (!history.TryGetValue(id, out var list)) history[id] = list = new List<object>();
                double year = rd.IsDBNull(1) ? 0 : rd.GetDouble(1);
                long pop = rd.IsDBNull(2) ? 0 : rd.GetInt64(2);

                // 采样步长从第一条拥有两帧的曲线反推 —— 供 Unity 时间轴定位，
                // 免得"步长"这个数字在 Unity 侧再写一遍（两处默认值会各自漂移）。
                // 行按 (tribe_id, year) 有序，故上一行的年就是同一部落的前一帧。
                if (stepGuess == 0 && list.Count == 1 && prevTribe == id)
                    stepGuess = (int)Math.Round(year - prevYear);
                prevTribe = id;
                prevYear = year;

                list.Add(new
                {
                    year = Math.Round(year, 2),
                    population = pop,
                    energyPerCapita = rd.IsDBNull(3) ? 0 : Math.Round(rd.GetDouble(3), 3),
                    lifeQuality = rd.IsDBNull(4) ? 0 : Math.Round(rd.GetDouble(4), 4),
                    materialTier = rd.IsDBNull(5) ? 0 : rd.GetInt32(5),
                });
                lastPop[id] = pop;
            }
        }

        // ── 每部落的技术与习得年份 ──
        var techs = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        var techIds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);   // 只看 id，供派生用
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT tribe_id, tech_id, acquired_year, variant
                FROM tech_state ORDER BY tribe_id, acquired_year;
                """;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                string id = rd.GetString(0);
                string techId = rd.GetString(1);
                if (!techs.TryGetValue(id, out var list)) techs[id] = list = new List<object>();
                if (!techIds.TryGetValue(id, out var set)) techIds[id] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(techId);
                list.Add(new
                {
                    id = techId,
                    year = rd.IsDBNull(2) ? 0 : Math.Round(rd.GetDouble(2), 2),
                    variant = rd.IsDBNull(3) ? null : rd.GetString(3),
                });
            }
        }

        // ── 部落本体 ──
        var tribeList = new List<object>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT tribe_id, continent_id, river_id, tributary_id, name,
                       latitude, longitude, elevation_m, biome, mean_temp_c, annual_rain_mm,
                       population, parent_tribe_id, founded_year,
                       season_amp_c, coldest_month_c, dist_to_sea_km, topsoil_m, soil_type, agriculture_factor
                FROM tribes ORDER BY continent_id, tribe_id;
                """;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                string id = rd.GetString(0);
                var hist = history.GetValueOrDefault(id);
                var own = techs.GetValueOrDefault(id) ?? new List<object>();

                // 聚落形态与农田形态：从技术集合派生，供 3D 侧选模型。
                // ⚠️ 刻意放在 CLI 而不是 Unity 里 —— 判定要看技术 id 的语义，
                //    那是 Core 的知识；Unity 只该拿到"画什么"这个结论。
                string? settlement = SettlementOf(
                    techIds.GetValueOrDefault(id) ?? EmptyIds, out string? farm);

                tribeList.Add(new
                {
                    id,
                    continentId = rd.IsDBNull(1) ? 0 : rd.GetInt32(1),
                    riverId = rd.IsDBNull(2) ? null : rd.GetString(2),
                    tributaryId = rd.IsDBNull(3) ? null : rd.GetString(3),
                    name = rd.IsDBNull(4) ? id : rd.GetString(4),
                    lat = rd.IsDBNull(5) ? 0 : Math.Round(rd.GetDouble(5), 5),
                    lon = rd.IsDBNull(6) ? 0 : Math.Round(rd.GetDouble(6), 5),
                    elevationM = rd.IsDBNull(7) ? 0 : Math.Round(rd.GetDouble(7), 2),
                    biome = rd.IsDBNull(8) ? null : rd.GetString(8),
                    meanTempC = rd.IsDBNull(9) ? 0 : Math.Round(rd.GetDouble(9), 3),
                    annualRainMm = rd.IsDBNull(10) ? 0 : Math.Round(rd.GetDouble(10), 2),
                    // 优先用时间轴末端的人口 —— tribes.population 也是终值，
                    // 但取曲线末端可以保证"标记大小"与"时间轴划到最后一帧"自洽，
                    // 两者若不一致，视图自己就会前后矛盾。
                    finalPopulation = lastPop.GetValueOrDefault(id, rd.IsDBNull(11) ? 0 : rd.GetInt64(11)),
                    parentId = rd.IsDBNull(12) ? null : rd.GetString(12),
                    foundedYear = rd.IsDBNull(13) ? 0 : Math.Round(rd.GetDouble(13), 2),
                    coldestMonthC = rd.IsDBNull(15) ? 0 : Math.Round(rd.GetDouble(15), 3),
                    distToSeaKm = rd.IsDBNull(16) ? 0 : Math.Round(rd.GetDouble(16), 2),
                    topsoilM = rd.IsDBNull(17) ? 0 : Math.Round(rd.GetDouble(17), 3),
                    soilType = rd.IsDBNull(18) ? null : rd.GetString(18),
                    agricultureFactor = rd.IsDBNull(19) ? 0 : Math.Round(rd.GetDouble(19), 4),
                    settlement,
                    farmland = farm,
                    history = hist ?? new List<object>(),
                    techs = own,
                });
            }
        }

        var doc = new
        {
            schemaVersion = 1,
            generatedBy = "humen export-world",
            seed = meta.GetValueOrDefault("seed", "?"),
            source = new { db = dbPath, evolvedTechsKnown = meta.GetValueOrDefault("evolved_techs_known", "?") },
            timeline = new
            {
                startYear,
                endYear,
                startYearDisplay = meta.GetValueOrDefault("start_year_display", ""),
                endYearDisplay = meta.GetValueOrDefault("end_year_display", ""),
                historyStepYears = stepGuess,
                firstAgricultureYear = MetaNum("evolved_first_agriculture_year"),
                firstIronYear = MetaNum("evolved_first_iron_year"),
                firstIndustrialYear = MetaNum("evolved_first_industrial_year"),
            },
            continents,
            rivers,
            tributaries,
            tribes = tribeList,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        string json = JsonSerializer.Serialize(doc, new JsonSerializerOptions
        {
            WriteIndented = pretty,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        File.WriteAllText(outPath, json, new UTF8Encoding(false));

        var fi = new FileInfo(outPath);
        Console.WriteLine($"导出 → {outPath}");
        Console.WriteLine($"      大陆 {continents.Count} · 河流 {rivers.Count} · 支流 {tributaries.Count} · " +
                          $"部落 {tribeList.Count} · 时间轴步长 {stepGuess} 年");
        Console.WriteLine($"      {fi.Length / 1024.0 / 1024.0:0.00} MB" +
                          (pretty ? "（--pretty）" : "（紧凑；加 --pretty 可读版）"));
        return 0;
    }

    /// <summary>
    /// 把 <c>spline_json</c> 的 <c>[[lat,lon],…]</c> 转成 JsonUtility 能吃下的
    /// <c>[{"lat":..,"lon":..},…]</c>。Unity 的序列化器不支持嵌套容器，这是硬约束。
    /// 解析失败返回空数组而不是抛 —— 一条河画不出来不该让整个世界导不出来。
    /// </summary>
    private static List<object> ParseSpline(string? json)
    {
        var outp = new List<object>();
        if (string.IsNullOrWhiteSpace(json)) return outp;
        try
        {
            using var d = JsonDocument.Parse(json);
            foreach (var pt in d.RootElement.EnumerateArray())
            {
                if (pt.ValueKind != JsonValueKind.Array || pt.GetArrayLength() < 2) continue;
                outp.Add(new
                {
                    lat = Math.Round(pt[0].GetDouble(), 5),
                    lon = Math.Round(pt[1].GetDouble(), 5),
                });
            }
        }
        catch (JsonException)
        {
            // 单条折线坏了就当作没有这条折线
        }
        return outp;
    }

    /// <summary>
    /// 从技术集合派生"画成什么样"。返回 <c>(聚落形态, 农田形态)</c>，两者都可为 null。
    ///
    /// ⚠️ 这个映射<b>是我写的</b>（同 <c>PowerSourceOf</c>），不是 design.md 规定的，
    /// 记在待裁里等作者过目 —— 尤其是"哪种技术算哪种房子"这件事，
    /// 属于作者的美术口径，不该由引擎单方面决定。
    /// </summary>
    private static readonly HashSet<string> EmptyIds = new(StringComparer.Ordinal);

    /// <summary>
    /// 从技术集合派生"画成什么样"。返回 <c>(聚落形态, 农田形态)</c>，两者都可为 null。
    ///
    /// ⚠️ 这个映射<b>是我写的</b>（同 <c>PowerSourceOf</c>），不是 design.md 规定的，
    /// 记在待裁里等作者过目 —— "哪种技术算哪种房子"属于作者的美术口径。
    ///
    /// ⚠️ <b>档位只有两档，这不是偷懒，是树里 `cat: 建筑` 就只有两个节点</b>（见 待裁 ⑲）。
    /// 附带一条我踩过的坑，写在这里免得后人再踩：<b>不要从节点 id 反推它是什么</b>。
    /// 我起初按名字把 <c>city_stage</c> 当成"城市"，实际它的字段是
    /// <c>cn: 市井演出/剧场 · cat: 文化 · kind: bridge</c> —— 是街头的戏台，
    /// 只因 <c>tag: [leisure, city]</c> 里带个 city。要判类别必须读 <c>cat</c>，不能读名字。
    /// </summary>
    private static string? SettlementOf(HashSet<string> ids, out string? farmland)
    {
        // 农田：会种地就有田；再有犁与粮仓，才谈得上成片的耕地。
        farmland = !ids.Contains("planting") ? null
                 : ids.Contains("plow") || ids.Contains("granary") ? "plowed_field"
                 : "field";

        if (ids.Contains("mudbrick")) return "mudbrick_village";   // 土坯房聚落
        if (ids.Contains("settlement")) return "village";
        return null;   // 石器时代：只有营地，没有"建筑"可画
    }

    private static void Section(Microsoft.Data.Sqlite.SqliteConnection conn, string title, string sql, params string[] headers)
    {
        Console.WriteLine($"── {title} " + new string('─', Math.Max(0, 66 - title.Length)));
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        // 先读进内存，再按实际内容定列宽 —— 否则长值会被截断，看数据时反而误导
        var rows = new List<string[]>();
        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read())
            {
                var cells = new string[rd.FieldCount];
                for (int i = 0; i < rd.FieldCount; i++)
                    cells[i] = rd.IsDBNull(i) ? "-" : Convert.ToString(rd.GetValue(i), CultureInfo.InvariantCulture) ?? "-";
                rows.Add(cells);
            }
        }

        var widths = new int[headers.Length];
        for (int i = 0; i < headers.Length; i++)
        {
            // 中文表头按 2 列宽算
            widths[i] = headers[i].Length * 2;
            foreach (var row in rows)
                if (i < row.Length)
                    widths[i] = Math.Max(widths[i], row[i].Length);
        }

        Console.WriteLine("  " + string.Join("  ", headers.Select((h, i) => PadTo(h, widths[i]))));
        foreach (var row in rows)
            Console.WriteLine("  " + string.Join("  ", row.Select((c, i) => PadTo(c, widths[Math.Min(i, widths.Length - 1)]))));

        if (rows.Count == 0) Console.WriteLine("  (空)");
        Console.WriteLine();
    }

    // ══════════════════════════════════════════════════════════════════
    //  输出
    // ══════════════════════════════════════════════════════════════════

    private static void Banner()
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  蓝星 Humen · 文明进化模拟器 · M4 气候与植被                 ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }

    // ══════════════════════════════════════════════════════════════════
    //  climate —— M4 的气候体检（§13 的验收口径就在这里落地）
    // ══════════════════════════════════════════════════════════════════

    // ══════════════════════════════════════════════════════════════════
    //  terrain —— M2 地形场的体检（§13.3 高原问题的量尺）
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 陆地高程分布 + mask 分布。
    ///
    /// 之所以单独做一个命令：§13.3 那条「陆地是一整块高原」的结论是对的，
    /// 但量它的脚本当时没留下来，于是"改完有没有变好"无从回答。
    /// 裁定 ① 要动高程映射，就必须先有一把**能重复量的尺子**。
    ///
    /// ⚠️ 一切占比按 <b>cos(纬度)</b> 面积加权。等距圆柱格网上每格看着一样大，
    /// 实际 80° 上一格只有赤道的 17 % —— 不加权会把极区放大五倍。
    /// </summary>
    private static int TerrainReport(string[] args)
    {
        long seed = ArgLong(args, "--seed", WorldConfig.DefaultSeed);

        Banner();
        Console.WriteLine($"种子     : {seed}");

        var field = PlanetField.Build(seed);

        // 1° 采样，与气候格网同分辨率，便于两份报告对照。
        const int Ny = 180, Nx = 360;
        var elevs = new List<double>(1 << 16);
        var masks = new List<double>(1 << 16);
        var fields = new List<double>(1 << 16);
        var byContinent = new Dictionary<int, List<double>>();

        for (int iy = 0; iy < Ny; iy++)
        {
            double lat = 90.0 - (iy + 0.5) * 1.0;
            double w = Math.Cos(lat * Math.PI / 180.0);

            for (int ix = 0; ix < Nx; ix++)
            {
                double lon = -180.0 + (ix + 0.5) * 1.0;
                if (!field.IsLandAt(lat, lon)) continue;

                double e = field.ElevationM(lat, lon);
                // 面积加权：把这一格按 w 重复若干次（w ∈ (0,1]，乘 1000 取整）
                int reps = Math.Max(1, (int)Math.Round(w * 1000));
                for (int k = 0; k < reps; k++)
                {
                    elevs.Add(e);
                    masks.Add(field.MaskAt(lat, lon));
                    fields.Add(field.FieldAt(lat, lon));
                }

                int c = field.NearestContinent(lat, lon);
                if (!byContinent.TryGetValue(c, out var l)) byContinent[c] = l = new List<double>();
                for (int k = 0; k < reps; k++) l.Add(e);
            }
        }

        elevs.Sort();
        masks.Sort();
        fields.Sort();

        Console.WriteLine($"陆地采样 : {elevs.Count:N0} 个加权样本（1° 格网 · cos 纬度加权）");
        Console.WriteLine();

        Console.WriteLine("── 陆地高程分位（m）──");
        Console.Write("  分位 ");
        foreach (int p in new[] { 1, 5, 10, 20, 40, 50, 60, 80, 90, 99 }) Console.Write($"{p,7}%");
        Console.WriteLine();
        Console.Write("  海拔 ");
        foreach (int p in new[] { 1, 5, 10, 20, 40, 50, 60, 80, 90, 99 })
            Console.Write($"{Pct(elevs, p),8:F0}");
        Console.WriteLine();
        Console.WriteLine();

        Console.WriteLine("── 各高度以下的陆地占比 ──");
        foreach (int t in new[] { 0, 100, 300, 500, 1000, 1500, 2000 })
        {
            double frac = (double)LowerBound(elevs, t) / elevs.Count;
            Console.WriteLine($"  < {t,5} m : {frac,7:P2}   {Bar(frac)}");
        }
        Console.WriteLine();

        Console.WriteLine("── mask 分布（看它饱和在哪 —— 这是「没有沿海低地」的根因所在）──");
        Console.WriteLine("  mask 是 0~1 的软陆海权重，高程由它插值而来。");
        Console.WriteLine("  若陆地几乎全挤在 mask = 1.0，说明 mask 早已饱和，");
        Console.WriteLine("  高程模型<b>失去了区分「近岸」与「内陆」的能力</b>。");
        Console.WriteLine();
        Console.WriteLine("    mask 区间        陆地占比");
        double[] edges = { 0.50, 0.55, 0.60, 0.65, 0.70, 0.80, 0.90, 1.0001 };
        for (int i = 0; i < edges.Length - 1; i++)
        {
            int lo = LowerBound(masks, edges[i]);
            int hi = LowerBound(masks, edges[i + 1]);
            double frac = (double)(hi - lo) / masks.Count;
            Console.WriteLine($"  {edges[i]:F2} ~ {Math.Min(edges[i + 1], 1.00):F2}   {frac,10:P2}   {Bar(frac)}");
        }
        Console.WriteLine();

        Console.WriteLine("── 陆地上的 field 分布（mask 由它线性映射而来）──");
        Console.WriteLine($"  §7.3 的 land 判据是 field > {PlanetField.Threshold}；");
        Console.WriteLine($"  mask 在 field ∈ [threshold±{PlanetField.MaskHalfWidth}] 上由 0 升到 1，");
        Console.WriteLine($"  故 field ≥ {PlanetField.Threshold + PlanetField.MaskHalfWidth:F2} 的陆地一律 mask = 1。");
        Console.WriteLine();
        Console.Write("  分位 ");
        foreach (int p in new[] { 5, 25, 50, 75, 90, 99 }) Console.Write($"{p,8}%");
        Console.WriteLine();
        Console.Write("  field");
        foreach (int p in new[] { 5, 25, 50, 75, 90, 99 }) Console.Write($"{Pct(fields, p),9:F3}");
        Console.WriteLine();
        Console.WriteLine();

        Console.WriteLine("── 分大陆（高程分位，m）──");
        Console.WriteLine("  大陆   样本     P10     P50     P90    最低    最高");
        foreach (var kv in byContinent.OrderBy(k => k.Key))
        {
            var l = kv.Value; l.Sort();
            Console.WriteLine($"  C{kv.Key,-3} {l.Count,7:N0} {Pct(l, 10),7:F0} {Pct(l, 50),7:F0} " +
                              $"{Pct(l, 90),7:F0} {l[0],7:F0} {l[^1],7:F0}");
        }
        Console.WriteLine();

        return 0;
    }

    /// <summary>已排序列表上的百分位（线性插值）。</summary>
    private static double Pct(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        double x = (p / 100.0) * (sorted.Count - 1);
        int i = (int)x;
        return i >= sorted.Count - 1 ? sorted[^1]
             : sorted[i] + (x - i) * (sorted[i + 1] - sorted[i]);
    }

    /// <summary>已排序列表中第一个 ≥ <paramref name="v"/> 的下标。</summary>
    private static int LowerBound(List<double> sorted, double v)
    {
        int lo = 0, hi = sorted.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (sorted[mid] < v) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    private static string Bar(double frac)
    {
        int n = (int)Math.Round(frac * 50);
        return new string('█', Math.Clamp(n, 0, 50));
    }

    private static int ClimateReport(string[] args)
    {
        long seed = ArgLong(args, "--seed", WorldConfig.DefaultSeed);
        double year = ArgDouble(args, "--year", WorldConfig.PresentYear);

        Banner();
        Console.WriteLine($"种子     : {seed}");

        var field = PlanetField.Build(seed);
        var grid = ClimateGrid.Build(field);

        double ice = GlacialCycle.IceVolume(year, seed);
        double sea = GlacialCycle.SeaLevelOffsetM(year, seed);
        double tempOff = GlacialCycle.GlobalTempOffsetC(year, seed);

        Console.WriteLine($"年代     : {WorldConfig.FormatYear(year)}");
        Console.WriteLine($"冰川旋回 : 冰量 {ice:F3} · 海平面 {sea:F1} m · 全球温度偏移 {tempOff:F2} ℃");
        Console.WriteLine($"气候格网 : {ClimateGrid.Nx}×{ClimateGrid.Ny}（1°）· " +
                          $"陆地格 {grid.LandCells:N0}" +
                          $"（{grid.LandCells / (double)(ClimateGrid.Nx * ClimateGrid.Ny):P2}）");
        Console.WriteLine();

        // 扫描与判定都在 Humen.Core 里，与 verify 的不变式<b>共用同一份实现</b>。
        // 这里只负责把结果排版成人能读的样子。
        var a = ClimateAudit.Evaluate(grid, seed, year);

        PrintBiomeShares(a.Now, a.Zonal);
        PrintLatitudeBands(a.Now, a.Zonal);
        PrintClimateTerms(a.Now);
        PrintAcceptance(a);
        return 0;
    }

    private static void PrintBiomeShares(ClimateAudit.ClimateScan now, ClimateAudit.ClimateScan zonal)
    {
        Console.WriteLine("── 群系占比（陆地，按 cos 纬度加权）──");
        Console.WriteLine("  15 行全列，含 0.00 % 的行 —— 「某个词在本世界里根本取不到」");
        Console.WriteLine("  本身就是结论，藏起来只会让人以为它存在却很少。");
        Console.WriteLine();
        Console.WriteLine("  群系              M1 降维版    M4 完整版    变化");
        for (int i = 0; i < Climate.BiomeCount; i++)
        {
            double a = zonal.ShareByBiome[i], b = now.ShareByBiome[i];
            Console.WriteLine($"  {Climate.BiomeName((byte)i),-12}  {a,9:P2}   {b,9:P2}   {b - a,+7:P2}");
        }
        Console.WriteLine();
    }

    private static void PrintLatitudeBands(ClimateAudit.ClimateScan now, ClimateAudit.ClimateScan zonal)
    {
        double totalW = now.BandWeight.Sum();

        Console.WriteLine("── 纬度带 × 群系（每带 10°，两半球合并）──");
        Console.WriteLine("  「占陆地」= 本带的陆地占全球陆地的比例（按 cos 纬度加权）。");
        Console.WriteLine("  各带占比 × 占陆地，纵向求和即得上面那张全球表 —— 两表由此自洽。");
        Console.WriteLine();
        Console.WriteLine("  纬度带        占陆地      冰盖        沙漠        森林    M4 主要群系（前 3）");
        for (int b = 0; b < ClimateAudit.Bands; b++)
        {
            double lo = b * 10.0, hi = lo + 10.0;
            if (zonal.BandWeight[b] <= 0) continue;      // 该带整圈没有陆地

            Console.WriteLine($"  {lo,3:F0}° ~ {hi,3:F0}°   " +
                $"{now.BandWeight[b] / totalW,8:P2}   " +
                $"{ClimateAudit.ShareOf(now, b, Climate.IceCap),8:P2}   " +
                $"{ClimateAudit.ShareOf(now, b, Climate.Desert),8:P2}   " +
                $"{ClimateAudit.ShareOf(now, b, ClimateAudit.GreenBiomes),8:P2}   " +
                $"{ClimateAudit.TopBiomes(now, b, 3)}");
        }
        Console.WriteLine();
    }

    private static void PrintClimateTerms(ClimateAudit.ClimateScan now)
    {
        Console.WriteLine("── §7.6 分项（陆地格平均，正值 = 增温 / 增雨）──");
        Console.WriteLine($"  洋流项 T_ocean          {now.MeanOceanAnomalyC,+7:F2} ℃");
        Console.WriteLine($"  大陆度 P_continentality {now.MeanContinentalityMm,+7:F0} mm");
        Console.WriteLine($"  迎风坡 P_orographic     {now.MeanOrographicMm,+7:F0} mm");
        Console.WriteLine($"  季风   P_monsoon        {now.MeanMonsoonMm,+7:F0} mm");
        Console.WriteLine($"  年均温 / 年降水          {now.MeanTempC:F1} ℃ / {now.MeanRainMm:F0} mm");
        Console.WriteLine($"  季节振幅（均值）         ±{now.MeanSeasonAmpC:F1} ℃");
        Console.WriteLine();
    }

    /// <summary>§13「赤道绿、副热带沙、极地白；冰期可驱动」的逐条判定。</summary>
    private static void PrintAcceptance(ClimateAudit.Acceptance a)
    {
        Console.WriteLine("── §13 验收口径 ──");
        foreach (var v in new[] { a.EquatorGreen, a.SubtropDesert, a.PolarIce }) Check(v);

        Console.WriteLine();
        Console.WriteLine($"  冰期可驱动：{WorldConfig.FormatYear(a.GlacialYear)}" +
                          $"（海平面 {a.GlacialSeaM:F1} m · 温度偏移 {a.GlacialOffsetC:F2} ℃）");
        double iceNow = ClimateAudit.ShareOfAll(a.Now, Climate.IceCap);
        double iceLgm = ClimateAudit.ShareOfAll(a.Glacial, Climate.IceCap);
        Console.WriteLine($"    {a.GlacialDrive.Glyph} 冰盖占比 {iceNow:P2} → {iceLgm:P2}" +
                          $"（×{a.GlacialDrive.Got:F2}，判据 ≥ ×{ClimateAudit.GlacialRatioNeed:F1}）");
        Console.WriteLine();

        if (a.AllPass) return;
        Console.WriteLine("  ⚠ 有验收项未达标 —— 详见上方 ❌。");
        Console.WriteLine();
    }

    /// <summary>
    /// 与 M1 降维版并排打一条。降维基线给的是<b>同一口径的另一个模型</b>，
    /// 用来回答「这一条到底是 M4 挣来的，还是纬度分带本来就够」。
    /// </summary>
    private static void Check(ClimateAudit.Verdict v)
    {
        Console.WriteLine($"  {v.Glyph} {v.What}（{v.Priority}）");
        string bl = v.HasBaseline ? $"· 同口径 M1 降维版为 {v.Baseline:P2}" : "";
        Console.WriteLine($"      实测主口径 {v.Got:P2}（判据 {v.Need:P0}）{bl}");
    }

    private static void Step(int i, int n, string what)
        => Console.WriteLine($"[{i}/{n}] {what}");

    private static void PrintResults(List<CheckResult> results)
    {
        Console.WriteLine("┌────────────────────┬────────┬──────────────────────────────────────────┐");

        foreach (var r in results)
        {
            string id = Pad(r.Id, 18);
            string glyph = Pad(r.Glyph, 6);
            Console.WriteLine($"│ {id} │ {glyph} │ {r.Title}");

            // 明细缩进换行，太长就折
            foreach (var line in Wrap(r.Detail, 66))
                Console.WriteLine($"│ {"",18} │ {"",6} │   {line}");
        }

        Console.WriteLine("└────────────────────┴────────┴──────────────────────────────────────────┘");
        int pass = results.Count(x => x.Status == CheckStatus.Pass);
        int skip = results.Count(x => x.Status == CheckStatus.Skip);
        int fail = results.Count(x => x.Status == CheckStatus.Fail);
        Console.WriteLine($"通过 {pass} · 跳过 {skip} · 失败 {fail}");
    }

    /// <summary>打印 M3 的部落落位质量报告（design.md §7.5 四条约束的满足情况）。</summary>
    private static void PrintPlacement(Geography.PlacementReport p)
    {
        Console.WriteLine($"      部落落位：完全满足 §7.5 四条约束的 {p.FullyCompliant} / {p.Total}");
        Console.WriteLine($"        海拔>海平面+5m {p.AboveSeaPlus5} · 非冰盖 {p.IceFree} · " +
                          $"坡度<15° {p.SlopeUnder15} · 间距≥30km {p.SpacingOk}");
        Console.WriteLine($"        最小间距 {p.MinSpacingKm:F1} km · 最大坡度 {p.WorstSlopeDeg:F1}°");
        Console.WriteLine($"      支流长度（§7.5 要主河的 25%~45%）：{p.TributariesShortOfSpec} 条不足 25%；" +
                          $"实测区间 {p.MinTributaryFrac:P0}~{p.MaxTributaryFrac:P0}；" +
                          $"单条最大养育能力 {p.MaxTribeCapacity}");
    }

    private static string Pad(string s, int n)
        => s.Length >= n ? s[..n] : s + new string(' ', n - s.Length);

    /// <summary>只补齐、不截断（打印数据时用，宁可错位也不要少显示字段）。</summary>
    private static string PadTo(string s, int n)
        => s.Length >= n ? s : s + new string(' ', n - s.Length);

    private static IEnumerable<string> Wrap(string s, int width)
    {
        for (int i = 0; i < s.Length; i += width)
        {
            // 中文按 2 列宽，这里够用即可
            int take = Math.Min(width, s.Length - i);
            yield return s.Substring(i, take);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  摘要
    // ══════════════════════════════════════════════════════════════════

    private static string BuildSummaryJson(long seed, Geography.World world,
                                           IReadOnlyList<Member> members, WorldDb.DbStats stats)
    {
        var byContinent = new List<object>();
        foreach (var c in Geography.Continents)
        {
            var tribes = world.Tribes.Where(t => t.ContinentId == c.Id).ToList();
            byContinent.Add(new
            {
                continent = c.Id,
                name = c.Name,
                climateZone = c.ClimateZone,
                agriculture = c.Agriculture.ToString(),
                domesticates = c.Domesticates,
                minerals = c.Minerals,
                tribes = tribes.Count,
                members = tribes.Count * WorldConfig.MembersPerTribe,
                meanTempC = Math.Round(tribes.Average(t => t.MeanTempC), 2),
                meanRainMm = Math.Round(tribes.Average(t => t.AnnualRainMm), 1),
                minElevationM = tribes.Min(t => t.ElevationM),
                maxElevationM = tribes.Max(t => t.ElevationM),
                biomes = tribes.GroupBy(t => t.Biome).ToDictionary(g => g.Key, g => g.Count()),
                namingStyle = NameGen.StyleName(c.Id),
                nameSpace = NameGen.CombinationSpace(c.Id),
            });
        }

        var genderByContinent = new List<object>();
        foreach (var c in Geography.Continents)
        {
            var ms = members.Where(m => m.ContinentId == c.Id).ToList();
            genderByContinent.Add(new
            {
                continent = c.Id,
                male = ms.Count(m => m.Gender == Gender.Male),
                female = ms.Count(m => m.Gender == Gender.Female),
            });
        }

        var doc = new
        {
            generatedBy = "humen build-world",
            schemaVersion = WorldDb.SchemaVersion,
            seed,
            timeline = new
            {
                startYear = WorldConfig.StartYear,
                endYear = WorldConfig.EndYear,
                startYearDisplay = stats.StartYearDisplay,
                endYearDisplay = stats.EndYearDisplay,
                totalSpanYears = WorldConfig.TotalSpanYears,
            },
            idWindow = new
            {
                start = WorldConfig.IdWindowStart,
                end = WorldConfig.IdWindowEnd,
                startDisplay = WorldConfig.FormatYear(WorldConfig.IdWindowStart),
                endDisplay = WorldConfig.FormatYear(WorldConfig.IdWindowEnd),
                note = WorldConfig.IdWindowNote,
                architecture = "three-layer v1.3 (L1 rows / L2 Derive() / L3 id_ledger)",
            },
            counts = new
            {
                continents = stats.Continents,
                rivers = stats.Rivers,
                tributaries = stats.Tributaries,
                tribes = stats.Tribes,
                genesisMembers = stats.Members,
                membersPerTribe = WorldConfig.MembersPerTribe,
            },
            identity = new
            {
                malePerTribe = WorldConfig.MalePerTribe,
                femalePerTribe = WorldConfig.FemalePerTribe,
                globalMale = members.Count(m => m.Gender == Gender.Male),
                globalFemale = members.Count(m => m.Gender == Gender.Female),
                ageRange = new[] { WorldConfig.MinAge, WorldConfig.MaxAge },
            },
            initialTech = WorldConfig.InitialTechSet,
            elevation = new { min = stats.MinElevationM, max = stats.MaxElevationM },
            db = new { file = stats.Path, sizeBytes = stats.SizeBytes },
            continents = byContinent,
            genderByContinent,
        };

        return JsonSerializer.Serialize(doc, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    // ══════════════════════════════════════════════════════════════════
    // ══════════════════════════════════════════════════════════════════
    //  evolve —— 把 30 万年的历史跑出来
    // ══════════════════════════════════════════════════════════════════

    private static int Evolve(string[] args)
    {
        long seed = ArgLong(args, "--seed", WorldConfig.DefaultSeed);
        string dbPath = ArgStr(args, "--db", DefaultOut);
        string treePath = ArgStr(args, "--file", "tech_tree.md");
        double step = ArgDouble(args, "--step", 25.0);
        double sample = ArgDouble(args, "--sample", 2500.0);
        int maxTribes = (int)ArgLong(args, "--max-tribes", 3000);
        bool carbon = args.Contains("--carbon");
        bool noWrite = args.Contains("--no-write");
        bool diag = args.Contains("--diag");

        // ── 技能增长标定的三个旋钮 ──
        // ⚠️ 没给默认值时<b>保持 EvolutionConfig 里的值</b>（用 NaN 作"未指定"哨兵），
        //    不能在 CLI 里再写一遍默认数 —— 那样两处默认值会各自漂移，
        //    跑出来对不上就会以为是引擎的问题。见 待裁 ⑮（标定数全部待作者过目）。
        double skillRate  = ArgDouble(args, "--skill-rate",  double.NaN);
        double ownedAccel = ArgDouble(args, "--owned-accel", double.NaN);
        double techAccel  = ArgDouble(args, "--tech-accel",  double.NaN);
        // ⚠️ 三态，不是 bool：不给开关时必须<b>跟随</b> EvolutionConfig 的默认值。
        //    曾经写成 `args.Contains("--expose-successors")` —— 于是不传开关时把默认的
        //    true 覆盖成了 false，而这一路又是静默的（下面那行提示只在显式传参时才打印）。
        //    后果：默认参数跑出来 104 项，与标定表里写的默认 130 项对不上，
        //    且 104 恰好等于"后继曝光=关"那条曲线的封顶值，看着像引擎的结构上限
        //    而不是一个命令行小 bug。三个 double 用 NaN 哨兵避开了这个坑，bool 当时漏了。
        bool? exposeSucc = args.Contains("--no-expose-successors") ? false
                         : args.Contains("--expose-successors")    ? true
                         : null;

        double from = WorldConfig.StartYear, to = WorldConfig.EndYear;
        string? span = ArgStr(args, "--years", null!);
        if (!string.IsNullOrEmpty(span))
        {
            var parts = span.Split("..", StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out from) ||
                !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out to))
                return UsageError("--years 形如 -12000..-8000");
            if (to <= from) return UsageError("--years 的终点必须大于起点");
        }

        Banner();

        // ── 技术树 ──
        TechTree tree;
        try { tree = TechTree.LoadFromFile(treePath); }
        catch (Exception ex) { Console.Error.WriteLine($"✗ 装载技术树失败：{ex.Message}"); return 1; }

        var refErrors = TechEffects.Validate(tree);
        if (refErrors.Count > 0)
        {
            Console.Error.WriteLine($"✗ 效果引用校验失败（{refErrors.Count} 条）：");
            foreach (string e in refErrors) Console.Error.WriteLine($"    {e}");
            return 1;
        }

        Console.WriteLine($"种子               : {seed}");
        Console.WriteLine($"技术树             : {treePath}（{tree.Count} 节点，最大链深 {tree.MaxDepth}）");
        Console.WriteLine($"演化区间           : {WorldConfig.FormatYear(from)} → {WorldConfig.FormatYear(to)}");
        Console.WriteLine($"步长 / 采样        : {step:0.#} 年 / {sample:0} 年");
        Console.WriteLine($"碳-气候耦合        : {(carbon ? "开（§4.6，Q-A8 实验分支）" : "关（默认，INV-26）")}");
        Console.WriteLine();

        // ── 地理骨架 ──
        // 引擎需要每个部落的经纬度/高程/气候/土壤。这些是 Geography 的产物，
        // 同一 seed 下必然与 build-world 写进库的那份逐位相同（INV-34），故此处重建是安全的。
        Step(1, 3, "重建地理骨架（与 build-world 同 seed，结果逐位相同）");
        var field = PlanetField.Build(seed);
        var world = Geography.Build(seed, field);
        Console.WriteLine($"      部落 {world.Tribes.Count} · 大陆 {Geography.Continents.Length}");

        // ── 跑 ──
        Step(2, 3, "演化");
        var def = new EvolutionConfig();
        var cfg = new EvolutionConfig
        {
            Seed = seed,
            StepYears = step,
            MaxTribes = maxTribes,
            CarbonClimateCoupling = carbon,
            SkillBaseRate   = double.IsNaN(skillRate)  ? def.SkillBaseRate   : skillRate,
            SkillOwnedAccel = double.IsNaN(ownedAccel) ? def.SkillOwnedAccel : ownedAccel,
            SkillTechAccel  = double.IsNaN(techAccel)  ? def.SkillTechAccel  : techAccel,
            ExposeSuccessors = exposeSucc ?? def.ExposeSuccessors,
        };

        // 无条件打印<b>生效值</b>（读 cfg，不读命令行）——
        // 之前加了个"只在显式传参时才打印"的省事条件，结果正是它把上面那个
        // 静默覆盖掩盖了整整一轮标定：日志里没有这行，就不会有人发现默认值没生效。
        // 一行日志换一个不会重犯的错误，划算。
        Console.WriteLine($"技能标定           : r={cfg.SkillBaseRate:G3} · 本域加速={cfg.SkillOwnedAccel:G3}/项 · " +
                          $"整体加速={cfg.SkillTechAccel:G3}/项 · 后继曝光={(cfg.ExposeSuccessors ? "开" : "关")}" +
                          (exposeSucc is null ? "（默认）" : "（命令行指定）"));

        var eng = new EvolutionEngine(tree, world, cfg);
        var history = new List<(double, int, double, double, double, int, double)>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 分段跑，每段结束采一次样 —— 采样点在引擎外部，故不干扰演化本身（INV-12）。
        double t = from;
        int lastReport = 0;
        var samples = new List<(double Year, double Pop, int Tribes, double MaxEnergy, int Techs)>();
        while (t < to - 1e-9)
        {
            double next = Math.Min(t + sample, to);
            eng.RunTo(next);
            t = next;

            foreach (var tr in eng.Tribes)
                if (tr.Alive)
                    history.Add((eng.Year, tr.Id, tr.Population, tr.EnergyPerCapita,
                                 tr.LifeQuality, (int)Math.Round(tr.MaterialTier), tr.ProdMultiplier));

            var aliveNow = eng.Tribes.Where(x => x.Alive).ToList();
            samples.Add((eng.Year, aliveNow.Sum(x => x.Population), aliveNow.Count,
                         aliveNow.Count == 0 ? 0 : aliveNow.Max(x => x.EnergyPerCapita),
                         eng.TechFrontierUnion.Count));

            // 每 ~10% 报一次进度
            int pct = (int)(100.0 * (eng.Year - from) / (to - from));
            if (pct >= lastReport + 10)
            {
                lastReport = pct / 10 * 10;
                Console.WriteLine($"      {pct,3}%  {WorldConfig.FormatYear(eng.Year),-16} " +
                                  $"部落 {aliveNow.Count,5} · 总人口 {aliveNow.Sum(x => x.Population),12:N0} · " +
                                  $"技术前沿 {eng.TechFrontierUnion.Count,3}");
            }
        }
        sw.Stop();

        Console.WriteLine($"      完成，耗时 {sw.Elapsed.TotalSeconds:F1} 秒（{eng.Events.Count:N0} 条事件）");
        Console.WriteLine();

        // ── 战报 ──
        Step(3, 3, "战报");
        PrintEvolutionReport(eng, samples);
        PrintDiscoveryTimeline(eng, tree);
        PrintUnreachedNodes(eng, tree);
        if (diag) PrintDiagnostics(eng, tree);

        // ── 演化侧不变量自检 ──
        // M1 那套 20 项判的是"世界生成"的产物，演化把它们推出界它一声不响。
        var violations = eng.VerifyInvariants();
        if (violations.Count == 0)
        {
            Console.WriteLine("  ── 不变量自检 ──");
            Console.WriteLine($"      ✓ INV-13/16/23/25/26/28 与数值健康全部通过（{eng.Tribes.Count} 个部落）");
            Console.WriteLine();
        }
        else
        {
            Console.Error.WriteLine($"✗ 演化侧不变量被跑破 {violations.Count} 处：");
            foreach (string v in violations.Take(20)) Console.Error.WriteLine($"    {v}");
            if (violations.Count > 20) Console.Error.WriteLine($"    …还有 {violations.Count - 20} 处");
            return 1;
        }

        if (noWrite)
        {
            Console.WriteLine();
            Console.WriteLine("--no-write：未落库。");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"落库 → {dbPath}");
        var stats = WorldDb.WriteEvolution(dbPath, eng, history);
        Console.WriteLine($"      部落 {stats.TribesTotal}（存活 {stats.TribesAlive} · 灭绝 {stats.TribesExtinct} · 分裂而生 {stats.TribesBorn}）");
        Console.WriteLine($"      事件 {stats.Discoveries:N0} 发现 · {stats.Forgottens:N0} 失传 · " +
                          $"{stats.Fissions:N0} 分裂 · {stats.Extinctions:N0} 灭绝 · {stats.Shocks:N0} 冲击");
        Console.WriteLine($"      技术前沿 {stats.TechsEverKnown}/{tree.Count}");

        return 0;
    }

    /// <summary>人口 / 技术前沿随时间的曲线，以及几个该被检验的时间点。</summary>
    private static void PrintEvolutionReport(
        EvolutionEngine eng, List<(double Year, double Pop, int Tribes, double MaxEnergy, int Techs)> samples)
    {
        Console.WriteLine("  ── 全球曲线 ──");
        Console.WriteLine("      年份              存活部落      总人口        最高人均能耗   技术前沿");
        foreach (var s in samples)
        {
            if (s.Tribes == 0 && s.Year < WorldConfig.EndYear - 1) continue;
            Console.WriteLine($"      {WorldConfig.FormatYear(s.Year),-16} {s.Tribes,8}  {s.Pop,14:N0}  " +
                              $"{s.MaxEnergy,12:N0} kWh  {s.Techs,5}/{eng.Tree.Count}");
        }
        Console.WriteLine();

        // ⚠️ 排序必须用"链深"作主键，不能用人均能耗 —— 前工业时代几乎所有部落
        //    的人均能耗都是同一个锚点值（§4.5 的 2000 kWh），按它排会退化成按 id 排，
        //    于是"最先进的 10 个部落"会输出 1.1…1.10 这种毫无信息量的名单（实测踩过）。
        int Depth(TribeState t)
        {
            int bd = -1;
            foreach (string id in t.Techs) bd = Math.Max(bd, eng.Tree.DepthOf(id));
            return bd;
        }
        var alive = eng.Tribes.Where(t => t.Alive)
                              .OrderByDescending(Depth)
                              .ThenByDescending(t => t.Techs.Count)
                              .ThenByDescending(t => t.EnergyPerCapita)
                              .ToList();
        Console.WriteLine("  ── 最先进的 10 个部落（按技术链深，同深比技术数）──");
        Console.WriteLine("      部落        人口      人均能耗    材料     技术  链深  生活体验  最高技术");
        foreach (var t in alive.Take(10))
        {
            string top = "—"; int bd = -1;
            foreach (string id in t.Techs)
                if (eng.Tree.DepthOf(id) > bd) { bd = eng.Tree.DepthOf(id); top = eng.Tree[id].Name; }
            Console.WriteLine($"      {t.Code,-11} {t.Population,8:N0}  {t.EnergyPerCapita,9:N0}  " +
                              $"{TechPhysics.MaterialName((int)Math.Round(t.MaterialTier)),-7} {t.Techs.Count,4}  " +
                              $"{bd,4}  {t.LifeQuality,7:F0}   {top}");
        }
        Console.WriteLine();

        // ── 材料阶梯的时刻（INV-13 的实证）──
        var ladder = new (string Tech, string Name, string Note)[]
        {
            ("pottery",    "陶器",     "tier 1 · 需 900 ℃"),
            ("copper_smelt","铜冶炼",  "tier 2 · 需 1085 ℃（§1.3 明说「锡冶炼 ≈ 极低」）"),
            ("bronze",     "青铜",     "tier 3 · 铜 + 锡，两种矿都得有"),
            ("iron",       "铁",       "tier 4 · 工艺温度 1200 ℃（块炼铁，非熔点 1538）"),
            ("coal_fuel",  "煤燃料",   "P5a 起飞 · 人均 20000 kWh"),
            ("refinery",   "炼油",     "50000 kWh"),
            ("nuclear_power","核能",   "300000 kWh —— §4.5 的核能锚点"),
        };
        Console.WriteLine("  ── 材料 / 能源阶梯首次出现的年份 ──");
        foreach (var (tech, name, note) in ladder)
        {
            var first = eng.Events.Where(e => e.Type == "discover" && e.TechId == tech)
                                  .OrderBy(e => e.Year).FirstOrDefault();
            Console.WriteLine(first is null
                ? $"      {name,-8} —— <b>从未出现</b>   {note}"
                : $"      {name,-8} {WorldConfig.FormatYear(first.Year),-16} {first.Description}   {note}");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// 未达成的节点逐个归因 —— 回答"技术前沿卡在 N 项"到底是<b>跑完了</b>还是<b>又卡住了</b>。
    ///
    /// 没有这一段，报告只说得出「前沿 129/175」，看不出剩下那几十项属于哪种：
    /// ① 前置链上没人走到过 → 树的设计问题或历史没走那条分支
    /// ② 全星球没有任何部落具备该禀赋 → 数据问题（词表对不上／矿没生成）
    /// ③ 全球技能最高值还不够 → 只是时间不够，继续跑会长
    /// ④ 够格了却没掷中 → <b>只有这一类才是引擎的锅</b>（发现率／扩散率太低）
    ///
    /// 前三类是设计结论，第四类是标定问题。混在一起看，会把"世界正常跑完"
    /// 误判成"引擎有 bug"，反之亦然 —— 之前 69 项封顶那次就是后者。
    /// </summary>
    private static void PrintUnreachedNodes(EvolutionEngine eng, TechTree tree)
    {
        var reached = eng.TechFrontierUnion;
        var missing = tree.Nodes.Where(n => !reached.Contains(n.Id)).ToList();
        if (missing.Count == 0)
        {
            Console.WriteLine($"  ── {tree.Count} 项技术全部达成 ──");
            Console.WriteLine();
            return;
        }

        // 当前世界"够得着"的上限：活着的部落里各技能域的最高值、实际能用上的禀赋并集。
        // ⚠️ 禀赋取 EffectiveGeo（含贸易换来的），否则会把"靠贸易才拿得到"的节点
        //    误判成"全星球没有" —— 那正是 battery 要铜+锌的情形。
        var maxSkill = new Dictionary<string, double>(StringComparer.Ordinal);
        var geo = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in eng.Tribes)
        {
            if (!t.Alive) continue;
            foreach (var kv in t.Skills)
                if (!maxSkill.TryGetValue(kv.Key, out double cur) || kv.Value > cur)
                    maxSkill[kv.Key] = kv.Value;
            geo.UnionWith(t.EffectiveGeo);
        }

        var g1 = new List<string>();   // 前置没人走到
        var g2 = new List<string>();   // 全星球无禀赋
        var g3 = new List<string>();   // 技能不够
        var g4 = new List<string>();   // 够格没掷中

        foreach (var n in missing)
        {
            string? pre = n.Requires.FirstOrDefault(p => tree.Has(p) && !reached.Contains(p));
            if (pre is not null) { g1.Add($"{n.Id}←{pre}"); continue; }

            string? g = n.Geo.FirstOrDefault(x => !geo.Contains(x));
            if (g is not null) { g2.Add($"{n.Id}缺{g}"); continue; }

            string? sk = n.Skills
                .Where(kv => maxSkill.GetValueOrDefault(kv.Key) < kv.Value)
                .Select(kv => $"{n.Id} {kv.Key}{maxSkill.GetValueOrDefault(kv.Key):F0}<{kv.Value}")
                .FirstOrDefault();
            if (sk is not null) { g3.Add(sk); continue; }

            g4.Add(n.Id);
        }

        Console.WriteLine($"  ── 未达成 {missing.Count} 项 / 共 {tree.Count} 项，逐条归因 ──");
        void Group(string label, List<string> nodes, string note)
        {
            if (nodes.Count == 0) return;
            Console.WriteLine($"      {label}（{nodes.Count}）{note}");
            Console.WriteLine("        " + string.Join("  ", nodes.Take(16)));
            if (nodes.Count > 16) Console.WriteLine($"        …还有 {nodes.Count - 16} 项");
        }
        Group("① 前置链没人走到", g1, "　← 历史没走那条分支，或树本身接不上");
        Group("② 全星球无此禀赋", g2, "　← 数据问题：词表对不上／矿没生成");
        Group("③ 全球技能还不够", g3, "　← 只是时间不够，继续跑会长");
        Group("④ 够格却没掷中", g4, "　← 只有这一类是引擎标定的锅");
        Console.WriteLine();
    }

    /// <summary>按年份列出"世界第一次掌握某技术"的清单 —— 这就是文明史。</summary>
    private static void PrintDiscoveryTimeline(EvolutionEngine eng, TechTree tree)
    {
        // ⚠️ §5.4 的初始技术集是<b>直接塞进部落</b>的，不产生 discover 事件。
        //    若不在此处补记，年表会把"第二个部落独立再发现用火"的那一年
        //    写成"世界首次掌握用火"，从而凭空造出"烽火传讯早于用火 8 万年"的假象（实测踩过）。
        var firsts = new Dictionary<string, (double Year, string Tribe)>(StringComparer.Ordinal);
        foreach (string id in WorldConfig.InitialTechSet)
            if (tree.Has(id))
                firsts[id] = (WorldConfig.StartYear, "开局");

        foreach (var e in eng.Events)
        {
            if (e.Type != "discover" || e.TechId is null) continue;
            if (!firsts.TryGetValue(e.TechId, out var cur) || e.Year < cur.Year)
                firsts[e.TechId] = (e.Year, e.Description.Split(' ')[0]);
        }

        var ordered = firsts.OrderBy(kv => kv.Value.Year).ToList();
        Console.WriteLine($"  ── 世界技术年表（共 {ordered.Count} 项被某个部落首先掌握）──");
        foreach (var (id, (year, tribe)) in ordered)
        {
            var n = tree[id];
            Console.WriteLine($"      {WorldConfig.FormatYear(year),-16} {n.Name,-12} {tribe,-10} " +
                              $"{n.Era} {n.Cat}  → {n.PowerSource}");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// 诊断：为什么技术没长出来。
    /// 打印领先部落的技能向量、候选前沿逐条的<b>阻塞原因</b>、以及全树的"从未有人够格"清单。
    /// </summary>
    private static void PrintDiagnostics(EvolutionEngine eng, TechTree tree)
    {
        var lead = eng.Tribes.Where(t => t.Alive).OrderByDescending(t => t.Techs.Count).First();

        Console.WriteLine($"  ── 诊断：领先部落 {lead.Code}（{lead.Techs.Count} 项技术，人口 {lead.Population:N0}）──");
        Console.WriteLine($"      地理禀赋：{string.Join("、", lead.Geo.OrderBy(x => x, StringComparer.Ordinal))}");
        Console.WriteLine($"      农业因子 {lead.AgricultureFactor:F3} · 承载 {lead.Capacity:N0} · 疆域 {lead.TerritoryKm2:N0}/{lead.TerritoryCapKm2:N0} km²");
        Console.WriteLine($"      知识保持 {lead.KnowledgeRetention:F3} · 制度容量 {lead.InstitutionCapacity:N0} · 组织效率 {lead.OrgEfficiency:F3}");
        Console.WriteLine();

        Console.WriteLine("      技能域（非零）：");
        var skills = lead.Skills.Where(kv => kv.Value > 0.01).OrderByDescending(kv => kv.Value).ToList();
        Console.WriteLine("        " + string.Join("  ", skills.Select(kv => $"{kv.Key}={kv.Value:F1}")));
        Console.WriteLine();

        Console.WriteLine($"      候选前沿 {lead.Frontier.Count} 项，逐条阻塞原因：");
        foreach (string id in lead.Frontier.OrderBy(x => x, StringComparer.Ordinal).Take(40))
        {
            var n = tree[id];
            var misses = new List<string>();
            foreach (var (d, need) in n.Skills)
                if (lead.GetSkill(d) < need) misses.Add($"技能 {d} {lead.GetSkill(d):F1}<{need}");
            foreach (string g in n.Geo)
                if (!lead.Geo.Contains(g)) misses.Add($"地理 缺{g}");
            string state = misses.Count == 0 ? "✓ 已够格（等掷骰）" : "✗ " + string.Join("；", misses);
            Console.WriteLine($"        {id,-22} {n.Name,-18} {state}");
        }
        Console.WriteLine();

        // 全树统计：有多少节点是"地球上任何部落都不可能够格"的
        double maxS = 0; foreach (var kv in lead.Skills) maxS = Math.Max(maxS, kv.Value);
        var geoUnion = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in eng.Tribes) geoUnion.UnionWith(t.Geo);

        var unreachableGeo = new List<string>();
        var unreachableSkill = new List<string>();
        foreach (var n in tree.Nodes)
        {
            if (n.Geo.Any(g => !geoUnion.Contains(g)))
                unreachableGeo.Add($"{n.Id}（需 {string.Join("/", n.Geo.Where(g => !geoUnion.Contains(g)))}）");
            else if (n.Skills.Count > 0 && n.Skills.All(kv => kv.Value > 100.0))
                unreachableSkill.Add(n.Id);
        }
        Console.WriteLine($"      全星球地理禀赋并集：{string.Join("、", geoUnion.OrderBy(x => x, StringComparer.Ordinal))}");
        Console.WriteLine($"      ⚠️ 全球没有任何部落具备其地理要求的节点：{unreachableGeo.Count} 个");
        foreach (string s in unreachableGeo.Take(15)) Console.WriteLine($"        · {s}");
        if (unreachableSkill.Count > 0)
            Console.WriteLine($"      ⚠️ 技能门槛 >100（永不可达）：{string.Join("、", unreachableSkill)}");
        Console.WriteLine();
    }

    // ══════════════════════════════════════════════════════════════════
    //  tech —— 技术树体检（INV-11）
    // ══════════════════════════════════════════════════════════════════

    private static int TechReport(string[] args)
    {
        string path = ArgStr(args, "--file", "tech_tree.md");
        string? one = ArgStr(args, "--node", null!);

        TechTree tree;
        try
        {
            tree = TechTree.LoadFromFile(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗ 装载技术树失败：{ex.Message}");
            return 1;
        }

        Banner();
        Console.WriteLine($"技术树：{path}");
        Console.WriteLine($"节点总数：{tree.Count}");
        Console.WriteLine();

        // ── 装载期硬校验：效果引用表必须落得住 ──────────────────────
        // 这一步曾救回 17 条静默失效的覆盖（见 TechEffects.Overrides 注释）。
        var refErrors = TechEffects.Validate(tree);
        if (refErrors.Count > 0)
        {
            Console.Error.WriteLine($"✗ 效果引用校验失败（{refErrors.Count} 条）：");
            foreach (string e in refErrors) Console.Error.WriteLine($"    {e}");
            Console.Error.WriteLine("  这些 id 命中不了覆盖表，效果会静默退化成默认推导 —— 必须先修。");
            return 1;
        }
        Console.WriteLine($"✓ 效果引用校验通过：{TechEffects.AuthoredCount} 条覆盖全部命中真实节点");
        Console.WriteLine();

        // ── 单节点模式 ──────────────────────────────────────────────
        if (!string.IsNullOrEmpty(one))
        {
            if (one == "missing")
            {
                PrintMissingNodes();
                return 0;
            }
            if (!tree.TryGet(one, out var n))
            {
                Console.Error.WriteLine($"✗ 没有节点 {one}（用 `humen tech --node missing` 看 §5.2 有而节点库没有的清单）");
                return 1;
            }
            Console.WriteLine($"── {n.Id} ──");
            Console.WriteLine($"  cn        {n.Name}");
            Console.WriteLine($"  era/cat   {n.Era} / {n.Cat}   → {n.PowerSource}");
            Console.WriteLine($"  kind      {n.Kind}");
            Console.WriteLine($"  pre       {(n.Requires.Length == 0 ? "（无，起点节点）" : string.Join(", ", n.Requires))}");
            Console.WriteLine($"  skill     {(n.Skills.Count == 0 ? "—" : string.Join(", ", n.Skills.Select(kv => $"{kv.Key}={kv.Value}")))}");
            Console.WriteLine($"  geo       {(n.Geo.Length == 0 ? "—" : string.Join(", ", n.Geo))}");
            Console.WriteLine($"  tag       {(n.Tags.Length == 0 ? "—" : string.Join(", ", n.Tags))}");
            Console.WriteLine($"  adopt     {n.Adopt}");
            Console.WriteLine($"  cost      {(n.Costs.Count == 0 ? "—" : string.Join(", ", n.Costs.Select(kv => $"{kv.Key}={kv.Value:0.##}")))}");
            Console.WriteLine($"  note      {n.Note}");

            var deps = tree.DependentsOf(n.Id);
            Console.WriteLine($"  后继({deps.Count})  {(deps.Count == 0 ? "—" : string.Join(", ", deps))}");

            // ── 效果：作者明写的还是我拟的？──
            var e = TechEffects.Of(n);
            Console.WriteLine();
            Console.WriteLine($"  效果  [{(TechEffects.IsAuthored(n.Id) ? "★ 作者明写（§5.2/§1.4/§1.5/§4.7 有出处）" : "○ 我拟的（按 cat+era 默认推导）")}]");
            void Ef(string label, double v)
            {
                if (Math.Abs(v) > 1e-12) Console.WriteLine($"        {label,-14} {v:0.###}");
            }
            Ef("P5a 生产力 +", e.ProdDelta);
            Ef("P5b 破坏力 +", e.DestructDelta);
            Ef("P3 能源锚点", e.EnergyAnchorKwh);
            Ef("P1b 保持率顶", e.RetentionCeiling);
            Ef("P1a 通信层级", e.InfoLevel);
            Ef("P4a 畜力", e.Beasts);
            Ef("P4a 机械", e.Machines);
            Ef("P6 体验 +", e.LifeDelta);
            Ef("P4b 容量 ×", e.CapacityMultiplier - 1.0);
            Ef("§4.7 抗逆 +", e.ShockDelta);
            Ef("§4.6 碳", e.CostCarbon);
            Ef("§4.6 土地", e.CostLand);
            Ef("§4.6 枯竭", e.CostDeplete);
            Ef("§4.6 撕裂", e.CostUnrest);
            Console.WriteLine($"        当前 MaxTemp   {TechPhysics.MaxTempOf(new[] { n.Id }):0} °C   （若这是唯一掌握的节点）");
            return 0;
        }

        // ── 层分布 ─────────────────────────────────────────────────
        Console.WriteLine("各层节点数：");
        foreach (var g in tree.Nodes.GroupBy(n => n.Era).OrderBy(g => g.Key, StringComparer.Ordinal))
            Console.WriteLine($"  {g.Key,-4} {g.Count(),3}  " + new string('█', Math.Min(g.Count(), 48)));
        Console.WriteLine();

        // ── 类别 → 力 ──────────────────────────────────────────────
        Console.WriteLine("类别 → 力（§5.3 可追溯性。⚠️ 这张映射是我拟的，见 TechTree.PowerSourceOf 注释）：");
        foreach (var (cat, power, count) in tree.CategoryBreakdown())
            Console.WriteLine($"  {cat,-7} → {power,-7} {count,3}");

        var unmapped = tree.UnmappedCategories().ToList();
        Console.WriteLine(unmapped.Count == 0
            ? "  ✓ 24 个类别全部有归宿"
            : $"  ✗ 有 {unmapped.Count} 个类别没有归宿：{string.Join(", ", unmapped)}");
        Console.WriteLine();

        // ── 技能域 ─────────────────────────────────────────────────
        var domains = tree.SkillDomains().ToList();
        Console.WriteLine($"技能域（{domains.Count} 个，受控词表应有 23）：");
        Console.WriteLine("  " + string.Join(" · ", domains));
        Console.WriteLine();

        // ── 起点节点 ───────────────────────────────────────────────
        var roots = tree.Nodes.Where(n => n.Requires.Length == 0).Select(n => n.Id).ToList();
        Console.WriteLine($"入度为零的起点节点（{roots.Count} 个）：");
        Console.WriteLine("  " + string.Join(", ", roots));
        Console.WriteLine();

        // ── 初始技术集核对（§5.4）──────────────────────────────────
        Console.WriteLine("§5.4 初始技术集核对：");
        foreach (string id in WorldConfig.InitialTechSet)
            Console.WriteLine($"  {(tree.Has(id) ? "✓" : "✗")} {id}");
        Console.WriteLine($"  {(tree.Has("fire_making") ? "✓" : "✗")} fire_making  （必须存在且<b>不</b>在初始集里，由发现引擎自发解锁）");
        Console.WriteLine();

        // ── 最深链条 ───────────────────────────────────────────────
        var (depthId, depth, chain) = LongestChain(tree);
        Console.WriteLine($"最长前置链：{depth} 级");
        Console.WriteLine("  " + string.Join(" → ", chain));
        Console.WriteLine();

        // ── 效果覆盖情况 ───────────────────────────────────────────
        Console.WriteLine($"效果覆盖：{TechEffects.AuthoredCount} / {tree.Count} 个节点的效果有出处，"
                        + $"其余 {tree.Count - TechEffects.AuthoredCount} 个走默认推导（我拟的）。");
        Console.WriteLine();

        // ── §5.2 归纳表 vs 节点库的缺口 ────────────────────────────
        Console.WriteLine($"§5.2 归纳表里点名、但节点库中没有的技术：{TechEffects.MissingNodes.Length} 项");
        Console.WriteLine("  （用 `humen tech --node missing` 看完整清单）");
        Console.WriteLine();

        Console.WriteLine("✓ INV-11 通过：无环、前置全可达、每节点有出处。");
        return 0;
    }

    private static void PrintMissingNodes()
    {
        Console.WriteLine("§5.2 归纳表点名、但 tech_tree.md 中没有对应节点的技术：");
        Console.WriteLine();
        foreach (var (concept, r) in TechEffects.MissingNodes)
            Console.WriteLine($"  ✗ {concept,-22} {r}");
        Console.WriteLine();
        Console.WriteLine("后果不是「少几个节点」那么轻：");
        Console.WriteLine("  · 没有 irrigation → §5.2 T4 的「灌溉 → 人口容量↑↑」无处落地");
        Console.WriteLine("  · 没有 law        → §1.7 的制度容量链（邓巴数→习俗→财产制→法律→货币→国家）断在中途，");
        Console.WriteLine("                      而它正是 §6.3 部落分裂判据的来源");
        Console.WriteLine("  · 没有 medicine   → §4.7 抗逆力的「抵御瘟疫」一路没有来源");
        Console.WriteLine();
        Console.WriteLine("另一种可能是这些概念被并进了近似节点（例如「语言」并入 counting）。");
        Console.WriteLine("是补节点还是合并，留作者裁定。");
    }

    /// <summary>
    /// 找最长前置链（按节点数）。
    /// 技术树已由 <see cref="TechTree"/> 校验为无环，故这里可以放心地做记忆化 DFS。
    /// </summary>
    private static (string Id, int Depth, List<string> Chain) LongestChain(TechTree tree)
    {
        var memo = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        List<string> Best(string id)
        {
            if (memo.TryGetValue(id, out var cached)) return cached;
            var node = tree[id];
            List<string> best = new();
            foreach (string p in node.Requires)
            {
                var c = Best(p);
                if (c.Count > best.Count) best = c;
            }
            var result = new List<string>(best) { id };
            memo[id] = result;
            return result;
        }

        List<string> longest = new();
        foreach (var n in tree.Nodes)
        {
            var c = Best(n.Id);
            if (c.Count > longest.Count) longest = c;
        }
        return (longest[^1], longest.Count, longest);
    }

    // ══════════════════════════════════════════════════════════════════
    //  参数解析
    // ══════════════════════════════════════════════════════════════════

    private static string ArgStr(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    private static long ArgLong(string[] args, string name, long fallback)
    {
        string s = ArgStr(args, name, "");
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : fallback;
    }

    private static double ArgDouble(string[] args, string name, double fallback)
    {
        string s = ArgStr(args, name, "");
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
    }
}
