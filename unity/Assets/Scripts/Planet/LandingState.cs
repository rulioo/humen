namespace Humen.Planet
{
    /// <summary>
    /// 跨场景携带的「降落」状态 —— 年份与当前选中的部落。
    ///
    /// 为什么需要这么一个静态类：行星视图与聚落视图是<b>两个场景</b>（§10.1：
    /// B 级到 C 级差 6371 倍，不是缩放而是降落），切换走
    /// <c>SceneManager.LoadScene</c>，场景里的一切都会被丢掉。
    /// 而"我在哪一年、在看哪个部落"这两件事必须活着穿过那次切换，
    /// 否则每次降落都会回到公元 2125 年的默认部落 —— 那不叫降落，叫传送。
    ///
    /// ⚠️ 这是<b>唯一</b>允许跨场景活着的状态。其余一概不留：
    /// 场景重建得出来的一律重建（§7.3），存下来的只有"用户的意图"。
    /// </summary>
    public static class LandingState
    {
        /// <summary>
        /// 没有点选任何部落时的落点：赤道大陆 <b>3.4.2</b>（当次技术前沿）。
        ///
        /// ⚠️ 与 <c>SettlementSceneBuilder.EvidenceTribe</c> / <c>SettlementView.DefaultTribeId</c>
        ///    <b>三处必须一致</b>，而这里<b>只能重复写一遍字符串</b> ——
        ///    Editor 程序集（SettlementSceneBuilder）不在运行时的引用范围里，
        ///    运行时代码引用不到它。所以这三个常量是"手工同步的副本"，
        ///    重跑世界之后要一起改。<b>漏改一个的症状：按 Tab 降落到一个早已不是前沿的部落。</b>
        /// </summary>
        public const string DefaultTribeId = "3.4.2";

        /// <summary>要带过去的年份（带符号，负数即公元前）。</summary>
        public static double Year = 2125;

        /// <summary>当前选中的部落 id。空则降落时改取当年人口最多的那个。</summary>
        public static string TribeId = DefaultTribeId;

        /// <summary>
        /// 年份是否已被显式设过。
        /// ⚠️ 必须在场：第一次进场景时<b>不该</b>覆盖场景自己的默认年份，
        /// 而 <c>Year</c> 有个非零初值，光看它是分不出"没设过"与"设成了 2125"的。
        /// </summary>
        public static bool HasYear;
    }
}
