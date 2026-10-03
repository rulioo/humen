using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Humen.Planet
{
    /// <summary>
    /// 让<b>打包出来的播放器自己出取证图</b>：带 <c>-humen-capture &lt;目录&gt;</c> 启动，
    /// 它会依次摆好几个状态、各截一张图，然后退出。不带这个参数时本组件<b>完全不存在</b>，
    /// 正常运行一点影响都没有。
    ///
    /// <b>为什么非要有它（而不是"编辑器里看过就行了"）：</b>
    /// 编辑器取证图与播放器画面是<b>两条不同的路</b> —— 场景序列化的结果、
    /// <c>StreamingAssets</c> 与 <c>Resources</c> 的打包结果、HUD、输入，
    /// 任何一环在打包后出问题，编辑器图都<b>照样是好的</b>。
    /// v0.23 已经吃过同一款亏：差分哨兵量错了自己却报出一个像样的数
    /// （<b>看起来像个结果</b>）。"播放器没人看过"与"播放器没问题"是两回事，
    /// 而前者在报告里是<b>看不出来</b>的 —— 除非把它变成一个能看的文件。
    ///
    /// ⚠️ 取证挂在<b>真实的组件</b>上：拨球走 <see cref="GlobeDrag.SimulateDrag"/>，
    /// 自转走 <see cref="PlanetBootstrap.AutoRotate"/>，
    /// 不另写一套"测试专用"的转动代码 —— 否则验的是测试，不是产品。
    ///
    /// <b>这一套存在之后立刻抓到的第一个问题</b>（值得留着当例子）：
    /// 01 与 02 两张图里，画面右侧那颗大白盘怎么看都是月亮，于是"图上有月亮"被写进了结论。
    /// 后来把几何复刻出来一算，它是<b>太阳</b>（离视轴 36.8°，这是 v0.23 的读数 ——
    /// 那时太阳还在 1.8R；v0.28 起它在 1012R，默认机位下离视轴 124.9°、在镜头背后），
    /// 而真正的月亮在 θ=0 时离视轴 54°，<b>根本不在 60° 视锥里</b>。
    /// 教训不是"要小心"，是：<b>关于画面的结论必须落到像素上</b>——
    /// 要么量出来，要么别写。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCapture : MonoBehaviour
    {
        /// <summary>命令行开关。带它才启用。</summary>
        public const string Flag = "-humen-capture";

        private static bool _booted;

        /// <summary>
        /// 在第一个场景载入后自动挂上。
        /// ⚠️ 用 <c>RuntimeInitializeOnLoadMethod</c> 而不是"往场景里放一个对象"，
        ///    是因为这样<b>两个场景都自动生效</b>，不必改场景文件，
        ///    也不会在正常游玩时留下一个空对象。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_booted) return;
            string dir = ArgValue(Flag);
            if (string.IsNullOrEmpty(dir)) return;   // 正常游玩：什么都不做
            _booted = true;

            var go = new GameObject("HumenPlayerCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<PlayerCapture>().Begin(dir);

            Debug.Log($"[Humen] 播放器取证已开启 → {dir}");
        }

        /// <summary>从命令行取 <c>-flag value</c>。取不到返回 null。</summary>
        private static string ArgValue(string flag)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (string.Equals(a[i], flag, StringComparison.OrdinalIgnoreCase))
                    return a[i + 1];
            return null;
        }

        private void Begin(string dir)
        {
            try { Directory.CreateDirectory(dir); }
            catch (Exception e) { Debug.LogError($"[Humen] 取证目录建不出来：{e.Message}"); return; }
            StartCoroutine(Sequence(dir));
        }

        private IEnumerator Sequence(string dir)
        {
            // ── ① 星球场景：默认状态 ────────────────────────────────
            // 等两秒：贴图、部落标记、日月都是这一帧之后才建齐的。
            // 不等就截的话，截到的是<b>半成品</b>（这与批处理第一次 Render 需要预热同源）。
            yield return new WaitForSeconds(2.0f);
            yield return Shot(dir, "01_星球_默认");

            var planet = FindFirstObjectByType<PlanetBootstrap>();
            var drag = FindFirstObjectByType<GlobeDrag>();

            // ── ①b 开局那一眼，天上到底有没有月亮 ────────────────────
            // 这是 v0.27 补的一问：月亮在开场相位若正好躲在蓝星背后，
            // 屏幕上<b>一个像素都没有</b>，而旧的两态 HUD 会说「在画面里」。
            // 所以这里不写"应该看得见"，直接把处境打出来 —— Visible 才算这一轮到位。
            var sky = FindFirstObjectByType<SunMoonSystem>();
            if (sky != null)
                Debug.Log($"[Humen] 取证：开局月亮处境 = {sky.SightMoon(Camera.main)}"
                          + $"，相位 {sky.MoonAngleDeg:F0}°"
                          + "（**Visible** 才算对；BehindPlanet 就说明 MoonStartAngleDeg 又该重扫了）");
            else Debug.LogWarning("[Humen] 取证：场景里没有 SunMoonSystem，日月没验到");

            // ── ①c 开局那一眼，太阳在哪儿 ────────────────────────────
            // v0.28 起首屏**没有日盘**是正常态（太阳在 1012R，默认机位下它在镜头背后），
            // 而"天上没有太阳"与"太阳根本没被造出来"在图上长得一模一样 ——
            // 同一个月亮栽过的跟头。所以连处境带偏角一起报，**BehindCamera 才是对的那一态**。
            if (sky != null)
                Debug.Log($"[Humen] 取证：开局太阳处境 = {sky.SightSun(Camera.main)}"
                          + $"，离视轴 {sky.SunOffAxisDeg(Camera.main):F2}°"
                          + $"，蓝星亮度 cos ψ {Mathf.Cos(sky.ViewedPhaseDeg(Camera.main) * Mathf.Deg2Rad):F3}"
                          + "（**BehindCamera** 才算对；Visible 反过来说明太阳又变近了）");

            // ── ② 关掉自转（R 键切换的就是这个标志位）──────────────
            if (planet != null)
            {
                planet.AutoRotate = false;
                Debug.Log($"[Humen] 取证：自转已关（AutoRotate={planet.AutoRotate}）");
            }
            else Debug.LogWarning("[Humen] 取证：场景里没有 PlanetBootstrap，自转开关没验到");

            // ── ②b 日月视差：转相机之后，日月在屏上必须各自动，关系必须一点不变 ──
            // 这是作者那两句话在<b>数字上</b>的判据：
            // 「拖动蓝星的时候，月亮和太阳的位置也得相应地改变」→ 屏上位移 > 0；
            // 「不能改变相对关系……得有距离与角度的空间关系」→ 三维量前后<b>完全相同</b>。
            // ⚠️ 两句话必须<b>同时</b>成立才算对。v0.25 只量了后者（而且量到 0 就收工），
            //    于是把"世界整个冻住"当成了"关系保住了"—— 前者其实一直是坏的。
            // 这一节自己拨一次<b>小</b>位移（够看出动、又不至于把日盘推出画面），
            // 拨完立刻复位，不影响后面 02 那张大位移的图。
            // ⚠️ 量法本身写在 <see cref="GlobeDrag.VerifyOrbitParallax"/> 里，不在这里重写一遍 ——
            //    编辑器取证（OrbitForensics）走的是同一个方法。两处各写一份的话，
            //    迟早会漂成"播放器里验过、编辑器里验的是另一件事"，而那正是本工程吃过多次的亏。
            if (drag != null)
            {
                float hh = Screen.height;
                var sf = new Vector2(Screen.width * 0.5f, hh * 0.5f);
                var st = sf + new Vector2(hh * 0.07f, 0f);      // 720p 下约 50 px：够看出动，又不至于推出画面
                Debug.Log(drag.VerifyOrbitParallax(sf, st));
                yield return null;
            }
            else Debug.LogWarning("[Humen] 取证：场景里没有 GlobeDrag，日月视差没验到");

            // ── ③ 拨球：走真正的拖拽代码路径 ──────────────────────
            // 从画面中心（必定落在球面上）往右下拖 240/120 px，等价于玩家按住球面往右下拨。
            // 这一张要能看出<b>大陆确实挪了位置</b>；挪不动就说明拖拽在播放器里没接上。
            //
            // 同时把松手角速度打出来：截图能证明"球转了"，证明不了"甩得动"——
            // 惯性算得对不对，只能靠这个数。
            if (drag != null)
            {
                // ⚠️ 位移必须**落在圆面之内**，否则"跟手"这条断言根本不适用。
                //    圆面半径 ≈ 0.306·H（= f·tan(asin(R/D))，f 为竖直焦距），
                //    720p 下约 220 px。原来取 (240,120) 合位移 269 px —— 已经出圈，
                //    量出来的"误差 48 px"其实只是"光标离轮廓 49 px"，与旋转对不对无关。
                //    现在取 0.264·H（720p 下 190 px），留 30 px 余量。
                float h = Screen.height;
                var from = new Vector2(Screen.width * 0.5f, h * 0.5f);
                var to = from + new Vector2(h * 0.236f, h * 0.118f);
                float flick = drag.SimulateDrag(from, to);
                Debug.Log($"[Humen] 取证：已拨球（右 {to.x - from.x:F0} px / 下 {to.y - from.y:F0} px），"
                          + $"松手角速度 {flick:F0} 度/秒"
                          + $"（惯性阈值 {drag.MinInertiaSpeedDeg:F0} 度/秒，所以这一把是会继续滑的）");

                // 「跟手」的定义就是这个数：抓住的那个地表点应当**恰好**落在终点光标下。
                // 理想是 0，而且是恒等而不是近似（Sensitivity=1 时不做任何缩放）。
                // 它同时是个回归哨兵 —— 右乘写成左乘时，画面只是"手感有点怪"，
                // 只有这个数会立刻从 0 跳起来。
                if (drag.LastGrabTargetHitSphere)
                    Debug.Log($"[Humen] 取证：抓取点跟手误差 {drag.LastGrabErrorPixels:F2} px"
                              + $"（0 = 地表严格跟手；几十 px 就说明旋转算式错了）");
                else
                    Debug.LogWarning("[Humen] 取证：拖拽终点落在球面之外，跟手误差不作数"
                                     + "（球上不存在光标底下那个点）");
            }
            else Debug.LogWarning("[Humen] 取证：场景里没有 GlobeDrag，拨球没验到");

            yield return new WaitForSeconds(0.6f);
            yield return Shot(dir, "02_星球_拨过且自转已关");

            // ── ③b 复位：作者要的那个操作，得在<b>播放器里</b>按下并看到效果 ──
            // 复位是个"操作"，不是个函数。所以这里调的是与 F 键<b>同一个</b>入口
            // （<see cref="GlobeDrag.ResetGlobe"/>），按完再拍一张 ——
            // 图与 01 应当是同机位、同自转角度（月亮会自己走一点，那是天体在动，不是没复位）。
            // ⚠️ 不能只断言"复位函数被调用了"：机架、星球自转、惯性三样里漏掉任何一样，
            //    画面都只是"看着差不多"，而差额恰好是"同一块大陆还歪着"那种**不显眼**的错。
            if (drag != null)
            {
                var camR = Camera.main;
                string before = camR != null ? Pose(camR) : "（无相机）";
                string spinBefore = planet != null ? planet.transform.rotation.eulerAngles.ToString("F2") : "（无星球）";

                drag.ResetGlobe();
                yield return new WaitForSeconds(0.4f);

                var camA = Camera.main;
                string after = camA != null ? Pose(camA) : "（无相机）";
                string spinAfter = planet != null ? planet.transform.rotation.eulerAngles.ToString("F2") : "（无星球）";

                Debug.Log($"[Humen] 复位：机位 {before} → {after}");
                Debug.Log($"[Humen] 复位：星球自转欧拉角 {spinBefore} → {spinAfter}");
                yield return Shot(dir, "03_星球_按F复位后");
            }

            // ── ③c 往夜面拖：日盘应当从镜头背后升进画面 ──────────────
            // 这是 v0.28 那条几何最直接的可观测后果，也是作者已经裁过的那件事：
            // 默认机位（ψ=55°）日盘在<b>镜头背后</b>；一路往同一边拨，δ 会穿过视锥横向半角 45.7°，
            // 日盘升进画面 —— 而<b>同一刻蓝星是暗的</b>（cos ψ < 0）。两者互斥，不是估计，是几何。
            // 编辑器侧 <see cref="OrbitForensics.ReportSweep"/> 已把 12 步整条轨迹量出来了；
            // 这一张要证的是<b>打包之后</b>同一条轨迹还在 —— 场景序列化、输入、HUD
            // 任何一环在打包后变样，编辑器图都照样是好的（见类注释）。
            if (sky != null && drag != null)
            {
                drag.ResetGlobe();
                float hh2 = Screen.height;
                var nf = new Vector2(Screen.width * 0.5f, hh2 * 0.5f);
                var nt = nf + new Vector2(hh2 * 0.10f, 0f);      // 720p 下每下 72 px，与编辑器的扫掠同档
                for (int i = 0; i < 6; i++) { drag.SimulateDrag(nf, nt); yield return null; }

                var camS = Camera.main;
                float psiNow = camS != null ? sky.ViewedPhaseDeg(camS) : -1f;
                Debug.Log($"[Humen] 取证：往夜面拨 6 下后 —— 日盘处境 {sky.SightSun(camS)}"
                          + $"（离视轴 {sky.SunOffAxisDeg(camS):F2}°）"
                          + $" · 蓝星亮度 cos ψ {Mathf.Cos(psiNow * Mathf.Deg2Rad):F3}"
                          + "（**日盘在画面里 ⟺ 蓝星是暗的**，两个读数必须同向出现）");
                yield return new WaitForSeconds(0.4f);
                yield return Shot(dir, "03b_星球_拖到夜面_日盘升起");

                drag.ResetGlobe();          // 复位，免得后面的图都停在夜面
                yield return new WaitForSeconds(0.3f);
            }

            // ── ④ 时间轴：同一机位，只有年份不同 ──────────────────
            // 这一条是本次要求（"让我能够在时间轴上看到进化"）在<b>产品里</b>的验证。
            // 编辑器取证图能证明图层画得对，但证明不了"播放器里拖时间轴画面会变" ——
            // 那是两条不同的路，而"播放器没人看过"在报告里是看不出来的（见类注释）。
            // 走 <c>WorldTimeline.SetYear</c> —— 与作者拖滑条时是同一段代码。
            var timeline = FindFirstObjectByType<WorldTimeline>();
            if (timeline != null)
            {
                // ⚠️ 先停住。播放器一开局 Playing = true，年份自己在走 ——
                //    不停的话四张图的年份会各自漂几百上千年，"同期对比"就不成立了。
                timeline.Playing = false;

                string[] tags = { "史前", "农业", "定居", "终点" };
                double[] years = { -300000.0, -244150.0, -220375.0, 2125.0 };
                for (int i = 0; i < years.Length; i++)
                {
                    timeline.SetYear(years[i]);
                    // 重建网格要一帧，等一拍再截 —— 与开头"等两秒"同源，
                    // 不等的话截到的是上一年的画面，而那看起来完全正常。
                    yield return new WaitForSeconds(0.5f);
                    yield return Shot(dir, $"04_时间轴_{tags[i]}");
                }
                timeline.SetYear(2125);
            }
            else Debug.LogWarning("[Humen] 取证：场景里没有 WorldTimeline，时间轴没验到");

            // ── ⑤ 聚落场景（降落）────────────────────────────────
            // 走 <c>LandingState</c> 的默认落点，与玩家按 Tab 得到的是同一个结果。
            SceneManager.LoadScene("Settlement");
            yield return new WaitForSeconds(2.5f);
            yield return Shot(dir, "05_聚落_默认落点");

            Debug.Log("[Humen] 播放器取证结束，退出。");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        /// <summary>把相机姿态写成一行，供复位前后对比。<b>位置与朝向都要有</b>——只比位置的话，
        /// 机架绕球心转 180°（位置挪了）与滚转（位置没挪）这两种错会漏掉一种。</summary>
        private static string Pose(Camera cam)
        {
            Vector3 p = cam.transform.position;
            Vector3 e = cam.transform.rotation.eulerAngles;
            return $"pos({p.x:F1},{p.y:F1},{p.z:F1}) euler({e.x:F1},{e.y:F1},{e.z:F1})";
        }

        /// <summary>
        /// 截一张并等它真的落盘。
        /// ⚠️ <c>CaptureScreenshot</c> 是在<b>帧末</b>写文件的，紧接着就退出会写出 0 字节 ——
        ///    必须等一帧再等一次磁盘。这是"取证文件看起来生成了"与"文件确实有内容"的区别。
        /// </summary>
        private IEnumerator Shot(string dir, string name)
        {
            string path = Path.Combine(dir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForEndOfFrame();
            yield return null;
            yield return new WaitForSeconds(0.3f);

            // 自检：文件必须真的存在且非空。不查的话，"截图没成功"会表现成
            // "图少了"，而人只会以为自己忘了看。
            var fi = new FileInfo(path);
            if (fi.Exists && fi.Length > 0)
                Debug.Log($"[Humen] 已截图 {name}.png（{fi.Length} bytes）");
            else
                Debug.LogError($"[Humen] 截图失败或为空：{path}");
        }
    }
}
