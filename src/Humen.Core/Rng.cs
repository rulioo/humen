namespace Humen.Core;

/// <summary>
/// 确定性伪随机数发生器（SplitMix64）。
///
/// ⚠️ <b>刻意不使用 <c>System.Random</c></b>：其内部算法在不同 .NET 版本间
/// 不保证稳定（.NET Core 3.0 起改过实现）。而本项目的核心不变量
/// INV-12 / INV-34 / INV-35 全都要求「<b>同 seed 下逐位一致</b>」——
/// 一旦 PRNG 变了，整个历史就重写了。故自带一个规格冻结的实现。
///
/// 算法：SplitMix64（Steele et al. 2014）。周期 2⁶⁴，通过 BigCrush。
/// </summary>
public struct Rng
{
    private ulong _state;

    public Rng(long seed) => _state = unchecked((ulong)seed);

    /// <summary>下一个 64 位无符号整数。</summary>
    public ulong NextU64()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15UL;   // 黄金比例增量
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>均匀分布于 [0, 1)。</summary>
    public double NextDouble() => (NextU64() >> 11) * (1.0 / 9007199254740992.0); // 2^53

    /// <summary>均匀分布于 [lo, hi) 的整数。hi ≤ lo 时返回 lo。</summary>
    public int NextInt(int lo, int hi)
    {
        if (hi <= lo) return lo;
        int v = lo + (int)(NextDouble() * (hi - lo));
        return v >= hi ? hi - 1 : v;   // 防浮点边界
    }

    /// <summary>均匀分布于 [lo, hi) 的实数。</summary>
    public double NextRange(double lo, double hi) => lo + NextDouble() * (hi - lo);

    /// <summary>以概率 p 返回 true。</summary>
    public bool Chance(double p) => NextDouble() < p;

    /// <summary>原地 Fisher–Yates 洗牌（用本 RNG，故可复现）。</summary>
    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = NextInt(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

/// <summary>
/// 稳定哈希：把 <c>(seed, domain, a, b, c)</c> 映射为 64 位。
///
/// 用途：<see cref="Identity"/> 的<b>纯函数</b>派生（id.md §3.2.3）。
/// 必须满足：同样的输入 → 同样的输出，跨进程、跨机器、跨 .NET 版本恒等（INV-34）。
/// 故不使用 <c>string.GetHashCode()</c>（.NET Core 起默认随机化，故意不可复现）。
/// </summary>
public static class Hashing
{
    private const ulong FnvOffset = 0xCBF29CE484222325UL;
    private const ulong FnvPrime = 0x00000100000001B3UL;

    /// <summary>FNV-1a 的一步混合 + 一次右移折叠，保证雪崩性。</summary>
    public static ulong Mix(ulong h, ulong v)
    {
        unchecked
        {
            h ^= v;
            h *= FnvPrime;
            h ^= h >> 29;
            return h;
        }
    }

    public static ulong Mix(ulong h, long v) => Mix(h, unchecked((ulong)v));
    public static ulong Mix(ulong h, int v) => Mix(h, unchecked((ulong)(uint)v));

    /// <summary>四元组哈希。<paramref name="domain"/> 用于隔离不同用途，避免跨用途碰撞。</summary>
    public static ulong Hash64(long seed, int domain, int a, int b, int c)
    {
        unchecked
        {
            ulong h = Mix(FnvOffset, seed);
            h = Mix(h, domain);
            h = Mix(h, a);
            h = Mix(h, b);
            h = Mix(h, c);

            // 终混：SplitMix64 的 finalizer，保证低位同样雪崩
            h += 0x9E3779B97F4A7C15UL;
            h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL;
            h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL;
            return h ^ (h >> 31);
        }
    }

    /// <summary>把 64 位哈希映射到 [0, 1)。</summary>
    public static double ToUnit(ulong h) => (h >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>把 64 位哈希映射到 [lo, hi)。</summary>
    public static double ToRange(ulong h, double lo, double hi) => lo + ToUnit(h) * (hi - lo);
}

/// <summary>
/// 哈希子域标签。同一个 <c>(seed, a, b, c)</c> 在不同子域下必须得到不相关的哈希，
/// 否则"性别"和"姓名"会同步变化，产生可见的相关性。
/// </summary>
public static class HashDomain
{
    public const int Gender = 1;
    public const int Name = 2;
    public const int Birth = 3;
    public const int Tribe = 4;
    public const int Elevation = 5;

    // ── 进化引擎（§4 / §6.3）──
    // 这些域的关键性质是：随机数由 (seed, 域, 部落 id, 项, 年桶) 纯函数导出，
    // 而不是从一条顺序流里取。于是「取数的次数/顺序」不影响结果 ——
    // 这正是 INV-12（同 seed 下无论焦点 LOD 如何切换，模拟结果完全一致）的实现基础：
    // 高 LOD 下多算了某些部落，低 LOD 下没算，但每个部落自己那一份随机数分毫不变。

    /// <summary>技术发现判定（§1.3）。项 = 节点在技术树中的序号，年桶 = floor(year)。</summary>
    public const int Discovery = 6;

    /// <summary>冲击判定（§4.7：饥荒 / 瘟疫 / 战争 / 气候突变）。</summary>
    public const int Shock = 7;

    /// <summary>部落分裂（§6.3）——决定新部落往哪个方向找新址。</summary>
    public const int Fission = 8;

    /// <summary>杂项（尚未归类的演化随机量）。</summary>
    public const int Evolution = 9;

    /// <summary>
    /// 知识扩散（§1.8 P1a：从邻邦习得技术）。
    /// 项 = 0 掷"这一步有无交流机会"、1 掷"挑中池里的哪一项"。
    /// </summary>
    public const int Diffusion = 10;
}
