using Humen.Planet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Humen.Editor
{
    /// <summary>
    /// 拨地球的<b>编辑期</b>取证：拨几次，把「日月在屏上动了」与「三维关系一点没变」一起量出来。
    ///
    /// <b>为什么要单开一个入口，而不是只在播放器里验。</b>
    /// 播放器取证（<see cref="PlayerCapture"/>）是最终判据，但它要重建播放器（分钟级），
    /// 而这一轮定的是<b>手感参数</b> —— 拨多少像素恰好能看出视差、又不把日盘推出画面，
    /// 是要试几次的。编辑期这条链路几十秒出一版读数，试稳了再用播放器复核一次。
    /// 两边跑的是<b>同一个</b> <see cref="GlobeDrag.VerifyOrbitParallax"/>，
    /// 所以"编辑器里过了、播放器里没过"这种事不可能发生 —— 它们不是两份实现。
    ///
    /// ⚠️ <b>非 Play 状态下 Awake 不会被调用</b>（本工程在 <see cref="SunMoonSystem"/> 上
    ///    已经栽过一次：预览图里日月整个不存在，而 Unity 一句错都不报）。
    ///    所以这里先把该解析的都解析出来 —— <c>SunMoonSystem.EnsureBuilt</c>、
    ///    <c>GlobeDrag.Resolve</c>、<c>OrbitCamera.Resolve</c> 都是幂等的，Play 下再调也无副作用。
    ///
    /// ⚠️ <b>必须挂一张 <see cref="RenderTexture"/> 再量。</b>
    ///    batchmode 下没有窗口，<c>Screen.width/height</c> 是 0 或垃圾值，
    ///    而"往右拨 50 px"这类位移全是从屏幕尺寸算出来的 —— 不挂 RT 的话，
    ///    量的是"拨了 0 px"，然后报告里日月一动不动，看起来像功能坏了。
    ///
    /// 用法：
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath &lt;proj&gt; \
    ///   -executeMethod Humen.Editor.OrbitForensics.Run -logFile &lt;log&gt;
    /// </code>
    /// </summary>
    public static class OrbitForensics
    {
        private const string ScenePath = "Assets/Scenes/Planet.unity";
        private const int Width = 1280, Height = 720;

        /// <summary>拨的位移（占屏高的比例）。720p 下约 22 / 50 / 86 px。</summary>
        private static readonly float[] DragFractions = { 0.03f, 0.07f, 0.12f };

        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid()) { Debug.LogError($"[Humen] 打不开场景 {ScenePath}"); return; }

            var cam = Object.FindFirstObjectByType<Camera>();
            var drag = Object.FindFirstObjectByType<GlobeDrag>();
            var sky = Object.FindFirstObjectByType<SunMoonSystem>();
            if (cam == null || drag == null || sky == null)
            {
                Debug.LogError("[Humen] 场景缺相机/GlobeDrag/SunMoonSystem，拨球取证跑不了。");
                return;
            }

            var orbit = cam.GetComponent<OrbitCamera>();
            if (orbit == null)
            {
                Debug.LogError("[Humen] 相机上没有 OrbitCamera —— 拨的会是球而不是相机，"
                             + "日月必然纹丝不动。这正是 v0.27 要修掉的那个样子。");
                return;
            }

            // 没有窗口，就得自己给一块画布。见类注释里那条 ⚠️。
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;

            try
            {
                sky.EnsureBuilt();     // 非 Play 下 Awake 没跑过，日月还不存在
                drag.Resolve();
                orbit.Resolve();

                Debug.Log($"[Humen] ══ 拨球取证 ══ 画布 {cam.pixelWidth}x{cam.pixelHeight} · "
                        + $"相机 {cam.transform.position} · 距离倍率 {orbit.DistanceFactor:F2}");

                // 三档位移各拨一次。看的是"日月动的幅度差"随位移怎么长 ——
                // 只拨一档的话，恰好那一档量出两个相等的数也看不出来。
                foreach (float f in DragFractions)
                {
                    var from = new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f);
                    var to = from + new Vector2(cam.pixelHeight * f, 0f);
                    Debug.Log(drag.VerifyOrbitParallax(from, to));
                }

                ReportSighting(cam, sky);
                ReportSunSighting(cam, sky);
                ReportSunMoonAlignment(sky);
                ReportSweep(cam, drag, sky);
                ReportReset(cam, drag, orbit);
                ReportCounterfactual(cam, drag, sky, orbit);   // ⚠️ 必须最后跑：它会把 OrbitCamera 拆掉
            }
            finally
            {
                cam.targetTexture = prevTarget;
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            Debug.Log("[Humen] ══ 拨球取证结束 ══");
        }

        /// <summary>
        /// 连着拨十几下会怎样 —— 也就是<b>玩家真正会做的事</b>。
        ///
        /// 单拨一次的读数证明不了"用起来没问题"：作者不会只拨 50 px，
        /// 他会一直拨到把球转过去看背面。
        ///
        /// ⚠️ <b>v0.28 改写了这里的理由，结论反而更强。</b>转的是<b>相机</b>、世界一动不动
        /// （见 <see cref="GlobeDrag"/>），所以相机一绕球，太阳与视轴的夹角 <c>δ</c>
        /// 就必然从 <c>180° − φ − ε</c> 一路扫下去（≈124.9°），直到日盘从镜头后面升进画面。
        /// 旧注释说的"太阳在 1.8R、相机在 3R，相机可能绕到太阳轨道外侧去"已经不对了
        /// （1012R 之外相机去不了），但<b>日盘照样会扫过整片天</b> ——
        /// 靠的不是太阳近，是相机在转。默认机位（δ = 124.9°）日盘在镜头背后，这是几何，不是 bug。
        /// <b>得先看见它长什么样</b>，才知道要不要像作者说的那样"搞不定就把日月去掉"。
        ///
        /// 这里每拨一下报一次日月离视轴的角度：<b>角度是在出画、乃至退到镜头背后之后仍然有定义的量</b>
        /// （见 <see cref="GlobeDrag.VerifyOrbitParallax"/> 里的说明），所以整条轨迹都读得出来 ——
        /// 屏上像素在 <c>z ≤ 0</c> 之后就没有意义了，这也正是<b>不能用"日盘动了几像素"当判据</b>的原因。
        /// </summary>
        private static void ReportSweep(Camera cam, GlobeDrag drag, SunMoonSystem sky)
        {
            drag.ResetGlobe();

            var from = new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f);
            var to = from + new Vector2(cam.pixelHeight * 0.10f, 0f);   // 720p 下约 72 px

            var sb = new System.Text.StringBuilder();
            sb.Append("[Humen] 连拨扫掠（每下 72 px，一路往同一方向拨）\n");
            // ⚠️ 每行必须<b>同时</b>报 δ（日盘离视轴）与 ψ（球心处相位角），因为
            //    这台相机「永远看着球心」，两点加上太阳构成三角形，于是
            //    **δ + ψ = 180° − ε**（ε = 相机-蓝星-太阳三角形在**太阳**处的内角，
            //    也就是太阳的视差角，1012R 下约 0.14°，无穷远才是 0）。
            //    只报 δ 会把「太阳在跟着拨」与「世界被冻住」印成同一串数 ——
            //    两者 δ+ψ 都接近 180。**所以还要报 cos ψ**：它是蓝星的亮度，
            //    太阳在跟着拨时它会一路变，世界冻住时它一个数都不会动。
            sb.Append("      δ=日盘离视轴  ψ=球心相位角  (δ+ψ = 180 − ε，ε 是太阳视差角 ≈0.14°，cos ψ 是蓝星亮度)\n");
            for (int i = 1; i <= 12; i++)
            {
                drag.SimulateDrag(from, to);

                Vector3 sun = sky.SunPosition, moon = sky.MoonPosition;
                float d = OffAxis(cam, sun), psi = sky.ViewedPhaseDeg(cam);
                sb.Append($"  {i,2} 下：δ {d,6:F2}° · ψ {psi,6:F2}° · ε {180f - d - psi,5:F2}°"
                        + $" · cos ψ {Mathf.Cos(psi * Mathf.Deg2Rad),6:F3}"
                        + $" · 月亮离视轴 {OffAxis(cam, moon),6:F2}°"
                        + $" · 日盘 {DescribeSight(sky.SightSun(cam))}\n");
            }

            drag.ResetGlobe();
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// 开局那一眼，天上到底有没有月亮。
        ///
        /// <b>为什么要专门报这一条。</b>v0.26 改了默认机位之后，<see cref="SunMoonSystem.MoonStartAngleDeg"/>
        /// 的旧值 294° 让月亮正好<b>躲进蓝星圆面背后</b>：屏幕上一个月亮像素都没有，
        /// 而当时的两态 HUD 只会说「在画面里」—— <b>读数与画面不一致，两边都不报错</b>。
        /// 所以这里不看"有没有没被造出来"这类断言，直接把处境打出来：
        /// <b>只有 <c>Visible</c> 才算对</b>，<c>BehindPlanet</c>/<c>OffScreen</c> 都说明相位又该重扫了。
        ///
        /// ⚠️ <c>MoonStartAngleDeg</c> 是<b>序列化在场景里</b>的（<c>Planet.unity</c> 里写着字面值），
        ///    所以光改 C# 里的默认值<b>不生效</b> —— 这一条就是被这件事咬过才加的：
        ///    改完默认值跑取证，月亮还在老地方（9.19°），因为场景把 294 存着。
        /// </summary>
        private static void ReportSighting(Camera cam, SunMoonSystem sky)
        {
            var sight = sky.SightMoon(cam);
            Debug.Log($"[Humen] 开局月亮处境：{sight}（相位 {sky.MoonAngleDeg:F1}°）"
                    + $" —— 只有 Visible 才算对；"
                    + $"{(sight == SunMoonSystem.MoonSighting.Visible ? "通过" : "**不对，相位该重扫了**")}");
        }

        /// <summary>
        /// 开局那一眼，天上到底有没有太阳 —— 以及它<b>为什么</b>不在。
        ///
        /// v0.28 起默认机位下日盘<b>必然</b>在镜头背后（δ = 180° − φ − ε ≈ 124.86°），这是几何，
        /// 不是故障。但"画面里没有太阳"与"太阳压根没被造出来"在截图上长得一模一样，
        /// 所以这一条把处境连同偏角一起打出来。
        /// <b>通过的标准是 BehindCamera 且 δ ≈ 124.86°</b>（容差 0.2°，所以 124.9 与 124.86 都算过，
        /// 而 125.00 也在容差内 —— 这条<b>分不出</b>"太阳推远了"与"太阳还在无穷远"，
        /// 分它的是 ε 那一列）—— 报 OffScreen/NotBuilt，
        /// 或偏角对不上，那才是真出了事。
        /// </summary>
        private static void ReportSunSighting(Camera cam, SunMoonSystem sky)
        {
            float off = sky.SunOffAxisDeg(cam);
            float psi = sky.ViewedPhaseDeg(cam);
            float expect = 180f - sky.SunPhaseDeg;

            // ⚠️ δ + ψ **不是**恒等于 180 —— 是 180 减掉**太阳那个角** ε。
            //    相机-蓝星-太阳三点成一个三角形，δ、ψ、ε 分别是相机处、蓝星处、太阳处的内角，
            //    三角形内角和 180 ⟹ δ + ψ = 180 − ε。
            //    ε 就是太阳的**视差角**，太阳越远越小（1012R 时约 0.14°，无穷远时才是 0）。
            //    所以"恒为 180.00"是错的写法：第一版就是这么写的，实测 179.86 打脸。
            //    这里用正弦定理独立算一个 ε 出来对照 —— 两条路必须吻合，否则是别的地方错了。
            Vector3 toSun = sky.SunPosition - sky.PlanetPosition;
            Vector3 toCam = cam.transform.position - sky.PlanetPosition;
            float dSun = toSun.magnitude, dCam = toCam.magnitude;
            float epsBySine = dSun > 1e-3f
                ? Mathf.Asin(Mathf.Clamp(dCam * Mathf.Sin(psi * Mathf.Deg2Rad) / dSun, -1f, 1f)) * Mathf.Rad2Deg
                : 0f;
            float epsMeasured = 180f - off - psi;

            bool ok = Mathf.Abs(off - expect) < 0.2f
                   && Mathf.Abs(epsBySine - epsMeasured) < 0.05f;
            Debug.Log($"[Humen] 开局太阳处境：{sky.SightSun(cam)} · δ {off:F2}° · ψ {psi:F2}°"
                    + $" · ε(=180−δ−ψ) {epsMeasured:F3}°（正弦定理独立算 {epsBySine:F3}°）"
                    + $" —— ε 就是**太阳的视差角**，不是误差：δ+ψ = 180 − ε，太阳越远 ε 越小"
                    + $" —— 期望 δ = 180 − SunPhaseDeg({sky.SunPhaseDeg:F0}°) = {expect:F2}°"
                    + $" ⟹ {(ok ? "通过（在镜头背后是默认态，不是故障）" : "**不对**")}");
        }

        /// <summary>
        /// 那个「月亮亮面朝不到日盘」的旧缺陷，如今差多少度。
        ///
        /// v0.27 及以前日盘画在 1.8R、落在月亮轨道 2.6R <b>以内</b>，于是"从月亮看天上那个日盘"
        /// 的方向与照它的平行光方向相差 <b>74.1°</b>（三维）—— 月亮亮面朝不到日盘。
        /// 这是尺度问题、不是参数问题，design.md 曾把它记成"日盘出框 or 没有月相，二者必居其一"。
        /// 太阳移到 1012R 之后该差值降到 atan(2.6/1012) ≈ <b>0.15°</b>，那个二选一随之作废。
        ///
        /// ⚠️ 这一条必须<b>主动报</b>：「一个待裁项自己消失了」是个会被忘掉的结论，
        ///    而忘了它，下一轮就会有人照着旧 design.md 去"修"一个已经不存在的问题。
        /// </summary>
        private static void ReportSunMoonAlignment(SunMoonSystem sky)
        {
            Vector3 toSunFromMoon = sky.SunPosition - sky.MoonPosition;
            if (toSunFromMoon.sqrMagnitude < 1e-6f)
            {
                Debug.LogWarning("[Humen] 日盘与月亮重合，无法量夹角。");
                return;
            }
            float deg = Vector3.Angle(sky.SunDirection, toSunFromMoon);
            float bound = Mathf.Atan2(sky.MoonOrbitRadiusFactor, sky.SunDistanceFactor) * Mathf.Rad2Deg;
            Debug.Log($"[Humen] 月亮处「平行光方向 vs 指向日盘方向」夹角 {deg:F3}°"
                    + $"（v0.27 是 74.1° · 理论<b>上界</b> atan({sky.MoonOrbitRadiusFactor:F1}/{sky.SunDistanceFactor:F0}) = {bound:F3}°）"
                    + $" ⟹ {(deg < 0.5f ? "通过：月亮亮面朝得到日盘，「待裁 C」作废" : "**不对：日盘还压在近处**")}");
        }

        /// <summary>把太阳处境压成一个短词，专供扫掠表逐行打印。</summary>
        private static string DescribeSight(SunMoonSystem.SunSighting s)
        {
            switch (s)
            {
                case SunMoonSystem.SunSighting.NotBuilt:     return "**没造出来**";
                case SunMoonSystem.SunSighting.BehindCamera: return "镜头背后";
                case SunMoonSystem.SunSighting.BehindPlanet: return "被蓝星挡住";
                case SunMoonSystem.SunSighting.OffScreen:    return "画面外";
                default:                                     return "在画面里";
            }
        }

        /// <summary>世界点偏离视轴多少度。出画也照样有定义。</summary>
        private static float OffAxis(Camera cam, Vector3 world)
        {
            Vector3 d = world - cam.transform.position;
            if (d.sqrMagnitude < 1e-9f) return -1f;
            return Vector3.Angle(cam.transform.forward, d.normalized);
        }

        /// <summary>
        /// 复位键（F）到底复位了几样东西。
        ///
        /// <b>为什么要单独量。</b>"复位"看起来是一句话，实现上是<b>三件事</b>：
        /// 机架回开机位姿、星球自转回开机角度、惯性清零。漏掉任何一样，
        /// 画面都只是"看着差不多" —— 而差额恰好是"同一块大陆还歪着"那种不显眼的错。
        /// 所以这里先把三样都弄乱，再按一下，逐样报出差值。<b>三个数都该是 0。</b>
        ///
        /// ⚠️ 不能只比相机位置：机架绕球心转 180° 时位置确实变了，但<b>滚转</b>（绕视轴自转）
        ///    位置几乎不动、画面却整个歪了。所以位置与朝向要<b>分开报</b>。
        /// </summary>
        private static void ReportReset(Camera cam, GlobeDrag drag, OrbitCamera orbit)
        {
            var planet = Object.FindFirstObjectByType<PlanetBootstrap>();
            if (planet == null) { Debug.LogWarning("[Humen] 复位取证跳过：场景里没有 PlanetBootstrap。"); return; }

            Vector3 pos0 = cam.transform.position;
            Quaternion camRot0 = cam.transform.rotation;
            // 复位要回的是"开机那一刻"的星球姿态。此刻刚开过场景、还没拨过，就是它。
            Quaternion spin0 = planet.transform.rotation;

            // ── 弄乱 ────────────────────────────────────────────────
            var from = new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f);
            var to = from + new Vector2(cam.pixelHeight * 0.20f, cam.pixelHeight * 0.10f);
            drag.SimulateDrag(from, to);
            planet.transform.Rotate(0f, 137f, 0f, Space.Self);   // 自转也拧歪（137° 是个不好凑巧回到原位的角）

            float camMoved = Vector3.Distance(pos0, cam.transform.position);
            float camTurned = Quaternion.Angle(camRot0, cam.transform.rotation);
            float spinTurned = Quaternion.Angle(spin0, planet.transform.rotation);

            // ── 按一下 F 走的那条路 ─────────────────────────────────
            drag.ResetGlobe();

            Debug.Log($"[Humen] 复位取证：拨乱之后 —— 机架挪了 {camMoved:F3}、转了 {camTurned:F3}°、"
                    + $"星球自转拧了 {spinTurned:F3}°（三个数都应当明显不为 0，否则这一测是空的）");
            Debug.Log($"[Humen] 复位取证：按 F 之后 —— 机架位置差 {Vector3.Distance(pos0, cam.transform.position):F4} · "
                    + $"机架朝向差 {Quaternion.Angle(camRot0, cam.transform.rotation):F4}° · "
                    + $"星球自转差 {Quaternion.Angle(spin0, planet.transform.rotation):F4}°"
                    + $" · 距离倍率 {orbit.DistanceFactor:F4}（都应当是 0）");
        }

        /// <summary>
        /// <b>反事实：把"转相机"换回"转球"，本轮那批判据还抓得住吗？</b>
        ///
        /// 这是整轮验证里最容易糊弄过去的一条。<see cref="GlobeDrag.VerifyOrbitParallax"/> 印出来的
        /// 是一串"变了/没变"的读数，而这些读数<b>只有在实现错了的时候会真的翻脸</b>才算判据 ——
        /// 不然它就只是一段好看的日志。<see cref="SunMoonSystem"/> 的类注释把这条写成了原则
        /// （"一个指标在两种相反原因下同值，它就不是判据"），这里就是拿它去量自己。
        ///
        /// 做法：先按正常路径拨一次（转的是机架），再把相机上的 <see cref="OrbitCamera"/> <b>拆掉</b>，
        /// 于是 <see cref="GlobeDrag.Orbit"/> 走它的兜底分支 —— 转球，也就是 v0.25 的老行为 ——
        /// 然后用<b>同一段代码</b>再拨同样的位移，把两组读数并排摆出来。
        ///
        /// ⚠️ <b>计划里对这条的预判是错的，所以更要真跑。</b>
        ///    原话是"若'转相机'退化成'转球'，三维不变量（日地/月地/日-地-月夹角）会变"。
        ///    <b>不会变。</b>月亮是<b>不挂父节点</b>的（<c>BuildMoon</c> 里 <c>SetParent(null, true)</c>），
        ///    位置按 <c>_planet.position + offset</c> 在世界系里算，而转球改的是 <c>Planet.rotation</c>，
        ///    <b>一个世界坐标都不动</b>。所以三维不变量对"转球"这个反面是<b>瞎的</b> ——
        ///    它对"世界冻住"同样瞎（两者都是"什么都没动"）。
        ///    真正抓住这个反面的只有一条：<b>δ 与 ψ 必须变</b>。这条跑出来的作用就是把这个分工钉死。
        ///
        /// 拆掉组件<b>不存盘</b>，场景文件不动。
        /// </summary>
        private static void ReportCounterfactual(Camera cam, GlobeDrag drag, SunMoonSystem sky, OrbitCamera orbit)
        {
            var from = new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f);
            var to = from + new Vector2(cam.pixelHeight * 0.10f, 0f);   // 与 ReportSweep 同一档，便于对照

            drag.ResetGlobe();
            string withOrbit = Probe(cam, drag, sky, from, to);

            Object.DestroyImmediate(orbit);      // 兜底分支从这里开始生效；drag 里缓存的引用会变成 Unity 假空
            drag.ResetGlobe();
            string withoutOrbit = Probe(cam, drag, sky, from, to);

            Debug.Log("[Humen] ══ 反事实：把「转相机」换成「转球」（拆掉相机上的 OrbitCamera）══\n"
                    + $"  正常（转机架）：{withOrbit}\n"
                    + $"  反面（转球）　：{withoutOrbit}\n"
                    + "  ⟹ 判据分工：三维不变量对**两个**反面都是瞎的（转球与冻住都是「什么都没动」），\n"
                    + "     能把它俩分开的只有「δ 与 ψ 必须变」这一条。上面两行必须一行全在动、一行一动不动。\n"
                    + "  （反面那行的起点不是 124.86°，因为 OrbitCamera 已经拆了，ResetGlobe 复位不了机架 ——\n"
                    + "    所以两行的起点本来就不同，要横着比的是**Δ**，不是绝对值。）");
        }

        /// <summary>拨一次、报一组读数。只印会翻脸的那几个量。</summary>
        private static string Probe(Camera cam, GlobeDrag drag, SunMoonSystem sky, Vector2 from, Vector2 to)
        {
            Vector3 p0 = sky.PlanetPosition, s0 = sky.SunPosition, m0 = sky.MoonPosition;
            float d0 = OffAxis(cam, s0), psi0 = sky.ViewedPhaseDeg(cam);
            Vector3 cam0 = cam.transform.position;

            drag.SimulateDrag(from, to);

            Vector3 p1 = sky.PlanetPosition, s1 = sky.SunPosition, m1 = sky.MoonPosition;
            float d1 = OffAxis(cam, s1), psi1 = sky.ViewedPhaseDeg(cam);

            return $"δ {d0:F2}°→{d1:F2}°（Δ{d1 - d0:+0.00;-0.00}）"
                 + $" · ψ {psi0:F2}°→{psi1:F2}°（Δ{psi1 - psi0:+0.00;-0.00}）"
                 + $" · cos ψ {Mathf.Cos(psi0 * Mathf.Deg2Rad):F3}→{Mathf.Cos(psi1 * Mathf.Deg2Rad):F3}"
                 + $" · 相机挪了 {Vector3.Distance(cam0, cam.transform.position):F1}"
                 + $" · 日地 {Vector3.Distance(p0, s0):F1}→{Vector3.Distance(p1, s1):F1}"
                 + $" · 月地 {Vector3.Distance(p0, m0):F1}→{Vector3.Distance(p1, m1):F1}";
        }
    }
}
