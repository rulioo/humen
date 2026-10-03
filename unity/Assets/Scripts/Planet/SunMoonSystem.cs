using UnityEngine;

namespace Humen.Planet
{
    /// <summary>
    /// 太阳与月亮，以及它们与蓝星之间的<b>真实关系</b>（作者要求 ②）。
    ///
    /// <b>只有一盏平行光。</b>太阳既是光源也是天体 —— 它的朝向就是光的方向。
    /// 这一点是整套关系能"自动正确"的关键：
    /// 月亮<b>不单独打光</b>，它和蓝星被同一盏灯照，
    /// 于是月相、蓝星的晨昏线、以及"月亮被谁照亮"三者必然自洽，
    /// 不可能出现"月亮画了个满月却在蓝星的夜面那一侧"这种自相矛盾的画面。
    /// 反过来，只要给月亮单独补一盏灯，这套一致性就散了 —— 而且散得很安静。
    ///
    /// <b>照抄的真实关系</b>（每一条都在 <see cref="StatusLine"/> 里可核对）：
    /// <list type="bullet">
    ///   <item>朔望月 = <b>27.32 个星球日</b>（恒星月），由 <see cref="PlanetBootstrap.SecondsPerDay"/> 折算 ——
    ///         自转快，月亮就跟着快，比值恒为 27.32，不是各转各的。</item>
    ///   <item><b>潮汐锁定</b>：自转周期 == 公转周期，永远同一面朝着蓝星。</item>
    ///   <item>直径 = 蓝星的 <b>0.27 倍</b>。</item>
    ///   <item>轨道面对蓝星赤道倾斜 <b>5.1°</b>。</item>
    ///   <item><b>顺行</b>：公转方向与蓝星自转方向相同（从北极看都是逆时针）。</item>
    /// </list>
    ///
    /// <b>哪些是压缩过的，为什么</b>（不写清楚就会变成"看起来像真的"的假关系）：
    /// <list type="bullet">
    ///   <item>真实月地距离 ≈ <b>60.34 个蓝星半径</b>。照抄的话，把相机拉到能看全蓝星的位置时，
    ///         月亮在<b>画面外 20 倍远</b>的地方 —— 作者要的是"把太阳和月亮显示出来"，
    ///         那就必须压缩。这里取 <see cref="MoonOrbitRadiusFactor"/> = 2.6R（压缩 <b>23.19 倍</b>）。</item>
    ///   <item>代价：月亮的<b>视直径被放大了同样的 23.19 倍</b>。
    ///         注意被保留的是<b>直径比 0.27</b>（这个是真实关系，也是能一眼看出来的），
    ///         被放大的只是"看着多大"。两者不可能同时真实 —— 距离一压缩，视大小必然膨胀。</item>
    ///   <item><b>太阳按同一个倍数压缩</b>（v0.28 改）。真实日地 23 481R ÷ 23.19 = <b>1012R</b>
    ///         （<see cref="SunDistanceFactor"/>），半径 <b>108.1R</b>（<see cref="SunRadiusFactor"/>）。
    ///         <b>关键在于"同一个倍数"</b>：整个太阳系等比缩小 23.19 倍、蓝星自己不缩，
    ///         于是日:月 距离比 = 1012/2.6 = <b>389.2</b>（真实 389.17）、
    ///         日/月 半径比 = 108.1/0.27 = <b>400.4</b>（真实 400.43）、
    ///         日盘/月盘 角径比 = <b>1.029</b>（真实 1.029）—— <b>全部为真</b>。
    ///         被牺牲的只有绝对尺度，而绝对尺度不可观测。
    ///         ⚠️ <b>v0.27 及以前这里是 1.8R —— 比月亮（2.6R）还近，比值 0.69，方向都是反的。</b>
    ///         而本类的注释在同一段里又写着"平行光的含义就是太阳在无穷远"。
    ///         <b>声称一套、画另一套</b>；作者那句「比例不对」指的就是这里。</item>
    ///   <item>太阳的<b>光</b>用的是真正的平行光 —— 平行光等价于"太阳在无穷远"，
    ///         对蓝星这种尺度的行星来说真实的太阳<b>确实</b>近似无穷远。
    ///         但 v0.28 之前这句话只是<b>嘴上</b>成立：日盘画在 1.8R、落在月亮轨道 2.6R
    ///         <b>以内</b>，从<b>月亮</b>看"天上那个日盘"的方向与照它的平行光方向相差
    ///         <b>74.1°</b>（三维）/ <b>38.5°</b>（屏上）—— <b>月亮的亮面朝不到日盘</b>，
    ///         而那是<b>结构性</b>的，不是参数没调好。
    ///         放到 1012R 之后这个差降到 <c>atan(2.6/1012)</c> ≈ <b>0.15°</b>：
    ///         <b>缺陷消失，月相照旧。</b></item>
    ///   <item><b>而当年那个"两害相权"本身就是压缩的产物。</b>
    ///         当时试过把点光源放到日盘位置上，全周扫下来月亮在画面内的<b>每一个</b>位置
    ///         都被照到 <b>96%~99.8%</b>（<c>tools/shoot_expect.py --sweep</c>）——
    ///         月相整个消失，天上多出一张和日盘几乎一样的白饼。
    ///         根因是那个点光源在月亮轨道<b>以内</b>：从轨道内部照一颗在轨道上的球，必然近乎全亮。
    ///         <b>太阳一放到 1012R，点光源与平行光的差别只剩 0.15°，这个坑自动填平</b> ——
    ///         所以 v0.28 之后"日盘出框 vs 没有月相"不是取舍，是<b>伪命题</b>。</item>
    /// </list>
    ///
    /// ⚠️ 太阳的方位角是<b>反推</b>出来的（<see cref="SunPhaseDeg"/>），不是随手填的欧拉角。
    ///    太阳的方位角由相机初始朝向反推，所以改了相机的 Yaw/Pitch，太阳会自动跟着转。
    ///    但它取多少<b>不是审美问题，是几何问题</b>，见下面这一节。
    ///
    /// <b>φ = SunPhaseDeg 在 v0.28 之前是"取舍"，之后只剩一半 —— 另一半变成了恒等式。</b>
    ///
    /// φ 的定义是<b>在球心处</b>量的夹角（太阳、球心、相机三点）。太阳放到 1012R 之后已经
    /// <b>近似无穷远</b>，于是日盘的<b>屏上偏角</b> δ 不再由距离决定，而与 φ 成一条近恒等式
    /// （相机永远看着球心，视轴就是 −toCam）。**相机、蓝星、太阳三点成一个三角形**，
    /// δ、φ 是其中两个内角（分别在相机处、蓝星处），于是：
    /// <code>
    /// δ + φ = 180° − ε        ε = 三角形在**太阳**处的内角 = 太阳的视差角
    /// </code>
    /// ⚠️ <b>ε 通常小到可以忽略（1012R 时约 0.14°），但它不是 0，"恒等于 180"是错的写法。</b>
    ///    第一版就写成了恒等式，实测 179.86 打脸：那 0.14 不是噪声、不是误差，
    ///    是太阳的视差角，它随太阳推远而 →0。**能算出来的东西不要写成"恒等"。**
    ///    取证里用正弦定理 <c>sin ε = d_cam·sin φ / d_sun</c> 独立算一份来对照，
    ///    两条路必须吻合（0.05° 内），否则错的是别的地方。
    ///
    /// 于是"日盘出不出画"与"蓝星亮不亮"<b>被同一个角锁死，而且方向相反</b>：
    /// δ 要小于水平半视锥 <b>45.7°</b>（⟹ φ > 134.2°）日盘才进得来，
    /// 而那一刻球心亮度 <c>cos 134.2° = −0.70</c>、可见圆面只有
    /// <c>(1+cos134.2°)/2 ≈ 15%</c> 被照亮 —— <b>日盘在画面里 ⟺ 蓝星是暗的，二者互斥。</b>
    ///
    /// v0.27 那张"蓝星 78.7% 被照亮 <b>且</b> 日盘 36.8°"的画面，<b>只可能来自假近的太阳</b>：
    /// 1.8R 的日盘落在相机（3R）轨道<b>以内</b>，屏幕偏角才可能与相位角解耦。
    /// 作者已就此裁过：<b>接受日盘在第一屏退到镜头后面</b>；蓝星的照明一个字不动（φ 没变），
    /// 往夜面拖，日盘会从镜头后面升进画面。见 <see cref="SightSun"/>。
    ///
    /// <b>所以 φ 现在只管一件事：蓝星被照亮多少。</b>圆面中心（正对相机那一点）的入射角就是 φ，
    /// 亮度 ∝ cos φ。取 <b>55°</b> 是久经复核的值：
    /// <code>
    ///  φ     可见圆面被照亮   圆面中心亮度
    ///  25°       95.3%          90.6%
    ///  55°       78.7%          57.4%   ← 取它
    ///  85°       54.4%           8.7%
    /// </code>
    /// ⚠️ <b>第一版取的是 85°，是错的 —— 而且错得看不出来。</b>
    ///    当时的理由是"蓝星约 46% 被照亮，能看清大陆又有明暗界线"，
    ///    渲出来才发现那 46% 是<b>球面</b>被照亮，而<b>可见圆面中心只有 8.7% 亮度</b>：
    ///    正对镜头的那一大片是黑的，亮的只有边缘一弯月牙，大陆根本看不清 ——
    ///    而作者要的恰恰是"可以放大看到每一片大陆"。
    ///    <b>教训：算"被照亮多少"要算镜头看得见的那一块，不是整个球面。</b>
    ///
    /// ⚠️ <b>v0.27 那张表里「太阳屏上偏角」一列，连同它上面那个
    ///    <c>tan α = S·sinφ/(D − S·cosφ)</c> 的公式，在 v0.28 全部作废</b> ——
    ///    它们的前提是"太阳不在无穷远"。现值就是 <c>δ + φ = 180° − ε</c>，
    ///    φ=55° 对应 <b>δ≈124.9°，日盘在镜头背后</b>（ε 是太阳的视差角，约 0.14°，
    ///    所以旧写法 <c>δ = 180° − φ</c> 只差这 0.14° —— 差得少不等于是对的，
    ///    见类注释里那段）。
    ///    <b>这不是退步：从这一刻起，"日盘在哪"与"蓝星多亮"终于自洽了。</b>
    ///
    /// <b>φ 说完了，还有月亮的开场相位 <see cref="MoonStartAngleDeg"/> —— 这个也一样不能随手填。</b>
    /// 初值原本是 <c>0</c>（<c>_moonAngleRad</c> 的默认值），而 <b>θ=0 时月亮离视轴 54°，
    /// 在 60° 视锥之外 —— 播放器里根本没有月亮。</b>
    /// 更坏的是<b>它看不出来</b>：编辑器取证图里那个大白盘是<b>太阳</b>（离视轴 36.8°，落在画面右侧），
    /// 位置、大小都像个"月亮"，于是"图上有月亮"这个结论被写进了报告，
    /// 而真正的月亮一直不在画面里。<b>拍到的和以为拍到的，是两回事。</b>
    ///
    /// 判据不能靠手算：月轨有 5.1° 倾角、视锥又是竖直的（横向还宽 1.87 倍），
    /// 所以是用 <c>tools/moon_frame.py</c> 把几何复刻出来全周扫的 —— 那个脚本先与已渲出的图逐项对上
    /// （日盘 (2168,684) vs 实测 (2176,687)、蓝星视半径 19.5°），然后才用来选数。
    /// 扫描的结论：2.6R 轨道下月亮只有 <b>27%</b> 的时间在画面内，可见窗口是 θ∈[271°,319°]。
    /// 取 <b>294°</b> 是窗口里最平衡的一点 —— 离蓝星圆面 122 px、离日盘 123 px、月相 0.56，
    /// 且从启动起连续可见约 <b>114 秒</b>。
    ///
    /// ⚠️ <b>上面这一段的数字全部作废了，v0.27 已按新机位重扫，现值见 <see cref="MoonStartAngleDeg"/>。</b>
    ///    原因不是月亮变了，是<b>相机变了</b>：v0.26 把默认机位改成对准最大那块大陆
    ///    （Yaw −35→5.39、Pitch 18→−11.79），而 <c>tools/moon_frame.py</c> 的输入
    ///    <b>还写着旧值</b>，于是 294° 之后再没被复核过。
    ///    在新机位下它是<b>错的</b>：月亮离视轴 9.2°，比蓝星视半径 19.5° 还小，
    ///    且比蓝星远 —— <b>整个藏在蓝星背后</b>，开局那一眼天上只有太阳。
    ///    <b>教训：取证脚本的输入一旦不跟着产品走，它的结论就会静悄悄地过期</b>，
    ///    而过期的结论与新鲜的结论在纸上长得一模一样。这个脚本现在把输入写在最上面，
    ///    并靠"预测的日盘像素位置必须与实测截屏对得上"来自检。
    ///
    /// ⚠️ 月相 0.56（接近半月）是<b>特意</b>选在这个窗口而不是另一个（θ∈[149°,197°]，月相 0.98）的。
    ///    满月是一张<b>均匀的白饼</b> —— 与日盘长得几乎一样，画面会变成"天上有两个太阳"；
    ///    半月有明确的明暗界线，一眼能看出"这是月亮"，也才看得出月相确实在变。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SunMoonSystem : MonoBehaviour
    {
        [Header("蓝星")]
        [Tooltip("星球半径（Unity 单位）。留空则取 PlanetBootstrap.Radius。")]
        public float PlanetRadius = 1000f;

        [Header("太阳")]
        [Tooltip("太阳离蓝星中心的距离（倍半径）。**不是随手压的数**：\n"
               + "真实日地 23 481R、月地 60.34R；月亮已压到 2.6R（压缩 23.19 倍），\n"
               + "太阳按**同一个倍数**压缩 ⟹ 23 481 ÷ 23.19 = 1012R。\n"
               + "于是日:月 距离比 = 389.2，与真实的 389.17 相同。\n"
               + "⚠️ 改这个数就要按比例改月亮，否则比例又错了 —— 两者共用同一个压缩倍数。")]
        public float SunDistanceFactor = 1012f;

        [Tooltip("太阳视面半径（倍星球半径）。真实日/月半径比 = 695700/1737.4 = 400.43，\n"
               + "乘本工程的月半径 0.27 ⟹ 108.1。于是日盘角径是月盘的 1.029 倍，\n"
               + "与真实的 1.029 相同 —— 日食的几何因此也是真的。")]
        public float SunRadiusFactor = 108.1f;

        [Tooltip("太阳、球心、相机 三点在**球心处**的夹角（度）。**默认 55**，不是 85。\n"
               + "⚠️ v0.28 起太阳在 1012R（≈无穷远），这个角不再决定日盘会不会出画（见类注释），\n"
               + "它现在只管一件事：蓝星被照亮多少。")]
        public float SunPhaseDeg = 55f;

        [Header("月亮")]
        [Tooltip("月地距离（倍星球半径）。真实值 60.3，此处压缩以便同框。")]
        public float MoonOrbitRadiusFactor = 2.6f;

        [Tooltip("月/地直径比。**真实值 0.27**，不改。")]
        public float MoonRadiusFactor = 0.27f;

        [Tooltip("轨道面对赤道的倾角（度）。**真实值 5.1**，不改。")]
        public float MoonInclinationDeg = 5.1f;

        [Tooltip("恒星月长度（星球日）。**真实值 27.32**，不改。")]
        public float SiderealMonthDays = 27.32f;

        [Tooltip("开场时月亮走到公转相位的哪一度。见类注释里「为什么要有这个初值」。\n"
               + "⚠️ 334° 是按 v0.26/v0.27 的相机机位重扫出来的；旧的 294° 是旧机位下的解，"
               + "在新机位下月亮正好藏在蓝星背后。改机位就要重扫 tools/moon_frame.py。")]
        public float MoonStartAngleDeg = 334f;

        private Transform _planet;
        private Transform _moon;
        private Transform _sunDisc;
        private Light _sunLight;
        private PlanetBootstrap _spin;

        private Vector3 _sunDir;        // 由蓝星指向太阳的单位向量（世界系）
        private float _moonAngleRad;    // 当前公转相位
        private float _moonAngularSpeed; // 弧度/秒
        private float _moonOrbitRadius;
        private float _moonRadius;

        /// <summary>给 HUD 用的一行状态。行星场景里才有意义。</summary>
        public string StatusLine
        {
            get
            {
                if (_sunLight == null) return "";
                // 月龄取自"离角"（日-地-月 的夹角）—— 这是**轨道上的事实**。
                // 本模型里太阳方向在世界系中是**固定的**（蓝星不绕日公转），
                // 所以恒星月与朔望月在这里是同一个周期，不像真实天体差 2.2 日。
                // 这是刻意的简化 —— 让"关系"保持可核对，而不是多加一层几乎看不出的修正。
                double theta = Vector3.Angle(_sunDir, MoonDirection());
                double age = theta / 360.0 * SiderealMonthDays;
                // ⚠️ 受照比例与月龄**口径不同**：前者是相机看到的（见 MoonLitFraction），
                //    后者是轨道位置给的。真实情形下二者满足教科书那条关系，
                //    本场景<b>不满足</b> —— 因为相机在 3.0R、月亮在 2.6R，
                //    观测者离月亮太近，不能当"站在球心"（实测 56% vs 28%）。
                //    两个数都留着、并注明口径，比只留一个让人猜要诚实。
                return $"太阳 {SunPhaseDeg:F0}° · 月亮受照 {MoonLitFraction:P0}（屏幕所见）"
                     + $" · 月龄 {age:F1} 日";
            }
        }

        private Vector3 MoonDirection() =>
            _moon != null ? (_moon.position - _planet.position).normalized : Vector3.up;

        /// <summary>月亮当前位置（世界系）。取证时用来判断它在不在画面里。</summary>
        public Vector3 MoonPosition => _moon != null ? _moon.position : _planet.position;

        /// <summary>太阳（看得见的那个日盘）当前位置（世界系）。</summary>
        public Vector3 SunPosition =>
            _sunDisc != null ? _sunDisc.position : _planet.position;

        /// <summary>蓝星球心（世界系）。</summary>
        public Vector3 PlanetPosition => _planet != null ? _planet.position : transform.position;

        /// <summary>由蓝星指向太阳的单位向量（世界系）。平行光的方向就是它。</summary>
        public Vector3 SunDirection => _sunDir;

        /// <summary>月亮当前公转相位（度）。</summary>
        public float MoonAngleDeg => _moonAngleRad * Mathf.Rad2Deg;

        /// <summary>
        /// 从相机看，月亮此刻是<b>哪一种"看不见"</b>。
        ///
        /// <b>为什么要分成四种，而不是回答一个是非题。</b>
        /// <c>"在不在画面里"</c> 这个问法是本工程栽过的那个跟头本身：
        /// 画面里没有月亮有<b>三种完全不同的原因</b>，而它们在截图上长得<b>一模一样</b> ——
        /// 没被造出来（组件没跑 <c>Awake</c>）、转出视锥了（会自己转回来）、
        /// <b>以及藏在蓝星背后</b>。只回一个 false，读的人就只能猜，
        /// 而"猜"正是当初把太阳认成月亮、并把它写进结论的那条路。
        ///
        /// ⚠️ <see cref="BehindPlanet"/> 这一态是 v0.27 补上的：
        ///    旧机位下月亮要么在画面里、要么在画面外，两态够用；
        ///    新机位（见类注释）下它会<b>整个躲进蓝星圆面之后</b> ——
        ///    视口判定说"在画面里"，屏幕上却一个像素都没有。
        ///    这类"读数与画面不一致、而两边都不报错"的毛病，
        ///    在本工程是<b>复发型</b>的（v0.23 差分哨兵、v0.25 "太阳位移 0 像素"都是它）。
        /// </summary>
        public enum MoonSighting
        {
            /// <summary>根本不存在 —— <c>Awake</c>/<c>EnsureBuilt</c> 没跑，或场景里就没这个组件。</summary>
            NotBuilt,
            /// <summary>造出来了，但在视锥之外。</summary>
            OffScreen,
            /// <summary>在视锥之内，但被蓝星本体挡住 —— 画面上看不见。</summary>
            BehindPlanet,
            /// <summary>真的看得见。</summary>
            Visible,
        }

        /// <summary>判月亮此刻的处境。几何全部取自本组件（半径、月位），不另写一份。</summary>
        public MoonSighting SightMoon(Camera cam)
        {
            if (_moon == null || cam == null) return MoonSighting.NotBuilt;

            // 判据用投影到 viewport，而不是"离视轴多少度"——视锥是竖直的、横向还宽 1.87 倍，
            // 与 SceneSwitcher 原来那行注释同一个理由。
            Vector3 v = cam.WorldToViewportPoint(_moon.position);
            if (v.z <= 0f || v.x <= 0f || v.x >= 1f || v.y <= 0f || v.y >= 1f)
                return MoonSighting.OffScreen;

            return Occluded(cam.transform.position, _moon.position)
                 ? MoonSighting.BehindPlanet
                 : MoonSighting.Visible;
        }

        /// <summary>相机→目标 的线段是否穿过蓝星本体。与 <c>tools/moon_frame.py</c> 的 <c>occluded()</c> 同式。</summary>
        private bool Occluded(Vector3 from, Vector3 to)
        {
            Vector3 centre = _planet.position;
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 1e-4f) return false;
            Vector3 u = d / len;
            float t = Vector3.Dot(centre - from, u);
            // 球心在射线起点背后、或落在目标之后 —— 都谈不上"挡在中间"。
            if (t <= 0f || t >= len) return false;
            float miss = (centre - (from + u * t)).magnitude;
            return miss < PlanetRadius;
        }

        /// <summary>
        /// 太阳此刻的处境。与 <see cref="SightMoon"/> 同一套思路，但多一态 ——
        /// 因为 v0.28 之后太阳的<b>常态</b>就是"在镜头背后"。
        ///
        /// <b>为什么必须说出来。</b>太阳移到 1012R 之后，默认第一屏的日盘<b>必然</b>消失
        /// （见类注释里那条 <c>δ + φ = 180° − ε</c>：日盘在画面里 ⟺ 蓝星是暗的）。
        /// 而"天上没有太阳"与"太阳根本没被造出来"在截图上<b>长得一模一样</b> ——
        /// 本工程在月亮上已经栽过一次同款的跟头（见 <see cref="MoonSighting"/>）。
        /// 所以这里不是补一个布尔，而是把这个球此刻到底在哪<b>读出来</b>。
        /// </summary>
        public enum SunSighting
        {
            /// <summary>根本没造出来 —— <c>Awake</c>/<c>EnsureBuilt</c> 没跑，或场景里就没这个组件。</summary>
            NotBuilt,
            /// <summary>在镜头<b>背后</b>。默认机位就是这一态（φ=55° ⟹ 偏角 124.9°），<b>不是 bug</b>。</summary>
            BehindCamera,
            /// <summary>在镜头前方、视锥之内，但被蓝星本体挡住。</summary>
            BehindPlanet,
            /// <summary>在镜头前方，但在视锥之外。</summary>
            OffScreen,
            /// <summary>真的看得见。需要 φ > 134.2°，而那一刻蓝星是暗的。</summary>
            Visible,
        }

        /// <summary>判太阳此刻的处境。</summary>
        public SunSighting SightSun(Camera cam)
        {
            if (_sunDisc == null || cam == null) return SunSighting.NotBuilt;

            Vector3 v = cam.WorldToViewportPoint(_sunDisc.position);
            // ⚠️ z ≤ 0 = 落在镜头平面**之后**。此刻 x/y 仍然有值（而且是镜像过的），
            //    所以**不能只看 x/y 判"出画"** —— "在背后"与"在画面外"必须分开报，
            //    否则默认机位会一路被读成"转到画面外了，会转回来"，而它其实是钉在背后的。
            if (v.z <= 0f) return SunSighting.BehindCamera;
            if (v.x <= 0f || v.x >= 1f || v.y <= 0f || v.y >= 1f) return SunSighting.OffScreen;

            return Occluded(cam.transform.position, _sunDisc.position)
                 ? SunSighting.BehindPlanet
                 : SunSighting.Visible;
        }

        /// <summary>
        /// 太阳偏离视轴多少度。<b>出画、乃至退到镜头背后，这个量都仍然有定义</b> ——
        /// 所以它才是唯一能把整条拨动轨迹读完的读数（屏幕像素在 z≤0 之后就没有意义了）。
        /// 取证靠它验 <c>δ + φ = 180° − ε</c>：φ=55° 应当读出 <b>124.86°</b>
        /// （不是 125.00° —— 差的 0.14° 就是太阳的视差角 ε，见类注释）。
        /// </summary>
        public float SunOffAxisDeg(Camera cam)
        {
            if (_sunDisc == null || cam == null) return -1f;
            Vector3 d = _sunDisc.position - cam.transform.position;
            if (d.sqrMagnitude < 1e-6f) return -1f;
            return Vector3.Angle(cam.transform.forward, d);
        }

        /// <summary>
        /// 蓝星的相位角 ψ：<b>从相机看</b>，太阳方向与"相机→球心"方向的夹角（度）。
        /// ψ = 0 是相机正对太阳（球是满的），ψ = 180° 是相机背着太阳（球全黑）。
        ///
        /// 这个量是 <see cref="SunOffAxisDeg"/> 的<b>反面</b>，两者之和是
        /// <c>180° − ε</c>（相机、蓝星、太阳三点构成的三角形内角和；
        /// ε 是太阳那个角，即太阳的视差角，1012R 下约 0.14°）。
        /// ⚠️ <b>不是"恒等于 180"</b> —— 见类注释：第一版就是这么写的，实测 179.86 打脸。
        /// 相机永远看着球心（<c>OrbitCamera</c> 把 <c>transform.rotation</c> 钉在机架上），
        /// 于是"日盘偏离视轴多少"与"球心处日-地-相机夹角多少"大体互补。
        /// 取证就是靠这一条把 <b>太阳真的在跟着拨</b> 与 <b>世界被冻住</b> 分开的：
        /// 后者会让 δ 与 ψ 一起不动，从而破坏不了什么 —— 所以必须<b>两个一起报</b>。
        /// </summary>
        public float ViewedPhaseDeg(Camera cam)
        {
            if (cam == null) return -1f;
            Vector3 toSun = _sunDir;                                   // 球心 → 太阳（单位向量）
            Vector3 toCam = cam.transform.position - PlanetPosition;
            if (toCam.sqrMagnitude < 1e-6f) return -1f;
            return Vector3.Angle(toSun, toCam);
        }

        /// <summary>
        /// 月亮此刻<b>在画面上</b>被照亮的比例（0 = 新月，1 = 满月）。
        ///
        /// ⚠️ 观测者是<b>相机</b>，不是球心。这在别的场景里是四舍五入的差别，
        ///    在本场景里却是<b>一倍</b>的差别，必须说清楚：
        ///    相机在 3.0R、月亮在 2.6R，两者同一个量级，"从球心看"与"从相机看"
        ///    根本不是一回事。v0.25 实测：相机看到 <b>56%</b>，而按"球心处夹角"的
        ///    老式子只有 <b>28%</b> —— HUD 原来写的是后者，于是<b>读数与画面差了一倍</b>。
        ///
        ///    而本类的注释里恰好写着"不可能出现读数说满月、画面上是月牙"。
        ///    那句话本身没错，<b>错的是式子取错了观测点</b> —— 换成相机视角之后，
        ///    那句话才重新成立。凡是能读出与画面同源的量，就该由同一个式子给出。
        ///
        ///    这个数怎么核：<c>tools/shoot_check.py</c> 在月盘上沿受光方向切一条
        ///    <b>中位数</b>亮度剖面（中位数是为了压掉月面那些程序生成的斑块 ——
        ///    按"亮度过半"数像素的第一版量得 21%，是错的），
        ///    再取"离开暗底 15% 动态范围"处为终止线，量得 <b>50%~55%</b>，
        ///    与这里的 56% 对得上（分箱 10%，取整误差就在这个量级）。
        /// </summary>
        public float MoonLitFraction
        {
            get
            {
                if (_moon == null) return 0f;

                // 平行光：光线处处同向，所以"来光方向"就是从月亮指向太阳的方向。
                Vector3 toLight = _sunDir;

                var cam = Camera.main;
                Vector3 toEye = cam != null
                    ? (cam.transform.position - _moon.position)
                    : (_planet.position - _moon.position);   // 取景前没有相机：退回球心视角
                if (toEye.sqrMagnitude < 1e-6f) return 0f;

                float alpha = Vector3.Angle(toLight, toEye);
                return (1f + Mathf.Cos(alpha * Mathf.Deg2Rad)) * 0.5f;
            }
        }

        /// <summary>
        /// 取证用：把月亮摆到指定公转相位（度）。<b>运行时不用它</b> ——
        /// 运行时的相位由 <see cref="Update"/> 按恒星月自行推进。
        ///
        /// 存在的理由：60° 视锥只罩得住月亮约四分之一行程，
        /// 按下快门那一刻它多半在画面外，取证图里就会"没有月亮"——
        /// 而那不是月亮不存在，是<b>没拍到</b>。
        /// </summary>
        public void SetMoonAngleDeg(float deg)
        {
            _moonAngleRad = deg * Mathf.Deg2Rad;
            PlaceMoon();
        }

        private void Awake()
        {
            Resolve();
            BuildSunDirection();
            BuildSun();
            BuildMoon();
        }

        /// <summary>
        /// 解析场景引用并把派生尺寸算好。
        ///
        /// <b>为什么单独抽出来：</b>编辑器在<b>非 Play</b> 状态下<b>根本不会调用 Awake</b>
        /// （本组件不是 <c>ExecuteAlways</c>）。而取证渲染（<c>PreviewRenderer</c>）
        /// 正是在非 Play 状态下开的场景 —— 于是第一版预览图里<b>太阳和月亮都不存在</b>，
        /// 画面只有一颗孤零零的蓝星，而 Unity 一句错都不报。
        /// ⚠️ 这个坑很值得记住：<b>"Play 下对"不等于"渲得出证据"</b>，
        ///    两者走的是不同的生命周期，而失败的样子只是"少了两样东西"。
        /// </summary>
        private void Resolve()
        {
            if (_planet != null) return;   // 幂等：Awake 已跑过就不要再解一遍

            var planetGo = GameObject.Find("Planet");
            _planet = planetGo != null ? planetGo.transform : transform;
            _spin = _planet.GetComponent<PlanetBootstrap>();
            if (_spin != null && _spin.Radius > 1f) PlanetRadius = _spin.Radius;

            _moonOrbitRadius = MoonOrbitRadiusFactor * PlanetRadius;
            _moonRadius = MoonRadiusFactor * PlanetRadius;

            // 公转角速度由"恒星月"折算。自转周期 == 公转周期（潮汐锁定），
            // 在 Update 里用同一句话完成 —— 见那里的注释。
            float secondsPerDay = _spin != null ? _spin.SecondsPerDay : 60f;
            _moonAngularSpeed = Mathf.PI * 2f / (SiderealMonthDays * secondsPerDay);

            // 开场相位。放在 Resolve 里，是为了让 Awake 与 EnsureBuilt 两条路拿到同一个初值
            // （与上面尺寸、角速度同源的理由一样：两处各写一份，迟早会漂开）。
            _moonAngleRad = MoonStartAngleDeg * Mathf.Deg2Rad;
        }

        /// <summary>
        /// 编辑器取证用：确保太阳与月亮已经建出来。<b>幂等</b>。
        ///
        /// Play 模式下 <see cref="Awake"/> 已经建过，这里是空操作；
        /// 非 Play 模式下它才是唯一的建造入口。两条路走<b>同一份</b>建造代码，
        /// 所以取证图与运行时画面是同一个东西，不会各画各的。
        /// </summary>
        public void EnsureBuilt()
        {
            Resolve();
            if (_sunDisc == null)
            {
                BuildSunDirection();
                BuildSun();
            }
            if (_moon == null) BuildMoon();
        }

        /// <summary>
        /// 把太阳挪到指定方向 —— <b>光与看得见的日盘一起动</b>。
        ///
        /// ⚠️ 只转灯不挪日盘是<b>看得见的假</b>：画面上一侧被照亮，
        ///    而太阳球挂在另一边。这两者本来就该是同源的，
        ///    所以它们必须由同一个方法一起改，不能各改各的。
        /// 供 <c>PreviewRenderer</c> 的逐大陆巡览使用（那是取证手段，不是运行时行为）。
        /// </summary>
        public void SetSunDirection(Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-6f) return;
            _sunDir = dir.normalized;

            if (_sunLight != null)
                _sunLight.transform.rotation = Quaternion.LookRotation(-_sunDir, Vector3.up);
            if (_sunDisc != null)
                _sunDisc.position = _planet.position + _sunDir * (SunDistanceFactor * PlanetRadius);
        }

        /// <summary>
        /// 按<b>当前</b>相机朝向重算太阳方位，并把新方位同步到灯与日盘上。
        ///
        /// 与开场那一次用的是<b>同一个算式</b>（<see cref="BuildSunDirection"/>），
        /// 只是时机不同 —— 所以不是"另写一套"，而是把既有那一套再跑一遍。
        ///
        /// <b>为什么要这个入口</b>：取证要把相机摆到某块大陆上再拍（逐大陆巡览、
        /// 逐年份的时间轴取证）。相机一摆，若太阳还留在开场那一次算出的方位上，
        /// 被拍的那块大陆多半正落在<b>夜半球</b> —— 出来的是一整块黑，
        /// 而那不是"这里没有文明"，是"没拍到"。本工程在月亮上已经栽过一次同款的跟头
        /// （图里没有月亮，被当成月亮不存在），故这里不靠"记得先转太阳"，
        /// 而是给一个把两件事绑在一起的方法。
        ///
        /// ⚠️ 必须<b>连看得见的日盘一起挪</b>：只转灯的话，画面上会出现
        ///    「这边被照亮、太阳球却挂在那边」的自相矛盾 —— 而那正是本类要维持的那条同一性被破坏的样子。
        /// </summary>
        public void RealignSunToCamera()
        {
            BuildSunDirection();
            // BuildSunDirection 只改 _sunDir；灯与日盘归 SetSunDirection 管。
            SetSunDirection(_sunDir);
        }

        /// <summary>
        /// 由相机<b>初始</b>位置反推太阳方位：从「球心 → 相机」这个方向起，绕屏幕右方掰开
        /// <see cref="SunPhaseDeg"/>。
        ///
        /// 只在 Awake 里算一次。之后用户右键转视角、左键拨地球，太阳都<b>不动</b> ——
        /// 太阳本来就该钉在天上。世界不动、球在转，正是"拨地球仪"与"转镜头"的区别所在。
        ///
        /// ⚠️ <b>基准方向是「球心 → 相机」，不是「相机 → 球心」。</b>
        ///    <see cref="SunPhaseDeg"/> 的定义是<b>在球心处</b>量的夹角（太阳、球心、相机三点），
        ///    所以基准必须是球心指向相机的那一支。这两支<b>只差一个负号</b>，
        ///    用反了不会报错、不会崩，实际夹角静悄悄地变成 <c>180° − φ</c>：
        ///    取 55° 得到 125° —— 太阳跑到蓝星<b>背面</b>去，日盘正压在球体边缘上，
        ///    （此处的 125° 是<b>相位角</b>走反了，与默认机位下日盘的偏角 124.86° 只是
        ///      数值上碰巧接近、毫无关系 —— 两个"125"别互相印证，那是错觉。）
        ///    而整颗球是黑的（圆面中心亮度 <c>cos 125° = −0.57</c>，中心落在夜半球）。
        ///    <b>症状只会表现为"画得有点暗"，从暗想到"夹角差了 180°"隔着好几步</b>，
        ///    第一版就是这么错的，白查了一轮。改动这里之后请务必用
        ///    <c>PreviewRenderer</c> 出的图复核：日盘应当<b>离开球体边缘一段距离</b>，
        ///    且球面正对镜头的那一块应当是亮的。
        /// </summary>
        private void BuildSunDirection()
        {
            var cam = Camera.main;

            // 球心 → 相机。相机缺席时退回一个固定方向，好让编辑器批处理也能出图。
            Vector3 toCam = cam != null
                ? (cam.transform.position - _planet.position)
                : Vector3.forward;
            if (toCam.sqrMagnitude < 1e-6f) toCam = Vector3.forward;   // 相机正落在球心，无方向可言
            toCam.Normalize();

            // 掰开的方向取屏幕右方：相机前向 = −toCam，故 右 = cross(世界上方, 前向) = cross(上方, −toCam)。
            // 取 +right 而不是 −right，太阳才落在画面<b>右侧</b>（与"球在左、光从右来"的观感一致）。
            Vector3 right = Vector3.Cross(Vector3.up, -toCam);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;     // 相机正对极点时的退化情形
            right.Normalize();

            float rad = SunPhaseDeg * Mathf.Deg2Rad;
            _sunDir = (toCam * Mathf.Cos(rad) + right * Mathf.Sin(rad)).normalized;
        }

        private void BuildSun()
        {
            // 光源：优先接管场景里已有的那盏平行光（PlanetSceneBuilder 会先建一盏）
            _sunLight = FindFirstObjectByType<Light>();
            if (_sunLight == null)
            {
                var go = new GameObject("Sun");
                _sunLight = go.AddComponent<Light>();
            }
            _sunLight.type = LightType.Directional;
            _sunLight.color = new Color(1.0f, 0.97f, 0.92f);
            _sunLight.intensity = 1.15f;
            // 光线方向 = 由太阳指向蓝星 = −_sunDir。平行光用 forward 表示传播方向。
            _sunLight.transform.rotation = Quaternion.LookRotation(-_sunDir, Vector3.up);
            // 光源本体不能挂在蓝星下面 —— 挂了就会跟着蓝星一起自转，太阳就"跑"了。
            _sunLight.transform.SetParent(null, true);

            // 看得见的那个太阳。自发光，不受任何光照影响。
            var disc = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            disc.name = "SunDisc";
            Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(null, true);
            disc.transform.position = _planet.position + _sunDir * (SunDistanceFactor * PlanetRadius);
            float r = SunRadiusFactor * PlanetRadius;
            disc.transform.localScale = Vector3.one * (r * 2f);   // Unity 的球直径 = 1
            disc.GetComponent<MeshRenderer>().sharedMaterial = BuildEmissiveMaterial(
                new Color(1.0f, 0.96f, 0.86f));
            _sunDisc = disc.transform;
        }

        private void BuildMoon()
        {
            var moon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            moon.name = "Moon";
            Destroy(moon.GetComponent<Collider>());
            // 同样不挂在蓝星下面：月亮绕蓝星转，不跟着蓝星自转（否则会一天转一圈）。
            moon.transform.SetParent(null, true);
            moon.transform.localScale = Vector3.one * (_moonRadius * 2f);

            var mr = moon.GetComponent<MeshRenderer>();
            mr.sharedMaterial = BuildMoonMaterial();

            _moon = moon.transform;

            // 尺寸与角速度已在 Resolve() 里算好 —— 那里是唯一的出处，
            // 免得 Awake 与非 Play 的 EnsureBuilt 两条路各算一份、慢慢漂开。
            PlaceMoon();
        }

        private void Update()
        {
            if (_moon == null) return;

            // 顺行 = 与蓝星自转同向。蓝星绕 +Y 用正的 Rotate，故这里也取正角速度。
            _moonAngleRad += _moonAngularSpeed * Time.deltaTime;
            if (_moonAngleRad > Mathf.PI * 2f) _moonAngleRad -= Mathf.PI * 2f;

            PlaceMoon();
        }

        private void PlaceMoon()
        {
            // 轨道面 = 蓝星赤道面（法线 +Y）绕 X 轴倾斜 5.1°。
            // 不自转蓝星的地轴跟着倾斜，是刻意的：见类注释里"哪些是简化的"。
            float inc = MoonInclinationDeg * Mathf.Deg2Rad;
            Vector3 orbitNormal = new Vector3(0f, Mathf.Cos(inc), -Mathf.Sin(inc));

            // 取轨道面内的一组正交基
            Vector3 u = Vector3.Cross(orbitNormal, Vector3.up).normalized;
            Vector3 w = Vector3.Cross(orbitNormal, u).normalized;

            Vector3 offset = (u * Mathf.Cos(_moonAngleRad) + w * Mathf.Sin(_moonAngleRad))
                             * _moonOrbitRadius;
            _moon.position = _planet.position + offset;

            // 潮汐锁定：永远同一面朝着蓝星。
            // 用 LookAt 而不是"自转角度 = 公转角度"来写，是因为前者把结论直接写在脸上 ——
            // 朝向由位置决定，公转走到哪，正面就跟到哪，不可能积累漂移。
            _moon.rotation = Quaternion.LookRotation(_planet.position - _moon.position,
                                                     orbitNormal);
        }

        // ══════════════════════════════════════════════════════════════
        //  材质
        // ══════════════════════════════════════════════════════════════

        private static Material BuildEmissiveMaterial(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "HumenSun" };

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", c);
            }
            return mat;
        }

        /// <summary>
        /// 月亮用**受光**材质 —— 这是月相能出现的前提。
        /// 若换成自发光，月亮会永远是个满圆，月相就没了，
        /// 而且看上去还挺好看，很难发现是错的。
        /// </summary>
        private static Material BuildMoonMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard")
                      ?? Shader.Find("Unlit/Texture");
            var mat = new Material(shader) { name = "HumenMoon" };
            var tex = BuildMoonTexture();
            mat.mainTexture = tex;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.05f);
            return mat;
        }

        /// <summary>
        /// 一张带斑点的小图，纯程序生成。
        ///
        /// <b>为什么要纹理：</b>潮汐锁定本身是看不见的 —— 一颗纯色球，
        /// 无论锁没锁、怎么转，画面上都一模一样，"永远同一面朝我们"这句话
        /// 就永远没法被验证，也没法被看见。有了斑点，锁定的月面就会
        /// <b>始终朝着蓝星</b>，而不再自转；若哪天锁定被改坏，斑点会开始绕着月亮转，
        /// 一眼就能看出来。**能让约束可见，才算真的实现了约束。**
        /// </summary>
        private static Texture2D BuildMoonTexture()
        {
            const int w = 256, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var px = new Color32[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // 三个不同频率的余弦叠加当"环形山"：不需要真噪声，只要不重复的花纹。
                    double u = x / (double)w, v = y / (double)h;
                    double n =
                        0.50 * System.Math.Cos((u * 7.0 + v * 3.0) * Mathf.PI * 2.0) +
                        0.30 * System.Math.Cos((u * 13.0 - v * 7.0) * Mathf.PI * 2.0) +
                        0.20 * System.Math.Cos((u * 23.0 + v * 17.0) * Mathf.PI * 2.0);

                    // 压到 0.62~0.95 的灰阶：月亮是暗的，但不能暗到看不出形状
                    double g = 0.785 + 0.165 * System.Math.Tanh(n * 1.4);
                    byte b = (byte)Mathf.Clamp((float)(g * 255.0), 0f, 255f);
                    px[y * w + x] = new Color32(b, b, (byte)Mathf.Min(255, b + 3), 255);
                }
            }

            tex.SetPixels32(px);
            tex.Apply();
            tex.wrapModeU = TextureWrapMode.Repeat;
            tex.wrapModeV = TextureWrapMode.Clamp;
            return tex;
        }
    }
}
