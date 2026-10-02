using System.Globalization;

namespace Humen.Core;

/// <summary>
/// 一个技术节点。字段与 <c>design.md</c> §5.1 的 <c>TechNode</c> 一一对应。
///
/// ⚠️ <b>与 §5.1 的一处字段名偏差</b>：§5.1 写的是 <c>SourceQuote</c>（power.txt 原文出处），
/// 而 <c>tech_tree.md</c> 里实际躺着的字段叫 <c>note</c>（一句话人话说明）。
/// 两者**作用相同**（都是"这个节点凭什么存在"的可追溯锚点），
/// 故此处保留 <see cref="Note"/> 原名，不擅自改名 —— 权威来源是 <c>tech_tree.md</c>。
/// INV-11 要求的"每个节点都有 SourceQuote"因此判的是 <see cref="Note"/> 非空。
///
/// 另：<c>tech_tree.md</c> 的字段字典里列了 <c>gate</c>（文明能力门槛），
/// 但 <b>176 个节点里一个都没实际用过</b>（实测 <c>grep -c "gate:" = 0</c>），
/// 故本类不为其设字段 —— 等真有节点用了再加，免得留一个恒为空的死字段。
/// </summary>
public sealed class TechNode
{
    public required string Id { get; init; }
    public required string Name { get; init; }        // cn
    public required string Era { get; init; }         // L0 ~ L9，仅供人读，引擎不据此行事
    public required string Cat { get; init; }         // 能源/工具/材料/...
    public required string Kind { get; init; }        // milestone / bridge / base
    public required string[] Requires { get; init; }  // pre
    public required Dictionary<string, int> Skills { get; init; }   // skill {域: 0~100}
    public required string[] Geo { get; init; }       // geo
    public required string[] Tags { get; init; }      // tag（需求拉力）
    public required string Adopt { get; init; }       // fast / mid / slow / ritual
    public required Dictionary<string, double> Costs { get; init; } // cost（§4.6 负外部性）
    public required string Note { get; init; }

    /// <summary>该节点影响哪个力（P1a~P6）。由 <see cref="Cat"/> 折算，见 <see cref="TechTree.PowerSourceOf"/>。</summary>
    public string PowerSource => TechTree.PowerSourceOf(Cat);

    public override string ToString() => $"{Id}（{Name}）";
}

/// <summary>
/// 技术树：<c>tech_tree.md</c> 的解析结果 + INV-11 校验。
///
/// <b>为什么要自己解析 Markdown 而不用 YAML 库</b>：
/// ① <c>Humen.Core</c> 刻意保持零第三方依赖（INV-34 要求跨机器逐位一致，
///    少一个依赖就少一处"版本变了结果就变"的风险）；
/// ② <c>tech_tree.md</c> 已明说"引擎可直接把整段抠出来当配置"，
///    即**该文件的格式是给引擎用的**，不是自由散文；
/// ③ 实际用到的字段只有 11 个，语法是受限的（标量 / <c>[列表]</c> / <c>{映射}</c>），
///    手写解析器不到 100 行，比引一个 YAML 库再裁它的行为更可控。
/// </summary>
public sealed class TechTree
{
    /// <summary>全部节点，按 <c>tech_tree.md</c> 中出现顺序（= L0→L9 的叙述顺序）。</summary>
    public IReadOnlyList<TechNode> Nodes { get; }

    private readonly Dictionary<string, TechNode> _byId;

    /// <summary>反向索引：某节点是哪些节点的前置。用于"解锁了一个技术后该重算哪些候选"。</summary>
    private readonly Dictionary<string, List<string>> _dependents;

    private TechTree(List<TechNode> nodes)
    {
        Nodes = nodes;
        _byId = new Dictionary<string, TechNode>(nodes.Count, StringComparer.Ordinal);
        foreach (var n in nodes) _byId[n.Id] = n;   // 重复 id 已在校验中拒绝

        _dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var n in nodes)
            foreach (string p in n.Requires)
            {
                if (!_dependents.TryGetValue(p, out var list))
                    _dependents[p] = list = new List<string>();
                list.Add(n.Id);
            }
    }

    public TechNode this[string id] => _byId[id];
    public bool TryGet(string id, out TechNode node) => _byId.TryGetValue(id, out node!);
    public bool Has(string id) => _byId.ContainsKey(id);
    public int Count => Nodes.Count;

    /// <summary>直接依赖 <paramref name="id"/> 的节点（不含传递闭包）。</summary>
    public IReadOnlyList<string> DependentsOf(string id)
        => _dependents.TryGetValue(id, out var l) ? l : Array.Empty<string>();

    private Dictionary<string, int>? _depth;

    /// <summary>
    /// 节点在<b>前置链上的深度</b>：无前置者为 0，否则为所有前置深度最大值 + 1。
    ///
    /// 这是"这个族群的技术走到多远了"的<b>正确</b>度量 —— 而不是 <c>era</c> 字段。
    /// 理由见 <c>tech_tree.md</c> 自己的声明：
    /// <i>"era 只是章节分组用的……引擎绝不看 era 字段行事。"</i>
    /// 按 era 排序会把同章里两个相距十万八千里的节点并列，按深度排才是真的"更靠链条末端"。
    ///
    /// 用迭代消除递归（链长 20+，但没必要冒栈风险），结果缓存。
    /// </summary>
    public int DepthOf(string id)
    {
        if (_depth is null)
        {
            var d = new Dictionary<string, int>(Nodes.Count, StringComparer.Ordinal);
            foreach (var n in Nodes) ComputeDepth(n.Id, d, new HashSet<string>(StringComparer.Ordinal));
            _depth = d;
        }
        return _depth.TryGetValue(id, out int v) ? v : 0;
    }

    private int ComputeDepth(string id, Dictionary<string, int> memo, HashSet<string> onStack)
    {
        if (memo.TryGetValue(id, out int c)) return c;
        if (!_byId.TryGetValue(id, out var n)) return 0;
        if (!onStack.Add(id)) return 0;        // 环（Validate 已拒绝，这里只防死循环）
        int best = 0;
        foreach (string p in n.Requires)
            best = Math.Max(best, ComputeDepth(p, memo, onStack) + 1);
        onStack.Remove(id);
        memo[id] = best;
        return best;
    }

    /// <summary>最大前置链深度（L0 到最深节点跨了几步）。</summary>
    public int MaxDepth
    {
        get
        {
            int m = 0;
            foreach (var n in Nodes) m = Math.Max(m, DepthOf(n.Id));
            return m;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  解析
    // ══════════════════════════════════════════════════════════════════

    /// <summary>解析 <c>tech_tree.md</c>。文件缺失或格式崩坏时抛 <see cref="InvalidDataException"/>。</summary>
    public static TechTree LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"找不到技术树文件：{path}。它是 §5.1 的权威节点库，引擎没有它就跑不起来。", path);
        return Parse(File.ReadAllLines(path));
    }

    /// <summary>
    /// 解析 <c>tech_tree.md</c> 的正文行。
    ///
    /// 只认 <c>```yaml</c> 围栏块内的 <c>- id:</c> 条目，故文档里的说明散文、
    /// 字段字典表、受控词表都不会被误吞。
    /// </summary>
    public static TechTree Parse(IReadOnlyList<string> lines)
    {
        var nodes = new List<TechNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();

        bool inYaml = false;
        int i = 0;
        while (i < lines.Count)
        {
            string line = lines[i];
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                // ```yaml 开、``` 关。只认标了 yaml 的开栏，免得把别的代码块也算进来。
                if (!inYaml) inYaml = trimmed.StartsWith("```yaml", StringComparison.OrdinalIgnoreCase);
                else inYaml = false;
                i++;
                continue;
            }

            if (!inYaml || !trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            // ── 收集本节点的所有行（直到下一个 "- " 或围栏结束）──
            int start = i;
            var body = new List<string> { trimmed[2..] };
            i++;
            while (i < lines.Count)
            {
                string t = lines[i].TrimStart();
                if (t.StartsWith("```", StringComparison.Ordinal) || t.StartsWith("- ", StringComparison.Ordinal)) break;
                if (t.Length > 0) body.Add(t);
                i++;
            }

            var node = ParseNode(body, start + 1, errors);
            if (node == null) continue;

            // 跳过 §3 的追加模板（它的 id 是占位符 my_new_thing）。
            // 这是唯一的"长得像节点但不是节点"的块 —— 用 id 判，比按章节位置判稳。
            if (node.Id == "my_new_thing") continue;

            if (!seen.Add(node.Id))
            {
                errors.Add($"第 {start + 1} 行：节点 id 重复 —— {node.Id}");
                continue;
            }
            nodes.Add(node);
        }

        if (nodes.Count == 0)
            throw new InvalidDataException("tech_tree.md 里一个节点都没解析出来（```yaml 围栏或 `- id:` 格式变了？）。");

        Validate(nodes, errors);
        if (errors.Count > 0)
            throw new InvalidDataException("tech_tree.md 校验未通过：\n  - " + string.Join("\n  - ", errors));

        return new TechTree(nodes);
    }

    private static TechNode? ParseNode(List<string> body, int lineNo, List<string> errors)
    {
        var scalar = new Dictionary<string, string>(StringComparer.Ordinal);
        var list = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var skill = new Dictionary<string, int>(StringComparer.Ordinal);
        var cost = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (string raw in body)
        {
            string line = StripComment(raw).Trim();
            if (line.Length == 0) continue;

            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                errors.Add($"第 {lineNo} 行附近：看不懂的行 —— {raw}");
                continue;
            }

            string key = line[..colon].Trim();
            string val = line[(colon + 1)..].Trim();

            switch (key)
            {
                case "pre":
                case "geo":
                case "tag":
                    list[key] = ParseList(val);
                    break;

                case "skill":
                    foreach (var (k, v) in ParseMap(val, lineNo, errors))
                        skill[k] = (int)Math.Round(v);
                    break;

                case "cost":
                    foreach (var (k, v) in ParseMap(val, lineNo, errors))
                        cost[k] = v;
                    break;

                default:   // id / cn / era / cat / kind / adopt / note
                    scalar[key] = val;
                    break;
            }
        }

        if (!scalar.TryGetValue("id", out string? id) || id.Length == 0)
        {
            // 不是节点块（比如模板里被拆出来的半截），静默跳过交给上层计数。
            return null;
        }

        return new TechNode
        {
            Id = id,
            Name = scalar.GetValueOrDefault("cn", id),
            Era = scalar.GetValueOrDefault("era", "L0"),
            Cat = scalar.GetValueOrDefault("cat", "—"),
            Kind = scalar.GetValueOrDefault("kind", "milestone"),
            Requires = list.GetValueOrDefault("pre", Array.Empty<string>()),
            Skills = skill,
            Geo = list.GetValueOrDefault("geo", Array.Empty<string>()),
            Tags = list.GetValueOrDefault("tag", Array.Empty<string>()),
            Adopt = scalar.GetValueOrDefault("adopt", "fast"),
            Costs = cost,
            // tech_tree.md 没有独立的"出处"字段，note 就是它的可追溯锚点（见 TechNode 的注释）。
            Note = scalar.GetValueOrDefault("note", ""),
        };
    }

    /// <summary>
    /// 剥掉行尾注释。
    /// 只在 <c>#</c> <b>前面是空白</b>时才算注释 —— 免得把值里的井号误切。
    /// （已实测 <c>tech_tree.md</c> 的 cn/note 里不含"空格+井号"，故这条规则安全。）
    /// </summary>
    private static string StripComment(string line)
    {
        for (int i = 1; i < line.Length; i++)
            if (line[i] == '#' && char.IsWhiteSpace(line[i - 1]))
                return line[..i];
        return line;
    }

    /// <summary>解析 <c>[a, b, c]</c> 或 <c>[]</c>。</summary>
    private static string[] ParseList(string val)
    {
        val = val.Trim();
        if (val.StartsWith('[') && val.EndsWith(']')) val = val[1..^1];
        if (val.Trim().Length == 0) return Array.Empty<string>();
        return val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>解析 <c>{k: v, k: v}</c> 或 <c>{}</c>。</summary>
    private static List<KeyValuePair<string, double>> ParseMap(string val, int lineNo, List<string> errors)
    {
        var outp = new List<KeyValuePair<string, double>>();
        val = val.Trim();
        if (val.StartsWith('{') && val.EndsWith('}')) val = val[1..^1];
        if (val.Trim().Length == 0) return outp;

        foreach (string pair in val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int c = pair.IndexOf(':');
            if (c <= 0)
            {
                errors.Add($"第 {lineNo} 行附近：映射项缺冒号 —— {pair}");
                continue;
            }
            string k = pair[..c].Trim();
            string v = pair[(c + 1)..].Trim();
            if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            {
                errors.Add($"第 {lineNo} 行附近：{k} 的值不是数 —— {v}");
                continue;
            }
            outp.Add(new KeyValuePair<string, double>(k, d));
        }
        return outp;
    }

    // ══════════════════════════════════════════════════════════════════
    //  INV-11 校验
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// INV-11：技术树无环；<c>Requires</c> 全部可达；每个节点都有出处。
    ///
    /// 这三条是<b>硬约束</b>而不是风格建议 —— 发现引擎是在这张图上做拓扑推进的，
    /// 有环就有节点永远等不到自己的前置，有悬空前置就有节点永远不可达。
    /// 这类错误一旦漏到运行时，表现是"某条科技线永远不亮"，极难反查，故在装载期就砸掉。
    /// </summary>
    private static void Validate(List<TechNode> nodes, List<string> errors)
    {
        var byId = new Dictionary<string, TechNode>(StringComparer.Ordinal);
        foreach (var n in nodes) byId[n.Id] = n;

        // ① 前置可达 + 出处非空
        foreach (var n in nodes)
        {
            foreach (string p in n.Requires)
                if (!byId.ContainsKey(p))
                    errors.Add($"节点 {n.Id} 的前置 {p} 不存在");

            if (n.Requires.Contains(n.Id, StringComparer.Ordinal))
                errors.Add($"节点 {n.Id} 把自己列为前置");

            if (string.IsNullOrWhiteSpace(n.Note))
                errors.Add($"节点 {n.Id} 没有 note（INV-11 要求每个节点都有出处）");
        }

        // ② 无环：Kahn 拓扑排序，排不完就是有环。
        //    ⚠️ 刻意不用 PriorityQueue —— 它的同优先级出队顺序不保证稳定，会破坏 INV-34。
        var indeg = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var n in nodes) indeg[n.Id] = n.Requires.Count(p => byId.ContainsKey(p));

        var queue = new Queue<string>(nodes.Where(n => indeg[n.Id] == 0).Select(n => n.Id));
        int visited = 0;
        var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var n in nodes)
            foreach (string p in n.Requires)
            {
                if (!byId.ContainsKey(p)) continue;
                if (!dependents.TryGetValue(p, out var l)) dependents[p] = l = new List<string>();
                l.Add(n.Id);
            }

        while (queue.Count > 0)
        {
            string id = queue.Dequeue();
            visited++;
            if (!dependents.TryGetValue(id, out var deps)) continue;
            foreach (string d in deps)
                if (--indeg[d] == 0) queue.Enqueue(d);
        }

        if (visited != nodes.Count)
        {
            var cyclic = nodes.Where(n => indeg[n.Id] > 0).Select(n => n.Id).Take(8);
            errors.Add($"技术树有环（INV-11）：涉及 {string.Join(", ", cyclic)} 等 {nodes.Count - visited} 个节点");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  类别 → 力（§5.1 的 PowerSource 可追溯性）
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 把 <c>cat</c> 折算成 <c>design.md</c> 的六力标签 —— §5.3 那个
    /// "点任意技术 → 显示它属于哪一力"的底层依据。
    ///
    /// ⚠️ <b>这张表是我拟的，不是作者定的（待裁）。</b>
    /// <c>tech_tree.md</c> 的 <c>cat</c> 是<b>类别</b>词表（24 个值，AI/城居/祭祀那种人类可读的分法），
    /// 而 §4.1 的六力是<b>动力学</b>分法，两套词表并非一一对应 —— 要一一对应，
    /// <c>tech_tree.md</c> 得加一个显式的 <c>power:</c> 字段。在作者裁定之前，
    /// 这张表只是把 §5.2 归纳表里已经写明的归属（如"齿轮滑轮 → P5 力量放大"、
    /// "量子计算 → P1 算力↑↑"、"机器人星际矿业 → P2"）落成代码。
    ///
    /// <b>兜底刻意返回 <c>"P?"</c> 而不是 <c>"P6"</c></b>：
    /// 曾用 <c>_ =&gt; "P6"</c>，结果 175 个节点里有 60 多个悄悄落进 P6
    /// （"计算机 → P6"、"航天 → P6"），而 P6 是个<a>看起来完全合理</a>的归宿，
    /// 于是没人会去查。让没归宿的类别<b>显眼地错</b>，比让它合情合理地错要好得多。
    /// </summary>
    public static string PowerSourceOf(string cat) => cat switch
    {
        // ── P1a 通信（横向·跨空间）／P1b 记录（纵向·跨时间）──
        "通讯" => "P1a", "媒体" => "P1a", "网络" => "P1a",
        "文字" => "P1b", "认知" => "P1b", "光学" => "P1b",   // 透镜/望远镜/显微镜＝获取知识的手段
        "计算机" => "P1b", "电子" => "P1b",                  // §5.2：量子计算 → "P1 算力↑↑"

        // ── P2 材料 ──
        "材料" => "P2", "化学" => "P2", "工艺" => "P2",
        "航天" => "P2",   // §5.2 T10：机器人星际矿业 → "P2 → 行星级"

        // ── P3 能源【主轴 §4.5】──
        "能源" => "P3", "电力" => "P3",

        // ── P4 劳动力／疆域 ──
        "农业" => "P4", "畜牧" => "P4", "社会组织" => "P4",
        "建筑" => "P4",   // 建筑＝把人固定在疆域上（§1.10 P4b）
        "历法" => "P4",   // §5.2 T4 明写"历法 → 农业成功率↑↑"；它同时是 P1b，此处从效果侧

        // ── P5a 生产／P5b 破坏 ──
        "工具" => "P5a", "机械" => "P5a", "交通" => "P5a",

        // ── P6 体验 ──
        "音乐" => "P6", "文化" => "P6",

        _ => "P?",   // 见上：显眼地错
    };

    /// <summary>没有归宿的类别 —— 必须为空，否则说明 <c>tech_tree.md</c> 加了新类别而这张表没跟上。</summary>
    public IEnumerable<string> UnmappedCategories()
        => Nodes.Select(n => n.Cat).Distinct()
                .Where(c => PowerSourceOf(c) == "?")
                .OrderBy(c => c, StringComparer.Ordinal);

    /// <summary>所有出现过的 cat 值 —— 供报告打印，确认没有类别掉进兜底分支。</summary>
    public IEnumerable<(string Cat, string Power, int Count)> CategoryBreakdown()
        => Nodes.GroupBy(n => n.Cat)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => (g.Key, PowerSourceOf(g.Key), g.Count()));

    /// <summary>所有出现的技能域（受控词表应有 23 个）。</summary>
    public IEnumerable<string> SkillDomains()
        => Nodes.SelectMany(n => n.Skills.Keys).Distinct().OrderBy(s => s, StringComparer.Ordinal);

    /// <summary>所有出现过的矿产/地理原料（geo 的并集），去重排序。</summary>
    public IEnumerable<string> GeoTerms()
        => Nodes.SelectMany(n => n.Geo).Distinct().OrderBy(s => s, StringComparer.Ordinal);
}
