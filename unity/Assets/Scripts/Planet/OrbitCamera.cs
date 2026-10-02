using UnityEngine;

namespace Humen.Planet
{
    /// <summary>
    /// 环绕相机。鼠标键的分配照 <b>CesiumJS 的默认映射</b>来：
    /// <b>右键拖拽 / 滚轮 → 缩放</b>，<b>中键拖拽 → 转视角</b>，
    /// 左键留给 <see cref="GlobeDrag"/> 拨地球（Cesium 的左键也是转地球）。
    ///
    /// 距离上下限直接取 §10.1 的两级视图：
    /// <list type="bullet">
    ///   <item><b>A 级 轨道</b> <c>1.2R ~ 4.0R</c> —— 看整颗星球、大陆轮廓</item>
    ///   <item><b>B 级 大陆</b> <c>1.005R ~ 1.2R</c> —— 贴着地表看大陆形状</item>
    /// </list>
    /// 本脚本只负责「看」。C 级（聚落）不是把镜头再往前推 ——
    /// §10.1 讲得很清楚：B→C 差 6371 倍，是<b>降落</b>到另一个场景，不是缩放。
    /// 所以这里的最小距离刻意停在 <c>1.005R</c>，不会一头扎进地表。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class OrbitCamera : MonoBehaviour
    {
        [Header("目标")]
        public Transform Target;

        [Tooltip("星球半径，用于换算 §10.1 的两级视图距离。")]
        public float Radius = 1000f;

        [Header("角度")]
        public float Yaw = -35f;
        public float Pitch = 18f;

        [Header("距离")]
        [Tooltip("初始距离倍率（相对半径）。3.0 即 3R，属 A 级轨道视图。")]
        public float DistanceFactor = 3.0f;

        [Tooltip("最近能推到多远（倍率）。1.005 = §10.1 的 B 级上限。")]
        public float MinFactor = 1.005f;

        [Tooltip("最远能拉到多远（倍率）。")]
        public float MaxFactor = 4.0f;

        [Header("手感")]
        [Tooltip("中键转视角的速度（度/像素）。")]
        public float TiltDegreesPerPixel = 0.15f;

        [Tooltip("滚轮缩放速度。（乘性：越近走得越细，见 HandleInput）")]
        public float ZoomSpeed = 0.12f;

        [Tooltip("右键拖拽缩放：每像素的指数系数。往下拖 350 px ≈ 拉近 4 倍。")]
        public float DragZoomPerPixel = 0.004f;

        public float Smoothing = 12f;

        private float _distFactor;
        private Vector2 _prevMouse;

        private void Awake()
        {
            _distFactor = Mathf.Clamp(DistanceFactor, MinFactor, MaxFactor);

            // 没指定目标就自己造一个：§10.1 的三级视图里，
            // 星球永远在场景原点，这跟世界坐标量级 1000 的浮点精度是绑在一起的
            // （相机对着原点，偏移才有意义），不是随手定的。
            if (Target == null)
            {
                var planet = GameObject.Find("Planet");
                if (planet != null) Target = planet.transform;
                else
                {
                    var go = new GameObject("PlanetPivot");
                    go.transform.position = Vector3.zero;
                    Target = go.transform;
                }
            }
        }

        private void LateUpdate()
        {
            HandleInput();

            var rot = Quaternion.Euler(Pitch, Yaw, 0f);
            var wanted = Target.position + rot * (Vector3.back * (_distFactor * Radius));

            // 平滑是为了让拖拽不抖，但插值与"模拟层"无关 ——
            // §10.6 的确定性约束管的是几何生成，不管镜头。
            float k = Smoothing <= 0f ? 1f : 1f - Mathf.Exp(-Smoothing * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, wanted, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(Target.position - transform.position, Vector3.up), k);
        }

        /// <summary>
        /// 鼠标键的分配照 <b>CesiumJS 的默认映射</b>：
        /// <c>zoomEventTypes = [RIGHT_DRAG, WHEEL, PINCH]</c>、中键 <c>tilt</c>、左键 <c>rotate</c>。
        /// 左键归 <see cref="GlobeDrag"/> 拨地球（Cesium 的左键也是转地球），这里只管另外两个。
        ///
        /// ⚠️ 三个键分开不是洁癖。左键若还兼着转相机，"拨球"和"绕着球飞"就会同时发生，
        ///    而成像几乎一样（都是大陆在动），只有太阳和明暗界线会露馅：
        ///    转球时明暗界线扫过大陆，转相机时明暗界线钉在屏幕上不动。
        ///    于是这个 bug 会表现为"太阳是假的"——很难从症状想到病因。
        ///    干脆分成三个键，语义上就不可能混。
        ///
        /// ⚠️ 位移一律取<b>像素增量</b>（<c>Input.mousePosition</c> 前后相减），不用
        ///    <c>GetAxis("Mouse X")</c>：后者的量纲是"Unity 的 Mouse 轴灵敏度"，
        ///    默认 0.1，于是同一个"度/像素"在改了 Input 设置之后会静悄悄地变成另一个手感，
        ///    而代码一个字都没改。像素就是像素，换算关系写在字段的 Tooltip 里。
        /// </summary>
        private void HandleInput()
        {
            Vector2 mouse = Input.mousePosition;
            Vector2 d = mouse - _prevMouse;
            _prevMouse = mouse;

            // ── 右键拖拽 → 缩放 ──────────────────────────────────────
            // 方向取「往下拖 = 拉近」（把球往自己这边拽）。乘性缩放：越近走得越细，
            // 否则贴着地表时一拖就飞出去。
            // ⚠️ Cesium 的上下符号没能从离线资料里核实到（它的文档只说明右键是 zoom，
            //    没写方向），这里选的是顺手的那一支。要反过来只需把下面改成正号。
            if (Input.GetMouseButton(1) && Mathf.Abs(d.y) > 0.01f)
                Zoom(Mathf.Exp(d.y * DragZoomPerPixel));

            // ── 中键拖拽 → 转视角（Cesium 的 tilt 也是中键）──────────
            // 留着它是因为缩放到贴地之后，还得能调整角度看大陆的形状。
            if (Input.GetMouseButton(2))
            {
                Yaw += d.x * TiltDegreesPerPixel;
                Pitch = Mathf.Clamp(Pitch - d.y * TiltDegreesPerPixel, -89f, 89f);
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
                Zoom(Mathf.Exp(-scroll * ZoomSpeed * 10f));
        }

        /// <summary>乘性缩放。夹在 §10.1 的两级视图距离之间。</summary>
        private void Zoom(float factor)
        {
            _distFactor = Mathf.Clamp(_distFactor * factor, MinFactor, MaxFactor);
        }

        /// <summary>供 Editor 侧构建场景时写入初始姿态。</summary>
        public void ApplyImmediately()
        {
            _distFactor = Mathf.Clamp(DistanceFactor, MinFactor, MaxFactor);
            var rot = Quaternion.Euler(Pitch, Yaw, 0f);
            transform.position = Target.position + rot * (Vector3.back * (_distFactor * Radius));
            transform.rotation = Quaternion.LookRotation(Target.position - transform.position, Vector3.up);
        }
    }
}
