using System.Collections.Concurrent;

namespace Humen.Core;

/// <summary>
/// 出生曲线：把部落内序号 <c>seq</c> 反查为出生年份。
///
/// <b>为什么需要它</b>：L2 程序层不存任何个人档案，却必须能回答「第 47,213 号生于何年」。
/// 答案不能凭空捏造 —— 它必须与宏观模拟（<c>tribe_history</c> 的逐期出生曲线）严格一致，
/// 否则 L2 与 L1 会打架（ID-15）。
///
/// 实现方式：对 <c>tribe_history</c> 的累积出生数做<b>求逆</b>（O(log n) 二分）。
/// M1 阶段模拟尚未运行，故用 <see cref="Unavailable"/> 占位，只覆盖元年区间。
/// </summary>
public interface IBirthCurve
{
    /// <summary>返回部落内第 <paramref name="seq"/> 号成员的出生年份；未知则返回 <see cref="double.NaN"/>。</summary>
    double Inverse(int continentId, int tribeId, int seq);

    /// <summary>M1 占位实现：模拟未运行，一切 L2 出生年未知。</summary>
    public static IBirthCurve Unavailable { get; } = new UnavailableBirthCurve();

    private sealed class UnavailableBirthCurve : IBirthCurve
    {
        public double Inverse(int continentId, int tribeId, int seq) => double.NaN;
    }
}

/// <summary>
/// ★★★ 三层身份架构的 <b>L2 程序层</b>（id.md §3.2）。
///
/// <b>核心命题</b>：<i>「有没有 ID」和「有没有档案」是两件事。</i>
/// 公元前 3000 年 → 公元 2126 年之间出生约 <b>2×10¹⁰</b> 人，全部有 ID；
/// 但只有 L1 实体层（元年 10 万 + 名人 + 焦点常驻者，≤10⁶）真正落库。
/// 其余的人由本类<b>纯函数派生</b>，<b>一行都不存</b>。存储量因此下降约 2 万倍。
///
/// <b>必须满足</b>：
///   ID-14  <see cref="Derive"/> 是纯函数，且<b>不读不写任何模拟状态</b>；
///   ID-15  跨 LOD 一致：结果与播放速度 / 焦点 / 步长 / 存档重载无关；
///   ID-17  事后补档产出的 ID，必须与「当时即档」<b>逐字相同</b>。
///
/// 这三条同时成立，靠的是：<b>所有派生输入都只来自 (seed, C, T, seq)，没有任何一条来自运行时状态。</b>
/// </summary>
public static class Identity
{
    // ──────────────────────────────────────────────────────────────────
    //  元年性别配额表（INV-3 要求误差为 0，故不能靠概率哈希）
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 每部落的元年性别表，由种子确定性洗牌得出，<b>首次访问时惰性构建</b>。
    /// 缓存不破坏纯函数性：表本身完全由 <c>(seed, C, T)</c> 决定（ID-14）。
    /// </summary>
    private static readonly ConcurrentDictionary<(long Seed, int C, int T), bool[]> GenesisGenderCache = new();

    private static bool[] GenesisGenderTable(long seed, int c, int t)
        => GenesisGenderCache.GetOrAdd((seed, c, t), key =>
        {
            var (s, cc, tt) = key;

            // 用 (seed, C, T) 生成独立流，与其它用途互不干扰
            ulong h = Hashing.Hash64(s, HashDomain.Gender, cc, tt, 0);
            var rng = new Rng(unchecked((long)h));

            // seq 1..1000 → 索引 0..999
            var seqs = new int[WorldConfig.MembersPerTribe];
            for (int i = 0; i < seqs.Length; i++) seqs[i] = i + 1;
            rng.Shuffle(seqs);

            var isMale = new bool[WorldConfig.MembersPerTribe + 1];  // 1-based
            for (int i = 0; i < WorldConfig.MalePerTribe; i++)
                isMale[seqs[i]] = true;
            return isMale;
        });

    // ──────────────────────────────────────────────────────────────────
    //  ID 文本
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 规范 ID 文本。<c>"1.1-103-1"</c> = 大陆1 · 部落1 · 编号103 · 男性。
    ///   req.txt 第 4 行：编号规则 <c>1.1-1</c> .. <c>1.1-1000</c>
    ///   req.txt 第 5 行：尾号 1 为男性，尾号 0 为女性
    ///   req.txt 第 7 行：<c>1.1-103-1</c> 为 部落 1.1、编号 103、男性
    /// </summary>
    public static string FormatId(int continentId, int localTribeIndex, int seq, int genderCode)
        => $"{continentId}.{localTribeIndex}-{seq}-{genderCode}";

    public static string FormatId(int continentId, int localTribeIndex, int seq, Gender gender)
        => FormatId(continentId, localTribeIndex, seq, (int)gender);

    /// <summary>部落代号，如 <c>"1.1"</c>。</summary>
    public static string TribeCode(int continentId, int localTribeIndex)
        => $"{continentId}.{localTribeIndex}";

    /// <summary>全局部落序号 0..99 ↔ (大陆 1..5, 大陆内序号 1..20)。</summary>
    public static (int ContinentId, int LocalIndex) SplitTribeIndex(int globalIndex)
        => (globalIndex / WorldConfig.TribesPerContinent + 1,
            globalIndex % WorldConfig.TribesPerContinent + 1);

    public static int JoinTribeIndex(int continentId, int localIndex)
        => (continentId - 1) * WorldConfig.TribesPerContinent + (localIndex - 1);

    /// <summary>解析 ID。缺尾号时按编号推断（<b>仅用于读旧数据</b>，写出的永远是规范形式）。</summary>
    public static bool TryParseId(string id, out int continentId, out int localIndex, out int seq, out int genderCode)
    {
        continentId = localIndex = seq = -1;
        genderCode = -1;
        if (string.IsNullOrWhiteSpace(id)) return false;

        string[] parts = id.Split('-');
        if (parts.Length < 2) return false;

        string[] head = parts[0].Split('.');
        if (head.Length != 2) return false;
        if (!int.TryParse(head[0], out continentId)) return false;
        if (!int.TryParse(head[1], out localIndex)) return false;
        if (!int.TryParse(parts[1], out seq)) return false;

        if (parts.Length >= 3) return int.TryParse(parts[2], out genderCode);
        return true;   // 旧式两段 ID，性别待补
    }

    // ──────────────────────────────────────────────────────────────────
    //  派生
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ <b>不要用这个判据决定 ID 来源</b>（id.md ID-5 明令禁止）。
    ///
    /// 「序号 ≤ 1000」只在<b>原生 100 部落</b>上等价于元年成员。部落分裂
    /// （<c>tribes.parent_tribe_id</c>）后，新部落的序号<b>从 1 重新开始</b>，
    /// 于是 <c>seq == 1</c> 既可能是公元前 30 万年的老祖宗，
    /// 也可能是公元 1500 年某个新部落的第一个婴儿。
    ///
    /// 本方法仅用于「在<b>已知是原生部落</b>时」快速圈定元年席位；
    /// 判定 ID 来源请一律用显式的 <see cref="IdSource"/>（M2 起由 <c>id_ledger</c> 给出）。
    /// </summary>
    public static bool IsOriginalSeatSeq(int seq)
        => seq >= 1 && seq <= WorldConfig.MembersPerTribe;

    /// <summary>
    /// ★ <b>唯一入口</b>：由种子 + 坐标派生一个完整身份（id.md §3.2.3）。
    ///
    /// <b>纯函数</b> —— 不读、不写任何模拟状态。同参数恒等（ID-14 / ID-15 / ID-17）。
    /// </summary>
    /// <param name="seed">世界种子</param>
    /// <param name="continentId">大陆 1..5</param>
    /// <param name="globalTribeIndex">全局部落序号 0..99</param>
    /// <param name="seq">部落内序号，1 起</param>
    /// <param name="source">
    /// ID 来源。<b>必须显式给出</b>，不得由 <paramref name="seq"/> 推断（ID-5）。
    /// <see cref="IdSource.Genesis"/> 走性别配额表 + 固定年龄；
    /// <see cref="IdSource.Derived"/> 走偏置哈希 + 出生曲线求逆。
    /// </param>
    /// <param name="birthCurve">出生曲线；<see cref="IdSource.Derived"/> 时必需</param>
    public static Member Derive(
        long seed,
        int continentId,
        int globalTribeIndex,
        int seq,
        IdSource source,
        IBirthCurve? birthCurve = null)
    {
        // 异常里必须写清<b>合法范围</b>。默认的 ArgumentOutOfRangeException 只给出
        // 「Specified argument was out of the range of valid values」——
        // 调用方（尤其是 CLI 使用者）拿到这句话，完全不知道到底该填几。
        if (continentId is < 1 or > WorldConfig.ContinentCount)
            throw new ArgumentOutOfRangeException(nameof(continentId), continentId,
                $"大陆号须在 1~{WorldConfig.ContinentCount}。");
        if (globalTribeIndex < 0 || globalTribeIndex >= WorldConfig.TribeCount)
            throw new ArgumentOutOfRangeException(nameof(globalTribeIndex), globalTribeIndex,
                $"全球部落序号须在 0~{WorldConfig.TribeCount - 1}。");
        if (seq < 1)
            throw new ArgumentOutOfRangeException(nameof(seq), seq, "部落内序号须 ≥ 1。");

        var (cont, local) = SplitTribeIndex(globalTribeIndex);

        Gender gender;
        double birthYear;
        int ageAtGenesis;

        if (source == IdSource.Genesis)
        {
            // ── 元年成员：性别走配额表（严格 512/488），年龄 13~17 ──
            if (seq > WorldConfig.MembersPerTribe)
                throw new ArgumentOutOfRangeException(nameof(seq),
                    $"元年席位只有 1..{WorldConfig.MembersPerTribe}，收到 {seq}");

            gender = GenesisGenderTable(seed, cont, local)[seq] ? Gender.Male : Gender.Female;

            ulong hAge = Hashing.Hash64(seed, HashDomain.Birth, cont, local, seq);
            int age = WorldConfig.MinAge
                    + (int)(hAge % (ulong)(WorldConfig.MaxAge - WorldConfig.MinAge + 1));
            ageAtGenesis = age;
            birthYear = WorldConfig.StartYear - age;
        }
        else
        {
            // ── L2：性别走偏置哈希（1.05:1），出生年走出生曲线求逆 ──
            gender = GenderOf(seed, cont, local, seq);

            birthYear = birthCurve?.Inverse(cont, globalTribeIndex, seq) ?? double.NaN;
            ageAtGenesis = -1;
        }

        var (surname, given) = NameGen.GenerateParts(seed, cont, globalTribeIndex, seq);

        return new Member(
            Id: FormatId(cont, local, seq, gender),
            ContinentId: cont,
            TribeId: globalTribeIndex,
            TribeCode: TribeCode(cont, local),
            Seq: seq,
            Gender: gender,
            FullName: surname.Length == 0 ? given : $"{surname} {given}",
            Surname: surname,
            GivenName: given,
            BirthYear: birthYear,
            AgeAtGenesis: ageAtGenesis,
            Source: source);
    }

    /// <summary>
    /// L2 性别派生：偏置哈希，<c>P(男) = 512/1000</c>（≈ req.txt 第 5 行的 1.05 : 1）。
    ///
    /// ⚠️ 这是<b>统计</b>意义下的比例，不保证任何有限样本恰好 512/488。
    /// <b>元年 10 万成员不走这条路</b> —— 他们由 <see cref="GenesisGenderTable"/> 严格配额（INV-3）。
    /// </summary>
    public static Gender GenderOf(long seed, int continentId, int localTribeIndex, int seq)
    {
        ulong h = Hashing.Hash64(seed, HashDomain.Gender, continentId, localTribeIndex, seq);
        return (h % 1000UL) < (ulong)WorldConfig.GenderBiasMilli ? Gender.Male : Gender.Female;
    }

    /// <summary>
    /// L2 出生年派生：出生曲线求逆（O(log n)）。
    /// 模拟未运行时返回 <see cref="double.NaN"/>。
    /// </summary>
    public static double BirthYearOf(IBirthCurve curve, int continentId, int globalTribeIndex, int seq)
        => curve.Inverse(continentId, globalTribeIndex, seq);

    // ──────────────────────────────────────────────────────────────────
    //  发放窗口
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 发放窗口判定（id.md §0 / ID-16）：<c>[−3000, 2125]</c>。
    /// <b>唯一例外</b>：元年 10 万成员恒有 ID（req.txt 第 4 行强制）。
    /// </summary>
    public static bool HasId(double birthYear, bool isGenesisMember = false)
    {
        if (isGenesisMember) return true;
        if (double.IsNaN(birthYear)) return false;
        return birthYear >= WorldConfig.IdWindowStart && birthYear <= WorldConfig.IdWindowEnd;
    }

    /// <summary>对一个已派生的成员做窗口判定。来源直接读 <see cref="Member.Source"/>，不做序号推断。</summary>
    public static bool HasId(Member m)
        => HasId(m.BirthYear, m.Source == IdSource.Genesis);

    /// <summary>
    /// 该部落已发放的 ID 上界（<b>L3 计数层</b>的语义，见 id.md §3.2.2）。
    /// M1 只有元年，故上界恒为 <see cref="WorldConfig.MembersPerTribe"/>。
    /// M2 起改读 <c>id_ledger.next_seq</c>。
    /// </summary>
    public static int IssuedSeqUpperBound(long seed, int globalTribeIndex)
        => WorldConfig.MembersPerTribe;
}
