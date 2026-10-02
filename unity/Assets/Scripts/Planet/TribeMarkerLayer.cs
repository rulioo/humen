using System.Collections.Generic;
using UnityEngine;

namespace Humen.Planet
{
    /// <summary>
    /// M4b-2：把部落画在星球上 —— 作者原话是"还有人口，千家万户"。
    ///
    /// <b>一个网格，五个子网格</b>，不是每个部落一个 GameObject：
    /// <list type="bullet">
    ///   <item>子网格 0 —— 游群（无定居聚落）</item>
    ///   <item>子网格 1 —— 村落（有 <c>settlement</c>）</item>
    ///   <item>子网格 2 —— 铁器（再会炼 <c>iron</c>）</item>
    ///   <item>子网格 3 —— 工业（再有 <c>steam_engine</c>）</item>
    ///   <item>子网格 4 —— 农田，更大一圈的半透明绿，垫在聚落底下</item>
    /// </list>
    /// 202 个部落若各自一个 GameObject，就是 202 个 transform 参与每帧的剔除与合批；
    /// 合成一个网格后是一次提交（每子网格一次 draw call）。更实际的好处是
    /// <b>时间轴拖动时只需重建顶点数组</b>，不必增删对象 —— 每帧增删 202 个对象
    /// 在编辑器里会明显卡顿。
    ///
    /// ⚠️ 分档<b>按文明阶段而不是材料等级</b>，这一点是更正过的：<c>materialTier</c>
    /// 量的是地质机会（可采储量 &gt; 0），在这个世界上恰好与发达程度<b>负相关</b> ——
    /// 详见 <see cref="WorldView.StageAt"/> 的说明。原先按 tier 上色，画面上是反的。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TribeMarkerLayer : MonoBehaviour
    {
        /// <summary>子网格总数：4 个文明阶段 + 1 个农田。改这里要同步改 <see cref="MarkerMaterials"/>。</summary>
        public const int SubMeshCount = 5;

        /// <summary>农田所在的子网格下标 —— 它不是一个阶段，故排在最后。</summary>
        public const int FieldSubMesh = 4;

        [Header("星球")]
        [Tooltip("星球半径。标记长在半径 × HeightScale 处，避免与地表 z-fighting。")]
        public float Radius = 1000f;

        [Tooltip("标记离地高度倍率。1.002 即离地 0.2% 半径（1000 单位下约 2 单位）。")]
        public float HeightScale = 1.002f;

        [Tooltip("农田离地高度倍率。比标记低 —— 田在地表，房子在田上。")]
        public float FieldHeightScale = 1.0008f;

        [Header("尺寸")]
        [Tooltip("人口最少时的标记边长（Unity 单位）。")]
        public float MinSize = 4f;

        [Tooltip("人口最多时的标记边长。")]
        public float MaxSize = 26f;

        [Tooltip("农田边长相对标记的倍数。")]
        public float FieldScale = 2.6f;

        [Tooltip("标记尺寸的基准相机距离（倍率 × 半径）。默认 3.0 ＝ §10.1 的 A 级轨道默认视距。")]
        public float ReferenceDistanceFactor = 3.0f;

        [Header("数据")]
        [Tooltip("世界快照路径。留空则用 StreamingAssets/World/world_view.json。")]
        public string WorldViewPath = "";

        [Header("时间")]
        [Tooltip("当前显示的年份。§WorldConfig：公元前 300,000 → 公元 2,126。")]
        public double Year = WorldConfig_EndYear;

        // 与 Core 的 WorldConfig 保持一致。刻意写常量而不是去引用 Humen.Core ——
        // 引用不了（见 PlanetBootstrap 的依赖方向说明）。这里只用来给 Inspector 一个默认值，
        // 真正的区间从快照的 timeline 里读，故即使对不上也不会画错。
        private const double WorldConfig_EndYear = 2125;

        private WorldView _world;
        private Mesh _mesh;
        private MeshRenderer _mr;

        // 每个部落当前的画法，重建网格时复用，避免每次重建都重新查一遍快照。
        private readonly List<Marker> _markers = new List<Marker>();

        private struct Marker
        {
            public Vector3 Position;
            public Vector3 East;
            public Vector3 North;
            public float Size;
            public float FieldRadius;
            public int Bucket;        // 0..3，见 WorldView.StageAt
            public bool HasField;
            public string TribeId;    // 供屏幕拾取用（点一个标记就"降落"到那个部落）
        }

        /// <summary>载入失败的原因（没有快照、字段对不上等），供 UI 显示。</summary>
        public string Error => _world != null ? _world.Error : "尚未载入世界快照";

        public bool Ready => _world != null && _world.Loaded;

        /// <summary>世界快照。供场景切换查人口／技术用（<see cref="SceneSwitcher"/>）。</summary>
        public WorldView World => _world;

        /// <summary>
        /// 屏幕拾取：找离 <paramref name="screenPos"/> 最近、且在标记自身大小之内的那个部落。
        ///
        /// 用<b>屏幕距离</b>而不是射线求交，有两个理由：
        /// 一是"看起来点到了没有"本来就该按屏幕算（标记是朝相机的四边形，
        /// 远处的小标记在屏幕上就那么几像素，用世界坐标的射线反而不好判定）；
        /// 二是不必去碰网格拓扑 —— 三角形属于哪个部落还得另建一张表，
        /// 而这张表在每次重建网格时都要跟着变。
        ///
        /// ⚠️ 容差取<b>标记自身的屏幕尺寸</b>与 <paramref name="minPixels"/> 的较大者：
        /// 恒定的像素容差会让远处的小部落几乎点不中，而近处的大标记旁边又会误点。
        /// </summary>
        public bool TryPick(Camera cam, Vector2 screenPos, float minPixels, out string tribeId)
        {
            tribeId = null;
            if (cam == null || _markers.Count == 0) return false;

            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float denom = 2f * Mathf.Tan(vHalf);
            float best = float.MaxValue;

            foreach (var m in _markers)
            {
                // ⚠️ m.Position 是**星球本地坐标**（PlanetGeometry.LatLonToLocal），
                //    网格建在 Planet 之下、跟着球一起转，所以**画出来是对的**；
                //    而拾取若拿它直接当世界坐标，就等于在"球没转过"的那个位置上找标记。
                //
                //    实测症状：点上去一点反应都没有，而且**球转得越多偏得越远**
                //    ——球 6°/s，赤道上的标记在屏幕上约 26 px/s 地漂，
                //    于是这个 bug 看起来像"容差太小/手点不准"，
                //    连点五次、每次都用刚截的图重新定位，仍然一次都没中。
                //    病因不在容差，在于少换算了一次坐标系。
                //    教训：**本地坐标画得对，不代表本地坐标也能拿来拾取。**
                Vector3 world = transform.TransformPoint(m.Position);
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z <= 0f) continue;                      // 在相机背后，投影是镜像的

                float dist = Vector3.Distance(cam.transform.position, world);
                float px = denom > 1e-6f ? m.Size * (Screen.height / (dist * denom)) : 0f;
                float r = Mathf.Max(minPixels, px * 0.5f);

                float dx = sp.x - screenPos.x, dy = sp.y - screenPos.y;
                float d2 = dx * dx + dy * dy;
                if (d2 <= r * r && d2 < best) { best = d2; tribeId = m.TribeId; }
            }
            return !string.IsNullOrEmpty(tribeId);
        }

        private void Awake()
        {
            Load();
            Build();
        }

        /// <summary>载入世界快照。Editor 侧建场景时也走这里，保证两条路一致。</summary>
        public void Load()
        {
            _world = string.IsNullOrEmpty(WorldViewPath)
                ? WorldView.LoadFromDefault()
                : WorldView.LoadFrom(WorldViewPath);

            if (!_world.Loaded)
            {
                Debug.LogWarning($"[Humen] 部落图层没数据：{_world.Error}");
                return;
            }

            if (Year <= 0 || Year > _world.File.timeline.endYear)
                Year = _world.File.timeline.endYear;
        }

        /// <summary>按当前 <see cref="Year"/> 重建标记。时间轴一动就调它。</summary>
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

            Collect();
            WriteMesh();
        }

        /// <summary>求出每个部落此刻的位置与大小。</summary>
        private void Collect()
        {
            _markers.Clear();

            // 人口的对数尺度：实测人口跨 307 ~ 296,709（约 1000 倍）。
            // 若按线性定大小，最大的那个会是其余全部的 1000 倍 —— 画面上只剩一个点，
            // 而"千家万户"恰好是那些小部落。取对数后 307 与 296,709 的差异约 3 倍，
            // 既分得出大小，又都看得见。
            double minPop = double.MaxValue, maxPop = 0;
            foreach (var t in _world.File.tribes)
            {
                double p = WorldView.PopulationAt(t, Year);
                if (p <= 0) continue;
                if (p < minPop) minPop = p;
                if (p > maxPop) maxPop = p;
            }
            if (maxPop <= 0) { minPop = 1; maxPop = 1; }

            double lo = System.Math.Log10(System.Math.Max(1.0, minPop));
            double hi = System.Math.Log10(System.Math.Max(1.0, maxPop));
            double span = System.Math.Max(1e-6, hi - lo);

            // 尺寸还要随相机推拉反比缩放 —— 见 SizeZoom 的说明。
            // 不缩的话，推到 B 级时 26 单位的标记会占掉屏幕高度的两成。
            float zoom = SizeZoom();

            foreach (var t in _world.File.tribes)
            {
                double pop = WorldView.PopulationAt(t, Year);
                if (pop <= 0) continue;                            // 这一年还没建立（或已消亡）

                double k = (System.Math.Log10(pop) - lo) / span;   // 0..1
                float size = Mathf.Lerp(MinSize, MaxSize, (float)k) * zoom;

                PlanetGeometry.SurfaceFrame(t.lat, t.lon, out _, out var east, out var north);

                // 阶段按<b>实际掌握的技术</b>逐年判（见 WorldView.StageAt 为何不能用 materialTier）。
                int stage = WorldView.StageAt(t, Year);

                _markers.Add(new Marker
                {
                    Position = PlanetGeometry.LatLonToLocal(t.lat, t.lon, Radius * HeightScale),
                    East = east,
                    North = north,
                    Size = size,
                    FieldRadius = size * FieldScale,
                    Bucket = stage,
                    // 田要在"会种地"<b>那一年之后</b>才有 —— 按 `planting` 的习得年份判，
                    // 不用快照里那个终值 `farmland`（它是最后一帧的结论，会整条时间轴都画上田）。
                    HasField = WorldView.HasTechAt(t, Year, "planting"),
                    TribeId = t.id,
                });
            }
        }

        /// <summary>
        /// 标记尺寸的缩放系数：按<b>相机到地表的距离</b>反比缩放，
        /// 使标记在屏幕上的视觉大小不随推拉而变。
        ///
        /// <b>为什么必须是屏幕尺度而不是世界尺度</b>：R=1000 单位下，1 单位 ＝ 6371/1000 ≈ 6.4 km，
        /// 所以 <see cref="MaxSize"/> 的 26 单位其实宽 165 km —— 这不是房子，是<b>地图图钉</b>。
        /// 图钉就该在任何缩放级别下保持相近的视觉大小；固定世界尺寸的话，
        /// 从 A 级（3R）推到 B 级（1.15R）时它在屏幕上的张角会放大二十倍，
        /// 实测整屏都是互相叠压的绿色方块，"千家万户"变成一堵墙。
        ///
        /// <b>为什么量到地表而不是量到球心</b>：推近时"相机到球心"趋于 R 而非 0，
        /// 用它做分母会在贴近地表时缩不下去。地表距离才正确趋于 0。
        /// </summary>
        private float SizeZoom()
        {
            // 场景里那颗相机打了 MainCamera 标签（见 PlanetSceneBuilder）。
            // 取不到就当作基准距离，宁可尺寸不准也不要报错中断整个图层。
            var cam = Camera.main;
            if (cam == null) cam = FindFirstObjectByType<Camera>();
            if (cam == null) return 1f;

            float toCenter = Vector3.Distance(cam.transform.position, transform.position);
            float toSurface = Mathf.Max(toCenter - Radius, Radius * 0.01f);
            float reference = Mathf.Max(Radius * (ReferenceDistanceFactor - 1f), 1f);

            // 下限 0.015 是为了贴到 1.005R 时不至于缩成一个点；
            // 上限 2 是为了拉到 4R 之外时标记不会涨得比大陆还大。
            return Mathf.Clamp(toSurface / reference, 0.015f, 2f);
        }

        private void WriteMesh()
        {
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "HumenTribeMarkers" };
                _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                var mf = GetComponent<MeshFilter>();
                if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
                mf.sharedMesh = _mesh;
            }
            _mesh.Clear();

            int quads = _markers.Count;
            int fieldQuads = 0;
            foreach (var m in _markers) if (m.HasField) fieldQuads++;

            // 每个方块 4 顶点、6 索引
            int verts = (quads + fieldQuads) * 4;
            var vertices = new Vector3[verts];
            var normals = new Vector3[verts];
            var uvs = new Vector2[verts];

            var tris = new List<int>[SubMeshCount];
            for (int i = 0; i < SubMeshCount; i++) tris[i] = new List<int>();

            int v = 0;

            void EmitQuad(Vector3 center, Vector3 east, Vector3 north, float halfSize, int submesh)
            {
                Vector3 a = center - east * halfSize - north * halfSize;
                Vector3 b = center + east * halfSize - north * halfSize;
                Vector3 c = center + east * halfSize + north * halfSize;
                Vector3 d = center - east * halfSize + north * halfSize;

                vertices[v] = a; vertices[v + 1] = b; vertices[v + 2] = c; vertices[v + 3] = d;
                for (int i = 0; i < 4; i++) normals[v + i] = center.normalized;

                uvs[v] = new Vector2(0, 0); uvs[v + 1] = new Vector2(1, 0);
                uvs[v + 2] = new Vector2(1, 1); uvs[v + 3] = new Vector2(0, 1);

                // ⚠️ 绕序必须与 east/north 的叉积方向一致，否则整层被<b>背面剔除</b>掉，
                //    画面上什么都没有 —— 而且一句错都不报。
                //    PlanetGeometry.SurfaceFrame 里 east = cross(worldUp, up)、north = cross(up, east)，
                //    故 east × north = up（朝外）。取 +Z 处一验：up=(0,0,1)、east=(1,0,0)、north=(0,1,0)。
                //    a,b,c,d 依次是左下/右下/右上/左上，于是 (a,b,c) 的几何法线
                //    (b−a)×(c−a) = (e×n)·4h² = +up —— 朝外，才看得见。
                //    原先写的是 (a,c,b)/(a,d,c)，法线是 n×e = −up，整层隐形。
                //    这个坑值得记着：地形与河流都没事（一个不是背剔除的面片、一个是 LineRenderer），
                //    所以"地形画得出来"完全不能推出"标记也画得出来"。
                tris[submesh].Add(v); tris[submesh].Add(v + 1); tris[submesh].Add(v + 2);
                tris[submesh].Add(v); tris[submesh].Add(v + 2); tris[submesh].Add(v + 3);
                v += 4;
            }

            // 先画农田，后画聚落 —— 两者用不同材质，
            // 顺序不影响深度，但按"田在下、房在上"的思路写更好读。
            foreach (var m in _markers)
            {
                if (!m.HasField) continue;
                // 田与房的经纬度相同，只是贴地更低 —— 沿法线把标记的位置收回来即可，
                // 不必再查一遍经纬度。
                Vector3 p = m.Position.normalized * (Radius * FieldHeightScale);
                EmitQuad(p, m.East, m.North, m.FieldRadius * 0.5f, FieldSubMesh);
            }
            foreach (var m in _markers)
                EmitQuad(m.Position, m.East, m.North, m.Size * 0.5f, m.Bucket);

            _mesh.vertices = vertices;
            _mesh.normals = normals;
            _mesh.uv = uvs;

            _mesh.subMeshCount = SubMeshCount;
            for (int i = 0; i < SubMeshCount; i++) _mesh.SetTriangles(tris[i], i);
            _mesh.RecalculateBounds();

            var mats = MarkerMaterials.Resolve();
            if (mats != null && mats.Length == SubMeshCount) _mr.sharedMaterials = mats;
            else _mr.sharedMaterial = MarkerMaterials.FallbackUnlit();
        }

        /// <summary>供 Editor 与运行时改时间轴用。</summary>
        public void SetYear(double year)
        {
            Year = year;
            if (Ready) Build();
        }

        public double StartYear => Ready ? _world.File.timeline.startYear : -300000;
        public double EndYear => Ready ? _world.File.timeline.endYear : 2125;

        /// <summary>当前这一年在画上出现了多少个部落 —— 给 UI 做"世界里有多少人"的读数。</summary>
        public int VisibleTribeCount => _markers.Count;

        /// <summary>
        /// 当前这一年各文明阶段各有多少部落，下标同 <see cref="WorldView.StageAt"/>。
        /// 做成读数而不是只上色，是因为阶段分布是这个世界最该被看见的一件事
        /// （实测终值 123 / 17 / 52 / 10，而 123 个游群只占人口的 2.1%）。
        /// </summary>
        public int[] StageCounts()
        {
            var n = new int[WorldView.StageNames.Length];
            foreach (var m in _markers)
                if (m.Bucket >= 0 && m.Bucket < n.Length) n[m.Bucket]++;
            return n;
        }

        /// <summary>快照里的时间轴信息，供 UI 显示"农业出现在公元前 X 年"这类锚点。</summary>
        public WvTimeline Timeline => Ready ? _world.File.timeline : null;
    }

    /// <summary>
    /// 四个子网格的材质。做成静态类而不是让 <see cref="TribeMarkerLayer"/> 自己 new，
    /// 是因为 Editor 侧建场景时要把它们<b>存成资产</b>（<c>AssetDatabase.CreateAsset</c>）——
    /// 否则每个引用了它的对象都会各存一份副本，场景文件会莫名其妙地胀大。
    /// </summary>
    public static class MarkerMaterials
    {
        public const string Folder = "Assets/Settings/Materials";

        public static readonly string[] AssetNames =
        {
            "Tribe_Band.mat", "Tribe_Village.mat", "Tribe_Iron.mat",
            "Tribe_Industry.mat", "Tribe_Field.mat",
        };

        /// <summary>
        /// 五档配色，按<b>文明阶段</b>排（不是材料等级，见 <see cref="WorldView.StageAt"/>）。
        ///
        /// ⚠️ <b>配色是被实测逼出来的，不是挑好看的。</b>第一版按「篝火黄／作物绿／锻铁青／电弧紫」排，
        /// 看着合理，渲出来才发现两档在真实地形上是隐形的：
        /// <list type="bullet">
        ///   <item><b>村落绿</b>（0.45,0.85,0.42）压在它自己的大陆上 —— 大陆 3 整块是植被绿，
        ///   774 px 的村落标记肉眼一个都找不出来。<b>这不是巧合而是必然而已</b>：
        ///   聚落只长在能种地的地方，而能种地的地方就是绿的，<b>绿标记永远落在绿地上</b>。</item>
        ///   <item><b>游群橙黄</b>（0.95,0.72,0.35）压在裸岩与漠土上 —— 大陆 1/2 的地表是土黄与灰，
        ///   橙黄标记糊在里面，只在灰岩上勉强可见。</item>
        /// </list>
        /// 教训：<b>标记配色要对着「地形调色板」选，不能对着「语义」选</b>。
        /// 本工程的地形只有五种色（植被绿／漠土黄／冰白／岩灰／海蓝），
        /// 所以标记一律取<b>饱和且非地球的色相</b>——橙红／明黄／亮青／品红，
        /// 每一个都与那五种色在至少一个通道上差 60 以上。
        ///
        /// ⚠️ 两两之间也必须分得开：像素计数法（<c>PreviewRenderer.CountMarkerColors</c>，容差 12）
        /// 是按下标顺序首次匹配即跳出，撞色会互相误判。现四档两两在某一通道上都差 60 以上。
        /// </summary>
        public static readonly Color[] Colors =
        {
            new Color(0.98f, 0.42f, 0.18f),           // 游群：篝火的橙红（在漠土黄、植被绿上都跳得出来）
            new Color(0.99f, 0.90f, 0.25f),           // 村落：麦田的明黄（绿地上唯一的暖色）
            new Color(0.25f, 0.85f, 0.95f),           // 铁器：锻铁的亮青（R 通道与冰白拉开 150，避开 ⑨ 的假阳性）
            new Color(0.88f, 0.25f, 0.80f),           // 工业：电弧的品红
            new Color(0.35f, 0.75f, 0.35f, 0.28f),    // 农田：半透明绿（贴地色块，故意低调）
        };

        public static Material[] Resolve()
        {
            var list = new Material[TribeMarkerLayer.SubMeshCount];
            for (int i = 0; i < list.Length; i++)
            {
                string path = $"{Folder}/{AssetNames[i]}";
#if UNITY_EDITOR
                list[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
#else
                list[i] = null;
#endif
                if (list[i] == null) list[i] = Create(Colors[i]);
            }
            return list;
        }

        /// <summary>建一个能显色的材质。URP 与内置管线的 shader 名不同，故运行期探测。</summary>
        public static Material Create(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            var mat = new Material(shader);
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);

            if (color.a < 1f)
            {
                // 半透明：农田要能透出底下的地形色
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            return mat;
        }

        public static Material FallbackUnlit()
        {
            var shader = Shader.Find("Unlit/Texture");
            return new Material(shader);
        }
    }
}
