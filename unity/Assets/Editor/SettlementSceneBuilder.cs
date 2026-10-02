using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Humen.Planet;

namespace Humen.EditorTools
{
    /// <summary>
    /// 用代码生成 <c>Assets/Scenes/Settlement.unity</c> —— C 级「聚落视景」（M4b-3）。
    ///
    /// 与 <see cref="PlanetSceneBuilder"/> 分开一个场景，是因为 §10.1 说的这件事：
    /// B 级到 C 级<b>不是缩放而是"降落"</b>，两者差 6371 倍。
    /// 行星场景的坐标单位是「1000 单位 = 6371 km」，聚落场景的单位是「1 单位 = 1 米」——
    /// 同一份场景里放不下，也不需要放。
    ///
    /// 出图入口 <see cref="RenderErasBatch"/> 是这一层的取证手段：
    /// <b>同一个部落在不同年份各渲一张</b>。作者要的是「符合进化阶段特征」，
    /// 那就必须证明"换个年份画面真的不一样"，而不是只渲一张好看的。
    /// </summary>
    public static class SettlementSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Settlement.unity";

        /// <summary>
        /// 取证用的部落：赤道大陆 <b>3.4.2</b>，23.6 万人，93 项技术（全球技术前沿），
        /// 聚落→铁→蒸汽机→文字四档全都走过。
        ///
        /// ⚠️ <b>这个常量会随每一次重跑世界而失效。</b>
        ///    上一版写的是 <c>3.11.1</c>（当次 29.6 万人）。改地理后重跑，
        ///    同一个 id 还在，但已经<b>换了命运</b>：只剩 47 项技术、8.1 万人，
        ///    连 steam_engine 与 writing 都没摸到 —— 拿它当"工业"那一档的取证对象，
        ///    渲染出来会和「铁器」几乎一样，而且<b>一句错都不会报</b>。
        ///    id 是稳定的，<b>id 背后的那个部落不是</b>。挑的时候按"技术前沿"去查，
        ///    不能照抄上一次的字符串。查询见 design.md §11 的重跑清单。
        /// </summary>
        public const string EvidenceTribe = "3.4.2";

        [MenuItem("Humen/重建聚落场景")]
        public static void BuildSceneMenu() => BuildScene();

        public static void BuildBatch() => BuildScene();

        /// <summary>
        /// 建场景 + 逐年份出图，一次跑完。
        /// 单为省一次 Unity 启动（每次约半分钟），改动这个文件时用得最多。
        /// </summary>
        public static void BuildAndRenderBatch()
        {
            BuildScene();
            RenderEras(1280, 720);
        }

        /// <summary>
        /// 重建两个场景 → 出四张演化图 → 编出可执行程序，一遍跑完。
        /// 一次 Unity 启动只接受一个 -executeMethod，而这三件事都要在同一个
        /// 编辑器会话里做完（场景是刚生成的，资产库也是刚刷新的）。
        /// </summary>
        public static void BuildEverythingBatch()
        {
            PlanetSceneBuilder.BuildScene();
            BuildScene();
            RenderEras(1280, 720);
            PlayerBuilder.BuildWindows();
        }

        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            EnsureMaterialAssets();

            var root = new GameObject("Settlement");
            var view = root.AddComponent<SettlementView>();
            view.TribeId = EvidenceTribe;
            view.Year = 2125;
            view.Load();
            view.Build();

            if (!view.Ready)
            {
                Debug.LogWarning($"[Humen] 聚落视图没数据：{view.Error}");
            }
            else
            {
                Debug.Log($"[Humen] 聚落视图就绪：{view.LastReport}");
            }

            // ── 太阳 ─────────────────────────────────────────────
            // 与行星那盏同样的理由（平行光），但这里<b>要开阴影</b>：
            // 聚落是人尺度，房子之间没有影子就看不出高低与前后，
            // 一排房子会像贴在地上的一层贴纸 —— 那正是作者嫌弃的"一张图片"。
            var sunGo = new GameObject("Sun");
            var light = sunGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.0f, 0.96f, 0.88f);   // 略暖，像低角度的日光
            light.intensity = 1.35f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.72f;
            sunGo.transform.rotation = Quaternion.Euler(42f, 130f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.30f, 0.33f, 0.38f);   // 天光，别让暗面死黑
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            // ⚠️ 雾色必须**等于天色**。差一点点，地平线上就会出现一条色带，
            //    一眼看出这是块板子；相等则地面在远处"化"进天空里。
            RenderSettings.fogColor = new Color(0.62f, 0.70f, 0.82f);
            RenderSettings.fogStartDistance = 900f;   // 远处淡出，给聚落一点空气感
            RenderSettings.fogEndDistance = 4200f;    // 地面尺寸必须让它落在雾外，见 SettlementView.BuildGround

            // ── 相机 ─────────────────────────────────────────────
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraFlags();
            cam.backgroundColor = new Color(0.62f, 0.70f, 0.82f);   // 白天的天色
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 12000f;
            cam.fieldOfView = 50f;

            FrameCamera(cam, view, 1280, 720);

            // ── 场景切换 ─────────────────────────────────────────
            camGo.AddComponent<SceneSwitcher>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            PlayerBuilder.EnsureBuildSettings();

            Debug.Log($"[Humen] 聚落场景已生成：{ScenePath}");
        }

        private static CameraClearFlags CameraFlags() =>
            RenderSettings.skybox == null ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;

        /// <summary>
        /// 把相机摆到能看全整个聚落。距离由<b>布局的实际半径</b>算，不是写死的 ——
        /// 一个 40 人的营地和一座 30 万人的城差二十倍，写死必然有一头是错的。
        /// </summary>
        private static void FrameCamera(Camera cam, SettlementView view, int width, int height)
        {
            // 半径取**布局的**（建成区，含田圈）。不取网格包围盒 —— 见 SettlementView.LastRadius。
            float radius = view.LastRadius > 1f ? view.LastRadius : EstimateRadius(view);

            // ⚠️ 取景距离是**算出来的，不是调出来的**。
            //
            // 聚落是一块躺着的盘子：水平方向的全宽就是直径 2R，竖直方向被俯角压成 2R·sin(俯角)。
            // 所以**约束在水平方向**，距离要按水平半 FOV 反推：
            //     d = R / tan(hHalf · fill)
            // 这样"聚落占画面几成宽"是一个能直接写下来的数（fill），
            // 换个部落、换个年份都自适应，而不是对着一座城调好、到另一座就报废。
            //
            // 俯角 20° 也是反推的：必须 < 25°（半 FOV 50° 的一半），画面上方才留得出天空。
            // 第一版等效俯角 27.8°，地平线被顶到框外，整幅成了"天上一块地"，又回到一张图片；
            // 但也不能压得太低，太低就看不出房子的高矮。
            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * (width / (float)height));
            const float fill = 0.85f;
            float dist = radius / Mathf.Tan(hHalf * fill);

            const float elev = 20f * Mathf.Deg2Rad;
            const float azim = 128f * Mathf.Deg2Rad;

            Vector3 focus = new Vector3(0f, radius * 0.06f, 0f);
            Vector3 off = new Vector3(Mathf.Cos(elev) * Mathf.Cos(azim),
                                      Mathf.Sin(elev),
                                      Mathf.Cos(elev) * Mathf.Sin(azim)) * dist;
            cam.transform.position = focus + off;
            cam.transform.rotation = Quaternion.LookRotation(focus - cam.transform.position, Vector3.up);
        }

        /// <summary>没有布局时给个保守值，免得相机掉进地里。</summary>
        private static float EstimateRadius(SettlementView view)
        {
            var f = view.GetComponent<MeshFilter>();
            if (f != null && f.sharedMesh != null)
            {
                var b = f.sharedMesh.bounds;
                float r = Mathf.Max(b.extents.x, b.extents.z);
                if (r > 1f) return r;
            }
            return 300f;
        }

        private static void EnsureMaterialAssets()
        {
            Directory.CreateDirectory(SettlementMaterials.Folder);
            for (int i = 0; i < SettlementMaterials.AssetNames.Length; i++)
            {
                string path = $"{SettlementMaterials.Folder}/{SettlementMaterials.AssetNames[i]}";
                var color = SettlementMaterials.Colors[i];

                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing != null)
                {
                    // 同 PlanetSceneBuilder：**必须更新而不是跳过**。
                    // 跳过的话改了 Colors[] 再重建，材质还是旧的，且一句错都不报。
                    if (existing.color != color)
                    {
                        existing.color = color;
                        if (existing.HasProperty("_BaseColor")) existing.SetColor("_BaseColor", color);
                        EditorUtility.SetDirty(existing);
                    }
                    continue;
                }

                var mat = SettlementMaterials.Create(color);
                mat.name = SettlementMaterials.AssetNames[i];
                AssetDatabase.CreateAsset(mat, path);
            }
            AssetDatabase.SaveAssets();
        }

        // ══════════════════════════════════════════════════════════════
        //  取证：同一个部落，逐年各一张
        // ══════════════════════════════════════════════════════════════

        [MenuItem("Humen/渲染聚落演化对比图")]
        public static void RenderErasMenu() => RenderEras(1280, 720);

        public static void RenderErasBatch() => RenderEras(1280, 720);

        /// <summary>
        /// 渲染 <see cref="EvidenceTribe"/> 在若干年份的样子。
        ///
        /// 年份不是随手挑的，每条都对着一个<b>技术门槛</b>（取该部落的习得年份前后）：
        /// 这样图与图之间的差别可以直接归因到某一项技术上，而不是"感觉变复杂了"。
        ///
        /// ⚠️ 实测 <b>3.4.2</b> 的关键年份（取自 <c>tech_state</c> / <c>tribe_history</c>）：
        /// <b>立族 −240,000</b> · 火 −300,000（承自祖先，早于立族）· 播种 −243,800 ·
        /// 陶窑 −238,425 · 轮 −234,275 · <b>聚落 −229,225</b> · 城市 −220,300 ·
        /// <b>铁 −209,875</b> · <b>蒸汽机 −199,925</b> · <b>文字 −197,975</b> · 活字 −196,450。
        /// 四档对应已掌握技术数 <b>19 / 29 / 52 / 63</b>，与 <see cref="WorldView.StageAt"/>
        /// 的 0/1/2/3 一一对上（判据只有三条：settlement、iron、steam_engine）。
        ///
        /// ⚠️ 第一版的「游群」取的是 <b>−250,000</b>，而它<b>早于立族年 −240,600</b> ——
        /// 那年这个部落根本还不存在，人口为 0，于是渲染出来差分 0 px。
        /// 这不是渲染坏了，是<b>年份选错了</b>；改到 −235,000（已立族、尚未有聚落技术）。
        /// 教训：挑年份要对着<b>这个部落自己的</b>时间线，不能想当然取一个"很早很早"。
        ///
        /// ⚠️ 「04_工业」原取 <b>−198,000</b>，当时writing 在 −198,600，落在它之后。
        ///    重跑后 writing 挪到 <b>−197,975</b>，−198,000 反而跑到它<b>前面</b>去了 ——
        ///    差 25 年，画面几乎不变，肉眼绝对看不出来，但"工业档已进入文字时代"这句话就成了假的。
        ///    改到 <b>−195,000</b>：蒸汽 −199,925、文字 −197,975、活字 −196,450 三者全在其前，
        ///    人口也从 1.72 万涨到 3.11 万，与上一档的差别更结实。
        ///    <b>教训：年份是相对门槛取的，门槛一动，年份必须跟着重算。</b>
        /// </summary>
        public static void RenderEras(int width, int height)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[Humen] 打不开场景 {ScenePath}；先跑一次「重建聚落场景」。");
                return;
            }

            var view = Object.FindFirstObjectByType<SettlementView>();
            var cam = Object.FindFirstObjectByType<Camera>();
            if (view == null || cam == null)
            {
                Debug.LogError("[Humen] 场景里缺 SettlementView 或相机。");
                return;
            }

            view.Load();

            // ⚠️ 先空渲一帧丢掉。批处理下相机的**第一次** Render 会漏建阴影贴图，
            //    于是第一张图的差分把<b>整帧地面</b>都算成了"聚落的贡献" ——
            //    实测第一张报 93.74%，而其余三张都在 4% 上下，差了二十倍。
            //    这属于<b>量具本身有问题</b>，比量错更危险：它看起来像个结果，
            //    会让人以为"游群那一档画得特别满"。
            WarmUp(cam, width, height);

            // 每个年份都从<b>布局的半径</b>重新取景：城比营地大得多，
            // 用同一个机位会把营地拍成一个点、把城拍出画外。
            var eras = new (double year, string tag)[]
            {
                (-235000.0, "01_游群"),     // 已立族（−240,000），尚未有聚落（−229,225）
                (-225000.0, "02_村落"),     // 聚落 −229,225 之后，铁 −209,875 之前
                (-205000.0, "03_铁器"),     // 铁 −209,875 之后，蒸汽机 −199,925 之前
                (-195000.0, "04_工业"),     // 蒸汽 −199,925、文字 −197,975、活字 −196,450 之后
            };

            foreach (var (year, tag) in eras)
            {
                view.SetYear(year);
                FrameCamera(cam, view, width, height);

                var px = Capture(cam, width, height);
                string file = $"unity_settlement_{tag}.png";
                WritePng(px, file, width, height);

                // 差分哨兵：把聚落网格整体关掉再渲一遍。
                // 与行星那边同一个理由 —— "画出来没有"不能靠肉眼在图上找，
                // 而要变成一个能进日志的数。为 0 就是没画出来。
                var rend = view.GetComponent<MeshRenderer>();
                bool had = rend != null && rend.enabled;
                if (rend != null) rend.enabled = false;
                var blank = Capture(cam, width, height);
                if (rend != null) rend.enabled = had;

                int diff = CountDiff(px, blank);
                float pct = 100f * diff / (width * (float)height);

                // ⚠️ 差分 0 有两种<b>截然不同</b>的成因，必须分开报：
                //    ① 那一年该部落还不存在（人口 0）—— 是<b>调用方选错了年份</b>；
                //    ② 该画却没画出来 —— 是<b>渲染本身有问题</b>（绕序／剔除／取景）。
                //    第一版两者共用一句"一个像素都没画出来"，长得一模一样，
                //    结果是真踩中了①（−250,000 早于立族年）却看着像②，白查一轮。
                //    <b>症状相同不等于病因相同；分不开的诊断信息等于没有。</b>
                if (diff == 0 && view.LastPopulation <= 0)
                    Debug.LogWarning($"[Humen] {tag}（{year:F0}）差分 0 px —— 该部落这一年尚未立族，" +
                                     $"没有可画的聚落（年份选错了，不是渲染的问题）。");
                else if (diff == 0)
                    Debug.LogWarning($"[Humen] {tag}（{year:F0}）差分 0 px —— 有人口 {view.LastPopulation:N0} " +
                                     $"却一个像素都没画出来，是渲染的问题。");
                else
                    Debug.Log($"[Humen] {tag}（{year:F0}）差分 {diff} px（占画面 {pct:F2}%）· {view.LastReport}");
            }
        }

        /// <summary>空渲一帧，把阴影贴图等一次性资源烘出来。见调用处的注释。</summary>
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
            File.WriteAllBytes(Path.GetFullPath(outFile), bytes);
            Object.DestroyImmediate(tex);
            Debug.Log($"[Humen] 已写出 {Path.GetFullPath(outFile)}（{width}×{height}, {bytes.Length} bytes）");
        }
    }
}
