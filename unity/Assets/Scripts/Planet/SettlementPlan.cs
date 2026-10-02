using System.Collections.Generic;
using UnityEngine;

namespace Humen.Planet
{
    /// <summary>
    /// M4b-3 的第一层：<b>纯数据</b>的聚落布局 —— 给定 (部落, 年份)，算出这一片聚落上
    /// 该有哪些构件、各在哪儿、多大。<b>不含任何网格与材质</b>，那些在
    /// <see cref="SettlementGeometry"/> 里做。分开是为了能单独验：布局错了（比如房子排到河对岸）
    /// 与几何错了（比如盒子翻了面）是两类问题，混在一起查会很慢。
    ///
    /// ⚠️ <b>一律按年份从技术派生，绝不读快照里的 <c>settlement</c> / <c>farmland</c> 字段。</b>
    /// 那两个是<b>终值</b>，只反映最后一刻。拿它们画，整条时间轴会是同一个样子 ——
    /// 而"看得见演化"正是作者要的（design.md v0.21 ⑯ 的补测记了这条）。
    ///
    /// ⚠️ 确定性（§10.6 ①）：所有随机量都由 <see cref="DetHash"/> 从
    /// <c>(seed, 域, 部落, 构件序号, 年桶)</c> 纯函数导出，与"按什么顺序摆"无关。
    /// 这与 Core 的 <c>Hashing.Hash64</c> 是<b>逐位相同的实现</b> ——
    /// Unity 引用不了 Humen.Core（net8.0 vs .NET Standard 2.1），只能照抄一份，
    /// 故这里写的是镜像而不是仿制：改了 Core 那一份，这一份必须跟着改。
    /// </summary>
    public static class SettlementPlan
    {
        /// <summary>构件种类。名字按 <c>cat</c> 语义取，不按 id 的名字（v0.20 ⑤ 的教训）。</summary>
        public enum Kind
        {
            Fire,        // 火塘
            Tent,        // 兽皮帐篷（游群）
            Hut,         // 窝棚
            House,       // 土坯房／茅屋
            Tenement,    // 多层民居（工业期，人口在涨、房子在变少、聚落在长高）
            Kiln,        // 陶窑
            Smelter,     // 冶炼场
            Forge,       // 锻炉
            Granary,     // 公仓（架高＋锥顶）
            Pen,         // 畜栏
            Field,       // 田块
            Plaza,       // 集市广场
            Road,        // 车辙路
            Wall,        // 围墙
            Tower,       // 角楼／钟楼
            Stele,       // 刻符石碑
            Workshop,    // 作坊（印刷／玻璃／火药）
            Factory,     // 厂房
            Chimney,     // 烟囱
            Dock,        // 码头
            Theater,     // 市井戏台（`city_stage` 的真身）
        }

        /// <summary>材质槽。与 <see cref="SettlementMaterials"/> 的下标一一对应。</summary>
        public enum Mat
        {
            Ground = 0,   // 夯土／路面
            Thatch = 1,   // 茅草／兽皮
            Mud = 2,      // 土坯墙
            Stone = 3,    // 石／砖
            Timber = 4,   // 木
            Metal = 5,    // 金属／工业
            Crop = 6,     // 作物（田）
        }

        public const int MatCount = 7;

        public struct Building
        {
            public Kind Kind;
            public Mat Mat;
            public float X, Z;      // 米。聚落本地坐标，原点在聚落中心
            public float RotY;      // 度
            public float W, D, H;   // 米（宽／进深／高）
            public float Extra;     // 备用（锥顶半径、烟囱高之类）
            public int Index;       // 该构件在本次布局里的序号，供几何层做确定性细节
        }

        public sealed class Layout
        {
            public readonly List<Building> Buildings = new List<Building>();
            public float Radius;        // 聚落外接半径（米），相机取景要用
            public int Stage;           // 0..3，见 WorldView.StageAt
            public double Population;
            public int Year;
        }

        // ── 尺度（§10.5）─────────────────────────────────────────────
        // 每栋住多少人、总数上限、屋间距。工业期每栋住得多（多层），故栋数反而少 ——
        // 「人口在涨、房子在变少、聚落在长高」。
        private static readonly int[] PerDwelling = { 5, 6, 8, 60 };
        private static readonly int[] DwellingCap = { 40, 160, 240, 320 };
        private static readonly float[] Spacing = { 20f, 15f, 14f, 22f };

        /// <summary>世界种子。取自快照的 seed 字段，保证与模拟同源。</summary>
        public static long Seed = 20261001L;

        private const int DomainLayout = 71;   // 本文件专用的哈希子域，别与 Core 的撞

        // ── 哈希镜像（Core: Hashing.Hash64 / ToUnit）──────────────────
        private const ulong FnvOffset = 0xCBF29CE484222325UL;
        private const ulong FnvPrime = 0x00000100000001B3UL;

        private static ulong Mix(ulong h, ulong v)
        {
            unchecked
            {
                h ^= v;
                h *= FnvPrime;
                h ^= h >> 29;
                return h;
            }
        }

        /// <summary>与 Core 的 <c>Hashing.Hash64</c> 逐位相同。</summary>
        public static ulong Hash64(long seed, int domain, int a, int b, int c)
        {
            unchecked
            {
                ulong h = Mix(FnvOffset, unchecked((ulong)seed));
                h = Mix(h, unchecked((ulong)(uint)domain));
                h = Mix(h, unchecked((ulong)(uint)a));
                h = Mix(h, unchecked((ulong)(uint)b));
                h = Mix(h, unchecked((ulong)(uint)c));
                h += 0x9E3779B97F4A7C15UL;
                h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL;
                h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL;
                return h ^ (h >> 31);
            }
        }

        /// <summary>把哈希映射到 [0,1)。与 Core 的 <c>Hashing.ToUnit</c> 相同。</summary>
        public static float Unit(ulong h) => (float)((h >> 11) * (1.0 / 9007199254740992.0));

        /// <summary>
        /// 部落 id（形如 <c>3.11</c>）→ 整数，供哈希用。
        /// 用 FNV-1a 而不是 <c>string.GetHashCode()</c>：后者在 .NET Core 起<b>默认随机化</b>，
        /// 每次进程都不一样，会直接毁掉确定性（Core 的 Rng.cs 里记了同一条理由）。
        /// </summary>
        public static int TribeKey(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            unchecked
            {
                ulong h = FnvOffset;
                foreach (char ch in id) h = Mix(h, (byte)ch);
                return (int)(h ^ (h >> 32));
            }
        }

        // ── 主入口 ──────────────────────────────────────────────────

        /// <summary>算出某部落某年的聚落布局。人口为 0 则返回空布局（那年还没有这个部落）。</summary>
        public static Layout Plan(WvTribe t, double year)
        {
            var L = new Layout
            {
                Stage = WorldView.StageAt(t, year),
                Population = WorldView.PopulationAt(t, year),
                Year = (int)year,
            };
            if (t == null || L.Population <= 0) return L;

            int key = TribeKey(t.id);
            int yearBucket = (int)(year / 250.0);     // 与快照的 2500 年帧错开，避免同帧内全同

            int stage = L.Stage;
            int idx = 0;

            // ── 1. 居所：聚落的主体，先排 ──────────────────────────
            int n = Mathf.Clamp(
                Mathf.CeilToInt((float)(L.Population / PerDwelling[stage])),
                4, DwellingCap[stage]);
            float spacing = Spacing[stage];
            float radius = spacing * Mathf.Sqrt(n) * 0.62f;
            L.Radius = radius;

            // 向日葵布点（黄金角）＋ 哈希抖动：均匀铺满圆盘又不呆板。
            // 用黄金角而不是网格，是因为网格在低栋数时会露出明显的方格，
            // 而聚落有机生长的样子更接近前者。抖动由哈希给，故仍可复现。
            const float GoldenAngle = 2.39996323f;
            for (int i = 0; i < n; i++)
            {
                ulong h = Hash64(Seed, DomainLayout, key, i, yearBucket);
                float jr = 0.82f + 0.36f * Unit(h);                       // 半径抖动 ±18%
                float ja = (Unit(h * 31) - 0.5f) * (Mathf.PI / n) * 6f;   // 角向抖动
                float rr = radius * Mathf.Sqrt((i + 0.5f) / n) * jr;
                float th = i * GoldenAngle + ja;

                var kind = stage == 0 ? (Unit(h * 7) < 0.35f ? Kind.Tent : Kind.Hut)
                                      : (stage >= 3 ? Kind.Tenement : Kind.House);
                Add(L, ref idx, kind, MatFor(kind), rr * Mathf.Cos(th), rr * Mathf.Sin(th),
                    Unit(h * 13) * 360f, SizeOf(kind, h));
            }

            // ── 2. 中心：火塘（第一个光源）────────────────────────
            if (WorldView.HasTechAt(t, year, "fire_use"))
                Add(L, ref idx, Kind.Fire, Mat.Mud, 0, 0, 0, new Vector3(2.6f, 2.6f, 0.5f));

            // ── 3. 生产设施：按技术逐年出现 ────────────────────────
            // 每条都是「有这项技术才画」，故时间轴一拖，聚落会一个个长出来。
            float ring = Mathf.Max(radius * 0.72f, 12f);

            if (WorldView.HasTechAt(t, year, "clay_kiln"))
                AddAtRing(L, ref idx, Kind.Kiln, Mat.Stone, ring, 0.5f, key, yearBucket, new Vector3(4f, 4f, 3.2f));
            if (WorldView.HasTechAt(t, year, "high_temp_kiln") || WorldView.HasTechAt(t, year, "bronze"))
                AddAtRing(L, ref idx, Kind.Smelter, Mat.Stone, ring, 1.6f, key, yearBucket, new Vector3(7f, 6f, 5f));
            if (WorldView.HasTechAt(t, year, "iron"))
                AddAtRing(L, ref idx, Kind.Forge, Mat.Stone, ring, 2.6f, key, yearBucket, new Vector3(6f, 5.5f, 4.5f));
            if (WorldView.HasTechAt(t, year, "granary"))
                AddAtRing(L, ref idx, Kind.Granary, Mat.Timber, ring, 3.7f, key, yearBucket, new Vector3(5f, 5f, 6f));
            if (WorldView.HasTechAt(t, year, "animal_herd"))
                for (int i = 0; i < 3; i++)
                    AddAtRing(L, ref idx, Kind.Pen, Mat.Timber, ring * 1.18f, 0.9f + i * 0.21f, key, yearBucket,
                              new Vector3(14f, 11f, 1.4f));
            if (WorldView.HasTechAt(t, year, "market"))
                AddAtRing(L, ref idx, Kind.Plaza, Mat.Ground, radius * 0.42f, 0.27f, key, yearBucket,
                          new Vector3(radius * 0.5f, radius * 0.5f, 0.25f));

            // 文字：刻符石碑。作者树里的 `writing` 是「记账符号→象形」。
            if (WorldView.HasTechAt(t, year, "writing"))
                AddAtRing(L, ref idx, Kind.Stele, Mat.Stone, radius * 0.3f, 0.63f, key, yearBucket,
                          new Vector3(1.8f, 1.0f, 4.2f));

            // 作坊类：三者共用一种体量，靠 Material 与高度区分，免得为每项写一套几何。
            if (WorldView.HasTechAt(t, year, "woodblock_print") || WorldView.HasTechAt(t, year, "glass")
                || WorldView.HasTechAt(t, year, "gunpowder"))
                AddAtRing(L, ref idx, Kind.Workshop, Mat.Stone, ring * 1.08f, 4.4f, key, yearBucket,
                          new Vector3(8f, 7f, 5f));

            // 火药：棱堡（矮而宽的方台）
            if (WorldView.HasTechAt(t, year, "gunpowder"))
                AddAtRing(L, ref idx, Kind.Wall, Mat.Stone, ring * 1.34f, 5.1f, key, yearBucket,
                          new Vector3(16f, 3.5f, 3f));

            // 钟楼／剧场 —— 都是 `cat` 明确的非建筑节点，但确实是聚落里的地标。
            if (WorldView.HasTechAt(t, year, "mechanical_clock"))
                AddAtRing(L, ref idx, Kind.Tower, Mat.Stone, radius * 0.55f, 5.8f, key, yearBucket,
                          new Vector3(5f, 5f, 18f));
            if (WorldView.HasTechAt(t, year, "city_stage"))
                AddAtRing(L, ref idx, Kind.Theater, Mat.Stone, radius * 0.62f, 6.4f, key, yearBucket,
                          new Vector3(13f, 11f, 6f));

            // 私有制 → 围墙＋角楼（聚落第一次有边界）
            if (WorldView.HasTechAt(t, year, "property_private"))
            {
                float wr = radius * 1.28f;
                int sides = 4;
                for (int i = 0; i < sides; i++)
                {
                    float a = i * (Mathf.PI * 2f / sides) + 0.4f;
                    Add(L, ref idx, Kind.Wall, Mat.Stone, wr * Mathf.Cos(a), wr * Mathf.Sin(a),
                        -a * Mathf.Rad2Deg, new Vector3(wr * 1.5f, 2.4f, 3.4f));
                    Add(L, ref idx, Kind.Tower, Mat.Stone, wr * 1.06f * Mathf.Cos(a), wr * 1.06f * Mathf.Sin(a),
                        0, new Vector3(3.2f, 3.2f, 8f));
                }
            }

            // 交通：车辙路。有轮子才画，从中心放射出去 —— 这是聚落里唯一的"线状地物"。
            // ⚠️ 尺寸是 (长, 宽, 厚)。第一版 (2.6R, 3.2, 0.15) 渲出来是<b>划痕</b>：
            //    3.2 m 宽的路在 1000 m 外不足一个像素，0.15 m 的厚度更是没有实体感。
            //    现在 6 m 宽、0.4 m 厚，长到田圈（1.6R）为止 —— 路从城里通到田里，
            //    而不是戳出城外一截。
            if (WorldView.HasTechAt(t, year, "cart") || WorldView.HasTechAt(t, year, "wheel"))
                for (int i = 0; i < 4; i++)
                    Add(L, ref idx, Kind.Road, Mat.Ground, 0, 0, i * 45f,
                        new Vector3(radius * 3.2f, 9f, 0.4f));

            // 码头：只有靠水的部落才有。快照里有 riverId，用它当"有没有水"的代理。
            if ((WorldView.HasTechAt(t, year, "ship") || WorldView.HasTechAt(t, year, "sail"))
                && !string.IsNullOrEmpty(t.riverId))
                AddAtRing(L, ref idx, Kind.Dock, Mat.Timber, radius * 1.45f, 7.2f, key, yearBucket,
                          new Vector3(20f, 4f, 0.6f));

            // 工业：厂房＋烟囱。`coal_fuel` 也算 —— 烧煤本身就是工业期的标志。
            bool industrial = WorldView.HasTechAt(t, year, "steam_engine")
                              || WorldView.HasTechAt(t, year, "coal_fuel");
            if (industrial)
            {
                int m = stage >= 3 ? 3 : 1;
                for (int i = 0; i < m; i++)
                {
                    AddAtRing(L, ref idx, Kind.Factory, Mat.Metal, ring * 1.25f, 0.37f + i * 0.23f, key, yearBucket,
                              new Vector3(24f, 14f, 9f));
                    AddAtRing(L, ref idx, Kind.Chimney, Mat.Metal, ring * 1.25f, 0.37f + i * 0.23f + 0.06f,
                              key, yearBucket, new Vector3(2.6f, 2.6f, 22f));
                }
            }

            // ── 4. 田：种地之后才有，铺在最外圈 ────────────────────
            if (WorldView.HasTechAt(t, year, "planting"))
            {
                // 犁过的田更规整（有 `plow`），没犁的是零散块。条数按人口定，封顶。
                bool plowed = WorldView.HasTechAt(t, year, "plow");
                int fields = Mathf.Clamp((int)(L.Population / 900.0), 4, 14);
                float fr = radius * (plowed ? 1.75f : 1.6f);
                for (int i = 0; i < fields; i++)
                {
                    ulong h = Hash64(Seed, DomainLayout, key, 900 + i, yearBucket);
                    float a = (i + 0.35f * Unit(h)) * (Mathf.PI * 2f / fields);
                    // 厚度 0.12 → 0.7：原来薄得像纸，侧看是一条虚线，根本认不出是田。
                    Add(L, ref idx, Kind.Field, Mat.Crop, fr * Mathf.Cos(a), fr * Mathf.Sin(a),
                        -a * Mathf.Rad2Deg, new Vector3(plowed ? 26f : 19f, plowed ? 15f : 12f, 0.7f));
                }
            }

            return L;
        }

        // ── 小工具 ──────────────────────────────────────────────────

        private static void Add(Layout L, ref int idx, Kind k, Mat m,
                                float x, float z, float rotY, Vector3 size)
        {
            L.Buildings.Add(new Building
            {
                Kind = k, Mat = m, X = x, Z = z, RotY = rotY,
                W = size.x, D = size.y, H = size.z, Index = idx++,
            });
            L.Radius = Mathf.Max(L.Radius, Mathf.Sqrt(x * x + z * z) + Mathf.Max(size.x, size.y) * 0.5f);
        }

        /// <summary>摆在以 <paramref name="ring"/> 为半径的环上，角度由哈希定 —— 同一个部落每次都在同一处。</summary>
        private static void AddAtRing(Layout L, ref int idx, Kind k, Mat m, float ring, float frac,
                                      int key, int yearBucket, Vector3 size)
        {
            ulong h = Hash64(Seed, DomainLayout, key, 500 + idx, yearBucket);
            float a = (frac + 0.08f * Unit(h)) * Mathf.PI * 2f;
            Add(L, ref idx, k, m, ring * Mathf.Cos(a), ring * Mathf.Sin(a), -a * Mathf.Rad2Deg, size);
        }

        private static Mat MatFor(Kind k)
        {
            switch (k)
            {
                case Kind.Tent: return Mat.Thatch;
                case Kind.Hut: return Mat.Thatch;
                case Kind.House: return Mat.Mud;
                case Kind.Tenement: return Mat.Stone;
                default: return Mat.Mud;
            }
        }

        private static Vector3 SizeOf(Kind k, ulong h)
        {
            switch (k)
            {
                // 抖动 ±15%，让聚落不至于像兵营
                case Kind.Tent: return new Vector3(4.2f, 4.2f, 2.4f) * (0.85f + 0.3f * Unit(h * 3));
                case Kind.Hut: return new Vector3(3.4f, 3.4f, 2.2f) * (0.85f + 0.3f * Unit(h * 3));
                case Kind.House: return new Vector3(7.5f, 6f, 3.4f) * (0.85f + 0.3f * Unit(h * 3));
                // ⚠️ 楼<b>高</b>单独抖，且抖得比平面尺寸狠得多（8.8～24 m）。
                //    第一版三个轴一起抖 ±15%，320 栋楼几乎一样高 ——
                //    渲出来是一片棋子，不是一座城。城市的样子主要来自<b>高低错落</b>，
                //    其次是密集，最后才是单体的细节。
                case Kind.Tenement: return new Vector3(
                    11f * (0.85f + 0.3f * Unit(h * 3)),
                    9f * (0.85f + 0.3f * Unit(h * 3)),
                    16f * (0.55f + 0.95f * Unit(h * 11)));
                default: return new Vector3(4f, 4f, 3f);
            }
        }
    }
}
