using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Humen.Planet;

namespace Humen.EditorTools
{
    /// <summary>
    /// 把<b>同一块大陆、同一个机位</b>在若干年份各渲一张 PNG —— 时间轴取证。
    ///
    /// <b>为什么需要这个入口。</b><see cref="PreviewRenderer"/> 只渲"当前那一年"
    /// （默认是快照的末年）。而作者这一次要的是
    /// <i>"让我能够在时间轴上看到进化"</i> —— 那么"进化"这件事本身就必须能量：
    /// 一组<b>机位、光照、分辨率全同、只有年份不同</b>的图，
    /// 逐张看过去，画面上变了的就是进化，没变的就是没做出来。
    /// 只出一张图的话，"时间轴起作用了没有"完全无法回答。
    ///
    /// ⚠️ 各年份之间<b>只改年份</b>，不改机位、不改太阳、不改分辨率、不改尺寸档位。
    ///    任何一处跟着年份动，差分出来的东西就分不清是"文明长了"还是"镜头动了"。
    ///
    /// ⚠️ 与 <see cref="PreviewRenderer"/> 一样<b>不能加</b> <c>-nographics</c>——
    ///    那个开关强制 Null 渲染设备，出来是全黑的，且一句错都不报。
    ///
    /// 用法：<c>Unity.exe -batchmode -quit -projectPath &lt;proj&gt;
    /// -executeMethod Humen.EditorTools.TimelinePreviewRenderer.RenderBatch</c>
    /// </summary>
    public static class TimelinePreviewRenderer
    {
        private const string ScenePath = "Assets/Scenes/Planet.unity";
        private const int Width = 1280, Height = 720;

        /// <summary>
        /// 取证的年份。刻意<b>跨过历史锚点</b>而不是等距取：
        /// 快照锚点（首次农业 −244,150、首次铁器 −211,000、首次工业 −220,375）之外，
        /// 还要取几个"锚点前后"的年份，才能看出某个东西是<b>哪一年冒出来的</b>。
        /// 末尾必须是终点年 —— 那是快照里唯一信息完整的一年。
        /// </summary>
        public static readonly double[] Years =
        {
            -300000,   // 起点：全世界还只有地理
            -250000,
            -244150,   // 首次农业（快照锚点）
            -220375,   // 首次工业（快照锚点）
            -211000,   // 首次铁器（快照锚点）
            -150000,
            -100000,
            -50000,
            -10000,
            0,         // 公元元年
            1000,
            1900,
            2125,      // 终点
        };

        /// <summary>取证机位：要哪一块大陆。默认 3 —— 80 个农耕部落全在它上面。</summary>
        public static int ContinentId = 3;

        public static void RenderBatch() => Render("unity_tl");

        public static void Render(string prefix)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid()) { Debug.LogError($"[Humen] 打不开场景 {ScenePath}"); return; }

            var cam = Object.FindFirstObjectByType<Camera>();
            var orbit = cam != null ? cam.GetComponent<OrbitCamera>() : null;
            var markers = Object.FindFirstObjectByType<TribeMarkerLayer>();
            if (cam == null || orbit == null || markers == null)
            {
                Debug.LogError("[Humen] 场景缺相机/OrbitCamera/部落图层，无法做时间轴取证。");
                return;
            }

            var world = WorldView.LoadFromDefault();
            if (!world.Loaded) { Debug.LogError($"[Humen] {world.Error}"); return; }

            // 图层按现行代码重画一遍（场景里烘的是上一次建场景时的网格）。
            var rivers = Object.FindFirstObjectByType<RiverLayer>();
            if (rivers != null) { rivers.Load(); rivers.Build(); }
            markers.Load();

            // ⚠️ 疆域与道路也必须在这里抓到手并逐年驱动。
            //    第一版漏了这一步：两层留在场景默认年（2125），于是取证图上
            //    公元前 300,000 年那张 —— 快照里部落数明明是 0 —— 照样铺满了疆域和路网。
            //    "机位对了、图也好看了"正是最容易让这种错溜过去的时刻：
            //    画面不是坏的，只是<b>每一张都在演同一年</b>。
            var territory = Object.FindFirstObjectByType<TerritoryLayer>();
            var routes = Object.FindFirstObjectByType<TradeRouteLayer>();
            if (territory != null) territory.Load();
            if (routes != null) routes.Load();
            if (territory == null || routes == null)
                Debug.LogWarning("[Humen] ⚠ 场景里缺疆域或道路图层 —— 取证图只有标记，看不到面状的进化。");

            var sky = Object.FindFirstObjectByType<SunMoonSystem>();
            if (sky != null) sky.EnsureBuilt();

            WvContinent continent = null;
            foreach (var c in world.File.continents)
                if (c.id == ContinentId) { continent = c; break; }
            if (continent == null)
            {
                Debug.LogError($"[Humen] 快照里没有大陆 {ContinentId}");
                return;
            }

            // 对准这块大陆上有人的地方（质心），而不是几何中心 ——
            // 实测北温带大陆的几何中心是一片秃地，对着它拍等于白拍。
            float aimLat = continent.centerLat, aimLon = continent.centerLon;
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var t in world.File.tribes)
            {
                if (t.continentId != continent.id) continue;
                sum += PlanetGeometry.NormalAt(t.lat, t.lon);
                n++;
            }
            if (sum.sqrMagnitude > 1e-6f)
            {
                sum.Normalize();
                aimLat = Mathf.Asin(Mathf.Clamp(sum.y, -1f, 1f)) * Mathf.Rad2Deg;
                aimLon = Mathf.Atan2(sum.x, sum.z) * Mathf.Rad2Deg;
            }

            // 相机：pitch = lat、yaw = 180 + lon（从 OrbitCamera 的算式反解，见 PreviewRenderer.AimAt）
            orbit.Pitch = aimLat;
            orbit.Yaw = 180f + aimLon;

            // 太阳按<b>新视轴</b>重摆。不摆的话，被拍的这块大陆多半落在夜半球，
            // 渲出来是一片黑 —— 那不是"没有文明"，是"没拍到"。
            //
            // ⚠️ 第一版这里写的是 SetSunDirection(该大陆的法线)，即把太阳<b>正对</b>目标 ——
            //    结果是日盘停在画面正中，<b>把要拍的那块大陆整个挡住</b>，
            //    而图上看起来只是"中间有个亮圆盘"，很容易被当成正常。
            //    SunPhaseDeg = 55° 那个久经复核的取角，正是为了避开这件事
            //    （日盘要离开球体边缘一段距离、又不能压在人脸上），故这里<b>复用那一套</b>，
            //    而不是自己另挑一个角度。
            var sun = Object.FindFirstObjectByType<Light>();
            var normal = PlanetGeometry.NormalAt(aimLat, aimLon);

            // 记下原来的太阳方位，出完图还原 —— 取证手段不该改掉场景的初始状态。
            var discBefore = GameObject.Find("SunDisc");
            var planetGo = Object.FindFirstObjectByType<PlanetBootstrap>();
            Vector3 sunDiscKept = (discBefore != null && planetGo != null)
                ? (discBefore.transform.position - planetGo.transform.position).normalized
                : Vector3.zero;

            if (sky != null) sky.RealignSunToCamera();
            else if (sun != null) sun.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);

            // 三组尺度各出一遍。三档不是"多拍几张"，而是三个各自会失效的问题：
            //   A = 3.0  应用一打开就是这个距离（OrbitCamera.DistanceFactor 默认 3R），
            //            要看的是"整个蓝星上，文明铺开了没有"。
            //   M = 1.45 作者滚轮推近时会停在这一带。A 级看"有没有"，M 级看"长成什么样"——
            //            这个距离刚好把<一块大陆上有人烟的那一片>框满，是读懂进化的主视图。
            //   B = 1.15 贴到地表，看单个聚落与它周围的路。
            // 只拍 A 与 B 的话，中间那一大段（也是作者最常停留的一段）没人验过。
            // ⚠️ 三档都必须在 OrbitCamera 的 [MinFactor 1.005, MaxFactor 4.0] 之内，
            //    否则 ApplyImmediately 会把它夹掉，出来的是同一个距离、白拍一遍。
            var scales = new[]
            {
                (suffix: "A", factor: 3.0f),
                (suffix: "M", factor: 1.45f),
                (suffix: "B", factor: 1.15f),
            };

            int written = 0;
            foreach (var sc in scales)
            {
                orbit.DistanceFactor = sc.factor;
                orbit.ApplyImmediately();

                foreach (double year in Years)
                {
                    // ⚠ 顺序要紧：先把年份推给图层，再让它们重建，最后才拍。
                    //   标记尺寸按相机到地表的距离反比缩放（见 TribeMarkerLayer.SizeZoom），
                    //   而相机刚刚才摆好，故这里的重建量到的才是这一档的距离。
                    //   SetYear 内部就是 Build()，故三层的重建顺序无所谓，只要都在 Capture 之前。
                    markers.SetYear(year);
                    if (territory != null) territory.SetYear(year);
                    if (routes != null) routes.SetYear(year);

                    var px = Capture(cam, Width, Height);
                    string file = $"{prefix}_{sc.suffix}_{(long)year}.png";
                    WritePng(px, file);

                    Debug.Log($"[Humen] 时间轴取证 {sc.suffix} 级 · {WorldTimeline.FormatYear(year)} · " +
                              $"部落 {markers.VisibleTribeCount} · {Describe(markers.StageCounts())} · " +
                              $"疆域 {(territory != null ? territory.TerritoryCount : -1)} · " +
                              $"道路 {(routes != null ? routes.RouteCount : -1)} → {file}");
                    written++;
                }
            }

            // 日盘压没压在球面上 —— 直接量屏幕位置报出来。
            // 这个坑第一版真踩了（SetSunDirection(法线) 把日盘钉在画面正中），
            // 而图上只是"中间有个亮圆盘"，不像错误。于是这里不让它靠肉眼判。
            ReportSunOcclusion(cam);

            // 把太阳还原 —— 取证手段不该改动场景里的初始状态。
            if (sky != null && sunDiscKept != Vector3.zero) sky.SetSunDirection(sunDiscKept);

            Debug.Log($"[Humen] 时间轴取证完成：{written} 张（大陆 {continent.id} {continent.name} · " +
                      $"对准 {aimLat:F0}°,{aimLon:F0}°）");

            // 绕序自检的读数。Build() 只在违规时才喊一声 —— 于是"没喊"既可能是对，
            // 也可能是这一层根本没被调用。这里把它<b>主动</b>报出来，让沉默不再是两义的。
            Debug.Log($"[Humen] 绕序自检：疆域朝内面 {(territory != null ? territory.InwardFaceCount : -1)} · " +
                      $"道路朝内面 {(routes != null ? routes.InwardFaceCount : -1)}（0 才对）");
        }

        /// <summary>
        /// 量一量日盘此刻在屏幕上是否与蓝星重叠。
        /// 判据用<b>最终真正作数的那个</b>（投影到屏幕后的像素距离），
        /// 而不是"球心处夹角多少度" —— 与 <c>SunMoonSystem.MoonLitFraction</c> 记的是同一条教训。
        ///
        /// ⚠️ <b>v0.28 起这个方法在默认机位下走的是"早退"那条路</b>：太阳在 1012R，
        /// 日盘退到镜头背后（δ = 124.9°），<c>discSp.z ≤ 0</c>。
        /// 早退本身是对的，但<b>一声不吭地早退是错的</b> ——
        /// "日盘没跟蓝星重叠"与"日盘压根不在画面里"会印成同一片沉默，
        /// 而这个方法当初就是为消灭这种两义沉默才写的。所以挡住的那一支也要说话。
        /// </summary>
        private static void ReportSunOcclusion(Camera cam)
        {
            var disc = GameObject.Find("SunDisc");
            var planet = Object.FindFirstObjectByType<PlanetBootstrap>();
            if (disc == null || planet == null || cam == null) return;

            Vector3 center = planet.transform.position;
            Vector3 discSp = cam.WorldToScreenPoint(disc.transform.position);
            if (discSp.z <= 0f)
            {
                // 日盘在镜头平面之后。报出来，别沉默 —— 见上面 ⚠️。
                var sky = Object.FindFirstObjectByType<SunMoonSystem>();
                float off = sky != null ? sky.SunOffAxisDeg(cam) : -1f;
                Debug.Log($"[Humen] 日盘在<b>镜头背后</b>（离视轴 {off:F1}°），未进画面，"
                        + "因此无遮挡可言 —— 这是 v0.28 太阳移到 1012R 后的默认情形，不是故障。");
                return;
            }

            // 球体在屏幕上的半径：取一条与视轴垂直的半径，投影量它有多长。
            Vector3 edgeWorld = center + cam.transform.right * planet.Radius;
            Vector3 centerSp = cam.WorldToScreenPoint(center);
            Vector3 edgeSp = cam.WorldToScreenPoint(edgeWorld);
            float planetPx = Vector2.Distance(new Vector2(centerSp.x, centerSp.y),
                                              new Vector2(edgeSp.x, edgeSp.y));

            // 日盘在屏幕上的半径：同样投影量。
            float sunPx = 0f;
            var discScale = disc.transform.localScale.x * 0.5f;   // Unity 球直径 = 1
            Vector3 discEdge = cam.WorldToScreenPoint(disc.transform.position + cam.transform.right * discScale);
            Vector2 de = new Vector2(discEdge.x, discEdge.y);
            sunPx = Vector2.Distance(new Vector2(discSp.x, discSp.y), de);

            float sep = Vector2.Distance(new Vector2(discSp.x, discSp.y), new Vector2(centerSp.x, centerSp.y));
            float overlap = (planetPx + sunPx) - sep;

            if (overlap > 0f)
                Debug.LogWarning($"[Humen] ⚠ 日盘与蓝星在屏幕上<b>重叠</b>约 {overlap:F0} px " +
                                 $"(日盘半径 {sunPx:F0} px · 星球半径 {planetPx:F0} px · 圆心距 {sep:F0} px) —— " +
                                 $"要拍的那块大陆会被日盘挡掉一部分。");
            else
                Debug.Log($"[Humen] 日盘未被遮挡：圆心距 {sep:F0} px > 星球半径 {planetPx:F0} + 日盘半径 {sunPx:F0} px");
        }

        /// <summary>各阶段部落数写成一行读数。下标同 <see cref="WorldView.StageAt"/>。</summary>
        private static string Describe(int[] counts)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < counts.Length; i++)
            {
                if (sb.Length > 0) sb.Append(" · ");
                string name = i < WorldView.StageNames.Length ? WorldView.StageNames[i] : $"?{i}";
                sb.Append($"{name} {counts[i]}");
            }
            return sb.ToString();
        }

        private static Color32[] Capture(Camera cam, int width, int height)
        {
            // 批处理下相机的第一次 Render() 出来的不是最终画面（管线状态还没建起来），
            // 先空渲一帧丢掉。这条在 PreviewRenderer 与 SettlementSceneBuilder 里各记过一次。
            WarmUp(cam, width, height);

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            rt.Create();

            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;

            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prevTarget;

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            var px = tex.GetPixels32();
            RenderTexture.active = prevActive;

            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            return px;
        }

        private static void WarmUp(Camera cam, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        private static void WritePng(Color32[] px, string outFile, int width = Width, int height = Height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.SetPixels32(px);
            tex.Apply();
            var bytes = tex.EncodeToPNG();
            File.WriteAllBytes(Path.GetFullPath(outFile), bytes);
            Object.DestroyImmediate(tex);
        }
    }
}
