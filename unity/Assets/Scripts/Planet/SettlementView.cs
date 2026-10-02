using UnityEngine;

namespace Humen.Planet
{
    /// <summary>
    /// M4b-3 的第三层：C 级「聚落视景」。作者原话是
    /// 「<i>放大看到每一片大陆，看到……人造建筑，修的路，农田之类的，还是造出来的设施，
    /// 符合进化阶段特征</i>」—— 这一层就是那句话里"人造建筑"的落点。
    ///
    /// 与行星视图的关系（§10.1 的三级）：A 级看大陆轮廓、B 级贴着地表看聚落在哪，
    /// C 级<b>不是继续放大</b>，而是"降落" —— 两者差 6371 倍，连续缩放不划算。
    /// 故这里是<b>另一个场景</b>，本地坐标以米为单位、原点在聚落中心，与星球那个 R=1000 单位的世界无关。
    ///
    /// ⚠️ 一律<b>按年份从技术派生</b>（<see cref="SettlementPlan.Plan"/> 内部走
    /// <see cref="WorldView.HasTechAt"/> / <see cref="WorldView.StageAt"/>），
    /// 绝不读快照里的 <c>settlement</c> / <c>farmland</c> 终值字段 ——
    /// 读了它们，整条时间轴会画成同一个样子。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettlementView : MonoBehaviour
    {
        [Header("看哪个部落的哪一年")]
        [Tooltip("部落 id，形如 3.4.2。留空则用 DefaultTribeId。")]
        public string TribeId = DefaultTribeId;

        [Tooltip("回落用的部落：赤道大陆 3.4.2，23.6 万人，93 项技术。必须与 LandingState.DefaultTribeId 一致。")]
        public const string DefaultTribeId = "3.4.2";

        public double Year = 2125;

        [Header("世界快照")]
        public string WorldViewPath = "";

        [Header("地形")]
        [Tooltip("在聚落底下铺一块地面。半径按聚落尺寸自适应。")]
        public bool ShowGround = true;

        // ⚠️ 这个颜色是**对着铺在它上面的东西**选的，不是对着好看选的。
        //    路与广场用的是 SettlementMaterials.Ground（0.40,0.34,0.27），
        //    第一版地面取 (0.34,0.30,0.22) —— 两者亮度只差一成半，于是
        //    **路几乎完全消失在底色里**，画面上只剩几道贴在地皮上的划痕。
        //    这与 M4b-2 标记配色栽的是同一个坑（见 design.md v0.22 ⑦）：
        //    前景色要对着**它背后的那一层**选，不能孤立地选。
        [Tooltip("地面材质颜色。取明显更深的地土色，好让铺在上面的路与广场跳出来。")]
        public Color GroundColor = new Color(0.30f, 0.26f, 0.18f);

        private WorldView _world;
        private Mesh _mesh;
        private MeshRenderer _mr;
        private GameObject _ground;

        /// <summary>上一次构建的诊断信息，供日志与 Inspector 读数。</summary>
        public string LastReport { get; private set; } = "尚未构建";

        /// <summary>
        /// 上一次构建时该部落的人口。<b>为 0 表示那一年它还没立族</b> ——
        /// 调用方靠这个把"年份选错了"与"渲染坏了"分开（两者都表现为差分 0 px）。
        /// </summary>
        public double LastPopulation { get; private set; }

        /// <summary>
        /// 上一次布局的<b>建成区半径</b>（米，含田圈）。相机取景要用这个，
        /// <b>不要用网格包围盒</b> —— 包围盒会被四条 3.2R 长的放射路撑到两倍大，
        /// 拿它取景会把一座城拍成一小撮（这个坑踩过一次，见 design.md v0.23）。
        /// </summary>
        public float LastRadius { get; private set; }

        public bool Ready => _world != null && _world.Loaded;

        public string Error => _world != null ? _world.Error : "尚未载入世界快照";

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
                Debug.LogWarning($"[Humen] 聚落视图没数据：{_world.Error}");
                return;
            }

            // 种子与模拟同源 —— 布局里的随机量全部由它导出（INV-34）。
            long seed;
            if (long.TryParse(_world.File.seed, out seed)) SettlementPlan.Seed = seed;
        }

        public void Build()
        {
            if (_mr == null)
            {
                _mr = GetComponent<MeshRenderer>();
                if (_mr == null) _mr = gameObject.AddComponent<MeshRenderer>();
            }

            if (!Ready) { _mr.enabled = false; LastReport = Error; LastPopulation = 0; return; }

            if (!_world.ById.TryGetValue(TribeId, out var tribe) || tribe == null)
            {
                _mr.enabled = false;
                LastReport = $"世界里没有部落 {TribeId}";
                LastPopulation = 0;
                Debug.LogWarning($"[Humen] {LastReport}");
                return;
            }

            var layout = SettlementPlan.Plan(tribe, Year);
            LastPopulation = layout.Population;
            LastRadius = layout.Radius;

            var geo = new SettlementGeometry();
            geo.Emit(layout);

            // ⚠️ 绕序自检。本工程在 TribeMarkerLayer 上栽过一次（整层被背面剔除、
            //    一个像素不画、还不报错），所以这里不是"仔细写"而是**每次都验**。
            if (geo.InwardFaceCount > 0)
                Debug.LogError($"[Humen] 聚落几何有 {geo.InwardFaceCount} 个面朝内（绕序反了）—— " +
                               $"这些面会被背面剔除掉，画面上是缺块的。");

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "HumenSettlement" };
                _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                var mf = GetComponent<MeshFilter>();
                if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
                mf.sharedMesh = _mesh;
            }

            // 复用同一个 Mesh 对象逐帧改，而不是每次新建 ——
            // 时间轴一拖就是每帧重建，新建 Mesh 会把内存吃光。
            geo.CopyTo(out var verts, out var normals, out var uvs, out var tris);

            _mesh.Clear();
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _mesh.SetVertices(verts);
            _mesh.SetNormals(normals);
            _mesh.SetUVs(0, uvs);
            _mesh.subMeshCount = SettlementPlan.MatCount;
            for (int i = 0; i < SettlementPlan.MatCount; i++) _mesh.SetTriangles(tris[i], i);
            _mesh.RecalculateBounds();

            _mr.enabled = geo.VertexCount > 0;
            var mats = SettlementMaterials.Resolve();
            if (mats.Length == SettlementPlan.MatCount) _mr.sharedMaterials = mats;

            BuildGround(layout);
            LastReport = Describe(layout, geo);
        }

        /// <summary>铺一块地面。半径跟着聚落走，免得近处看是一块悬空的板。</summary>
        private void BuildGround(SettlementPlan.Layout layout)
        {
            if (_ground == null)
            {
                _ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                _ground.name = "SettlementGround";
                _ground.transform.SetParent(transform, false);
            }
            _ground.SetActive(ShowGround);
            if (!ShowGround) return;

            // ⚠️ 地面要大到**它的边落在雾的尽头之外**（fogEnd 4200 m，见
            //    SettlementSceneBuilder）。第一版取 2.4 倍聚落直径，边缘明晃晃地
            //    戳在画面里，整个聚落像放在一张桌子上 —— 那正是作者嫌弃的"一张图片"。
            //    Plane 是 10×10 单位，故 scale = 直径/10；6000 m 的对角线 4243 m > 4200 m，
            //    留出余量取 9000 m，边就在雾里彻底化掉了。
            float diam = Mathf.Max(layout.Radius * 8f, 9000f);
            _ground.transform.localPosition = Vector3.zero;
            _ground.transform.localScale = new Vector3(diam / 10f, 1f, diam / 10f);

            var rend = _ground.GetComponent<MeshRenderer>();
            if (rend != null)
            {
                var mat = rend.sharedMaterial;
                if (mat == null || mat.name != "Set_GroundPlane")
                {
                    mat = SettlementMaterials.Create(GroundColor);
                    mat.name = "Set_GroundPlane";   // 运行期临时材质，不落盘
                    rend.sharedMaterial = mat;
                }
                else mat.color = GroundColor;
            }
        }

        private string Describe(SettlementPlan.Layout L, SettlementGeometry geo)
        {
            var counts = new System.Collections.Generic.Dictionary<SettlementPlan.Kind, int>();
            foreach (var b in L.Buildings)
            {
                counts.TryGetValue(b.Kind, out int n);
                counts[b.Kind] = n + 1;
            }
            var sb = new System.Text.StringBuilder();
            sb.Append($"{TribeId} · {WorldTimeline.FormatYear(Year)} · ");
            sb.Append($"阶段 {WorldView.StageNames[L.Stage]} · 人口 {L.Population:N0} · ");
            sb.Append($"构件 {L.Buildings.Count} 个 · 顶点 {geo.VertexCount:N0} · 半径 {L.Radius:F0} m");
            if (counts.Count > 0)
            {
                sb.Append(" —— ");
                bool first = true;
                foreach (var kv in counts)
                {
                    if (!first) sb.Append(" · ");
                    sb.Append($"{kv.Key} {kv.Value}");
                    first = false;
                }
            }
            return sb.ToString();
        }

        public void SetYear(double year)
        {
            Year = year;
            if (Ready) Build();
        }

        public void SetTribe(string tribeId)
        {
            TribeId = tribeId;
            if (Ready) Build();
        }
    }
}
