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
        private Camera _cam;

        /// <summary>
        /// 相机机架的<b>权威</b>姿态：把相机从"目标正后方"那个基准位姿转到这里。
        ///
        /// <b>为什么不是每帧从 <see cref="Yaw"/>/<see cref="Pitch"/> 现算。</b>
        /// 拨地球（<see cref="GlobeDrag"/>）要给机架叠加一个<b>任意轴</b>的旋转，
        /// 而"欧拉角 → 机架"这一步会<b>丢掉绕视轴的滚转</b>：
        /// 每帧现算的话，那个滚转分量会被下一帧悄悄抹掉，
        /// 表现就是"按住地皮拖，地皮慢慢从光标底下溜走" ——
        /// 而这正是本工程在 v0.25 花了 0.26 px 才量准的那条「跟手」。
        /// 所以姿态存成一个四元数，欧拉角退化成它的一个<b>作者入口</b>（见 <see cref="ApplyImmediately"/>）。
        /// </summary>
        private Quaternion _orbitRot = Quaternion.identity;

        /// <summary>上一次由欧拉角写进 <see cref="_orbitRot"/> 的 Yaw/Pitch，用来发现"有人直接改了字段"。</summary>
        private float _eulerYaw, _eulerPitch;

        // 开机机位。复位键（见 GlobeDrag）要回到的就是这一组 —— 由 PlanetSceneBuilder 写进场景。
        private float _homeYaw, _homePitch, _homeFactor;

        private void Awake() => Resolve();

        /// <summary>
        /// 幂等的初始化。<see cref="Awake"/> 与<b>非 Play</b> 的取证路径都走这里。
        ///
        /// ⚠️ 为什么要单独抽出来：编辑器在非 Play 状态下<b>根本不会调用 Awake</b>
        ///    （本组件不是 <c>ExecuteAlways</c>），而取证渲染正是在非 Play 下开场景的。
        ///    本工程已经在 <see cref="SunMoonSystem"/> 上栽过同款跟头 ——
        ///    那次的表现是"预览图里太阳和月亮都不存在"，而 Unity 一句错都不报。
        /// </summary>
        public void Resolve()
        {
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

            // 开机机位只记一次。非 Play 路径下 Awake 没跑过，第一次走到这里才记 ——
            // 记的是场景里序列化的那几个值，正是"应用一打开的那一帧"。
            if (_homeFactor > 0f) return;
            _homeYaw = Yaw; _homePitch = Pitch; _homeFactor = DistanceFactor;
            _distFactor = Mathf.Clamp(DistanceFactor, MinFactor, MaxFactor);
            _orbitRot = Quaternion.Euler(Pitch, Yaw, 0f);
            _eulerYaw = Yaw; _eulerPitch = Pitch;
        }

        private void LateUpdate()
        {
            HandleInput();
            SyncEulerIfEditedExternally();
            ApplyRig(true);
        }

        /// <summary>
        /// 把机架姿态写进 Transform。<paramref name="smooth"/> 为假时<b>立刻</b>落位。
        ///
        /// ⚠️ 拨地球必须走<b>不插值</b>的那一支。插值是为了让镜头不抖，
        ///    但"手底下的地皮"要求的是<b>恒等</b>跟手：镜头若落后一帧，
        ///    地皮就会从光标底下溜走 —— 那不是抖动，是压根不跟手。
        ///    所以 <see cref="OrbitBy"/> 落位是即时的，平滑只留给缩放与中键转视角。
        /// </summary>
        private void ApplyRig(bool smooth)
        {
            Resolve();   // 幂等；只为兜住"非 Play 路径下 Target 还没解析过"
            var wanted = Target.position + _orbitRot * (Vector3.back * (_distFactor * Radius));

            if (!smooth)
            {
                transform.position = wanted;
                transform.rotation = _orbitRot;

                // ⚠️ Unity 的 worldToCameraMatrix 是**缓存**的，通常要到渲染时才刷新。
                //    而这里必须在<b>同一帧内</b>立刻成立：GlobeDrag 刚刚靠 ScreenPointToRay
                //    算出该转多少，紧接着（SimulateDrag 的自检里）就要用 WorldToScreenPoint
                //    把结果量回来。中间夹着一个陈旧的矩阵，量出来的"跟手误差"就是假的 ——
                //    而那正是本轮唯一能证明"转相机"与"转球"等价的那个读数。
                if (_cam == null) _cam = GetComponent<Camera>();
                if (_cam != null) _cam.ResetWorldToCameraMatrix();
                return;
            }

            // 平滑是为了让拖拽不抖，但插值与"模拟层"无关 ——
            // §10.6 的确定性约束管的是几何生成，不管镜头。
            float k = Smoothing <= 0f ? 1f : 1f - Mathf.Exp(-Smoothing * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, wanted, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, _orbitRot, k);
        }

        /// <summary>
        /// 有人<b>直接改了</b> <see cref="Yaw"/>/<see cref="Pitch"/> 字段时，把它写回机架。
        ///
        /// 没有这一步，直接赋值会<b>静悄悄地什么都不做</b> ——
        /// 因为姿态的权威是 <see cref="_orbitRot"/>，字段只是它的一个入口。
        /// 本工程在 §13.4 反复记过同一款病：读数对不上画面，而两边都不报错。
        /// </summary>
        private void SyncEulerIfEditedExternally()
        {
            if (Mathf.Approximately(Yaw, _eulerYaw) && Mathf.Approximately(Pitch, _eulerPitch)) return;
            _orbitRot = Quaternion.Euler(Pitch, Yaw, 0f);
            _eulerYaw = Yaw; _eulerPitch = Pitch;
        }

        /// <summary>把机架姿态反写成 Yaw/Pitch（供 HUD 与外部读取）。滚转分量在此丢失，这是欧拉角的性质。</summary>
        private void SyncEulerFromRig()
        {
            Vector3 dir = _orbitRot * Vector3.back;
            Yaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
            Pitch = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
            _eulerYaw = Yaw; _eulerPitch = Pitch;
        }

        /// <summary>
        /// 给机架叠加一个<b>世界系</b>旋转 <paramref name="delta"/>（绕 <see cref="Target"/> 转）。
        ///
        /// 这是 <see cref="GlobeDrag"/> 拨地球用的入口：它传进来的是"把球转 q"的<b>逆</b>，
        /// 于是相机整体绕球心刚性转过去 —— <b>世界里的东西一件都没动</b>。
        /// 太阳、月亮因此仍钉在原处，而它们在<b>屏幕上</b>会各自按自己的距离移动（真视差）——
        /// 这正是"拖动蓝星时日月的位置要跟着变，但相对关系不能变"的实现方式。
        /// </summary>
        public void OrbitBy(Quaternion delta)
        {
            _orbitRot = delta * _orbitRot;
            SyncEulerFromRig();
            ApplyRig(false);          // ⚠️ 不插值，见 ApplyRig
        }

        /// <summary>复位键要回到的那一组数值（开机时由场景写死的 Yaw/Pitch/DistanceFactor）。</summary>
        public float HomeYaw => _homeYaw;
        public float HomePitch => _homePitch;

        /// <summary>
        /// 把机位复位到开机那一组 —— 连同距离。拨乱之后按一下就能回来。
        /// 立刻落位（不插值）：复位是个<b>瞬时的意图</b>，慢慢滑回去只会让人分不清
        /// "它还在动"还是"我已经按到了"。
        /// </summary>
        public void ResetToHome()
        {
            Resolve();   // 非 Play 路径下 Awake 没跑过，这里补上"开机机位"的记账

            // 开机值在 Resolve 之后就不再变 —— 复位要回的是**开机那一刻**，
            // 不是"上一次看着顺眼的那一刻"。所以这里只读不写。
            Yaw = _homeYaw;
            Pitch = _homePitch;
            _orbitRot = Quaternion.Euler(Pitch, Yaw, 0f);
            _eulerYaw = Yaw; _eulerPitch = Pitch;
            _distFactor = Mathf.Clamp(_homeFactor, MinFactor, MaxFactor);
            ApplyRig(false);
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
            Resolve();
            _distFactor = Mathf.Clamp(DistanceFactor, MinFactor, MaxFactor);
            _orbitRot = Quaternion.Euler(Pitch, Yaw, 0f);
            _eulerYaw = Yaw; _eulerPitch = Pitch;
            ApplyRig(false);
        }
    }
}
