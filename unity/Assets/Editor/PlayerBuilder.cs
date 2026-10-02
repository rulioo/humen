using System.IO;
using UnityEditor;
using UnityEditor.Build;            // NamedBuildTarget
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Humen.EditorTools
{
    /// <summary>
    /// 把工程编成一个<b>双击就能跑</b>的 Windows 程序（不需要装 Unity 编辑器）。
    ///
    /// 与 <see cref="PreviewRenderer"/> / <see cref="SettlementSceneBuilder"/> 的区别：
    /// 那两个是<b>取证</b>工具（渲一张图给人看），这一个才是<b>交付物</b> ——
    /// 作者要的是"看到一个活生生的世界"，那就得能运行，而不是只有几张图。
    ///
    /// ⚠️ 两个场景都必须进 Build Settings，否则 <c>SceneManager.LoadScene</c> 会失败
    ///    ——而它的失败方式是<b>运行期</b>报错（编辑器里 Play 时也会），
    ///   构建本身是成功的、一句警告都没有。故 <see cref="EnsureBuildSettings"/>
    ///   在<b>每次</b>建场景与构建时都跑一遍，不依赖"我记得加过"。
    /// </summary>
    public static class PlayerBuilder
    {
        public const string SceneDir = "Assets/Scenes";
        public const string OutputDir = "Build/Windows";
        public const string ExeName = "Humen.exe";

        /// <summary>
        /// Build Settings 里该有的场景，<b>顺序即启动顺序</b> —— 索引 0 是程序打开时进的那个。
        /// 行星视图排第一：作者要看的第一眼是那颗三维蓝星，不是某一片聚落。
        /// </summary>
        public static readonly string[] Scenes =
        {
            SceneDir + "/Planet.unity",
            SceneDir + "/Settlement.unity",
        };

        [MenuItem("Humen/生成可执行程序（Windows）")]
        public static void BuildMenu() => BuildWindows();

        public static void BuildWindowsBatch() => BuildWindows();

        /// <summary>
        /// 把 <see cref="Scenes"/> 注册进 Build Settings。
        /// 两个场景的生成器都调它，保证"重建场景"与"打包"看到的是同一份清单。
        /// </summary>
        public static void EnsureBuildSettings()
        {
            var list = new EditorBuildSettingsScene[Scenes.Length];
            for (int i = 0; i < Scenes.Length; i++)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Scenes[i]) == null)
                {
                    Debug.LogError($"[Humen] 场景不存在：{Scenes[i]} —— 先把两个场景生成出来。");
                    return;
                }
                list[i] = new EditorBuildSettingsScene(Scenes[i], true) { enabled = true };
            }

            EditorBuildSettings.scenes = list;

            // 自检：读回来确认，不靠"设了应该就对了"。
            var back = EditorBuildSettings.scenes;
            if (back.Length != Scenes.Length)
                Debug.LogError($"[Humen] Build Settings 写回后是 {back.Length} 个场景，应为 {Scenes.Length} 个。");
            else
                Debug.Log($"[Humen] Build Settings 已注册 {back.Length} 个场景：{string.Join(" · ", Scenes)}");
        }

        public static void BuildWindows()
        {
            EnsureBuildSettings();

            // ── 玩家设置 ────────────────────────────────────────────
            PlayerSettings.companyName = "Humen";
            PlayerSettings.productName = "蓝星 Humen";
            PlayerSettings.bundleVersion = "1.0";
            // 窗口化、可缩放。全屏起步对"第一次打开"不友好 —— 用户不知道该按什么退出。
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            // 拖走焦点时继续跑（切出去看一眼再切回来，时间轴不该跳）。
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

            Directory.CreateDirectory(OutputDir);
            string exe = Path.Combine(OutputDir, ExeName);

            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            Debug.Log($"[Humen] 开始构建 {options.target} → {Path.GetFullPath(exe)}");
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Humen] 构建成功：{Path.GetFullPath(exe)}" +
                          $"（{summary.totalSize / (1024 * 1024)} MB，耗时 {summary.totalTime.TotalSeconds:F0} 秒）");
            }
            else
            {
                Debug.LogError($"[Humen] 构建失败：{summary.result}，" +
                               $"{summary.totalErrors} 个错误。详情见 Editor.log。");
                foreach (var step in report.steps)
                    foreach (var msg in step.messages)
                        if (msg.type == LogType.Error || msg.type == LogType.Exception)
                            Debug.LogError($"[Humen]   {step.name}: {msg.content}");
            }
        }
    }
}
