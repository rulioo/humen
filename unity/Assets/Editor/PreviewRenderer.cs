using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Humen.Planet;

namespace Humen.EditorTools
{
    /// <summary>
    /// 在<b>不打开编辑器界面</b>的情况下，把星球场景渲成一张 PNG —— 用来取证。
    ///
    /// 存在的理由很实际：改完渲染代码，如果只能说"应该没问题"，
    /// 那就等于没验。这个入口让 <c>Unity.exe -batchmode</c> 能直接吐出一张图，
    /// 肉眼一看就知道成没成。
    ///
    /// ⚠️ 跑这个<b>不能加</b> <c>-nographics</c> —— 那个开关会强制 Null 渲染设备，
    /// 渲出来是全黑。加了它 Unity 也不报错，只是图是黑的，很容易误判成"代码写错了"。
    /// </summary>
    public static class PreviewRenderer
    {
        private const string ScenePath = "Assets/Scenes/Planet.unity";

        public static void RenderBatch() => Render(1280, 720, "unity_preview.png");

        [MenuItem("Humen/渲染预览图")]
        public static void RenderMenu() => Render(1280, 720, "unity_preview.png");

        public static void Render(int width, int height, string outFile)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[Humen] 打不开场景 {ScenePath}");
                return;
            }

            // 场景里存的网格是<b>上一次建场景时烘进去的</b>，改完代码不重建就还是旧的。
            // 这个入口的职责是"验当前代码"，所以先把图层按现行代码重画一遍。
            RebuildLayers();

            // Unity 6：FindObjectOfType 系列已废弃，用 FindFirstObjectByType
            var cam = Object.FindFirstObjectByType<Camera>();
            if (cam == null)
            {
                Debug.LogError("[Humen] 场景里没有相机");
                return;
            }

            var marker = Object.FindFirstObjectByType<TribeMarkerLayer>();
            var markerRenderer = marker != null ? marker.GetComponent<MeshRenderer>() : null;

            // ── 月亮：挑一个它在画面里的相位 ──────────────────────────
            // 月亮绕蓝星一圈要 27.32 个星球日，而 60° 视锥只罩得住它约四分之一的行程，
            // 按下快门那一刻它多半在画面外 —— 图上就会"没有月亮"，而这不是月亮不存在，
            // 是没拍到。故取证时扫一遍相位，取第一个落进画面安全区的。
            // 运行时不用这一套：那里月亮照常按恒星月走，该出画就出画。
            var sky = Object.FindFirstObjectByType<SunMoonSystem>();
            if (sky != null)
            {
                float phase = PickVisibleMoonPhase(sky, cam);
                if (float.IsNaN(phase))
                    Debug.LogWarning("[Humen] 扫遍全周都没能把月亮放进画面 —— 这张取证图里不会有月亮。");
                else
                    Debug.Log($"[Humen] 月亮取相位 {phase:F0}°（被照亮 {sky.MoonLitFraction:P0}）· " +
                              $"相位是为取证挑的，运行时由恒星月自行推进");
            }
            else
            {
                Debug.LogWarning("[Humen] 场景里没有 SunMoonSystem —— 太阳与月亮都不会出现在这张图里。");
            }

            // ── 差分取证 ────────────────────────────────────────────
            // 只写一张 PNG 的话，"图层到底画没画出来"仍然只能靠肉眼在图上找。
            // 而这一层是可能<b>静默</b>不画的 —— 本工程就踩过：绕序反了，
            // 整层被背面剔除，画面上什么都没有，Unity 一句错都不报，
            // 而地形照样画得出来（它不是背剔除的面片），所以"地形在"完全不能推出"标记在"。
            //
            // 于是渲两遍：一遍正常，一遍把标记层关掉，逐像素比。
            // 差出来的像素数就是这一层的实际贡献 —— 与配色、色彩空间、光照都无关。
            // ⚠️ 先空渲一帧丢掉。批处理下相机的**第一次** Render() 出来的不是最终画面
            //    （阴影贴图/管线状态还没建起来），于是第一次采样与第二次差出一个巨大的数 ——
            //    实测 16.13%，看起来像"部落图层占了画面的六分之一"，而画面上那些标记
            //    明明只有几十像素。**量具坏了会伪装成结果**，这正是差分哨兵最容易被骗的地方。
            //    同一款病在 SettlementSceneBuilder 里已经吃过一次（v0.23 ③，那边报到 93.74%）。
            WarmUp(cam, width, height);

            var withLayer = Capture(cam, width, height);

            bool hadRenderer = markerRenderer != null && markerRenderer.enabled;
            if (markerRenderer != null) markerRenderer.enabled = false;
            var withoutLayer = Capture(cam, width, height);
            if (markerRenderer != null) markerRenderer.enabled = hadRenderer;

            int diff = CountDiff(withLayer, withoutLayer);

            WritePng(withLayer, outFile, width, height);

            if (markerRenderer == null)
            {
                Debug.LogWarning("[Humen] 场景里找不到 TribeMarkerLayer —— 世界图层没接上。");
            }
            else if (diff == 0)
            {
                // 不写成"警告"就没人看。这一条正是这次绕序 bug 的自动哨兵：
                // 有 202 个部落却一个像素都没画出来，必然是几何/材质层面的问题。
                Debug.LogWarning($"[Humen] ⚠ 部落图层<b>一个像素都没画出来</b>（差分 0 px）。" +
                                 $"当前 {marker.VisibleTribeCount} 个部落。多半是绕序/材质/剔除的问题，不是数据的问题。");
            }
            else
            {
                float pct = 100f * diff / (width * (float)height);
                Debug.Log($"[Humen] 部落图层差分：{diff} px（占画面 {pct:F2}%）· 当前 {marker.VisibleTribeCount} 个部落");
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  大陆巡览 —— 一块大陆一张图
        // ══════════════════════════════════════════════════════════════
        //
        // 为什么要逐大陆出图：默认视角只看得见一块大陆，而五块大陆的文明阶段
        // <b>差别极大</b>（实测：新世界两块是工业城镇 tier 4，东方大陆整块是村落 tier 1，
        // 两块环极大陆还是游群 tier 0）。只渲默认视角的话，另外三个子网格
        // —— 游群与城镇 —— <b>根本没被验证过</b>，只能说"应该也一样"。
        // 而"应该也一样"正是绕序那个 bug 骗过一轮的说法。

        public static void RenderContinentsBatch() => RenderContinents(1280, 720);

        [MenuItem("Humen/渲染各大陆预览图")]
        public static void RenderContinentsMenu() => RenderContinents(1280, 720);

        public static void RenderContinents(int width, int height)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid()) { Debug.LogError($"[Humen] 打不开场景 {ScenePath}"); return; }

            RebuildLayers();

            var cam = Object.FindFirstObjectByType<Camera>();
            var orbit = cam != null ? cam.GetComponent<OrbitCamera>() : null;
            var markers = Object.FindFirstObjectByType<TribeMarkerLayer>();
            if (cam == null || orbit == null || markers == null)
            {
                Debug.LogError("[Humen] 场景缺相机/OrbitCamera/部落图层，无法巡览。");
                return;
            }

            var world = WorldView.LoadFromDefault();
            if (!world.Loaded) { Debug.LogError($"[Humen] {world.Error}"); return; }

            float keepYaw = orbit.Yaw, keepPitch = orbit.Pitch;
            float keepDist = orbit.DistanceFactor;

            // 太阳是固定的，于是永远有半个星球在夜里 —— 而五块大陆里有两块
            // （恰是那两块工业的）正好落在夜半球，渲出来是一片黑，作不了证。
            // 巡览时把太阳临时转到正对当前大陆（该大陆正午），出完图再转回去。
            // 这只影响取证图，<b>不改场景里那盏灯的朝向</b>。
            // 太阳与月亮（以及"太阳在哪 = 光从哪来"这条同一性）都归 SunMoonSystem 管。
            // ⚠️ 巡览要临时把太阳转到当前大陆的正午，那就必须<b>连看得见的日盘一起转</b>：
            //    只转灯的话，画面上会出现「这边被照亮、太阳球却挂在那边」的自相矛盾 ——
            //    而那正是本次要实现的那条"真实关系"被破坏的样子。
            // 平行光的 forward 是"光走的方向"，故太阳方向（球心→太阳）= −forward。
            var sky = Object.FindFirstObjectByType<SunMoonSystem>();
            var sun = Object.FindFirstObjectByType<Light>();
            Vector3 keepSunDir = sun != null ? -sun.transform.forward : Vector3.up;

            // 两种尺度各出一遍。§10.1 的 A 级看大陆轮廓、B 级贴着地表 ——
            // 作者的原话是"可以放大看到每一片大陆……还有人口，千家万户"，
            // 而 A 级下 26 单位的标记只有几个像素，"千家万户"根本看不清。
            // 只验 A 级等于没验到他要的那一屏。
            var scales = new[]
            {
                (suffix: "", factor: 3.0f, label: "A级轨道"),
                (suffix: "_close", factor: 1.15f, label: "B级贴近"),
            };

            foreach (var c in world.File.continents)
            {
                // 这一块大陆上有多少部落，以及它们的<b>质心</b>。
                // B 级贴近要对着"有人住的地方"，不能对着几何中心 ——
                // 实测北温带大陆的几何中心是一片秃地，贴近了只有 194 px 的标记，等于白拍。
                int n = 0;
                Vector3 sum = Vector3.zero;
                foreach (var t in world.File.tribes)
                {
                    if (t.continentId != c.id) continue;
                    n++;
                    sum += PlanetGeometry.NormalAt(t.lat, t.lon);
                }

                // 质心是球面上的方向平均，再反解回经纬度（NormalAt 的逆）：
                // n = (cosLat·sin lon, sinLat, cosLat·cos lon) ⟹ lat = asin(n.y)、lon = atan2(n.x, n.z)。
                float closeLat = c.centerLat, closeLon = c.centerLon;
                if (sum.sqrMagnitude > 1e-6f)
                {
                    sum.Normalize();
                    closeLat = Mathf.Asin(Mathf.Clamp(sum.y, -1f, 1f)) * Mathf.Rad2Deg;
                    closeLon = Mathf.Atan2(sum.x, sum.z) * Mathf.Rad2Deg;
                }

                foreach (var sc in scales)
                {
                    bool close = sc.suffix.Length > 0;
                    float aimLat = close ? closeLat : c.centerLat;
                    float aimLon = close ? closeLon : c.centerLon;

                    AimAt(aimLat, aimLon, out float pitch, out float yaw);
                    orbit.Pitch = pitch;
                    orbit.Yaw = yaw;

                    // 让光从目标法线照进去（该大陆正午）。有 SunMoonSystem 时交给它，
                    // 灯与日盘一起动；没有时才退回"只转灯"。
                    var normal = PlanetGeometry.NormalAt(aimLat, aimLon);
                    if (sky != null) sky.SetSunDirection(normal);
                    else if (sun != null) sun.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);

                    orbit.DistanceFactor = sc.factor;
                    orbit.ApplyImmediately();

                    // ⚠ 必须在摆好相机<b>之后</b>再建网格：标记尺寸按相机到地表的距离反比缩放
                    //   （见 TribeMarkerLayer.SizeZoom），相机没摆好就建，量到的是上一档的距离。
                    markers.Build();

                    var px = Capture(cam, width, height);
                    var counts = CountMarkerColors(px);
                    WritePng(px, $"unity_continent_{c.id}{sc.suffix}.png", width, height);

                    Debug.Log($"[Humen] 大陆 {c.id} {c.name} {sc.label}（对准 {aimLat:F0}°,{aimLon:F0}° · " +
                              $"pitch={pitch:F0} yaw={yaw:F0} 距离={sc.factor:F2}R）· 部落 {n} · " +
                              $"标记像素 {DescribeCounts(counts)}");
                }
            }

            orbit.Yaw = keepYaw;
            orbit.Pitch = keepPitch;
            orbit.DistanceFactor = keepDist;
            orbit.ApplyImmediately();
            if (sky != null) sky.SetSunDirection(keepSunDir);
            else if (sun != null) sun.transform.rotation = Quaternion.LookRotation(-keepSunDir, Vector3.up);
        }

        /// <summary>
        /// 把相机摆到正对某经纬度。
        ///
        /// 由 <see cref="OrbitCamera"/> 的算式，相机相对球心的偏移方向是
        /// <c>(−cosθ·sin y, sinθ, −cosθ·cos y)</c>（θ=pitch，y=yaw）；
        /// 而球面上 (lat,lon) 的外法线是 <c>(cosLat·sin lon, sinLat, cosLat·cos lon)</c>
        /// （见 <see cref="PlanetGeometry"/>）。令两者相等即得：
        /// <c>pitch = lat</c>、<c>yaw = 180 + lon</c>。
        ///
        /// 这两个式子是<b>从代码反解出来的</b>，不是试出来的 —— 换了相机公式就得重推。
        /// </summary>
        private static void AimAt(float latDeg, float lonDeg, out float pitch, out float yaw)
        {
            pitch = latDeg;
            yaw = 180f + lonDeg;
        }

        /// <summary>
        /// 数一数画面上各档标记各占多少像素。
        ///
        /// 与差分法互补：差分只说"这一层画了东西"，这里说"画的是哪一档"。
        /// 之所以能直接比色，是因为材质走的是 Unlit，渲出来的像素就是材质本色
        /// （实测村落绿渲出来正是 (115,217,107)，与 <see cref="MarkerMaterials.Colors"/> 逐位相同）。
        /// 若将来换成受光材质，这里会整体失配 —— 那时改成差分＋连通域更稳。
        /// </summary>
        private static int[] CountMarkerColors(Color32[] px)
        {
            int k = TribeMarkerLayer.SubMeshCount;
            var want = new Color32[k];
            for (int i = 0; i < k; i++) want[i] = MarkerMaterials.Colors[i];

            var n = new int[k];
            foreach (var p in px)
            {
                for (int i = 0; i < k; i++)
                {
                    if (System.Math.Abs(p.r - want[i].r) <= 12 &&
                        System.Math.Abs(p.g - want[i].g) <= 12 &&
                        System.Math.Abs(p.b - want[i].b) <= 12) { n[i]++; break; }
                }
            }
            return n;
        }

        /// <summary>把一档档的像素数写成一行读数。档名取自 WorldView.StageNames，改阶段时不会漏改这里。</summary>
        private static string DescribeCounts(int[] counts)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < counts.Length; i++)
            {
                string name = i < WorldView.StageNames.Length ? WorldView.StageNames[i] : "农田";
                if (i == TribeMarkerLayer.FieldSubMesh) name = "农田";
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append($"{name} {counts[i]}");
            }
            return sb.ToString();
        }

        /// <summary>把图层按现行代码重画。网格是烘进场景文件的，不重画就只能验到上一次的代码。</summary>
        private static void RebuildLayers()
        {
            var rivers = Object.FindFirstObjectByType<RiverLayer>();
            if (rivers != null) { rivers.Load(); rivers.Build(); }

            var territory = Object.FindFirstObjectByType<TerritoryLayer>();
            if (territory != null) { territory.Load(); territory.Build(); }

            var routes = Object.FindFirstObjectByType<TradeRouteLayer>();
            if (routes != null) { routes.Load(); routes.Build(); }

            var markers = Object.FindFirstObjectByType<TribeMarkerLayer>();
            if (markers != null) { markers.Load(); markers.Build(); }

            // ⚠️ 太阳与月亮也必须在这里重建 —— 编辑器在<b>非 Play</b> 状态下不跑 Awake，
            //    而 SunMoonSystem 是在 Awake 里造这两个天体的。不补这一步的话，
            //    取证图里只有一颗孤零零的蓝星，太阳和月亮<b>根本不存在</b>，
            //    且一句错都不报。这个坑本组件已经踩过一次（第一版预览图就是这样）。
            var sky = Object.FindFirstObjectByType<SunMoonSystem>();
            if (sky != null) sky.EnsureBuilt();
        }

        /// <summary>
        /// 先空渲一帧丢掉，让渲染管线把该建的状态建起来。
        /// 与 <c>SettlementSceneBuilder.WarmUp</c> 是同一件事 —— 那两个入口各有一份，
        /// 是因为它们的 <c>Capture</c> 也是各写各的（都是 static，互不可见）。
        /// </summary>
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

        /// <summary>
        /// 挑一个月亮落在画面里的公转相位。<b>全周扫描</b>，返回第一个满足条件的角度；
        /// 扫不到返回 <see cref="float.NaN"/>（不静默地随便给一个）。
        ///
        /// 为什么扫而不是"对着相机解析地算"：月亮的轨道面有 5.1° 倾角，
        /// 解析式要一并考虑投影与视锥的非对称（FOV 是<b>竖直</b>的，横向还宽 1.78 倍）。
        /// 扫 72 个相位只要几毫秒，而且判据用的就是最终真正作数的那个判据
        /// （<see cref="Camera.WorldToViewportPoint"/>）—— 算出来"应该在画面里"
        /// 与"确实在画面里"是两回事，这里直接量后者。
        /// </summary>
        private static float PickVisibleMoonPhase(SunMoonSystem sky, Camera cam)
        {
            const float margin = 0.12f;   // 内缩一圈，免得月亮贴着边缘只露半个
            for (int deg = 0; deg < 360; deg += 5)
            {
                sky.SetMoonAngleDeg(deg);
                Vector3 v = cam.WorldToViewportPoint(sky.MoonPosition);
                if (v.z > 0f && v.x > margin && v.x < 1f - margin
                             && v.y > margin && v.y < 1f - margin)
                    return deg;
            }
            return float.NaN;
        }

        private static Color32[] Capture(Camera cam, int width, int height)
        {
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

        /// <summary>逐像素差。容差 8 —— 抗锯齿与半透明混合会让边缘差几个色阶，那是同一层画出来的。</summary>
        private static int CountDiff(Color32[] a, Color32[] b)
        {
            if (a.Length != b.Length) return -1;
            int n = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (System.Math.Abs(a[i].r - b[i].r) > 8 ||
                    System.Math.Abs(a[i].g - b[i].g) > 8 ||
                    System.Math.Abs(a[i].b - b[i].b) > 8) n++;
            }
            return n;
        }

        private static void WritePng(Color32[] px, string outFile, int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.SetPixels32(px);
            tex.Apply();

            var bytes = tex.EncodeToPNG();
            var full = Path.GetFullPath(outFile);
            File.WriteAllBytes(full, bytes);

            Object.DestroyImmediate(tex);

            Debug.Log($"[Humen] 预览图已写出：{full}  ({width}×{height}, {bytes.Length} bytes)");
        }
    }
}
