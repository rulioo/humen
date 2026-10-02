using System.Text;

namespace Humen.Core;

/// <summary>
/// 五大陆命名生成（design.md §8.3）。
///
/// 设计要点：
///   · 每个大陆一套音节库，风格各异（华夏 / 南岛海洋 / 热带部落 / 北欧 / 极地）。
///   · 名字是<b>纯函数</b>：<c>NameGen.Generate(seed, c, t, seq)</c> 只依赖哈希，
///     不依赖任何状态 —— 这是 L2 程序层能够「一个人都不存」的前提（ID-14）。
///   · 组合空间 ≥ 5×10⁷（design.md §8.3 要求），由 <see cref="CombinationSpace"/> 实测保证，
///     并由不变量检查逐大陆断言。
///   · Unity 侧若要改音节表，编辑 <c>Assets/Data/Names/naming_{1..5}.json</c>；
///     本文件是同一份数据的 C# 镜像（由 <c>build-world --emit-names</c> 导出）。
/// </summary>
public static class NameGen
{
    // ══════════════════════════════════════════════════════════════════
    //  大陆 1 · 华夏风
    //  结构：姓(128) × 名首(96) × 名中(96) × 名末(96) ≈ 1.13×10⁸
    // ══════════════════════════════════════════════════════════════════
    private static readonly string[] C1Surname =
    {
        "赵","钱","孙","李","周","吴","郑","王","冯","陈","褚","卫","蒋","沈","韩","杨",
        "朱","秦","尤","许","何","吕","施","张","孔","曹","严","华","金","魏","陶","姜",
        "戚","谢","邹","喻","柏","水","窦","章","云","苏","潘","葛","奚","范","彭","郎",
        "鲁","韦","昌","马","苗","凤","花","方","俞","任","袁","柳","酆","鲍","史","唐",
        "费","廉","岑","薛","雷","贺","倪","汤","滕","殷","罗","毕","郝","邬","安","常",
        "乐","于","时","傅","皮","卞","齐","康","伍","余","元","卜","顾","孟","平","黄",
        "和","穆","萧","尹","姚","邵","湛","汪","祁","毛","禹","狄","米","贝","明","臧",
        "计","伏","成","戴","谈","宋","茅","庞","熊","纪","舒","屈","项","祝","董","梁",
    };

    private static readonly string[] C1Given =
    {
        "伟","芳","娜","敏","静","丽","强","磊","军","洋","勇","艳","杰","娟","涛","明",
        "超","秀","霞","平","刚","桂","英","华","建","文","玉","兰","俊","燕","峰","梅",
        "鹏","飞","宇","浩","晨","欣","怡","睿","涵","轩","泽","昊","然","琪","琳","瑶",
        "宁","康","瑞","祥","嘉","博","思","远","志","宏","凯","锋","锐","铭","钰","楠",
        "柯","桦","松","柏","竹","清","澜","汐","辰","曦","煜","炜","灿","烁","炎","焱",
        "禾","苗","荞","麦","穗","稷","粟","稻","霖","霁","雪","霜","露","泠","沅","湛",
    };

    // ══════════════════════════════════════════════════════════════════
    //  大陆 2 · 南岛海洋风   音节 86⁴ ≈ 5.47×10⁷
    // ══════════════════════════════════════════════════════════════════
    private static readonly string[] C2Syll =
    {
        "ta","ra","ngi","mo","ka","ha","va","ni","pu","sa","te","ki","ru","ma","no","li",
        "wa","fu","si","tu","ne","po","ga","hi","ja","la","mi","oa","pi","ro","su","to",
        "ua","we","ba","de","fi","gu","lu","na","ri","se","ti","vu","ai","ei","ou","ui",
        "anga","enga","inga","onga","unga","ara","ere","iri","oro","uru","ami","emi","imo","omu",
        "tai","rei","noa","hua","kia","manu","rangi","moana","whenua","tapu","ariki","toa",
        "hine","tane","wai","kai","roa","iti","nui","poto","mata","nga","whare","papa",
    };

    // ══════════════════════════════════════════════════════════════════
    //  大陆 3 · 热带部落风   音节 94⁴ ≈ 7.81×10⁷
    // ══════════════════════════════════════════════════════════════════
    private static readonly string[] C3Syll =
    {
        "ka","ba","la","ma","na","ta","sa","wa","ya","za","da","ga","ha","ja","pa","ra",
        "be","le","me","ne","se","te","we","ye","ze","de","ge","ke","pe","re","ve","ce",
        "bi","li","mi","ni","si","ti","wi","yi","zi","di","gi","ki","pi","ri","vi","ci",
        "bo","lo","mo","no","so","to","wo","yo","zo","do","go","ko","po","ro","vo","co",
        "bu","lu","mu","nu","su","tu","wu","yu","zu","du","gu","ku","pu","ru","vu","cu",
        "nko","mba","ndi","ngu","nta","sse","kwe","lya","mwi","nye","twa","zha","shi","chi",
    };

    // ══════════════════════════════════════════════════════════════════
    //  大陆 4 · 北欧风   音节 96⁴ ≈ 8.49×10⁷
    // ══════════════════════════════════════════════════════════════════
    private static readonly string[] C4Syll =
    {
        "sig","hal","stein","bjorn","ulf","rag","nar","eir","ast","rid","gunn","hild",
        "thor","odin","fre","loki","bal","tyr","heim","gard","vall","skag","fjor","eyr",
        "thor","vald","grim","sver","ing","ald","marr","ket","til","ey","dis","ger",
        "svein","bjart","nir","hjal","mar","skul","run","olaf","hav","knut","leif","sten",
        "arv","vid","arn","frid","leif","tork","esb","jor","han","sax","ulf","kel",
        "sigr","bryn","holl","yst","rann","veig","dyr","hraf","sael","mold","eyv","asg",
        "thor","vind","hrann","logi","finn","eirik","snae","hel","ymir","aeg","bur","gisl",
        "brand","skal","varg","hauk","ormr","falk","reyr","gaut","styr","haddr","ingi","sverr",
    };

    // ══════════════════════════════════════════════════════════════════
    //  大陆 5 · 极地风   音节 124⁴ ≈ 2.36×10⁸
    // ══════════════════════════════════════════════════════════════════
    private static readonly string[] C5Syll =
    {
        "qa","nu","tu","pi","la","ka","ma","si","na","uk","ik","ak","ek","ok","aq","iq",
        "qu","su","mu","nu","lu","ku","pu","ru","ta","sa","nga","nng","qaa","qii","quu","qaq",
        "tup","qan","nan","sik","ila","mal","kuk","suk","ang","ing","ung","ang","tuk","puk",
        "qila","nuna","siku","tiri","kala","maku","putu","sana","tala","qimu","nivi","ukiu",
        "aqqa","inuk","angak","tupil","qiviu","nanuq","ukali","sikuu","qanni","pikki","tunni","millu",
        "arsa","kang","qer","nuk","tuk","sav","imi","qer","tasi","uqaq","ping","suli",
        "kala","isik","takk","sior","nipa","qaqq","kuja","kimm","igdl","erli","tasi","naner",
        "aulla","eqqa","tikk","qaqo","nuni","sila","qila","pikk","tunu","sann","matt","qimu",
    };

    /// <summary>大陆 1 是「姓 + 三字名」（两个池）；其余是「四音节名」（单池复用四槽）。</summary>
    private static (string[] Head, string[] Body) PoolsOf(int continentId) => continentId switch
    {
        1 => (C1Surname, C1Given),
        2 => (C2Syll, C2Syll),
        3 => (C3Syll, C3Syll),
        4 => (C4Syll, C4Syll),
        5 => (C5Syll, C5Syll),
        _ => throw new ArgumentOutOfRangeException(nameof(continentId), continentId, "大陆编号必须为 1..5"),
    };

    /// <summary>槽位数：1 个头槽 + 3 个身槽。</summary>
    private const int BodySlots = 3;

    /// <summary>
    /// 组合空间大小 = |Head| × |Body|³。design.md §8.3 要求 ≥ 5×10⁷。
    /// <b>这是可验证的硬指标</b>，由 <see cref="Invariants"/> 逐大陆断言。
    /// </summary>
    public static double CombinationSpace(int continentId)
    {
        var (head, body) = PoolsOf(continentId);
        return (double)head.Length * Math.Pow(body.Length, BodySlots);
    }

    /// <summary>
    /// 生成姓名。<b>纯函数</b>：同 <c>(seed, c, t, seq)</c> 恒等（ID-14）。
    /// 不查表、不缓存、不读任何模拟状态。
    /// </summary>
    public static string Generate(long seed, int continentId, int tribeId, int seq)
    {
        var (surname, given) = GenerateParts(seed, continentId, tribeId, seq);
        return surname.Length == 0 ? given : $"{surname} {given}";
    }

    /// <summary>
    /// 同 <see cref="Generate"/>，但把姓与名分开返回
    /// （<c>members.surname</c> / <c>members.given_name</c> 两列需要，design.md §8.4）。
    ///
    /// 无姓氏传统的文化（大陆 2~5）返回空 <c>Surname</c>，全部文本落在 <c>Given</c>。
    /// </summary>
    public static (string Surname, string Given) GenerateParts(
        long seed, int continentId, int tribeId, int seq)
    {
        var (head, body) = PoolsOf(continentId);

        // 四个槽位各取一次哈希；子域 +s 使同一 seq 的四次取样互不相关。
        int h0 = Pick(seed, 0, continentId, tribeId, seq, head.Length);
        int b0 = Pick(seed, 1, continentId, tribeId, seq, body.Length);
        int b1 = Pick(seed, 2, continentId, tribeId, seq, body.Length);
        int b2 = Pick(seed, 3, continentId, tribeId, seq, body.Length);

        if (continentId == 1)
        {
            // 华夏风：姓（单字）+ 三字名
            string given = string.Concat(body[b0], body[b1], body[b2]);
            return (head[h0], given);
        }

        // 其余：首音节大写 + 次音节 + 撇号 + 后两音节
        var sb = new StringBuilder(20);
        sb.Append(Capitalize(body[b0]))
          .Append(body[b1])
          .Append('\'')
          .Append(Capitalize(body[b2]))
          .Append(head[h0]);
        return (string.Empty, sb.ToString());
    }

    private static int Pick(long seed, int slot, int c, int t, int seq, int n)
    {
        ulong h = Hashing.Hash64(seed, HashDomain.Name + slot, c, t, seq);
        return (int)(h % (ulong)n);
    }

    private static string Capitalize(string s)
        => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>
    /// 导出 Unity 侧使用的音节表 JSON（design.md §8.3 约定的路径）。
    /// 内容与本文件的常量逐字一致。
    /// </summary>
    public static string EmitJson(int continentId)
    {
        var (head, body) = PoolsOf(continentId);
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"continent\": {continentId},\n");
        sb.Append($"  \"style\": \"{StyleName(continentId)}\",\n");
        sb.Append($"  \"structure\": \"{StructureName(continentId)}\",\n");
        sb.Append($"  \"combinationSpace\": {CombinationSpace(continentId):0},\n");
        sb.Append("  \"head\": [");
        for (int i = 0; i < head.Length; i++)
            sb.Append($"\"{head[i]}\"").Append(i == head.Length - 1 ? "" : ", ");
        sb.Append("],\n");
        sb.Append("  \"body\": [");
        for (int i = 0; i < body.Length; i++)
            sb.Append($"\"{body[i]}\"").Append(i == body.Length - 1 ? "" : ", ");
        sb.Append("]\n}\n");
        return sb.ToString();
    }

    public static string StyleName(int continentId) => continentId switch
    {
        1 => "华夏风",
        2 => "南岛海洋风",
        3 => "热带部落风",
        4 => "北欧风",
        5 => "极地风",
        _ => "未知",
    };

    private static string StructureName(int continentId)
        => continentId == 1 ? "姓 + 三字名" : "头音节 + 三音节";
}
