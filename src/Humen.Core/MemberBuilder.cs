namespace Humen.Core;

/// <summary>
/// 元年 10 万成员生成（req.txt 第 4~5 行；design.md §8.1~§8.3）。
///
/// ⚠️ 这 10 万人是 <b>L1 实体层</b>—— 他们会真的落进 <c>members</c> 表。
/// 之后的 2×10¹⁰ 人则交给 <see cref="Identity.Derive"/> 按需派生，一字不存（id.md §3.2）。
///
/// 生成逻辑本身极薄：<b>一切都已封装在 <see cref="Identity.Derive"/> 里</b>。
/// 这不是偷懒，而是刻意的 —— 元年成员与后世成员必须走<b>同一条代码路径</b>，
/// 否则 ID 格式迟早会分叉（ID-17）。
/// </summary>
public static class MemberBuilder
{
    /// <summary>生成全部 100 个部落 × 1000 人。</summary>
    public static List<Member> BuildGenesis(long seed, IReadOnlyList<Tribe> tribes)
    {
        var members = new List<Member>(WorldConfig.GenesisMemberCount);

        foreach (var tribe in tribes)
        {
            for (int seq = 1; seq <= WorldConfig.MembersPerTribe; seq++)
            {
                members.Add(Identity.Derive(
                    seed,
                    continentId: tribe.ContinentId,
                    globalTribeIndex: tribe.GlobalIndex,
                    seq: seq,
                    source: IdSource.Genesis));
            }
        }
        return members;
    }

    /// <summary>按部落分组统计性别，供 INV-3 验收。</summary>
    public static (int Male, int Female) CountGenders(IEnumerable<Member> members)
    {
        int m = 0, f = 0;
        foreach (var x in members)
        {
            if (x.Gender == Gender.Male) m++; else f++;
        }
        return (m, f);
    }
}
