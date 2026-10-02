using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Humen.Planet;

namespace Humen.EditorTools
{
    /// <summary>
    /// 用代码生成 <c>Assets/Scenes/Planet.unity</c>。
    ///
    /// 为什么不用手拖一个场景存下来：
    /// 场景文件是 YAML，手工维护既易错又不可复现 —— 而本项目通篇的原则
    /// （§7.3「参数化可复现」、INV-34）就是「能被代码重新算出来的，就不要存成手工状态」。
    /// 场景同理：留着这段脚本，任何时候都能一键重建出同一个场景。
    ///
    /// 用法：菜单 <b>Humen ▸ 重建星球场景</b>，
    /// 或命令行 <c>Unity.exe -batchmode -quit -projectPath &lt;proj&gt; -executeMethod Humen.EditorTools.PlanetSceneBuilder.BuildScene</c>
    /// </summary>
    public static class PlanetSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Planet.unity";
        private const float PlanetRadius = 1000f;   // §10.1

        [MenuItem("Humen/重建星球场景")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── 星球 ─────────────────────────────────────────────
            var planet = new GameObject("Planet");
            planet.transform.position = Vector3.zero;   // 世界原点 = 星球中心
            var bootstrap = planet.AddComponent<PlanetBootstrap>();
            bootstrap.Radius = PlanetRadius;
            bootstrap.Segments = 128;
            bootstrap.Rings = 64;
            bootstrap.DegreesPerSecond = 6f;
            bootstrap.Build();

            // ── 世界图层（M4b-2）────────────────────────────────
            // 河流与部落都挂在星球<b>下面</b>，跟着星球一起自转 ——
            // 挂到场景根上的话，球转了而标记不转，肉眼一看就是错的。
            EnsureMarkerMaterialAssets();

            var riverGo = new GameObject("Rivers");
            riverGo.transform.SetParent(planet.transform, false);
            var rivers = riverGo.AddComponent<RiverLayer>();
            rivers.Radius = PlanetRadius;
            rivers.Load();
            rivers.Build();

            var markerGo = new GameObject("Tribes");
            markerGo.transform.SetParent(planet.transform, false);
            var markers = markerGo.AddComponent<TribeMarkerLayer>();
            markers.Radius = PlanetRadius;
            markers.Load();
            markers.Build();

            var timeline = planet.AddComponent<WorldTimeline>();
            timeline.Markers = markers;
            timeline.Rivers = rivers;
            if (markers.Ready) timeline.Year = markers.Timeline.endYear;
            timeline.Apply();

            if (!markers.Ready || !rivers.Ready)
            {
                // 不静默吞掉 —— 图层没数据时画面上只是"少了东西"，
                // 不打日志的话会以为是渲染代码的问题。
                Debug.LogWarning($"[Humen] 世界图层数据不全：部落「{markers.Error}」· 河流「{rivers.Error}」");
            }
            else
            {
                Debug.Log($"[Humen] 世界图层就绪：河流 {rivers.RiverCount} + 支流 {rivers.TributaryCount} · " +
                          $"部落 {markers.VisibleTribeCount}（{WorldTimeline.FormatYear(timeline.Year)}）");
            }

            // ── 太阳与月亮 ───────────────────────────────────────
            // 这里只放**一盏**方向光，朝向与那个看得见的太阳球
            // 全部交给 SunMoonSystem 在运行时按相机初始视轴反推（含月亮的公转/潮汐锁定）。
            // 场景里不写死欧拉角，是因为太阳方位和相机朝向是一对 --
            // 写死的话，以后一改相机 Yaw/Pitch，太阳就跑到画面外去了，
            // 而且不会报错，只会"看不见太阳"。
            var sun = new GameObject("Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.0f, 0.97f, 0.92f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.None;   // M4b-1 只要看得见，阴影是 C 级聚落的事

            planet.AddComponent<SunMoonSystem>();

            // 环境光留一点，别让夜面死黑 —— 真实的行星夜面也有星光与大气散射
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.06f, 0.08f, 0.13f);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;

            // ── 相机 ─────────────────────────────────────────────
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.016f, 0.027f, 0.043f);   // 深空底色
            cam.nearClipPlane = 0.1f;
            // 远裁剪面要罩得住月亮（2.6R）与太阳（1.8R）之外的天幕。
            cam.farClipPlane = 60000f;
            // ⚠️ 45° 改成 60°：太阳的<b>屏上偏角</b>是 36.8°（SunPhaseDeg = 55°，见 SunMoonSystem
            //    那一节的取舍表；注意 55 是<b>球心处</b>的相位角，不是屏上偏角，两者别混）。
            //    60° 的竖直半视锥是 30°、横向 47.2° —— 日盘偏在横向这一侧，留有余量。
            //    而月亮在 2.6R 轨道上要宽得多，放宽视锥是唯一能松的一档：
            //    它不改变任何几何关系，只是让更多天空进画面。
            cam.fieldOfView = 60f;

            var orbit = camGo.AddComponent<OrbitCamera>();
            orbit.Target = planet.transform;
            orbit.Radius = PlanetRadius;
            orbit.Yaw = -35f;
            orbit.Pitch = 18f;
            orbit.DistanceFactor = 3.0f;     // A 级轨道视图
            orbit.MinFactor = 1.005f;        // §10.1 B 级上限，再近就该"降落"了
            orbit.MaxFactor = 5.0f;          // 拉远能同时收进蓝星与月亮
            orbit.ApplyImmediately();

            // ── 交互 ─────────────────────────────────────────────
            // 拨地球（左键）+ 自转开关（R）。挂在蓝星上，转的是蓝星自己。
            planet.AddComponent<GlobeDrag>();

            // ── 场景切换 ─────────────────────────────────────────
            // 挂在相机上：切换要用 Camera.main 做屏幕拾取，而且相机在哪个场景里都有。
            camGo.AddComponent<SceneSwitcher>();

            // ── 存盘 ─────────────────────────────────────────────
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            // 两个场景一起注册（原先这里只登记 Planet，Settlement 是漏的 ——
            // 漏了不会在构建时报错，只会在运行期 LoadScene 失败）。
            PlayerBuilder.EnsureBuildSettings();

            Debug.Log($"[Humen] 星球场景已生成：{ScenePath}");
        }

        /// <summary>
        /// 把部落图层的四个材质存成<b>资产文件</b>。
        ///
        /// 不这么做的话，<c>TribeMarkerLayer</c> 每次运行新建的材质会成为"场景内嵌对象"，
        /// 被整份写进 <c>Planet.unity</c> —— 场景里引用了它的对象越多，副本越多。
        /// 存成资产后场景里只留引用。与 <c>UrpSetup</c> 生成管线资产是同一条思路：
        /// 能被代码重新算出来的，就不要存成手工状态（§7.3），但<b>引用</b>要落到资产上。
        /// </summary>
        private static void EnsureMarkerMaterialAssets()
        {
            System.IO.Directory.CreateDirectory(MarkerMaterials.Folder);

            for (int i = 0; i < MarkerMaterials.AssetNames.Length; i++)
            {
                string path = $"{MarkerMaterials.Folder}/{MarkerMaterials.AssetNames[i]}";
                var color = MarkerMaterials.Colors[i];

                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing != null)
                {
                    // ⚠️ 这里**必须更新而不是跳过**。原先写的是 `if (已存在) continue;`，
                    //    于是改了 Colors[] 再重建场景，材质还是旧的 ——
                    //    症状是"我明明改了配色，渲出来一点没变"，而且一句错都不报。
                    //    这是本工程吃过好几次的那一类 bug：**看着像缓存，其实是没生效**。
                    //    与 §7.3「能被代码重新算出来的，就不要存成手工状态」同一条原则：
                    //    颜色由代码定义，资产只是它的落盘副本，副本必须跟着源走。
                    if (existing.color != color)
                    {
                        existing.color = color;
                        if (existing.HasProperty("_BaseColor")) existing.SetColor("_BaseColor", color);
                        EditorUtility.SetDirty(existing);
                    }
                    continue;
                }

                var mat = MarkerMaterials.Create(color);
                mat.name = MarkerMaterials.AssetNames[i];
                AssetDatabase.CreateAsset(mat, path);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 一次跑完：先切 URP，再建场景。
        ///
        /// 为什么要合并 —— <c>Unity.exe</c> 一次运行<b>只接受一个</b> <c>-executeMethod</c>，
        /// 分两次跑就是两次完整的编辑器启动（各约 2~4 分钟）。这个入口是给
        /// <c>启动.cmd</c> 的「重建 Unity 场景」菜单项用的。
        /// </summary>
        public static void BuildAll()
        {
            UrpSetup.Setup();
            BuildScene();
        }

        /// <summary>批处理入口的别名 —— 命令行里写这个短名字更省事。</summary>
        public static void BuildBatch() => BuildAll();
    }
}
