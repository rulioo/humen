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

            // 疆域垫在最底下（HeightScale 最低），路在它之上，标记在最上面。
            // 三层都挂在蓝星下面、跟着球一起自转 —— 挂到场景根上的话，
            // 球转了而图层不转，肉眼一看就是错的（与河流那条同源）。
            var territoryGo = new GameObject("Territory");
            territoryGo.transform.SetParent(planet.transform, false);
            var territory = territoryGo.AddComponent<TerritoryLayer>();
            territory.Radius = PlanetRadius;
            territory.Load();
            territory.Build();

            var routeGo = new GameObject("Routes");
            routeGo.transform.SetParent(planet.transform, false);
            var routes = routeGo.AddComponent<TradeRouteLayer>();
            routes.Radius = PlanetRadius;
            routes.Load();
            routes.Build();

            var markerGo = new GameObject("Tribes");
            markerGo.transform.SetParent(planet.transform, false);
            var markers = markerGo.AddComponent<TribeMarkerLayer>();
            markers.Radius = PlanetRadius;
            markers.Load();
            markers.Build();

            var timeline = planet.AddComponent<WorldTimeline>();
            timeline.Markers = markers;
            timeline.Rivers = rivers;
            timeline.Territory = territory;
            timeline.Routes = routes;
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
            // 远裁剪面要罩得住<b>日盘本身</b>。v0.28 起太阳在 1012R（真实日地距离按月亮同一个
            //    压缩倍数缩下来，见 SunMoonSystem 类注释），加日盘半径 108.1R、再加相机最远的
            //    5R（OrbitCamera.MaxFactor）＝ 1125.1R，取 1300000 留一倍余量。
            // ⚠️ 旧的 60000 是照着"太阳在 1.8R"写的，那个值现在是错的 —— 不改这里，
            //    日盘会被远平面直接裁掉，而且<b>不报错</b>，只是"看不见太阳"。
            //    放大 far <b>不掉深度精度</b>：定点深度缓冲 Δz ≈ z²/(2ⁿ·near)，在 far ≫ z 时与 far
            //    无关（z=3000、near=0.1、n=24 时两种 far 都是 5.4 单位）—— 决定精度的只有 near，没动。
            cam.farClipPlane = 1300000f;
            // ⚠️ 45° 改成 60° 是为了<b>月亮</b>：它在 2.6R 轨道上，放宽视锥是唯一能松的一档 ——
            //    不改变任何几何关系，只是让更多天空进画面。
            //    （v0.27 这里写的理由是"日盘屏上偏角 36.8°、横向半视锥 47.2° 留有余量"，
            //     那个理由 v0.28 作废了：太阳退到 1012R 之后日盘偏角与蓝星相位被
            //     δ + φ = 180° − ε 锁死，默认机位下 124.86°，日盘在镜头背后 —— 与视锥宽窄无关。）
            cam.fieldOfView = 60f;

            var orbit = camGo.AddComponent<OrbitCamera>();
            orbit.Target = planet.transform;
            orbit.Radius = PlanetRadius;

            // ⚠ 默认机位<b>不是随手填的欧拉角</b>，对准"部落最多的那块大陆"。
            //    原先写死 Yaw=−35 / Pitch=18，实测出来第一屏对着的是<b>汪洋与夜半球</b>，
            //    作者要看的那 197 个部落被挤在画面左下角一小片、还多半在阴影里
            //    （见 unity_preview.png）。默认机位是这个演示的第一屏 ——
            //    它对着哪儿，就等于"这个作品展示了什么"。
            float aimLat = 18f, aimLon = 0f;
            if (markers.Ready && AimAtMainContinent(markers.World, out float lat, out float lon))
            {
                aimLat = lat; aimLon = lon;
                Debug.Log($"[Humen] 默认机位对准部落最多的大陆：{aimLat:F0}°, {aimLon:F0}°");
            }
            orbit.Yaw = 180f + aimLon;       // 由 OrbitCamera 的算式反解：yaw = 180 + lon
            orbit.Pitch = aimLat;            // 同上：pitch = lat

            orbit.DistanceFactor = 3.0f;     // A 级轨道视图
            orbit.MinFactor = 1.005f;        // §10.1 B 级上限，再近就该"降落"了
            orbit.MaxFactor = 5.0f;          // 拉远能同时收进蓝星与月亮
            orbit.ApplyImmediately();

            // ⚠ 相机摆好之后必须<b>再</b> Apply 一次。上面那次 Apply 跑在相机建出来之前，
            //   而道路线宽按"相机到地表的距离"反比缩放（TradeRouteLayer.SizeZoom）——
            //   没有相机时它退回 1 倍，烘进场景的就是一个<b>错误的线宽</b>。
            //   与 PreviewRenderer 里"必须在摆好相机之后再建网格"是同一件事。
            timeline.Apply();

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
        /// 求出"部落最多的那块大陆"上有人的地方（该大陆诸部落的<b>方向平均</b>），
        /// 再用 <see cref="PlanetGeometry.NormalAt"/> 的逆式解回经纬度。
        ///
        /// 为什么要质心而不是大陆的几何中心：几何中心可能是<b>一片秃地或内海</b>
        /// —— 实测北温带大陆的几何中心就没人住，对着它开场等于白开。
        ///
        /// 逆式：<c>n = (cosLat·sin lon, sinLat, cosLat·cos lon)</c>
        /// ⟹ <c>lat = asin(n.y)</c>、<c>lon = atan2(n.x, n.z)</c>。
        /// </summary>
        private static bool AimAtMainContinent(WorldView world, out float lat, out float lon)
        {
            lat = 0f; lon = 0f;
            if (world == null || !world.Loaded) return false;

            // 每块大陆各有多少部落、方向之和是多少
            var counts = new System.Collections.Generic.Dictionary<int, int>();
            var sums = new System.Collections.Generic.Dictionary<int, Vector3>();
            foreach (var t in world.File.tribes)
            {
                if (t == null) continue;
                counts.TryGetValue(t.continentId, out int c);
                counts[t.continentId] = c + 1;
                sums.TryGetValue(t.continentId, out Vector3 s);
                sums[t.continentId] = s + PlanetGeometry.NormalAt(t.lat, t.lon);
            }

            int best = -1, bestN = 0;
            foreach (var kv in counts)
                if (kv.Value > bestN || (kv.Value == bestN && kv.Key < best)) { bestN = kv.Value; best = kv.Key; }
            if (best < 0) return false;

            Vector3 sum = sums[best];
            if (sum.sqrMagnitude < 1e-6f) return false;
            sum.Normalize();

            lat = Mathf.Asin(Mathf.Clamp(sum.y, -1f, 1f)) * Mathf.Rad2Deg;
            lon = Mathf.Atan2(sum.x, sum.z) * Mathf.Rad2Deg;
            return true;
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
            EnsureMaterialAssets(MarkerMaterials.Folder, MarkerMaterials.AssetNames, MarkerMaterials.Colors);
            EnsureMaterialAssets(TradeRouteMaterials.Folder, TradeRouteMaterials.AssetNames, TradeRouteMaterials.Colors);
            EnsureMaterialAssets(TerritoryMaterials.Folder, TerritoryMaterials.AssetNames, TerritoryMaterialColors());
        }

        /// <summary>疆域四档的颜色由 <see cref="TerritoryMaterials.ColorFor"/> 生成（标记色 + 低不透明度）。</summary>
        private static Color[] TerritoryMaterialColors()
        {
            var c = new Color[TerritoryMaterials.AssetNames.Length];
            for (int i = 0; i < c.Length; i++) c[i] = TerritoryMaterials.ColorFor(i);
            return c;
        }

        /// <summary>
        /// 把一组材质存成资产文件。三个图层共用这一份实现 ——
        /// 各写一遍的话，"已存在时要更新而不是跳过"那条教训（见下面）迟早会在新副本里丢掉。
        /// </summary>
        public static void EnsureMaterialAssets(string folder, string[] names, Color[] colors)
        {
            System.IO.Directory.CreateDirectory(folder);

            for (int i = 0; i < names.Length; i++)
            {
                string path = $"{folder}/{names[i]}";
                var color = colors[i];

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
                mat.name = names[i];
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
