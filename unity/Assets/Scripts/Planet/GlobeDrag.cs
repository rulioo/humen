using UnityEngine;

namespace Humen.Planet
{
    /// <summary>
    /// 用鼠标<b>拨地球</b>，手感照 <b>CesiumJS 的球面拖拽</b>（它内部叫 <c>rotate3D</c>）来。
    /// <c>R</c> 开关自转。
    ///
    /// <b>手感到底是什么。</b>一句话：<b>按住的那块地皮，一直黏在光标底下。</b>
    /// 按下的那一刻把光标指着的球面点记下来（存在<b>球的本地坐标系</b>里），
    /// 之后每一帧只做一件事 —— 把球转到「这个点重新回到光标底下」。
    /// 于是往右拖多快、地皮就走多快，不多也不少；拖动是<b>1:1</b> 的，
    /// 松手前手上是什么感觉，屏幕上就是什么感觉。这就是 Cesium 的手感。
    ///
    /// <b>为什么不是"每像素转固定角度"。</b>本脚本第一版是 <c>0.30°/像素</c> 固定速率，
    /// 绕相机的上/右轴转。<b>它错在两个地方，而且都不报错：</b>
    /// <list type="number">
    ///   <item><b>不跟光标。</b>固定速率下，你按住某条海岸线往右拖，
    ///         海岸线会以它自己的速度走 —— 走得跟手指不一样。
    ///         于是你会不自觉地来回修正，手感就是"滑"，是"我在推一个不听话的东西"。
    ///         跟手的话，手是<b>粘</b>在球上的，不用修正。</item>
    ///   <item><b>球面是弯的，固定速率在球面上必然有地方不跟手。</b>
    ///         圆面中心 1 像素 ≈ 0.18°（<c>asin</c> 在 0 处斜率是 1，除以圆面像素半径），
    ///         越靠边同样 1 像素对应的角度越大，到轮廓处趋于无穷。
    ///         取 0.30 意味着<b>中心快了 1.7 倍、边缘反而慢</b> ——
    ///         两个地方的错还不一样，怎么调都只能对上一处。</item>
    /// </list>
    ///
    /// <b>为什么还要惯性。</b>Cesium 甩一下之后球会继续转、慢慢停。
    /// 没有惯性时，松手 = 画面<b>瞬间冻住</b>，那一顿比转得太快更伤手感 ——
    /// 手是甩出去的，画面却像撞在墙上。所以松手后按 <c>exp(−阻尼·t)</c> 继续转，
    /// 低于阈值停住。角速度从<b>真实的每帧转角</b>里估，不另设一套"甩动强度"。
    ///
    /// ⚠️ <b>转的是球，不是相机 —— 这条是硬的，不是风格偏好。</b>
    ///    太阳、月亮、平行光三者的位置都在<b>世界系</b>里，谁也不挂在蓝星下面
    ///    （见 <see cref="SunMoonSystem"/> 里那两处 <c>SetParent(null, true)</c>）。
    ///    所以拨球时<b>世界不动，只有球在转</b>：明暗界线会扫过大陆，月亮和太阳纹丝不动、
    ///    相对关系保持原样。这正是作者要的两件事：
    ///    「还原真实的关系」看得见的那一半，以及「拖动地球的时候，月球和太阳的相对关系要保持」。
    ///    改成转相机就会立刻破功 —— 星空与光照跟着一起转，太阳永远停在同一个方位，
    ///    明暗界线像贴在屏幕上。两者在静止时长得一模一样，一动才露馅。
    ///
    /// ⚠️ 月亮<b>不</b>跟着拨球改轨道面。月轨按定义是蓝星赤道面，
    ///    严格说球被拨斜之后轨道面也该跟着斜。不跟，是因为跟了就会变成
    ///    "拖一下地球，月亮在天上扫过去" —— 那正好违反上面那条要求。
    ///    这里选的是作者明确要的那一条。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GlobeDrag : MonoBehaviour
    {
        [Header("目标")]
        [Tooltip("要拨动的那颗球。留空则自动找名为 Planet 的对象。")]
        public Transform Planet;

        [Header("拖拽")]
        [Tooltip("灵敏度倍率。1 = 光标下的地表点严丝合缝跟着指针走（Cesium 的手感）。" +
                 "调它只是把转过的角度整体放大/缩小，不改变跟手的性质。")]
        public float Sensitivity = 1f;

        [Tooltip("低于这个累计位移（像素）就当作「点击」，不产生转动。")]
        public float ClickSlopPixels = 5f;

        [Header("惯性")]
        [Tooltip("松手后角速度的衰减率（每秒）。越大停得越快。")]
        public float InertiaDamping = 2.2f;

        [Tooltip("角速度低于此值（度/秒）就停住，免得无限地慢慢爬。")]
        public float MinInertiaSpeedDeg = 3f;

        [Header("自转开关")]
        [Tooltip("开着时按此键切换地球自转。")]
        public KeyCode ToggleKey = KeyCode.R;

        private PlanetBootstrap _spin;
        private float _radius = 1000f;

        private bool _dragging;
        private bool _coasting;
        private float _dragPixels;
        private Vector2 _prevMouse;

        /// <summary>按下时光标下的那个球面点，存在<b>球的本地坐标系</b>里。</summary>
        private Vector3 _grabLocal;

        /// <summary>世界系角速度（弧度/秒），方向是转轴。松手后靠它继续转。</summary>
        private Vector3 _angVel;

        /// <summary>本帧是否正被拖拽。</summary>
        public bool IsDragging => _dragging;

        /// <summary>本次按下以来累计的位移（像素）。供拾取判断"这是点击不是拖拽"。</summary>
        public float DragPixels => _dragPixels;

        /// <summary>自转当前是否开着（真正的状态在 <see cref="PlanetBootstrap"/> 上）。</summary>
        public bool AutoRotating => _spin != null && _spin.AutoRotate;

        private void Awake()
        {
            if (Planet == null)
            {
                var go = GameObject.Find("Planet");
                if (go != null) Planet = go.transform;
            }
            if (Planet == null) return;

            _spin = Planet.GetComponent<PlanetBootstrap>();
            // 半径取 PlanetBootstrap 的序列化值 —— 它在 Awake 之前就有效，
            // 不依赖 Build() 有没有跑过。
            if (_spin != null && _spin.Radius > 1f) _radius = _spin.Radius;
        }

        private void Update()
        {
            // ── 自转开关（作者要求：可以开启/停止地球自转效果）──
            if (_spin != null && Input.GetKeyDown(ToggleKey))
                _spin.AutoRotate = !_spin.AutoRotate;

            if (Planet == null || Camera.main == null) return;

            var cam = Camera.main;
            Vector2 mouse = Input.mousePosition;

            if (Input.GetMouseButtonDown(0)) BeginGrab(cam, mouse);
            if (_dragging && Input.GetMouseButton(0)) Drag(cam, mouse);
            if (Input.GetMouseButtonUp(0)) _dragging = false;

            Coast();

            // 拖拽期间、以及松手后还在滑行的期间，都按住自转的"暂停键"。
            // 见 PlanetBootstrap.DragHold 的注释：手底下的球如果还在自己转，手感立刻就散了。
            if (_spin != null) _spin.DragHold = _dragging || _coasting;
        }

        // ══════════════════════════════════════════════════════════════
        //  抓取与转动
        // ══════════════════════════════════════════════════════════════

        private void BeginGrab(Camera cam, Vector2 screen)
        {
            _dragging = true;
            _dragPixels = 0f;
            _prevMouse = screen;
            _angVel = Vector3.zero;   // 重新抓住球 —— 上一次的惯性立刻作废

            _grabLocal = Planet.InverseTransformPoint(PointOnSphere(cam, screen));
        }

        private void Drag(Camera cam, Vector2 screen)
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            // 累计位移用**逐帧增量**而不是"离按下点多远"：
            // 后者在拖回原点时会被判成 0，于是"拖出去再拖回来"会被当成点击。
            _dragPixels += (screen - _prevMouse).magnitude;
            _prevMouse = screen;

            // ⚠️ 超过阈值之前不转。否则手一抖就把"点选部落"变成"拨地球"，
            //    手指底下那颗球会微微一动 —— 作者点一下没选中的时候，
            //    是分不清"点歪了"还是"球动了"的。
            //    超过之后的第一次转动会一次补上全部累计角度（最多也就 5 px ≈ 1°），
            //    这是"跟手"的必然结果，不是跳变。
            if (_dragPixels <= ClickSlopPixels) return;

            RotateGrabTo(cam, screen, dt);
        }

        /// <summary>
        /// 把球转到「按下的那个本地点重新回到光标底下」。
        ///
        /// 数学上只有两行：算出光标现在指着的球面点（也化成球的本地坐标），
        /// 再求把 <c>_grabLocal</c> 转到它上面去的那个最短旋转，右乘上去。
        /// 右乘之后 <c>_grabLocal</c> 的世界位置<b>恰好</b>是光标下的那个点 —— 不是近似，是恒等。
        /// </summary>
        private void RotateGrabTo(Camera cam, Vector2 screen, float dt)
        {
            Vector3 local = Planet.InverseTransformPoint(PointOnSphere(cam, screen));

            Quaternion q = Quaternion.FromToRotation(_grabLocal, local);
            if (!Mathf.Approximately(Sensitivity, 1f))
            {
                q.ToAngleAxis(out float a, out Vector3 ax);
                q = Quaternion.AngleAxis(a * Sensitivity, ax);
            }

            Quaternion before = Planet.rotation;

            // ⚠️ <b>必须右乘。</b>q 是量在球的<b>本地坐标系</b>里的旋转，
            //    右乘才是"球自己这么转了一下"。
            //    写成 <c>q * Planet.rotation</c> 只差一个顺序、不报错、不崩，
            //    但那是绕<b>世界</b>轴转 —— 球被拨到侧面之后，
            //    "往右拖"会变成往斜上方走，手感整个错乱，而症状看着像"轴配错了"。
            Planet.rotation = Planet.rotation * q;

            // 本帧真正转过的世界系角增量 → 角速度。惯性要的是这个，不是指针速度：
            // 同样的指针位移，在球的中心转过的角度小、在边缘转过的角度大。
            Quaternion d = Planet.rotation * Quaternion.Inverse(before);
            d.ToAngleAxis(out float deg, out Vector3 axis);
            if (deg > 180f) deg -= 360f;      // ToAngleAxis 给 [0,360)，取最短的那一支
            Vector3 inst = axis * (deg * Mathf.Deg2Rad / dt);

            // 单帧估出来的角速度很毛躁，直接拿去甩会一跳一跳的；平滑一下。
            float k = 1f - Mathf.Exp(-12f * dt);
            _angVel = Vector3.Lerp(_angVel, inst, k);
        }

        /// <summary>松手后的惯性：继续转，按指数衰减，低于阈值停住。</summary>
        private void Coast()
        {
            _coasting = false;
            if (_dragging) return;

            float speed = _angVel.magnitude;
            if (speed * Mathf.Rad2Deg < MinInertiaSpeedDeg)
            {
                _angVel = Vector3.zero;
                return;
            }

            float dt = Time.deltaTime;
            // 用 Space.World：角速度是量在世界系里的，而地轴已经被拨得不在世界 Y 上了。
            Planet.Rotate(_angVel / speed, speed * Mathf.Rad2Deg * dt, Space.World);
            _angVel *= Mathf.Exp(-InertiaDamping * dt);
            _coasting = true;
        }

        /// <summary>
        /// 光标射线打在球面上的那个点（世界系）。
        /// 打不到球时退回<b>轮廓上的点</b>，见下面的说明。
        /// </summary>
        private Vector3 PointOnSphere(Camera cam, Vector2 screen) =>
            PointOnSphere(cam, screen, out _);

        /// <summary>
        /// 同上，但把"射线到底有没有打在球上"也带出来。
        ///
        /// 为什么要这个布尔量：取证里那条"抓取点应当落在光标下"的断言，
        /// <b>只在射线打得到球的时候才成立</b>。指针滑出圆面之后，
        /// 球面上<b>不存在</b>光标底下的那个点，误差必然等于"光标离轮廓多远" ——
        /// 实测 48.43 px，正是终点（离球心 269 px）减去圆面半径（220 px）。
        /// 没有这个标志位，那 48 px 会被当成"旋转算式写错了"，
        /// 而真相是<b>断言本身在那个位置不适用</b>。
        /// </summary>
        private Vector3 PointOnSphere(Camera cam, Vector2 screen, out bool hit)
        {
            Ray ray = cam.ScreenPointToRay(screen);
            Vector3 c = Planet.position;
            Vector3 oc = ray.origin - c;

            float b = Vector3.Dot(oc, ray.direction);        // direction 已是单位向量
            float cc = Vector3.Dot(oc, oc) - _radius * _radius;
            float disc = b * b - cc;

            if (disc >= 0f)
            {
                hit = true;
                float t = -b - Mathf.Sqrt(disc);             // 近的那一个交点
                if (t < 0f) t = 0f;                          // 相机已经进到球里面了
                return ray.GetPoint(t);
            }
            hit = false;

            // 没打到球：取射线上离球心最近的点，再拉回球面 —— 也就是轮廓上的那个点。
            //
            // 拉回球面而不是"干脆不动"，是为了让手势**连续**：
            // 指针滑出球体的那一瞬间如果球就不动了，手感上像是<b>球从手里掉了</b>；
            // 拉回轮廓则变成"继续往那个方向转"，与 Cesium 一致。
            float tm = Mathf.Max(0f, -b);
            Vector3 p = ray.GetPoint(tm) - c;
            if (p.sqrMagnitude < 1e-6f) return c + ray.direction * _radius;   // 射线正穿球心（理论上打得到，兜底）
            return c + p.normalized * _radius;
        }

        /// <summary>
        /// 上一次 <see cref="SimulateDrag"/> 结束时，「抓住的那个地表点」离<b>光标</b>还差多少像素。
        ///
        /// <b>这个数就是"跟手"的定义本身。</b>抓取式拖拽（Cesium 的 rotate3D）的全部内容
        /// 就是"按下的那个本地点始终待在光标底下"，所以它理想值是 <b>0</b>，
        /// 而且不是"接近 0"，是<b>恒等</b> —— 每帧都解一次"把本地点转回光标下"的方程。
        /// 于是它同时也是一个<b>回归哨兵</b>：哪天有人把右乘改成左乘、
        /// 或者把 <c>_grabLocal</c> 换成世界坐标，这个数会立刻从 0 跳到几十像素，
        /// 而画面看上去只是"手感有点怪"——那种没人会去查的怪。
        /// 负值表示这次没能测量（没有相机，或球心落在相机背后）。
        ///
        /// ⚠️ 这个数<b>只在 <see cref="LastGrabTargetHitSphere"/> 为真时才有意义</b> ——
        ///    指针滑出圆面之后球上根本没有光标底下那个点，误差必然是"光标离轮廓的距离"。
        /// </summary>
        public float LastGrabErrorPixels { get; private set; } = -1f;

        /// <summary>上一次 <see cref="SimulateDrag"/> 的终点是否落在球面内。
        /// 为假时 <see cref="LastGrabErrorPixels"/> 不作数（见那里的说明）。</summary>
        public bool LastGrabTargetHitSphere { get; private set; }

        /// <summary>
        /// 取证用：模拟一次完整的「按下 → 拖到别处 → 松手」，走的是与真鼠标<b>同一份</b>代码
        /// （见 <see cref="PlayerCapture"/>）—— 若测试自己另写一遍转动算式，验的就是测试而不是产品了。
        ///
        /// 返回<b>松手瞬间的角速度（度/秒）</b>。惯性算得对不对只能这样量：
        /// 截图能证明"球转了"，证明不了"甩得动"。
        /// 返回前把角速度清零 —— 取证要的是可复现的那一帧，不是甩出去之后漂到哪。
        /// </summary>
        public float SimulateDrag(Vector2 from, Vector2 to, int steps = 12)
        {
            var cam = Camera.main;
            if (cam == null || Planet == null) return 0f;

            LastGrabErrorPixels = -1f;
            // 终点打不打得到球，与球当前转到哪儿无关（球是正球，圆面在屏上是固定的）。
            PointOnSphere(cam, to, out bool endHit);
            LastGrabTargetHitSphere = endHit;

            const float dt = 1f / 60f;   // 名义帧长
            BeginGrab(cam, from);
            for (int i = 1; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(from, to, i / (float)steps);
                _dragPixels += (p - _prevMouse).magnitude;
                _prevMouse = p;
                if (_dragPixels > ClickSlopPixels) RotateGrabTo(cam, p, dt);
            }
            _dragging = false;

            // 自检：把抓住的那个本地点投回屏幕，看它是不是正落在终点光标下。
            Vector3 world = Planet.TransformPoint(_grabLocal);
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z > 0f)
                LastGrabErrorPixels = Vector2.Distance(new Vector2(sp.x, sp.y), to);

            float degPerSec = _angVel.magnitude * Mathf.Rad2Deg;
            _angVel = Vector3.zero;
            return degPerSec;
        }
    }
}
