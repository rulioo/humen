using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Humen.Planet
{
    // ══════════════════════════════════════════════════════════════════
    //  数据结构 —— 与 `humen export-world` 的输出逐字对应
    // ══════════════════════════════════════════════════════════════════
    //
    // ⚠️ 这些类是为了 Unity 的 JsonUtility 而写的，形态受它限制，不是随手设计：
    //    · 只认 public 字段（不认属性），故全是字段
    //    · 不支持 Dictionary，故所有"按 id 索引"都在载入后自己建表
    //    · 不支持嵌套容器（List<List<T>>），故折线点是 {lat,lon} 对象数组
    //    改这些字段名时，`Program.cs` 里 ExportWorld 的匿名对象必须同步改。

    [Serializable]
    public sealed class WorldViewFile
    {
        public int schemaVersion;
        public string generatedBy;
        public string seed;
        public WvTimeline timeline;
        public WvContinent[] continents;
        public WvRiver[] rivers;
        public WvRiver[] tributaries;   // 与 rivers 同构，只是多了 riverId
        public WvTribe[] tribes;
    }

    [Serializable]
    public sealed class WvTimeline
    {
        public double startYear;
        public double endYear;
        public string startYearDisplay;
        public string endYearDisplay;
        public int historyStepYears;
        public double firstAgricultureYear;
        public double firstIronYear;
        public double firstIndustrialYear;
    }

    [Serializable]
    public sealed class WvContinent
    {
        public int id;
        public string name;
        public string climateZone;
        public float centerLat;
        public float centerLon;
        public float areaRatio;
        public bool agricultureFeasible;
        public int domesticableSpecies;
        public string mineralRichness;
    }

    [Serializable]
    public sealed class WvPoint
    {
        public float lat;
        public float lon;
    }

    /// <summary>
    /// 干流与支流同构 —— 支流多一个 <see cref="riverId"/>（指向所属干流），余下字段一致。
    /// 合成一个类是因为画法完全相同：都是"一串经纬度，沿大圆连成线"。
    /// </summary>
    [Serializable]
    public sealed class WvRiver
    {
        public string id;
        public int continentId;
        public string riverId;       // 仅支流有；干流为 null
        public string name;
        public float sourceLat, sourceLon, mouthLat, mouthLon;
        public float lengthKm;
        public float dischargeM3s;
        public WvPoint[] points;
    }

    [Serializable]
    public sealed class WvTribe
    {
        public string id;
        public int continentId;
        public string riverId;
        public string tributaryId;
        public string name;
        public float lat, lon, elevationM;
        public string biome;
        public float meanTempC, annualRainMm;
        public long finalPopulation;
        public string parentId;
        public float foundedYear;
        public float coldestMonthC, distToSeaKm, topsoilM;
        public string soilType;
        public float agricultureFactor;
        public string settlement;     // null / village / mudbrick_village
        public string farmland;       // null / field / plowed_field
        public WvSample[] history;
        public WvTech[] techs;
    }

    [Serializable]
    public sealed class WvSample
    {
        public float year;
        public long population;
        public float energyPerCapita;
        public float lifeQuality;
        public int materialTier;
    }

    [Serializable]
    public sealed class WvTech
    {
        public string id;
        public float year;
        public string variant;
    }

    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 载入世界快照，并提供"某一年长什么样"的查询。
    ///
    /// <b>为什么会有这个类</b>：Unity 侧不能引用 <c>Humen.Core</c>
    /// （net8.0 vs .NET Standard 2.1，见 <c>PlanetBootstrap</c> 的依赖方向说明），
    /// 两边的边界只能划在<b>数据文件</b>上。此前的边界只够传一张贴图，
    /// 于是三维视图里没有部落、没有人口、没有技术 —— 作者要的"活生生的世界"
    /// 缺的正是这一层。数据由 <c>humen export-world</c> 产出。
    ///
    /// 载入后把 <c>tribes</c> 数组收进以 id 为键的字典，因为画面上要反复按 id 取。
    /// </summary>
    public sealed class WorldView
    {
        public const string DefaultRelativePath = "World/world_view.json";

        public WorldViewFile File { get; private set; }
        public bool Loaded => File != null;

        private readonly Dictionary<string, WvTribe> _byId = new Dictionary<string, WvTribe>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, WvTribe> ById => _byId;

        /// <summary>载入失败的原因；成功时为 null。给 UI 显示用 —— 静默失败最难查。</summary>
        public string Error { get; private set; }

        public static string DefaultPath => Path.Combine(Application.streamingAssetsPath, DefaultRelativePath);

        public static WorldView LoadFromDefault() => LoadFrom(DefaultPath);

        public static WorldView LoadFrom(string fullPath)
        {
            var wv = new WorldView();
            try
            {
                if (!System.IO.File.Exists(fullPath))
                {
                    wv.Error = $"找不到世界快照：{fullPath}\n" +
                               "先跑 `humen export-world`（它会把 world.db 倒成这个文件）。";
                    return wv;
                }

                string json = System.IO.File.ReadAllText(fullPath, System.Text.Encoding.UTF8);
                var file = JsonUtility.FromJson<WorldViewFile>(json);

                if (file == null || file.tribes == null)
                {
                    wv.Error = $"世界快照解析后没有部落数组：{fullPath}";
                    return wv;
                }
                if (file.schemaVersion != 1)
                {
                    // 宁可报出来。字段对不上时 JsonUtility 是<b>静默留默认值</b>的，
                    // 症状会是"部落全挤在几内亚湾"（经纬度默认 0），极难查。
                    wv.Error = $"世界快照 schemaVersion={file.schemaVersion}，本脚本只认 1。";
                    return wv;
                }

                wv.File = file;
                foreach (var t in file.tribes)
                    if (t != null && !string.IsNullOrEmpty(t.id)) wv._byId[t.id] = t;
            }
            catch (Exception ex)
            {
                wv.Error = $"载入世界快照失败：{ex.Message}";
            }
            return wv;
        }

        /// <summary>
        /// 某部落某年的人口。按关键帧线性插值 —— 快照里存的是每 2500 年一帧，
        /// 直接取最近帧的话，人口会在整 2500 年边界上"跳"。
        /// <list type="bullet">
        ///   <item>建立之前 → 0（还没有这个部落）</item>
        ///   <item>最后一帧之后 → 末帧值（曲线到此为止，不外推 —— 外推会凭空造出人口）</item>
        /// </list>
        /// </summary>
        public static double PopulationAt(WvTribe t, double year)
        {
            var h = t.history;
            if (h == null || h.Length == 0) return t.finalPopulation;
            if (year < h[0].year) return 0.0;
            if (year >= h[h.Length - 1].year) return h[h.Length - 1].population;

            // 帧数约 121，线性扫描足够；且这里刻意不做二分，
            // 是为了让"帧是等距的"这个前提一旦被破坏时不会静默出错。
            for (int i = 1; i < h.Length; i++)
            {
                if (year > h[i].year) continue;
                double y0 = h[i - 1].year, y1 = h[i].year;
                double span = y1 - y0;
                double k = span <= 0 ? 1.0 : (year - y0) / span;
                return h[i - 1].population + (h[i].population - h[i - 1].population) * k;
            }
            return h[h.Length - 1].population;
        }

        /// <summary>某部落某年的材料等级（同样按帧取，但等级是离散量，故取<b>前一帧</b>不插值）。</summary>
        public static int MaterialTierAt(WvTribe t, double year)
        {
            var h = t.history;
            if (h == null || h.Length == 0) return 0;
            int tier = h[0].materialTier;
            for (int i = 0; i < h.Length; i++)
            {
                if (h[i].year > year) break;
                tier = h[i].materialTier;
            }
            return tier;
        }

        /// <summary>
        /// 某部落某年的<b>文明阶段</b> 0..3。
        ///
        /// ⚠️ 这个查询存在的理由，是一次更正：<b>阶段不能拿材料等级当</b>（v0.21 前实现就错在这里）。
        ///
        /// <c>MaterialTier</c> 的定义是
        /// <c>max{ m : 工艺温度(m) ≤ MaxTemp 且 <b>本地可采储量(m) &gt; 0</b> }</c>
        /// （见 Core 的 <c>TechPhysics.MaterialTierOf</c>）—— 它量的是<b>地质机会</b>，
        /// 不是<b>已达到的水平</b>。而这个世界恰好把两者拉得很开：
        /// 大陆 1/2 那 78 个部落坐在<b>裸岩与漠土</b>上，脚下有铁矿却种不出粮，
        /// 于是拿着 tier 4（铁）而过着 0 个聚落、平均 1,512 人、平均 28.3 项技术的日子；
        /// 而真正有城的 79 个部落在<b>砖红壤</b>的大陆 3，平均 85,595 人、48.8 项技术，
        /// 材料等级却只有 1（陶）—— 因为那儿没有铁矿。
        /// 一句话：<b>tier 高的那批是"守着铁矿的穷光蛋"</b>。照 tier 上色，画面是反的。
        ///
        /// 阶段改为从<b>实际掌握的技术</b>派生；判据一律看 <c>cat</c> 的语义而不看 id 的名字
        /// （本工程踩过：<c>city_stage</c> 其实是"市井演出/剧场"，<c>cat: 文化</c>，不是城市）：
        /// <list type="bullet">
        ///   <item>0 游群 —— 没有 <c>settlement</c>（定居聚落，全树仅两项 <c>cat: 建筑</c> 之一）</item>
        ///   <item>1 村落 —— 有 <c>settlement</c></item>
        ///   <item>2 铁器 —— 再会炼 <c>iron</c></item>
        ///   <item>3 工业 —— 再有 <c>steam_engine</c></item>
        /// </list>
        /// 实测终值分布 <b>123 / 17 / 52 / 10</b>，人口加权 <b>2.1% / 18.1% / 62.4% / 17.4%</b> ——
        /// 123 个游群只占世界人口的 2.1%，这个反差本身就是这个世界最该被看见的一件事。
        /// </summary>
        public static int StageAt(WvTribe t, double year)
        {
            if (!HasTechAt(t, year, "settlement")) return 0;
            if (HasTechAt(t, year, "steam_engine")) return 3;
            if (HasTechAt(t, year, "iron")) return 2;
            return 1;
        }

        /// <summary>阶段的中文名，供 UI 读数。索引与 <see cref="StageAt"/> 的返回值对应。</summary>
        public static readonly string[] StageNames = { "游群", "村落", "铁器", "工业" };

        /// <summary>某部落到某年为止已掌握的技术数。用于时间轴上的"文明程度"。</summary>
        public static int TechCountAt(WvTribe t, double year)
        {
            if (t.techs == null) return 0;
            int n = 0;
            foreach (var tech in t.techs)
                if (tech != null && tech.year <= year) n++;
            return n;
        }

        /// <summary>
        /// 某部落在某年是否已掌握某项技术。
        ///
        /// 有这个查询，时间轴才是<b>真的</b>：农田的出现年份能精确到发明 `planting` 那一年，
        /// 而不是"反正它最后有田，就整条时间轴都画上"。
        /// 快照里每项技术都带 <c>acquired_year</c>，正是为了这个。
        ///
        /// 注意语义边界：这是"<b>学过</b>"，不是"现在还有"。
        /// 世界上确实存在"学会又失传"（待裁 ㉑ 实测 `mudbrick` 发现 652 次、遗忘 652 次），
        /// 而 `tech_state` 表的语义是"<b>当前</b>掌握"，导出时已按此过滤 ——
        /// 所以出现在这里的，都是走到最后一刻仍然持有的技术。
        /// 若将来要画"失传"，需要另加一张历史表，不能从这份快照倒推。
        /// </summary>
        public static bool HasTechAt(WvTribe t, double year, string techId)
        {
            if (t.techs == null) return false;
            foreach (var tech in t.techs)
            {
                if (tech == null) continue;
                if (string.Equals(tech.id, techId, StringComparison.Ordinal))
                    return tech.year <= year;
            }
            return false;
        }
    }
}
