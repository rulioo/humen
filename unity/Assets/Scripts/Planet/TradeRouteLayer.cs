using System.Collections.Generic;
using UnityEngine;

namespace Humen.Planet
{
    /// <summary>
    /// M4c-1：把<b>聚落之间的路</b>画到球面上 —— 作者要的
    /// "地表内容可以参考《文明》游戏的设定"里，最认得出来的那一件就是<b>路网</b>：
    /// 一座座孤立的城不算文明，<b>城与城之间连起来</b>才是。
    ///
    /// 在此之前，星球上只有"点"（部落标记）与"线"（河流），没有任何一条
    /// <b>人造的线</b> —— 于是无论把时间轴拖到哪一年，画面上都看不出
    /// "这些聚落之间有关系"。这一层补的就是这件事。
    ///
    /// <b>路从哪来</b>：不新造任何仿真数据。判据是快照里已有的技术节点 ——
    /// 会造车（<c>cart</c>）或会用轮（<c>wheel</c>）之后才有陆路，
    /// 会烧煤／有蒸汽机（<c>steam_engine</c>）之后路升级成深色的大道。
    /// 于是一条时间轴拖过去，路网会<b>从无到有、从疏到密</b>。
    /// ⚠️ <c>tech_tree.md</c> 是作者的数据文件，<b>本层一个节点都不改</b>（见 design.md 待裁 ⑲）。
    ///
    /// <b>连谁</b>：每个部落连到<b>同大陆上最近的那个同样有路的部落</b>，
    /// 边去重后一次画一条。这给出的是"最近邻图"而不是全连接 ——
    /// 全连接在 197 个部落下是 19306 条线，画面上会糊成一团墨。
    ///
    /// <b>为什么不跨海</b>：最近邻若不限大陆，靠海的部落会连到对岸去，
    /// 海面上出现一条路 —— 那不是"这个世界的路网"，那是 bug 的样子。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TradeRouteLayer : MonoBehaviour
    {
        /// <summary>子网格数：0 土路（车／轮），1 大道（蒸汽／煤）。</summary>
        public const int SubMeshCount = 2;

        [Header("星球")]
        public float Radius = 1000f;

        [Tooltip("路面离地高度倍率。比部落标记低 —— 路在地表，标记是图钉。")]
        public float HeightScale = 1.0012f;

        [Header("宽度（Unity 单位，按屏幕尺度缩放后的基准）")]
        [Tooltip("土路宽度。")]
        public float DirtWidth = 7f;

        [Tooltip("大道宽度。")]
        public float TrunkWidth = 11f;

        [Tooltip("路宽基准相机距离（倍率 × 半径）。与 TribeMarkerLayer 同一个约定。")]
        public float ReferenceDistanceFactor = 3.0f;

        [Header("加密")]
        [Tooltip("相邻采样点最多隔多少度。路是沿大圆的弧，不加密的话弦会陷进球体。")]
        public float MaxSegmentDegrees = 1.0f;

        [Tooltip("一条路最长多少度。超过即当作孤立聚落，不修路 —— 免得跨着半个星球拉一条线。")]
        public float MaxRouteDegrees = 30f;

        [Header("数据")]
        public string WorldViewPath = "";

        [Header("时间")]
        public double Year = 2125;

        private WorldView _world;
        private Mesh _mesh;
        private MeshRenderer _mr;
        private Material[] _mats;

        private Vector3[] _norms;        // 每个部落的单位法线，载入时缓存一次
        private readonly List<Edge> _edges = new List<Edge>();

        /// <summary>绕序自检发现的朝内面数。非零即说明法线反了、整层会被背面剔除。
        /// 与 <see cref="SettlementGeometry.InwardFaceCount"/> 是同一道保险 ——
        /// 本工程在 <see cref="TribeMarkerLayer"/> 上真栽过：绕序反了，一个像素都不画，且不报错。</summary>
        public int InwardFaceCount { get; private set; }

        private struct Edge
        {
            public int A, B;          // 部落下标
            public int SubMesh;       // 0 土路 / 1 大道
        }

        public string Error => _world != null ? _world.Error : "尚未载入世界快照";
        public bool Ready => _world != null && _world.Loaded;

        /// <summary>当前年份画出来的路的条数 —— 给 UI 做读数。</summary>
        public int RouteCount => _edges.Count;

        private void Awake()
        {
            Load();
            Build();
        }

        public void Load()
        {
            _world = string.IsNullOrEmpty(WorldViewPath)
                ? WorldView.LoadFromDefault()
                : WorldView.LoadFrom(WorldViewPath);

            if (!_world.Loaded)
            {
                Debug.LogWarning($"[Humen] 道路图层没数据：{_world.Error}");
                return;
            }

            // 法线只与经纬度有关，与年份无关 —— 载入时算一次，之后每次重建都直接用。
            var tribes = _world.File.tribes;
            _norms = new Vector3[tribes.Length];
            for (int i = 0; i < tribes.Length; i++)
                _norms[i] = PlanetGeometry.NormalAt(tribes[i].lat, tribes[i].lon);

            if (Year > _world.File.timeline.endYear) Year = _world.File.timeline.endYear;
        }

        /// <summary>按当前 <see cref="Year"/> 重算路网并重建网格。时间轴一动就调它。</summary>
        public void Build()
        {
            if (_mr == null)
            {
                _mr = GetComponent<MeshRenderer>();
                if (_mr == null) _mr = gameObject.AddComponent<MeshRenderer>();
            }

            if (!Ready)
            {
                _mr.enabled = false;
                return;
            }
            _mr.enabled = true;

            CollectEdges();
            WriteMesh();
        }

        /// <summary>算出这一年该有哪些路。</summary>
        private void CollectEdges()
        {
            _edges.Clear();

            var tribes = _world.File.tribes;
            int n = tribes.Length;

            // 这一年"有路可走"的部落。判据全在快照已有的技术上，不新增任何仿真量。
            var hasRoad = new bool[n];
            var isTrunk = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var t = tribes[i];
                if (WorldView.PopulationAt(t, Year) <= 0) continue;         // 这一年还没建立
                hasRoad[i] = WorldView.HasTechAt(t, Year, "cart")
                          || WorldView.HasTechAt(t, Year, "wheel");
                // 大道的判据刻意与 StageAt 的"工业"一致（steam_engine），
                // 免得出现"画面上是工业城、路却还是土路"这种自相矛盾。
                isTrunk[i] = WorldView.HasTechAt(t, Year, "steam_engine")
                          || WorldView.HasTechAt(t, Year, "coal_fuel");
            }

            float maxCos = Mathf.Cos(MaxRouteDegrees * Mathf.Deg2Rad);
            var seen = new HashSet<long>();

            for (int i = 0; i < n; i++)
            {
                if (!hasRoad[i]) continue;

                int best = -1;
                float bestDot = maxCos;      // 只接受夹角小于 MaxRouteDegrees 的
                for (int j = 0; j < n; j++)
                {
                    if (j == i || !hasRoad[j]) continue;
                    // ⚠ 必须同大陆。不限的话靠海的部落会连到对岸，
                    //   海面上出现一条路 —— 那不是路网，那是 bug 的样子。
                    if (tribes[j].continentId != tribes[i].continentId) continue;

                    float d = Vector3.Dot(_norms[i], _norms[j]);
                    if (d > bestDot) { bestDot = d; best = j; }
                }
                if (best < 0) continue;

                int lo = i < best ? i : best;
                int hi = i < best ? best : i;
                long key = ((long)lo << 32) | (uint)hi;
                if (!seen.Add(key)) continue;          // 同一条边正反各算过一次，只画一条

                _edges.Add(new Edge
                {
                    A = lo,
                    B = hi,
                    // 两端只要有一头是工业，整条就按大道画 —— 路是两边共用的，
                    // 一半土一半柏油在画面上只会像贴图错位。
                    SubMesh = (isTrunk[lo] || isTrunk[hi]) ? 1 : 0,
                });
            }
        }

        private void WriteMesh()
        {
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "HumenTradeRoutes" };
                _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                var mf = GetComponent<MeshFilter>();
                if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
                mf.sharedMesh = _mesh;
            }
            _mesh.Clear();

            InwardFaceCount = 0;

            float zoom = SizeZoom();
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>[SubMeshCount];
            for (int i = 0; i < SubMeshCount; i++) tris[i] = new List<int>();

            float h = Radius * HeightScale;

            foreach (var e in _edges)
            {
                float width = (e.SubMesh == 1 ? TrunkWidth : DirtWidth) * zoom;
                EmitRibbon(verts, norms, uvs, tris[e.SubMesh],
                           _norms[e.A], _norms[e.B], width, h);
            }

            _mesh.SetVertices(verts);
            _mesh.SetNormals(norms);
            _mesh.SetUVs(0, uvs);
            _mesh.subMeshCount = SubMeshCount;
            for (int i = 0; i < SubMeshCount; i++) _mesh.SetTriangles(tris[i], i);
            _mesh.RecalculateBounds();

            if (_mats == null || _mats.Length != SubMeshCount) _mats = TradeRouteMaterials.Resolve();
            _mr.sharedMaterials = _mats;

            if (InwardFaceCount > 0)
                Debug.LogWarning($"[Humen] ⚠ 道路图层有 {InwardFaceCount} 个面朝内 —— " +
                                 $"绕序反了，这些面会被背面剔除（画面上是路缺一段，且不报错）。");
        }

        /// <summary>
        /// 沿大圆把一条路铺成缎带（quad strip）。
        ///
        /// <b>绕序是这里唯一的真风险</b>，故当场自检：缎带的每个面片，
        /// 其几何法线必须与该处地表外法线同向。<see cref="InwardFaceCount"/> 记违规数。
        ///
        /// 推导（与 <see cref="TribeMarkerLayer.EmitQuad"/> 同一套约定，
        /// <c>east × north = up</c>）：设切向 <c>t</c>（沿路前进方向）、外法线 <c>u</c>、
        /// 侧向 <c>s = u × t</c>。四个角依次为
        /// <c>a = p − s·h</c>、<c>b = p + s·h</c>、<c>c = q + s·h</c>、<c>d = q − s·h</c>。
        /// 若按 (a,b,c,d) 写，则 <c>(b−a)×(c−a) = (2h·s)×(t·L + 2h·s) = 2hL·(s×t) = −2hL·u</c>
        /// —— <b>朝内</b>，整层会被背面剔除。按 <b>(a,d,c,b)</b> 写才有
        /// <c>(d−a)×(c−a) = L·t × (t·L + 2h·s) = 2hL·(t×s) = +2hL·u</c> ✓。
        /// （<c>t×s = t×(u×t) = u(t·t) − t(t·u) = u</c>，因 t ⊥ u。）
        /// </summary>
        private void EmitRibbon(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs,
                                List<int> tri, Vector3 na, Vector3 nb, float width, float height)
        {
            float theta = Mathf.Acos(Mathf.Clamp(Vector3.Dot(na, nb), -1f, 1f));
            int steps = Mathf.Clamp(
                Mathf.CeilToInt(theta * Mathf.Rad2Deg / Mathf.Max(0.05f, MaxSegmentDegrees)), 2, 512);

            float half = width * 0.5f;
            int baseIdx = verts.Count;

            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps;
                Vector3 p = PlanetGeometry.SlerpLocal(na, nb, t).normalized;
                Vector3 c = p * height;

                // 切向：取相邻采样点之差，投影到该点切平面（去掉径向分量）。
                Vector3 a = s == 0 ? na : PlanetGeometry.SlerpLocal(na, nb, (float)(s - 1) / steps).normalized;
                Vector3 b = s == steps ? nb : PlanetGeometry.SlerpLocal(na, nb, (float)(s + 1) / steps).normalized;
                Vector3 tangent = (b - a) - p * Vector3.Dot(b - a, p);
                if (tangent.sqrMagnitude < 1e-12f) tangent = Vector3.Cross(p, Vector3.up);
                tangent.Normalize();

                Vector3 side = Vector3.Cross(p, tangent).normalized;

                verts.Add(c - side * half);
                verts.Add(c + side * half);
                norms.Add(p); norms.Add(p);
                uvs.Add(new Vector2(0f, t)); uvs.Add(new Vector2(1f, t));
            }

            // (a,d,c,b) —— 见上面那段的推导。
            for (int s = 0; s < steps; s++)
            {
                int a = baseIdx + s * 2;
                int b = a + 1;
                int d = a + 2;
                int c = a + 3;

                CheckWinding(verts, a, d, c);
                CheckWinding(verts, a, c, b);

                tri.Add(a); tri.Add(d); tri.Add(c);
                tri.Add(a); tri.Add(c); tri.Add(b);
            }
        }

        /// <summary>绕序自检：面片几何法线与该处外法线同向才算对。</summary>
        private void CheckWinding(List<Vector3> verts, int i0, int i1, int i2)
        {
            Vector3 a = verts[i0], b = verts[i1], c = verts[i2];
            Vector3 geo = Vector3.Cross(b - a, c - a);
            if (geo.sqrMagnitude < 1e-12f) return;        // 退化面片没有法线，不判
            Vector3 mid = (a + b + c) / 3f;
            if (Vector3.Dot(geo.normalized, mid.normalized) < 0f) InwardFaceCount++;
        }

        /// <summary>
        /// 路宽的缩放系数：按相机到<b>地表</b>的距离反比缩放，使路在屏幕上的粗细
        /// 不随推拉而变。与 <see cref="TribeMarkerLayer.SizeZoom"/> 是同一个约定
        /// （那边记了完整理由：R=1000 下 1 单位 ≈ 6.4 km，所以这是"地图上的线"而不是"真实宽度"）。
        /// 两份各写一遍是因为都是实例方法、取的 Radius 也可能不同；改了其中一份，另一份要跟着改。
        /// </summary>
        private float SizeZoom()
        {
            var cam = Camera.main;
            if (cam == null) cam = FindFirstObjectByType<Camera>();
            if (cam == null) return 1f;

            float toCenter = Vector3.Distance(cam.transform.position, transform.position);
            float toSurface = Mathf.Max(toCenter - Radius, Radius * 0.01f);
            float reference = Mathf.Max(Radius * (ReferenceDistanceFactor - 1f), 1f);

            // 下限比标记层(0.015)高：路本来就细，再缩就断成虚线了。
            return Mathf.Clamp(toSurface / reference, 0.05f, 2f);
        }

        public void SetYear(double year)
        {
            Year = year;
            if (Ready) Build();
        }

        /// <summary>当前路网里"有路的部落"数，供 UI 读数。</summary>
        public int ConnectedTribeCount
        {
            get
            {
                if (!Ready) return 0;
                var set = new HashSet<int>();
                foreach (var e in _edges) { set.Add(e.A); set.Add(e.B); }
                return set.Count;
            }
        }
    }

    /// <summary>
    /// 道路材质。两个子网格两种颜色，都取<b>深色</b> ——
    /// 与 <see cref="MarkerMaterials.Colors"/> 那条教训（配色要对着地形调色板选、
    /// 不能对着语义选）同源，但结论相反：标记要"跳出来"，路要"沉下去"。
    /// 本工程地形只有植被绿／漠土黄／冰白／岩灰／海蓝五种，
    /// 一条深棕的路在这五种底子上都读得出来；而浅色的路会在冰白与漠土黄上消失。
    /// </summary>
    public static class TradeRouteMaterials
    {
        public const string Folder = "Assets/Settings/Materials";

        public static readonly string[] AssetNames =
        {
            "Route_Dirt.mat", "Route_Trunk.mat",
        };

        public static readonly Color[] Colors =
        {
            new Color(0.36f, 0.25f, 0.15f),   // 土路：车辙压出来的深褐
            new Color(0.16f, 0.16f, 0.20f),   // 大道：碎石／轨道的深青灰
        };

        public static Material[] Resolve()
        {
            var list = new Material[TradeRouteLayer.SubMeshCount];
            for (int i = 0; i < list.Length; i++)
            {
                string path = $"{Folder}/{AssetNames[i]}";
#if UNITY_EDITOR
                list[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
#else
                list[i] = null;
#endif
                if (list[i] == null) list[i] = MarkerMaterials.Create(Colors[i]);
            }
            return list;
        }
    }
}
