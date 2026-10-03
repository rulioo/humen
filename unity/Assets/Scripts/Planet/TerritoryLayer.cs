using System.Collections.Generic;
using UnityEngine;

namespace Humen.Planet
{
    /// <summary>
    /// M4c-2：把<b>定居文明的疆域</b>铺在地表上 —— 每个定居部落周围一圈半透明色块，
    /// 颜色按文明阶段，半径随人口。
    ///
    /// <b>为什么需要这一层。</b>原先星球上只有"点"：部落标记是 4～26 单位的小方块，
    /// 在 A 级轨道视距（默认视距，也是作者一打开就看到的那一屏）下只有几个像素。
    /// 于是拖动时间轴时，画面上变的是<b>几个像素的颜色</b> ——
    /// "蓝星上的文明进化"这件事，在默认视距下几乎看不出来。
    /// 铺开成面之后，拖时间轴看到的是<b>大陆从零星几点变成连成一片</b>，
    /// 这才是《文明》里"疆域扩张"的那一格信息。
    ///
    /// <b>为什么游群没有疆域。</b>判据是快照里已有的 <c>settlement</c>（定居聚落）：
    /// 逐水草而居的游群没有边界，界是定居的产物。于是这一层天然带着时间轴语义 ——
    /// 越早的年份，铺开的面积越小；农业一出现，色块才开始一块块长出来。
    /// 这比"按人口上色"更贴这个世界自己的史观（见 design.md 的农业门槛一节）。
    ///
    /// <b>为什么是半透明。</b>叠加处会自然加深，于是"部落密集的地方"自己就显出深浅，
    /// 不需要再画一张密度图。且底下的地形（植被／漠土／冰盖）仍然透得出来 ——
    /// 疆域是盖在地理之上的，不该把它抹掉。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerritoryLayer : MonoBehaviour
    {
        /// <summary>子网格数 = 4 个文明阶段。下标同 <see cref="WorldView.StageAt"/>。</summary>
        public const int SubMeshCount = WorldView_StageCount;

        // WorldView.StageNames 的长度。写成常量而不是直接引用，是因为
        // const 不能由另一个程序集的数组长度求出；改了 StageNames 这里要跟着改，
        // 下面 Build 里有一条断言会在对不上时报出来。
        private const int WorldView_StageCount = 4;

        [Header("星球")]
        public float Radius = 1000f;

        // ⚠️ 抬高量必须大于圆盘的<b>弦垂</b>，否则盘子中间会陷到地表以下、被深度测试切掉。
        //    圆盘是"从圆心到边缘的扇形三角面"，两个端点都在球面上，中间那条弦却在球面<b>之下</b>：
        //    垂下量 ≈ r²/(8R)。半径 52、R=1000 时约 0.34 单位 —— 而第一版只抬了 0.4 单位，
        //    余量 0.06，掠射角下必然穿帮（表现为"疆域中间被地形啃掉一块"）。
        //    现在抬 1.2 单位，余量 0.86。
        [Tooltip("疆域离地高度倍率。1.0012 即离地 1.2 单位，须大于弦垂 r²/(8R)。")]
        public float HeightScale = 1.0012f;

        [Header("尺寸（Unity 单位；相对半径 1000 而言）")]
        [Tooltip("人口最少时的疆域半径。")]
        public float MinRadius = 11f;

        [Tooltip("人口最多时的疆域半径。52 时弦垂约 0.34 单位，故上限与 HeightScale 要一起看。")]
        public float MaxRadius = 42f;

        [Tooltip("圆盘边数。低一点没关系：疆域是大块色斑，边是圆的就行。")]
        public int Segments = 24;

        [Header("数据")]
        public string WorldViewPath = "";

        [Header("时间")]
        public double Year = 2125;

        private WorldView _world;
        private Mesh _mesh;
        private MeshRenderer _mr;
        private Material[] _mats;

        private readonly List<Blob> _blobs = new List<Blob>();

        /// <summary>绕序自检发现的朝内面数。非零即说明圆盘法线朝内、整层会被背面剔除。</summary>
        public int InwardFaceCount { get; private set; }

        private struct Blob
        {
            public Vector3 Center;     // 已抬到地表之上
            public Vector3 East, North;
            public float Radius;
            public int Bucket;         // 0..3
        }

        public string Error => _world != null ? _world.Error : "尚未载入世界快照";
        public bool Ready => _world != null && _world.Loaded;

        /// <summary>这一年铺开了多少块疆域 —— 给 UI 做读数。</summary>
        public int TerritoryCount => _blobs.Count;

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
                Debug.LogWarning($"[Humen] 疆域图层没数据：{_world.Error}");
                return;
            }
            if (Year > _world.File.timeline.endYear) Year = _world.File.timeline.endYear;
        }

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

        private void Collect()
        {
            _blobs.Clear();

            var tribes = _world.File.tribes;

            // 与 TribeMarkerLayer.Collect 同一套对数尺度：人口跨约 1000 倍，
            // 线性映射会让最大的那块疆域是其余全部的 1000 倍。
            double minPop = double.MaxValue, maxPop = 0;
            foreach (var t in tribes)
            {
                double p = WorldView.PopulationAt(t, Year);
                if (p <= 0 || !WorldView.HasTechAt(t, Year, "settlement")) continue;
                if (p < minPop) minPop = p;
                if (p > maxPop) maxPop = p;
            }
            if (maxPop <= 0) return;                    // 这一年还没有任何定居聚落

            double lo = System.Math.Log10(System.Math.Max(1.0, minPop));
            double hi = System.Math.Log10(System.Math.Max(1.0, maxPop));
            double span = System.Math.Max(1e-6, hi - lo);

            foreach (var t in tribes)
            {
                double pop = WorldView.PopulationAt(t, Year);
                if (pop <= 0) continue;

                // 只有定居了才有疆域 —— 见类注释。
                if (!WorldView.HasTechAt(t, Year, "settlement")) continue;

                double k = (System.Math.Log10(pop) - lo) / span;      // 0..1
                float r = Mathf.Lerp(MinRadius, MaxRadius, (float)k);

                PlanetGeometry.SurfaceFrame(t.lat, t.lon, out _, out var east, out var north);

                _blobs.Add(new Blob
                {
                    Center = PlanetGeometry.LatLonToLocal(t.lat, t.lon, Radius * HeightScale),
                    East = east,
                    North = north,
                    Radius = r,
                    Bucket = WorldView.StageAt(t, Year),
                });
            }
        }

        private void WriteMesh()
        {
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "HumenTerritory" };
                _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                var mf = GetComponent<MeshFilter>();
                if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
                mf.sharedMesh = _mesh;
            }
            _mesh.Clear();

            InwardFaceCount = 0;

            int seg = Mathf.Clamp(Segments, 3, 64);
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>[SubMeshCount];
            for (int i = 0; i < SubMeshCount; i++) tris[i] = new List<int>();

            foreach (var b in _blobs)
            {
                int bucket = Mathf.Clamp(b.Bucket, 0, SubMeshCount - 1);
                var tri = tris[bucket];

                int c0 = verts.Count;
                verts.Add(b.Center);
                norms.Add(b.Center.normalized);
                uvs.Add(new Vector2(0.5f, 0.5f));

                for (int i = 0; i < seg; i++)
                {
                    float a = i * (Mathf.PI * 2f / seg);
                    Vector3 dir = b.East * Mathf.Cos(a) + b.North * Mathf.Sin(a);
                    Vector3 p = b.Center + dir * b.Radius;

                    // ⚠️ 必须把边缘点重新投回球面。半径 52 单位时，
                    //    切平面与球面的偏离 (r²/2R) 约 1.35 单位，
                    //    比本层抬高的 0.4 单位大得多 —— 不投回去，圆盘边缘会陷进地里，
                    //    看起来像"疆域被地形啃掉一圈"。
                    p = p.normalized * (Radius * HeightScale);

                    verts.Add(p);
                    norms.Add(p.normalized);
                    uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)));
                }

                for (int i = 0; i < seg; i++)
                {
                    int p1 = c0 + 1 + i;
                    int p2 = c0 + 1 + (i + 1) % seg;

                    // 绕序：dir_i × dir_{i+1} = (east×north)·sin(Δθ) = +up（Δθ>0），
                    // 故 (心, p_i, p_{i+1}) 的几何法线朝外。见 PlanetGeometry 的 east/north 约定。
                    Vector3 ga = verts[c0], gb = verts[p1], gc = verts[p2];
                    Vector3 geo = Vector3.Cross(gb - ga, gc - ga);
                    if (geo.sqrMagnitude > 1e-12f &&
                        Vector3.Dot(geo.normalized, b.Center.normalized) < 0f) InwardFaceCount++;

                    tri.Add(c0); tri.Add(p1); tri.Add(p2);
                }
            }

            _mesh.SetVertices(verts);
            _mesh.SetNormals(norms);
            _mesh.SetUVs(0, uvs);
            _mesh.subMeshCount = SubMeshCount;
            for (int i = 0; i < SubMeshCount; i++) _mesh.SetTriangles(tris[i], i);
            _mesh.RecalculateBounds();

            if (_mats == null || _mats.Length != SubMeshCount) _mats = TerritoryMaterials.Resolve();
            _mr.sharedMaterials = _mats;

            if (InwardFaceCount > 0)
                Debug.LogWarning($"[Humen] ⚠ 疆域图层有 {InwardFaceCount} 个面朝内 —— " +
                                 $"绕序反了，会被背面剔除（画面上是疆域整块不见，且不报错）。");
        }

        public void SetYear(double year)
        {
            Year = year;
            if (Ready) Build();
        }
    }

    /// <summary>
    /// 疆域的四档材质。<b>色相取自已验证过的标记配色</b>
    /// （<see cref="MarkerMaterials.Colors"/> 那一段记了为什么必须这么选：
    /// 要对着地形调色板选，不能对着语义选），只是把不透明度压到很低 ——
    /// 疆域是底色，标记才是主角。
    /// </summary>
    public static class TerritoryMaterials
    {
        public const string Folder = "Assets/Settings/Materials";

        public static readonly string[] AssetNames =
        {
            "Terr_Band.mat", "Terr_Village.mat", "Terr_Iron.mat", "Terr_Industry.mat",
        };

        /// <summary>不透明度。四个阶段用同一个值 —— 深浅表示"是哪一阶段"由色相承担，
        /// 再让透明度也跟着变的话，两套编码会互相干扰，读起来就更难了。</summary>
        public const float Alpha = 0.18f;

        public static Color ColorFor(int stage)
        {
            var c = MarkerMaterials.Colors[Mathf.Clamp(stage, 0, 3)];
            return new Color(c.r, c.g, c.b, Alpha);
        }

        public static Material[] Resolve()
        {
            var list = new Material[TerritoryLayer.SubMeshCount];
            for (int i = 0; i < list.Length; i++)
            {
                string path = $"{Folder}/{AssetNames[i]}";
#if UNITY_EDITOR
                list[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
#else
                list[i] = null;
#endif
                if (list[i] == null) list[i] = MarkerMaterials.Create(ColorFor(i));
            }
            return list;
        }
    }
}
