using UnityEngine;

namespace Humen.Planet
{
    /// <summary>
    /// 用鼠标<b>拨蓝星</b>，手感照 <b>CesiumJS 的球面拖拽</b>（它内部叫 <c>rotate3D</c>）来。
    /// <c>R</c> 开关自转，<c>F</c> 把机位与星球自转复位。
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
    /// ════════════════════════════════════════════════════════════════════
    ///  ⚠️ v0.27 反转：<b>转的是相机，不是球</b>
    /// ════════════════════════════════════════════════════════════════════
    /// 这里原先是反过来的，而且写成了硬规矩：「⚠️ <b>转的是球，不是相机</b>」，
    /// 并把「拨球时太阳的屏幕位置一像素都不动」当成<b>成功</b>记进了 v0.25。
    /// 作者看过后否掉了，理由是：
    /// 「拖动蓝星的时候，月亮和太阳的位置也得相应地改变……不能改变相对关系，
    /// 这就是我给你说参考 Cesium 的原因，得有距离与角度的空间关系。」
    ///
    /// <b>那个"0 像素"不是关系被保住了，恰恰是关系已经没了的读数。</b>
    /// 月亮在 <c>2.6R</c>（见 <see cref="SunMoonSystem"/>），是个<b>近处的实体</b>，
    /// 不是贴在屏幕上的画。真实世界里偏一下头，近的东西挪得多、远的东西挪得少 ——
    /// 这就是视差，也正是"距离与角度"四个字的含义。
    /// 而"球在转、月亮纹丝不动"等于宣称它在<b>无穷远</b>：
    /// 屏幕上永远钉在同一个像素，一点视差都没有。
    /// 画面看着"相对位置没变"，其实那是一张<b>贴在屏幕上的画</b> ——
    /// 把相机绕到球的背面去，月亮还挂在画面右边，而真实情形里它早该转到另一边了。
    ///
    /// ⚠️ <b>v0.28 起，太阳不再参与这条论证了。</b>太阳被放到 1012R（作者：「得是上百倍」），
    ///    它的屏上视差因此小到量不出来。这是<b>照实说</b>，不是"关系又丢了"：
    ///    日:月的距离比、半径比、角径比现在<b>全部是真值</b>（见 <see cref="SunMoonSystem"/> 类注释），
    ///    而真实的太阳在短距离位移下本来就该几乎不动。视差这条论证从此由<b>月亮</b>一个人扛 ——
    ///    它扛得住：2.6R 就在眼前，屏上位移是量得出来的。
    ///
    /// <b>正确做法（也正是 Cesium 的做法）：世界不动，动的是观察者。</b>
    /// Cesium 的 <c>rotate3D</c> 转的是<b>相机</b>，地球在世界系里一动不动。于是：
    /// <list type="bullet">
    ///   <item>蓝星、月亮、太阳三者之间的<b>每一个距离、每一个角度都原封不动</b> ——
    ///         因为压根没有任何物体被移动过。这不是"努力维持"，是<b>构造上就不可能变</b>。</item>
    ///   <item>而它在<b>屏幕上</b>按自己的距离移动：机架绕球心转 Δ 时，
    ///         蓝星圆面钉在正中，月亮（2.6R）扫过一段。
    ///         <b>远近不同 → 屏上位移不同</b>，这就是视差，也就是作者要的那件事。
    ///         （v0.27 这里还写着"太阳（1.8R，更近）扫过更大的一段"——
    ///          v0.28 太阳移到 1012R 后这一项没了，理由见上文 ⚠️。）</item>
    ///   <item>明暗界线跟着太阳在屏幕上一起走，晨昏线扫过大陆的形状也跟着变 ——
    ///         因为相机换了个位置，去看同一个被照亮的球。</item>
    /// </list>
    ///
    /// <b>跟手为什么没丢。</b>这条是 v0.25 花 0.26 px 量准的，不能因为换了模型就松掉。
    /// 记球心为 c、抓住的世界方向为 <c>p−c</c>、光标下的世界方向为 <c>cur−c</c>，
    /// 令 <c>q = FromToRotation(p−c, cur−c)</c>（把球转 q 就能让地皮回到光标下，这是 v0.25 的算法）。
    /// 现在改成把<b>相机机架</b>绕 c 转 <c>d = q⁻¹</c>。对同一个点 p：
    /// <code>
    /// 新机架看到：  v' = R'⁻¹(p − t') = R⁻¹( d⁻¹(p−c) − (t−c) )
    /// 旧机架看 p₀： v  = R⁻¹(p₀ − t) = R⁻¹( d⁻¹(p−c) − (t−c) )    其中 p₀ = d⁻¹(p−c) + c
    /// </code>
    /// 两式<b>恒等</b>。也就是说：机架转 d，与"把球转 q"在屏幕上<b>完全等价</b> ——
    /// 不是近似，是同一个式子。所以按下的那个本地点照样<b>恰好</b>落在光标下，
    /// <see cref="LastGrabErrorPixels"/> 仍然应当读到 0。
    /// <b>手感的保证原封不动地搬了过来，换掉的只是"谁在动"。</b>
    ///
    /// ⚠️ 月亮<b>不</b>跟着拨球改轨道面。月轨按定义是蓝星赤道面，
    ///    严格说球被拨斜之后轨道面也该跟着斜。不跟，是因为跟了就会变成
    ///    "拖一下地球，月亮在天上扫过去" —— 那正好违反上面那条要求。
    ///    这里选的是作者明确要的那一条。（改成转相机之后这一条是白送的：
    ///    世界压根不动，月轨当然也不动。）
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

        [Header("按键")]
        [Tooltip("开着时按此键切换地球自转。")]
        public KeyCode ToggleKey = KeyCode.R;

        [Tooltip("按此键把机位与星球自转复位到开机状态。\n" +
                 "⚠️ 不能取 Home —— 那个键已经是时间轴的「跳到起始年」" +
                 "（见 WorldTimeline.HandleInput），同一帧按下去会两件事一起做。")]
        public KeyCode ResetKey = KeyCode.F;

        private OrbitCamera _orbit;
        private PlanetBootstrap _spin;
        private float _radius = 1000f;
        private bool _warnedNoOrbit;

        private bool _dragging;
        private bool _coasting;
        private float _dragPixels;
        private Vector2 _prevMouse;

        /// <summary>按下时光标下的那个球面点，存在<b>球的本地坐标系</b>里。</summary>
        private Vector3 _grabLocal;

        /// <summary>
        /// <b>机架</b>的角速度（世界系，弧度/秒），方向是转轴。松手后靠它继续转。
        ///
        /// ⚠️ 量的是机架，不是球。v0.25 量的是球的角速度 —— 数值大小一样（两者互为逆），
        ///    但<b>方向相反</b>。惯性那一段直接拿它当转轴用，符号弄反了不会报错，
        ///    只会表现为"一甩就往反方向跑"，而那看起来像"阻尼参数调歪了"。
        /// </summary>
        private Vector3 _angVel;

        /// <summary>开机时星球的姿态。<see cref="ResetGlobe"/> 要回到的就是它。</summary>
        private Quaternion _homePlanetRot;
        private bool _homeCaptured;

        /// <summary>本帧是否正被拖拽。</summary>
        public bool IsDragging => _dragging;

        /// <summary>本次按下以来累计的位移（像素）。供拾取判断"这是点击不是拖拽"。</summary>
        public float DragPixels => _dragPixels;

        /// <summary>自转当前是否开着（真正的状态在 <see cref="PlanetBootstrap"/> 上）。</summary>
        public bool AutoRotating => _spin != null && _spin.AutoRotate;

        private void Awake() => Resolve();

        /// <summary>
        /// 幂等的初始化。<see cref="Awake"/> 与<b>非 Play</b> 的取证路径都走这里 ——
        /// 后者下 Awake 根本不会被调用（本组件不是 <c>ExecuteAlways</c>），
        /// 而取证正是要在那条路上量"日月动了没有"。
        /// 与 <see cref="SunMoonSystem.EnsureBuilt"/> 同一个理由，不是重复造轮子。
        /// </summary>
        public void Resolve()
        {
            if (Planet == null)
            {
                var go = GameObject.Find("Planet");
                if (go != null) Planet = go.transform;
            }

            if (_orbit == null) _orbit = FindFirstObjectByType<OrbitCamera>();
            if (Planet == null) return;

            if (_spin == null) _spin = Planet.GetComponent<PlanetBootstrap>();
            // 半径取 PlanetBootstrap 的序列化值 —— 它在 Awake 之前就有效，
            // 不依赖 Build() 有没有跑过。
            if (_spin != null && _spin.Radius > 1f) _radius = _spin.Radius;

            // 复位要回的是**开机那一刻**的星球姿态，不是"上一次看着顺眼的那一刻"。
            if (_homeCaptured) return;
            _homePlanetRot = Planet.rotation;
            _homeCaptured = true;
        }

        private void Update()
        {
            // ── 自转开关、复位（作者要求：可以开启/停止自转；转乱了要能一键回来）──
            if (_spin != null && Input.GetKeyDown(ToggleKey))
                _spin.AutoRotate = !_spin.AutoRotate;
            if (Input.GetKeyDown(ResetKey)) ResetGlobe();

            if (Planet == null || Camera.main == null) return;

            // Awake 时相机可能还没建好（场景构建顺序不保证），这里补一次。
            if (_orbit == null) _orbit = FindFirstObjectByType<OrbitCamera>();

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
            // ⚠️ 光标压在 HUD 上时不许抓球。IMGUI 是在 OnGUI 阶段处理鼠标的，
            //    而 Update 比它早 —— 不挡的话，"点一下界面上的东西"会同时在球上抓一把。
            //    HUD 现在只有一个只读的 Label（没有按钮），但这条要在加按钮之前就位，
            //    否则那个 bug 的表现是"点按钮的时候地球会轻轻动一下"，很难归因。
            if (GUIUtility.hotControl != 0) return;

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
        /// 把机架转到「按下的那个本地点重新回到光标底下」。
        ///
        /// 与 v0.25 的算式<b>同源</b>：都是先求"把抓取点转到光标点"的那个最短旋转 q，
        /// 区别只在最后一步 —— 那时是把 q 右乘给球，现在是把 q⁻¹ 交给机架。
        /// 两者在屏幕上恒等（证明见类注释）。
        /// </summary>
        private void RotateGrabTo(Camera cam, Vector2 screen, float dt)
        {
            // 抓取点<b>此刻</b>在世界系的哪个方向 —— 由本地点换算，不是按下时存下的死方向。
            // 于是即便星球这一帧还在自转（DragHold 生效之前的那一帧），地皮也仍然跟手。
            Vector3 grabDir = Planet.TransformPoint(_grabLocal) - Planet.position;
            Vector3 curDir = PointOnSphere(cam, screen) - Planet.position;
            if (grabDir.sqrMagnitude < 1e-6f || curDir.sqrMagnitude < 1e-6f) return;

            Quaternion q = Quaternion.FromToRotation(grabDir.normalized, curDir.normalized);
            if (!Mathf.Approximately(Sensitivity, 1f))
            {
                q.ToAngleAxis(out float a, out Vector3 ax);
                q = Quaternion.AngleAxis(a * Sensitivity, ax);
            }

            // 把球转 q ≡ 把机架绕球心转 q⁻¹。这条是硬的，见类注释里的推导。
            Quaternion d = Quaternion.Inverse(q);

            // 本帧真正转过的世界系角增量 → 角速度。惯性要的是这个，不是指针速度：
            // 同样的指针位移，在圆面中心转过的角度小、在边缘转过的角度大。
            d.ToAngleAxis(out float deg, out Vector3 axis);
            if (deg > 180f) deg -= 360f;      // ToAngleAxis 给 [0,360)，取最短的那一支
            Vector3 inst = axis * (deg * Mathf.Deg2Rad / dt);

            // 单帧估出来的角速度很毛躁，直接拿去甩会一跳一跳的；平滑一下。
            float k = 1f - Mathf.Exp(-12f * dt);
            _angVel = Vector3.Lerp(_angVel, inst, k);

            Orbit(d);
        }

        /// <summary>
        /// 把一次转动落到<b>机架</b>上。
        ///
        /// ⚠️ 没有 <see cref="OrbitCamera"/> 时退回"转球"（v0.25 的老行为）并<b>报错</b>。
        ///    这是兜底，正常场景里走不到 —— 但它必须存在且响亮：
        ///    "拖不动"和"悄悄退化成没有视差的假关系"是两种都<b>看不出来</b>的故障，
        ///    而它们恰好是本轮要修掉的那个东西，不能让它以任何形式从后门溜回来。
        /// </summary>
        private void Orbit(Quaternion d)
        {
            if (_orbit != null)
            {
                _orbit.OrbitBy(d);
                return;
            }

            if (!_warnedNoOrbit)
            {
                _warnedNoOrbit = true;
                Debug.LogError("[Humen] 场景里没有 OrbitCamera，拨球退化成「转球」——"
                             + "日月会失去视差，正是 v0.27 要修掉的那个样子。请检查主相机。");
            }

            // d 是世界系旋转，左乘即绕世界轴转（与机架那一支同向）。
            if (Planet != null) Planet.rotation = Quaternion.Inverse(d) * Planet.rotation;
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
            // 角速度量在世界系里，按世界轴转机架 —— 与拖拽时走的是同一条路（Orbit），
            // 于是"甩出去"与"手拖着"在几何上是同一件事，只是没有手了。
            Orbit(Quaternion.AngleAxis(speed * Mathf.Rad2Deg * dt, _angVel / speed));
            _angVel *= Mathf.Exp(-InertiaDamping * dt);
            _coasting = true;
        }

        /// <summary>
        /// 把蓝星与机位一起复位到开机状态。
        ///
        /// <b>三样都要复位，少一样就"没复位干净"：</b>
        /// <list type="bullet">
        ///   <item><b>机架</b> → <see cref="OrbitCamera.ResetToHome"/>（含距离）。
        ///         这是"被玩家转乱"最直接的那一部分。</item>
        ///   <item><b>星球自转角度</b> → 开机记下的那一格。只复位机架的话，
        ///         自转已经转掉的那半圈还在 —— 同一块大陆依然背对着你，
        ///         看起来就像"复位键没起作用"。</item>
        ///   <item><b>惯性</b> → 清零。不清的话，复位完它会<b>接着朝刚才的方向滑</b>，
        ///         症状同样是"复位键只生效了一半"。</item>
        /// </list>
        /// 自转的<b>开关</b>不动：那是玩家的一个持续选择，不是"转乱了"。
        /// </summary>
        public void ResetGlobe()
        {
            Resolve();
            _dragging = false;
            _coasting = false;
            _dragPixels = 0f;
            _angVel = Vector3.zero;

            if (_orbit != null) _orbit.ResetToHome();
            if (Planet != null && _homeCaptured) Planet.rotation = _homePlanetRot;
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
        /// 于是它同时也是一个<b>回归哨兵</b>：哪天有人把 q 的符号弄反、
        /// 忘了取逆、或者把 <c>_grabLocal</c> 换成世界坐标，这个数会立刻从 0 跳到几十像素，
        /// 而画面看上去只是"手感有点怪"——那种没人会去查的怪。
        /// <b>v0.27 把"转球"换成"转相机"之后它必须仍然是 0</b>：
        /// 那两个模型在屏幕上恒等，这个数就是那份恒等的实测凭据。
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
        /// ⚠️ 相机姿态由 <see cref="OrbitCamera.OrbitBy"/> <b>同步落位</b>（不插值），
        ///    所以下面那次 <c>WorldToScreenPoint</c> 读到的就是转动之后的机位。
        ///    若哪天有人把落位改成走插值，这个数会立刻变大 —— 那是真话，不是误差。
        ///
        /// 返回<b>松手瞬间的角速度（度/秒）</b>。惯性算得对不对只能这样量：
        /// 截图能证明"球转了"，证明不了"甩得动"。
        /// 返回前把角速度清零 —— 取证要的是可复现的那一帧，不是甩出去之后漂到哪。
        /// </summary>
        public float SimulateDrag(Vector2 from, Vector2 to, int steps = 12)
        {
            Resolve();
            // 非 Play 路径下 Camera.main 未必有值（主相机标签是运行时才生效的），兜一个查找。
            var cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
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

        // ══════════════════════════════════════════════════════════════
        //  取证：日月视差 ← v0.27 的核心断言
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 拨一次位移，把作者那两句话同时量出来，然后复位。返回一份可打印的多行报告。
        ///
        /// <b>三个读数必须一起报，因为每一个都能被另一种错法骗过去：</b>
        /// <list type="bullet">
        ///   <item>「月亮和太阳的位置也得相应地改变」→ 屏上<b>必须动</b>。
        ///         月亮动得多（2.6R，就在眼前）；太阳动得极少 ——
        ///         v0.28 起它在 1012R，视差小到量不出来，这是<b>照实</b>，不是又冻住了。</item>
        ///   <item>「不能改变相对关系……得有距离与角度的空间关系」→ 三维的
        ///         日地距、月地距、日-地-月 夹角<b>前后完全相同</b>。</item>
        ///   <item>⚠️ <b>v0.28 新增：ψ 必须变</b>（<see cref="SunMoonSystem.ViewedPhaseDeg"/>，
        ///         球心处日-地-相机夹角）。这一条是前两条的<b>反面排除器</b>：
        ///         把世界冻住（v0.25 的做法）时屏上一动不动、三维距离也确实不变，
        ///         <b>前两条读数会全部"通过"</b>；而 ψ 只要相机在绕球转就必然变，
        ///         且与 δ 反向 —— 冻住时它一个数都不会动。delta 单独一个数是<b>不可判</b>的。</item>
        /// </list>
        /// <b>两种错法长得不一样：</b>全都冻住满足第二条、破坏第一条，看起来"关系保住了"；
        /// 把日月挂到球上跟着转则满足第一条、破坏第二条，
        /// 看起来"日月动得很自然"—— 而那时日月已经不在它们该在的位置上了。
        ///
        /// ⚠️ 所以这个函数<b>必须返回三个方向的读数</b>，不能只报一个。
        ///    v0.25 只量了"关系变了没有"，量到 0 就写成了成功 ——
        ///    而那个 0 恰恰是第一句话被破坏的证据。
        /// </summary>
        public string VerifyOrbitParallax(Vector2 from, Vector2 to)
        {
            Resolve();
            var cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            var sky = FindFirstObjectByType<SunMoonSystem>();
            if (cam == null) return "[Humen] 日月视差未验：场景里没有相机。";
            if (Planet == null) return "[Humen] 日月视差未验：没有星球。";
            if (sky == null) return "[Humen] 日月视差未验：场景里没有 SunMoonSystem。";
            if (_orbit == null)
                return "[Humen] 日月视差未验：场景里没有 OrbitCamera —— 拨的是球而不是相机，"
                     + "日月必然纹丝不动。这正是 v0.27 要修掉的那个样子。";

            ResetGlobe();     // 从开机机位起量

            Vector3 c0 = sky.PlanetPosition;
            Vector3 sun0 = sky.SunPosition, moon0 = sky.MoonPosition;
            float sunAng0 = OffAxis(cam, sun0), moonAng0 = OffAxis(cam, moon0);
            float psi0 = sky.ViewedPhaseDeg(cam);
            string sunPos0 = SkyPos(cam, sun0), moonPos0 = SkyPos(cam, moon0);
            var sight0 = sky.SightSun(cam);

            float flick = SimulateDrag(from, to);

            Vector3 c1 = sky.PlanetPosition;
            Vector3 sun1 = sky.SunPosition, moon1 = sky.MoonPosition;
            float sunAng1 = OffAxis(cam, sun1), moonAng1 = OffAxis(cam, moon1);
            float psi1 = sky.ViewedPhaseDeg(cam);

            string report =
                $"[Humen] 日月视差 · 拨了 {Vector2.Distance(from, to):F0} px，松手 {flick:F0} 度/秒\n"
              + $"  离视轴角度：太阳 {sunAng0:F2}° → {sunAng1:F2}°"
              + $" · 月亮 {moonAng0:F2}° → {moonAng1:F2}°\n"
              + $"  ⚠️ ψ 球心相位角（冻结的反面排除器，必须变、且与 δ 反向）："
              + $"{psi0:F2}° → {psi1:F2}°（Δ {psi1 - psi0:+0.00;-0.00}°）"
              + $" · ε(=180−δ−ψ，即太阳视差角) {180f - sunAng0 - psi0:F2}° → {180f - sunAng1 - psi1:F2}°"
              + $"（ε 不是误差：δ+ψ = 180 − ε，太阳越远 ε 越小）\n"
              + $"  蓝星亮度 cos ψ：{Mathf.Cos(psi0 * Mathf.Deg2Rad):F3} → {Mathf.Cos(psi1 * Mathf.Deg2Rad):F3}\n"
              + $"  太阳处境：{sight0} → {sky.SightSun(cam)}\n"
              + $"  屏幕位置（供核对；日盘在镜头背后时像素无意义）："
              + $"太阳 {sunPos0} → {SkyPos(cam, sun1)}"
              + $" · 月亮 {moonPos0} → {SkyPos(cam, moon1)}\n"
              + $"  三维关系（必须一模一样）："
              + $"日地 {Vector3.Distance(c0, sun0):F4} → {Vector3.Distance(c1, sun1):F4} · "
              + $"月地 {Vector3.Distance(c0, moon0):F4} → {Vector3.Distance(c1, moon1):F4} · "
              + $"日-地-月 {Vector3.Angle(sun0 - c0, moon0 - c0):F4}° → "
              + $"{Vector3.Angle(sun1 - c1, moon1 - c1):F4}°\n"
              + $"  跟手误差 {LastGrabErrorPixels:F2} px"
              + (LastGrabTargetHitSphere ? "（终点落在球面内，这个数作数）" : "（终点出圈，不作数）");

            ResetGlobe();     // 量完拨回去，免得取证把世界留在歪的位置上
            return report;
        }

        /// <summary>某个世界点偏离视轴多少度。出画也照样有定义，故比像素可靠。</summary>
        private static float OffAxis(Camera cam, Vector3 world)
        {
            Vector3 d = world - cam.transform.position;
            if (d.sqrMagnitude < 1e-9f) return -1f;
            return Vector3.Angle(cam.transform.forward, d.normalized);
        }

        /// <summary>
        /// 世界点落在屏幕哪里。在相机背后、或跑出画面都会写明。
        /// ⚠️ 边界用 <c>cam.pixelWidth/pixelHeight</c> 而不是 <c>Screen.*</c>：
        ///    相机渲染到 RenderTexture 时（编辑期取证就是这样），
        ///    <c>WorldToScreenPoint</c> 的坐标系是那块 RT，而 <c>Screen.*</c> 是窗口的 —— 两者不是一回事。
        /// </summary>
        private static string SkyPos(Camera cam, Vector3 world)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return "（相机背后）";
            bool inside = sp.x >= 0f && sp.x <= cam.pixelWidth && sp.y >= 0f && sp.y <= cam.pixelHeight;
            return $"({sp.x:F0},{sp.y:F0})" + (inside ? "" : "出画");
        }
    }
}
