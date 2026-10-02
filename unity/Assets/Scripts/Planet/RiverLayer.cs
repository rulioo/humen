using System.Collections.Generic;
using UnityEngine;

namespace Humen.Planet
{
    /// <summary>
    /// M4b-2：把 5 条干流与 25 条支流画到球面上。
    ///
    /// <b>为什么用 LineRenderer 而不是塞进一个网格</b>：河是"有宽度的线"，
    /// 而宽度是<b>世界单位</b>（贴地看要粗、拉远要细，这是相机的透视管的，不是几何管的）。
    /// LineRenderer 天生就是干这个的，且 30 条线的开销可以忽略。
    /// 反过来说，部落标记该合并成一个网格是因为它有 202 个、且是面片不是线 —— 两者的取舍不同。
    ///
    /// ⚠️ 折线必须<b>沿大圆加密</b>：快照里相邻控制点最远约 2°，直接连直线的话弦会陷进球体，
    /// 拉近看就是"河钻进了地下"。见 <see cref="PlanetGeometry.SlerpLocal"/>。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RiverLayer : MonoBehaviour
    {
        [Header("星球")]
        public float Radius = 1000f;

        [Tooltip("河面离地高度倍率。比部落标记低 —— 河在地表。")]
        public float HeightScale = 1.0006f;

        [Header("加密")]
        [Tooltip("相邻控制点之间最多插到多少度一段。越小越贴球面，顶点也越多。")]
        public float MaxSegmentDegrees = 0.5f;

        [Header("宽度（Unity 单位；相对半径 1000 而言）")]
        [Tooltip("干流宽度。")] public float RiverWidth = 3.2f;
        [Tooltip("支流宽度。")] public float TributaryWidth = 1.6f;

        [Header("数据")]
        public string WorldViewPath = "";

        private WorldView _world;
        private Material _material;

        public string Error => _world != null ? _world.Error : "尚未载入世界快照";
        public bool Ready => _world != null && _world.Loaded;

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

            if (!_world.Loaded) Debug.LogWarning($"[Humen] 河流图层没数据：{_world.Error}");
        }

        /// <summary>清掉旧的河再重画。Editor 侧反复重建场景时会累计，故必须先清。</summary>
        public void Build()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }

            if (!Ready) return;

            if (_material == null) _material = ResolveMaterial();

            foreach (var r in _world.File.rivers) BuildOne(r, RiverWidth, "River");
            foreach (var t in _world.File.tributaries) BuildOne(t, TributaryWidth, "Tributary");
        }

        private void BuildOne(WvRiver r, float width, string prefix)
        {
            if (r?.points == null || r.points.Length < 2) return;

            List<Vector3> pts = Drape(r.points);
            if (pts.Count < 2) return;

            var go = new GameObject($"{prefix}_{r.id}");
            go.transform.SetParent(transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;                 // 点的坐标是星体<b>局部</b>坐标
            lr.positionCount = pts.Count;
            lr.SetPositions(pts.ToArray());
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = _material;
            lr.alignment = LineAlignment.View;        // 永远侧对镜头，免得贴着地表时侧面看不见
        }

        /// <summary>
        /// 把折线沿大圆加密后抬到地表之上。
        ///
        /// 相邻点按<b>球心夹角</b>决定插几段 —— 用夹角而不是经纬度差：
        /// 高纬度处同样的经度差对应的实际距离小得多，按经纬度插会在极区插得过密。
        /// </summary>
        private List<Vector3> Drape(WvPoint[] raw)
        {
            var outp = new List<Vector3>(raw.Length * 4);

            Vector3 Prev(int i) => PlanetGeometry.LatLonToLocal(raw[i].lat, raw[i].lon, 1f);

            Vector3 prev = Prev(0);
            outp.Add(prev * (Radius * HeightScale));

            for (int i = 1; i < raw.Length; i++)
            {
                Vector3 cur = Prev(i);

                float theta = Mathf.Acos(Mathf.Clamp(Vector3.Dot(prev, cur), -1f, 1f));
                float thetaDeg = theta * Mathf.Rad2Deg;
                int steps = Mathf.Clamp(Mathf.CeilToInt(thetaDeg / Mathf.Max(0.05f, MaxSegmentDegrees)), 1, 256);

                for (int s = 1; s <= steps; s++)
                {
                    Vector3 p = PlanetGeometry.SlerpLocal(prev, cur, (float)s / steps);
                    outp.Add(p.normalized * (Radius * HeightScale));
                }
                prev = cur;
            }
            return outp;
        }

        private Material ResolveMaterial()
        {
            // 水的颜色。刻意压暗、偏青 —— 星球的陆地下方就是海，河要比海亮一点才分得出，
            // 但又不能亮到像发光的线。
            var color = new Color(0.30f, 0.62f, 0.86f, 1f);

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            var mat = new Material(shader) { name = "HumenRiver" };
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            return mat;
        }

        /// <summary>按当前世界快照统计出来的河数，供 UI 与验收读数。</summary>
        public int RiverCount => Ready ? _world.File.rivers.Length : 0;
        public int TributaryCount => Ready ? _world.File.tributaries.Length : 0;
    }
}
