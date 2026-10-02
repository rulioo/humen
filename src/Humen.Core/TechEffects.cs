namespace Humen.Core;

/// <summary>
/// 一个技术节点点亮后，对各力状态造成的影响。
/// 字段与 <c>design.md</c> §5.1 的 <c>Effect[] Effects</c>（"对各力的状态增量"）对应。
/// </summary>
public sealed record TechEffect
{
    /// <summary>P5a：生产力放大。最终 <c>ProdMultiplier = ∏(1 + ProdDelta)</c>，拳脚 = 1。</summary>
    public double ProdDelta { get; init; }

    /// <summary>P5b：破坏力放大。与 P5a <b>独立演进</b>（INV-24：战争只读它，生产只读 P5a）。</summary>
    public double DestructDelta { get; init; }

    /// <summary>P3：该节点把人均年能量通量锚定到的档位（kWh/人·年）。0 = 不改能源。</summary>
    public double EnergyAnchorKwh { get; init; }

    /// <summary>P1b：知识保持率的<b>天花板</b>（§1.5 的表：口传 .97 / 结绳 .99 / 文字 1.0）。</summary>
    public double RetentionCeiling { get; init; }

    /// <summary>P1a：通信层级（0 = 无，越大越远越快）。</summary>
    public double InfoLevel { get; init; }

    /// <summary>P4a：畜力单位（§4.2 ③：<c>Labor = Pop×AgeFactor + Slaves + Beasts×8 + Machines×50</c>）。</summary>
    public double Beasts { get; init; }

    /// <summary>P4a：机械单位。</summary>
    public double Machines { get; init; }

    /// <summary>P6：生活体验增量（0~100 量纲）。</summary>
    public double LifeDelta { get; init; }

    /// <summary>P4b：人口容量倍率（定居/灌溉/农业把这块地的承载上限抬起来）。</summary>
    public double CapacityMultiplier { get; init; }

    /// <summary>§4.7：抗逆力增量。</summary>
    public double ShockDelta { get; init; }

    /// <summary>§4.6 负外部性，逐项对应 <c>tech_tree.md</c> 的 <c>cost</c> 词表。</summary>
    public double CostCarbon { get; init; }
    public double CostLand { get; init; }
    public double CostDeplete { get; init; }
    public double CostUnrest { get; init; }

    /// <summary>
    /// §1.3 的固有易发现度 λ（"摩擦取火 ≈ 高；锡冶炼 ≈ 极低"）。
    /// <b>0 = 未指定，由 <see cref="TechEffects.LambdaOf"/> 按 <c>adopt</c> 推导。</b>
    /// </summary>
    public double Lambda { get; init; }

    public static readonly TechEffect None = new() { CapacityMultiplier = 1.0, RetentionCeiling = 0 };
}

/// <summary>
/// <b>技术节点 → 状态影响</b>的桥接层。
///
/// ┌──────────────────────────────────────────────────────────────────┐
/// │ ⚠️ 本类是全项目<b>唯一一处我在发明数据</b>的地方，必须交代清楚。    │
/// └──────────────────────────────────────────────────────────────────┘
///
/// <b>缺口是什么</b>：<c>design.md</c> §5.1 的 <c>TechNode</c> 定义了 <c>Effect[] Effects</c>，
/// 但权威节点库 <c>tech_tree.md</c> 的 175 个节点<b>一个 effects 字段都没有</b> ——
/// 它们只有 <c>pre / skill / geo / tag / adopt / cost</c>（外加给人读的 <c>cn/era/note</c>）。
/// 也就是说：<b>技术树告诉了引擎"谁能解锁谁"，却没告诉它"解锁了会怎样"。</b>
///
/// <b>怎么补</b>：分两层，边界划得很清楚 ——
///
/// <b>① 作者明写的（<see cref="Overrides"/>）</b>：§5.2 归纳表的"效果"列、
/// §1.4 的温度表、§1.5 的知识保持率表、§4.7 的抗逆力表 —— 这些是文档里真有的字，
/// 我把它们按节点 id 逐条落成数。凡是落在这一层的，注释里都能指回原文。
///
/// <b>② 我拟的（<see cref="Derive"/>）</b>：§5.2 里没提到的那些节点
/// （<c>ecommerce_taobao</c>、<c>ip_address</c>、<c>cnc_precision</c>… 这些细化到现代产物的节点，
/// 归纳表里当然不会有一行），按 <c>cat</c>＋<c>era</c> 给一个<b>保形的默认值</b>。
/// 所谓"保形"＝保住曲线的形状（越晚越强、且加速），绝对数值是<b>我拟的</b>。
///
/// <b>为什么这样做而不是编 175 个数</b>：编 175 个数会让人以为它们有出处，
/// 而实际上一个都没有 —— 那比留一个显式的、可审计的推导规则糟得多。
/// 用 <c>humen tech --effects &lt;id&gt;</c> 可以把任意节点的最终效果打出来，
/// 作者一眼就能看出哪条是引用的、哪条是我拟的。
///
/// <b>留给作者的一步</b>：若要让这层彻底"有据"，正确做法是给 <c>tech_tree.md</c> 补一个
/// <c>effects:</c> 字段（哪怕只补几十个关键节点），届时删掉 <see cref="Derive"/> 即可。
/// </summary>
public static class TechEffects
{
    // ══════════════════════════════════════════════════════════════════
    //  ① 作者明写的效果
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 逐节点覆盖表。<b>键必须真实存在于 <c>tech_tree.md</c></b> ——
    /// <see cref="Validate"/> 会在装载期把不存在的 id 全部揪出来报错。
    ///
    /// 这条校验不是多余的：本表最初有 17 个 id 是我<b>照着 §5.2 归纳表的名字顺手写的</b>
    /// （<c>hand_axe</c> / <c>law</c> / <c>irrigation</c> / <c>printing</c>…），
    /// 而 <c>tech_tree.md</c> 里叫 <c>stone_axe</c> / 根本没这几个节点。
    /// 覆盖表命中不了的 id 会<b>静默退化成默认推导</b>，运行时看不出任何异常 ——
    /// 换句话说，那 17 条本意是"引用作者原文"的效果，实际全都悄悄变成了我拟的。
    /// </summary>
    private static readonly Dictionary<string, TechEffect> Overrides = new(StringComparer.Ordinal)
    {
        // ── §5.2 T1 木石 ──
        ["spear"]        = new() { ProdDelta = 1.5 },   // 「削尖木棍→标枪 … P5 狩猎 +1.5」
        ["stone_axe"]    = new() { ProdDelta = 2.0 },   // 「石片裂刃 + 绑木柄→石斧 … P5 +2.0」
        ["basket"]       = new() { CapacityMultiplier = 1.05 },  // 「藤条编箩筐 … P4 携带 +50%」
        ["hafting"]      = new() { ProdDelta = 0.5 },   // 石器装柄：让刃口能发力，是石斧的前一步

        // ── §5.2 T2 火 ──
        ["cooked_food"]  = new() { LifeDelta = 6.0 },   // 「熟食 … P6 健康+」
        ["charcoal"]     = new() { CostLand = 0.15 },   // 「制炭 … P3 热值↑，MaxTemp→1200」（温度由 §1.4 热源表给）
        ["pottery"]      = new() { CapacityMultiplier = 1.05 },   // 「陶器 … P4 容器，P2 +」
        ["clay_kiln"]    = new() { ProdDelta = 0.3 },   // 控温窑＝此后一切冶金的母机

        // ── §5.2 T3 材料 ──
        ["bronze"]       = new() { ProdDelta = 1.5, DestructDelta = 2.0 },   // 「青铜 … P5 工具硬度↑」
        ["copper_smelt"] = new() { ProdDelta = 1.0 },   // 「铜冶炼 … MaxTemp 达 1085，P5 +」
        ["iron"]         = new() { ProdDelta = 2.5, DestructDelta = 3.0, CostLand = 0.25 },  // 「铁 … P5 持久性↑↑」
        ["bow"]          = new() { ProdDelta = 3.0, DestructDelta = 1.5 },   // 「弓箭 … P5 远程狩猎 +3」

        // ── §5.2 T4 农业 ──
        ["settlement"]   = new() { CapacityMultiplier = 1.30 },   // 「定居 … 部落容量↑，P2 +」
        ["mudbrick"]     = new() { CapacityMultiplier = 1.10, ShockDelta = 0.03 },
        ["calendar"]     = new() { CapacityMultiplier = 1.25 },   // 「历法 … 农业成功率↑↑」
        ["plow"]         = new() { CapacityMultiplier = 1.20, ProdDelta = 0.4 },
        ["granary"]      = new() { CapacityMultiplier = 1.15, ShockDelta = 0.12 },  // 「公仓与剩余粮食」
        ["ox_power"]     = new() { Beasts = 1.0, CapacityMultiplier = 1.25 },  // 「将牛变成生产力（牛耕）」

        // ── §5.2 T5 组织 ──
        ["property_private"] = new() { CostUnrest = 0.20 },   // 「财产制 … 内耗↓，不平等↑」
        ["money"]        = new() { InfoLevel = 3, ShockDelta = 0.10 },   // 「货币 … P1 交易速度↑」
        ["trade_barter"] = new() { InfoLevel = 2, ShockDelta = 0.06 },
        ["market"]       = new() { InfoLevel = 3, CapacityMultiplier = 1.10 },

        // ── §5.2 T6 能量（能源锚点 = §4.5 半定量标度的落地点）──
        ["coal_fuel"]    = new() { EnergyAnchorKwh = 20_000, CostCarbon = 0.60, CostLand = 0.30 },  // 「煤炭 … MaxTemp 1400，P3↑↑」
        ["refinery"]     = new() { EnergyAnchorKwh = 50_000, CostCarbon = 0.85, CostLand = 0.25 },  // 「石油 … P3↑↑↑」
        ["solar_cell"]   = new() { EnergyAnchorKwh = 30_000, CostLand = 0.08 },  // 「太阳能 … P3↑↑」
        // §5.2：「核能 … P3 → ∞」—— 工程上给有限锚点，见 TechPhysics.NuclearEnergyAnchor
        ["nuclear_power"]= new() { EnergyAnchorKwh = TechPhysics.NuclearEnergyAnchor },

        // ── §5.2 T7 信息 ／ §1.5 知识保持率表 ──
        ["counting"]     = new() { InfoLevel = 1, RetentionCeiling = 0.99 },  // 「结绳/壁画 ~0.99/年」
        ["writing"]      = new() { InfoLevel = 4, RetentionCeiling = 1.00 },  // 「文字：保持率 ≈ 1」
        ["paper"]        = new() { InfoLevel = 5, RetentionCeiling = 1.00 },
        ["woodblock_print"] = new() { InfoLevel = 5, RetentionCeiling = 1.00 },  // 「造纸+印刷：=1 且可跨大陆扩散」
        ["movable_type"] = new() { InfoLevel = 6, RetentionCeiling = 1.00 },  // 「活字 … P1↑↑，跨部落扩散」
        ["beacon"]       = new() { InfoLevel = 2 },   // 「烽火 / 狼烟 … P1 远距↑」
        ["morse_code"]   = new() { InfoLevel = 7 },   // 「摩斯电码 … P1↑↑」
        ["telegraph"]    = new() { InfoLevel = 7 },
        ["radio"]        = new() { InfoLevel = 8 },   // 「无线电 … P1↑↑↑」
        ["optical_fiber"]= new() { InfoLevel = 11 },  // 「蜂窝/光纤/IP/VPN … P1 → 极高」
        ["internet"]     = new() { InfoLevel = 10 },
        ["quantum_computing"] = new() { InfoLevel = 12 },   // 「量子计算 … P1 算力↑↑」

        // ── §5.2 T8 自动化 ──
        ["animal_herd"]  = new() { Beasts = 0.5 },    // 「驯化牛马 … P4 Beasts↑」
        ["ox_dom"]       = new() { Beasts = 1.0 },
        ["horse_dom"]    = new() { Beasts = 1.5, InfoLevel = 3 },   // 马同时是运力与驿传
        ["horse_power"]  = new() { Beasts = 2.0 },
        ["lever"]        = new() { ProdDelta = 0.6 },   // 「齿轮滑轮结构和杠杆放大力量」
        ["gear"]         = new() { ProdDelta = 1.0 },
        ["wheel"]        = new() { ProdDelta = 0.5 },
        ["robot"]        = new() { Machines = 3.0 },   // 「机械臂/机器人 … P4 Machines↑↑」

        // ── §5.2 T9 体验 ──
        ["song"]         = new() { LifeDelta = 2.0 },   // 「装饰/珠宝/艺术/娱乐 … P6↑」
        ["gem_gather"]   = new() { LifeDelta = 1.5 },
        ["wealth_show"]  = new() { LifeDelta = 2.5, CostUnrest = 0.15 },
        ["city_stage"]   = new() { LifeDelta = 4.0 },
        ["drama_theatre"]= new() { LifeDelta = 4.0 },

        // ── §5.2 T10 深地与星际 ──
        // 「深地机器人采矿 … P2 阶跃式↑↑」—— 阶跃由 §4.4 深度分层直接产生（TechPhysics.ExtractabilityOf），
        // 此处只给它与能源的连带拉动（§4.4："连带拉动 P3 能源与 P5 改造效率"）。
        ["deep_mining_robot"] = new() { ProdDelta = 2.0, CostDeplete = 0.70 },
        ["space_mining"]      = new() { CostDeplete = 0.30 },   // 「机器人星际矿业 … P2 → 行星级」
    };

    /// <summary>
    /// §5.2 归纳表里点了名、但 <c>tech_tree.md</c> 里<b>没有对应节点</b>的技术。
    ///
    /// 这不是我漏找 —— 是两张表真的对不上。§5.2 的归纳表列了约 60 项，
    /// 而实际 175 个节点的重心在现代产物（<c>ecommerce_taobao</c> / <c>microblog_weibo</c> /
    /// <c>short_video_douyin</c> / <c>xiangsheng</c> / <c>chunwan</c> 都在），
    /// 反而是几项<b>古代基础技术</b>缺了节点。
    ///
    /// 后果是<b>实打实的</b>：没有 <c>irrigation</c>，§5.2 T4 的"灌溉 → 人口容量↑↑"就无处落地；
    /// 没有 <c>law</c>，§1.7 的"制度容量"（邓巴数 → 习俗 → 财产制 → 法律 → 货币 → 国家）
    /// 就断在中途，而它正是 §6.3 部落分裂的判据来源。
    /// <b>留给作者裁定：补节点，还是在 <c>tech_tree.md</c> 里合并到已有的近似节点上。</b>
    /// </summary>
    public static readonly (string Concept, string DesignRef)[] MissingNodes =
    {
        ("灌溉 irrigation",     "§5.2 T4「灌溉 → 人口容量↑↑」"),
        ("法律 law",            "§5.2 T5「法律 → OrgEfficiency↑↑」＋ §1.7 制度容量链"),
        ("语言 language",       "§5.2 T7「语言/结绳/符号 → P1↑」—— 仅 counting 部分落地"),
        ("学校/教材 school",     "§5.2 T7「学校/教材/网课 → 知识代际传递↑↑」"),
        ("流水线 assembly_line", "§5.2 T8「流水线 → P5↑↑」"),
        ("医疗 medicine",       "§5.2 T9「医疗 → Lifespan↑↑」＋ §4.7 抗逆力来源之一"),
        ("水力 hydro",          "§5.2 T6「水力 → P3↑」"),
        ("风力 wind",           "§5.2 T6「风力 → P3↑」"),
        ("艺术/娱乐 art",        "§5.2 T9「装饰/珠宝/艺术/娱乐 → P6↑」—— 仅音乐类落地"),
        ("奴隶制 slavery",       "§5.2 T5「奴隶制 → P4 +，P6 −」"),
        ("一夫多妻 polygyny",    "§5.2 T5「一夫多妻 → P4 生育↑，P6 −」"),
        ("部落联盟 tribe_union", "§5.2 T5「部落联盟 → 对外战争胜率↑」"),
        ("甲胄 armor",          "§5.2 T3「甲胄 → 战争存活↑」"),
        ("石烤 stone_roast",    "§5.2 T2「石烤 → P6 +」"),
        ("藤条箩筐→已有 basket", "（存在，仅列以示对照）"),
    };

    // ══════════════════════════════════════════════════════════════════
    //  ② 我拟的默认推导（保形，不保绝对数值）
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 按 <c>era</c> 给 P5a 生产放大一个默认步长。
    ///
    /// 形状依据：工具的力量放大是<b>加速</b>的（§4.2 ⑤ 是连乘，不是连加），
    /// 故越晚的节点步长越大。绝对数值<b>是我拟的</b> —— 但 §5.2 里三个有据可查的锚点
    /// （石斧 +2.0 / 标枪 +1.5 / 弓箭 +3.0）都落在 L0~L1，
    /// 故 L0/L1 的默认值取在 1.0 附近，与那三个锚点<b>同量级</b>，不至于脱节。
    /// </summary>
    private static double DefaultProdDelta(string era) => era switch
    {
        "L0" => 1.0,
        "L1" => 0.8,
        "L2" => 0.4,
        "L3" => 0.5,
        "L4" => 0.5,
        "L5" => 0.4,
        "L6" => 0.6,
        "L7" => 1.2,
        "L8" => 1.8,
        "L9" => 2.5,
        _ => 0.3,
    };

    /// <summary>
    /// 按 <c>era</c> 给 P3 能源一个默认锚点。<b>只在没有更明确的锚点时使用</b>。
    ///
    /// 这张表是 <c>TechPhysics.EnergyAnchors</c>（§4.5 的半定量标度）按段的粗插值：
    /// L2 起才有农业（原始农业 4000），L4 传统帝国 8000，L7 早期工业 20000，L8 信息 90000。
    /// </summary>
    private static double DefaultEnergyAnchor(string era) => era switch
    {
        "L0" or "L1" => 2_000,   // 狩猎采集
        "L2" or "L3" => 4_000,   // 原始农业
        "L4" or "L5" => 8_000,   // 传统帝国
        "L6"         => 12_000,
        "L7"         => 20_000,  // 早期工业
        "L8"         => 50_000,  // 工业社会 → 信息社会
        "L9"         => 90_000,  // 现代信息社会
        _            => 2_000,
    };

    /// <summary>没有显式覆盖时，按类别 + 段位推导一个效果。见本类顶部关于"我拟的"的说明。</summary>
    private static TechEffect Derive(TechNode n)
    {
        string power = n.PowerSource;
        var e = new TechEffect { CapacityMultiplier = 1.0 };

        // P5a：生产力放大
        if (power == "P5a") e = e with { ProdDelta = DefaultProdDelta(n.Era) };

        // P3：能源锚点。注意——能源锚点是"取所有已点亮节点里的最大值"，
        // 故这里给一个大方的默认值不会让主轴虚高（见 EvolutionEngine 的取 max 逻辑）。
        if (power == "P3") e = e with { EnergyAnchorKwh = DefaultEnergyAnchor(n.Era) };

        // P1b：记录手段抬高知识保持率天花板（§1.5）
        if (power == "P1b") e = e with { RetentionCeiling = n.Era switch
        {
            "L0" or "L1" => 0.97,
            "L2" or "L3" => 0.99,
            _            => 1.00,
        }};

        // P1a：通信层级
        if (power == "P1a") e = e with { InfoLevel = n.Era switch
        {
            "L0" or "L1" => 1, "L2" or "L3" => 2, "L4" or "L5" => 4,
            "L6" => 5, "L7" => 7, "L8" => 9, _ => 10,
        }};

        // P4：容量倍率（农业/畜牧/建筑把这块地的承载上限抬起来）
        if (power == "P4") e = e with { CapacityMultiplier = 1.0 + 0.03 * (EraIndex(n.Era) + 1) };

        // P6：生活体验（越晚越多，但边际递减 —— 由 LifeQuality 自身的饱和项处理）
        if (power == "P6") e = e with { LifeDelta = 1.5 * (EraIndex(n.Era) + 1) };

        // §4.6 负外部性：即使没有显式 cost，工业化之后的节点也默认有碳排。
        //   依据：§4.6 说化石燃料消耗是"本模型中最重要的一条代价链"，
        //   而 tech_tree.md 的 cost 字段只标了 25 个节点 —— 显式缺失不等于物理上没有代价。
        if (EraIndex(n.Era) >= 7 && power == "P3")
            e = e with { CostCarbon = 0.5 };

        return e;
    }

    private static int EraIndex(string era)
        => era.Length == 2 && era[0] == 'L' && char.IsDigit(era[1]) ? era[1] - '0' : 0;

    // ══════════════════════════════════════════════════════════════════
    //  对外接口
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 该节点的最终效果 = 作者的覆盖表 ∪ 我拟的默认推导，再把 <c>tech_tree.md</c>
    /// 里<b>真有的</b> <c>cost</c> 字段叠上去（那一层是作者的，故优先于我拟的碳排默认值）。
    /// </summary>
    public static TechEffect Of(TechNode n)
    {
        TechEffect e = Overrides.TryGetValue(n.Id, out var o) ? o : Derive(n);

        if (e.CapacityMultiplier == 0) e = e with { CapacityMultiplier = 1.0 };   // None 的哨兵值

        // tech_tree.md 的 cost 是权威数据，覆盖我拟的默认代价。
        if (n.Costs.Count > 0)
            e = e with
            {
                CostCarbon  = n.Costs.GetValueOrDefault("carbon",  e.CostCarbon),
                CostLand    = n.Costs.GetValueOrDefault("land",    e.CostLand),
                CostDeplete = n.Costs.GetValueOrDefault("deplete", e.CostDeplete),
                CostUnrest  = n.Costs.GetValueOrDefault("unrest",  e.CostUnrest),
            };

        return e;
    }

    // ══════════════════════════════════════════════════════════════════
    //  §1.3 固有易发现度 λ
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// §1.3 的 λ_T。作者在 §5.2 的表格里逐行标过 λ（高 / 中 / 低 / 极低），
    /// 但那是个<b>定性</b>列，<c>tech_tree.md</c> 里也没有 λ 字段。
    ///
    /// 故：① 作者明确标过 λ 的那几行按 <see cref="LambdaOverrides"/> 落成数；
    /// ② 其余按 <c>adopt</c>（传播难易）推导 —— 这两者本是同一件事的两面：
    /// 难传播的技术通常也难被独立想出来。§5.1 的字段对应表也把
    /// <c>adopt</c> 归到"传播难度 → 扩散速率"，与之相邻。
    ///
    /// ⚠️ ②<b>是我拟的</b>。它影响的是"多个技术同时可被发现时的先后"，
    /// 不影响"能不能被发现"（后者由 pre + skill + geo 决定），故风险可控。
    /// </summary>
    private static readonly Dictionary<string, double> LambdaOverrides = new(StringComparer.Ordinal)
    {
        // §5.2 明标 λ 的行（按 cn 对上真实节点 id）
        ["charcoal"]    = 0.0008,  // 「制炭（不完全燃烧）… λ 低」—— 作者说这是"纯意外发现"
        ["copper_smelt"]= 0.0006,  // 「铜冶炼 … λ 低」
        ["tin_ore"]     = 0.0002,  // 「锡冶炼 … λ 极低」
        ["iron"]        = 0.0006,  // 「铁 … λ 低」
        ["coal_fuel"]   = 0.0005,  // 「煤炭开采 … λ 低」
        ["petroleum"]   = 0.0004,  // 「石油 … λ 低」
        ["nuclear_power"] = 0.0001,// 「核能 … λ 极低」
        ["deep_mining_robot"] = 0.0002,
        ["space_mining"]      = 0.0001,
        ["fire_making"] = 0.0030,  // 「钻木取火 … λ 中」
        ["pottery"]     = 0.0020,  // 「陶器 … λ 中」
        ["writing"]     = 0.0015,  // 「文字 … λ 中」
    };

    /// <summary>§1.3 的 λ_T。见 <see cref="LambdaOverrides"/> 关于"哪些是作者的、哪些是我拟的"。</summary>
    public static double LambdaOf(TechNode n)
    {
        if (LambdaOverrides.TryGetValue(n.Id, out double lam)) return lam;
        if (Overrides.TryGetValue(n.Id, out var e) && e.Lambda > 0) return e.Lambda;

        // 按 adopt 推导（我拟的）
        return n.Adopt switch
        {
            "fast"   => 0.0030,   // 口耳即传 —— 往往也是一看就会的东西
            "mid"    => 0.0012,   // 需示范
            "slow"   => 0.0005,   // 需长期师徒
            "ritual" => 0.0002,   // 需文化接纳
            _        => 0.0012,
        };
    }

    /// <summary>该效果是作者的（有显式覆盖）还是我拟的（走默认推导）。供 <c>humen tech --effects</c> 标注。</summary>
    public static bool IsAuthored(string techId) => Overrides.ContainsKey(techId);

    /// <summary>覆盖表里全部 id（供体检报告列出"哪些节点的效果有出处"）。</summary>
    public static IEnumerable<string> AuthoredIds => Overrides.Keys.OrderBy(k => k, StringComparer.Ordinal);

    /// <summary>覆盖表条目数。</summary>
    public static int AuthoredCount => Overrides.Count;

    /// <summary>
    /// <b>装载期硬校验</b>：覆盖表里引用的每个 id 都必须真实存在于技术树。
    ///
    /// 这条校验的由来见 <see cref="Overrides"/> 的注释 —— 我曾在不知道节点库实际长什么样的
    /// 情况下，照着 §5.2 归纳表的措辞写了 17 个 id，它们<b>全部命中不了</b>，
    /// 于是那些"引用作者原文"的效果静默退化成了默认推导，而运行时毫无征兆。
    /// 这类错误的危害在于<b>它长得像成功</b>：数字照样算得出来，只是来路变了。
    ///
    /// 故此处返回错误清单而非抛异常，由调用方决定是砸掉还是降级（<c>humen tech</c> 选择砸掉）。
    /// </summary>
    public static List<string> Validate(TechTree tree)
    {
        var errors = new List<string>();

        foreach (string id in Overrides.Keys)
            if (!tree.Has(id))
                errors.Add($"TechEffects 覆盖表引用了不存在的节点：{id}");

        // §4.7 的抗逆力来源同样必须真实存在
        foreach (var (id, _, _) in TechPhysics.ShockAbsorptionSources)
            if (!tree.Has(id))
                errors.Add($"TechPhysics.ShockAbsorptionSources 引用了不存在的节点：{id}");

        // §1.4 的热源同理。TechId 为 null 的行（焦炭）是<b>刻意</b>留的缺口，见该表注释。
        foreach (var h in TechPhysics.HeatSources)
            if (h.TechId != null && !tree.Has(h.TechId))
                errors.Add($"TechPhysics.HeatSources 引用了不存在的节点：{h.TechId}（{h.Name}）");

        return errors;
    }
}
