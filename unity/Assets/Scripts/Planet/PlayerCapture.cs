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
    /// 后来把几何复刻出来一算，它是<b>太阳</b>（离视轴 36.8°），
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

            // ── ② 关掉自转（R 键切换的就是这个标志位）──────────────
            if (planet != null)
            {
                planet.AutoRotate = false;
                Debug.Log($"[Humen] 取证：自转已关（AutoRotate={planet.AutoRotate}）");
            }
            else Debug.LogWarning("[Humen] 取证：场景里没有 PlanetBootstrap，自转开关没验到");

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

            // ── ④ 聚落场景（降落）────────────────────────────────
            // 走 <c>LandingState</c> 的默认落点，与玩家按 Tab 得到的是同一个结果。
            SceneManager.LoadScene("Settlement");
            yield return new WaitForSeconds(2.5f);
            yield return Shot(dir, "03_聚落_默认落点");

            Debug.Log("[Humen] 播放器取证结束，退出。");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
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
