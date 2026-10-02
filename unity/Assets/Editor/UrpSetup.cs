using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Humen.EditorTools
{
    /// <summary>
    /// 把工程切到 URP。
    ///
    /// <b>先澄清一个容易搞反的事实</b>：URP 在 Unity 6 里是<b>内置包</b>。
    /// 查 <c>Library/PackageCache</c> 与 <c>Packages/packages-lock.json</c> 可见
    /// <c>"com.unity.render-pipelines.universal": { "version": "17.0.3", "source": "builtin" }</c>
    /// —— <c>source</c> 是 <c>builtin</c>，随编辑器安装，<b>不需要联网下载</b>。
    /// （公开注册表 <c>packages.unity.com</c> 上确实查不到 17.x，那是另一回事，
    /// 不代表不能用 —— 这一点是实测验出来的，不是查出来的。）
    ///
    /// 只装包而不建管线资产，Unity 仍然走内置管线。本脚本负责把资产建出来并挂上去。
    /// </summary>
    public static class UrpSetup
    {
        private const string SettingsDir = "Assets/Settings";
        private const string RendererPath = SettingsDir + "/URP-Renderer.asset";
        private const string AssetPath = SettingsDir + "/URP-Asset.asset";

        [MenuItem("Humen/切换到 URP")]
        public static void Setup()
        {
            System.IO.Directory.CreateDirectory(SettingsDir);

            // 渲染器资产：定义 Forward/Deferred、阴影、后处理这一层
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererPath);
            }

            // 管线资产本身
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(urp, AssetPath);
            }

            // 挂上去。两处都要设：GraphicsSettings 管"没有 Quality 覆盖时用哪条管线"，
            // QualitySettings 管当前质量档。只设前者在编辑器里常看着像没生效。
            GraphicsSettings.defaultRenderPipeline = urp;
            QualitySettings.renderPipeline = urp;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Humen] 已切到 URP：{AssetPath}");
        }
    }
}
