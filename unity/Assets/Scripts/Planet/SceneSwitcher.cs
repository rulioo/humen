using UnityEngine;
using UnityEngine.SceneManagement;

namespace Humen.Planet
{
    /// <summary>
    /// 行星视图 ⇄ 聚落视图的切换 —— 也就是作者要的那个「放大看到每一片大陆，
    /// 看到人造建筑、修的路、农田」的<b>下降动作本身</b>。
    ///
    /// 两个场景各挂一份：
    /// · 行星场景里 —— <b>点</b>一个部落标记即选中该部落，按 <c>Tab</c> 降落；
    ///   没点过则落到当年<b>人口最多</b>的那个（保证一按下去就有东西可看）。
    /// · 聚落场景里 —— 按 <c>Tab</c> 回到行星，并且<b>把年份带回去</b>。
    ///
    /// ⚠️ 为什么带年份是必须的：年份是整个演示里唯一的"时间轴"，两个场景各有一份；
    /// 不带的话，从公元 2125 年的工业城回到行星会跳回默认年份，
    /// 用户会以为自己按错了键。**跨场景能丢的东西很多，但时间不能丢。**
    /// </summary>
    [DisallowMultipleComponent]
    // ⚠️ 必须早于 SettlementView / WorldTimeline 的 Awake 跑 —— 理由见 Awake 里的注释。
    //    改了这个数字，降落就会落在错误的部落与年份上，而且不会报错。
    [DefaultExecutionOrder(-100)]
    public sealed class SceneSwitcher : MonoBehaviour
    {
        public const string PlanetScene = "Planet";
        public const string SettlementScene = "Settlement";

        [Header("操作")]
        [Tooltip("切换场景的键。")]
        public KeyCode SwitchKey = KeyCode.Tab;

        [Tooltip("点选部落用的鼠标键（0 = 左键）。")]
        public int PickButton = 0;

        [Tooltip("点选的最小容差（像素）。标记很小时按它算。")]
        public float PickPixels = 8f;

        [Tooltip("按下到松开之间的位移（像素）超过它就当作「拖拽」，不点选。")]
        public float PickDragSlop = 6f;

        [Header("提示")]
        public bool ShowHint = true;

        private WorldTimeline _timeline;
        private TribeMarkerLayer _markers;
        private SettlementView _settlement;
        private GlobeDrag _globe;
        private SunMoonSystem _sky;
        private Camera _cam;

        private string _selected;
        private string _note = "";
        private GUIStyle _style;

        private static bool InSettlement =>
            SceneManager.GetActiveScene().name == SettlementScene;

        private void Awake()
        {
            _timeline = FindFirstObjectByType<WorldTimeline>();
            _markers = FindFirstObjectByType<TribeMarkerLayer>();
            _settlement = FindFirstObjectByType<SettlementView>();
            _globe = FindFirstObjectByType<GlobeDrag>();
            _sky = FindFirstObjectByType<SunMoonSystem>();
            _cam = Camera.main;

            // ⚠️ 本组件 executionOrder = −100，故这里跑在 SettlementView.Awake <b>之前</b>。
            //    此时 SettlementView 还没 Load/Build，改它的字段会被它随后的 Awake 采用 ——
            //    这正是"降落能落在指定部落与年份"的全部机制。
            //    若把这段挪到 Start，SettlementView 已经照默认值建完了，改了还得重建，
            //    而且第一次画面会先闪一下错误的聚落。
            if (_settlement != null)
            {
                _settlement.TribeId = LandingState.TribeId;
                _settlement.Year = LandingState.Year;
            }
        }

        private void Start()
        {
            // WorldTimeline.Awake 会把 Year 拉到最后一年，而 Awake 早于 Start，
            // 故"带回来的年份"只能在 Start 里再盖一次。盖完立刻 Apply，让标记跟上。
            if (_timeline != null && LandingState.HasYear)
                _timeline.SetYear(LandingState.Year);

            _selected = LandingState.TribeId;
        }

        private void Update()
        {
            if (Input.GetKeyDown(SwitchKey))
            {
                Switch();
                return;
            }

            // 只在行星视图里支持点选 —— 聚落视图里没有可点的部落标记。
            //
            // ⚠️ 判据从 GetMouseButtonDown 改成了 **GetMouseButtonUp + 位移门限**。
            //    左键现在兼任"拨地球"（<see cref="GlobeDrag"/>），若还在按下那一刻点选，
            //    那么每次拨球的开头都会顺手选中一个部落 —— 用户觉得自己只是在转地球。
            //    改成松手时结算、且位移小于门限才算点击：拨球不选，点击才选。
            //
            //    用**位移**而不是"是否在拖拽"来判，是因为拖拽标志位有帧序问题：
            //    松手那一帧 <see cref="GlobeDrag"/> 与这里的 Update 谁先跑不确定，
            //    读标志位会随执行顺序翻转；而累计位移是单调的，怎么排都一样。
            if (!InSettlement && _cam != null && _markers != null
                && Input.GetMouseButtonUp(PickButton)
                && (_globe == null || _globe.DragPixels <= PickDragSlop)
                && _markers.TryPick(_cam, Input.mousePosition, PickPixels, out string id))
            {
                _selected = id;
                LandingState.TribeId = id;
                _note = $"已选中 {id}";
            }
        }

        private void Switch()
        {
            if (InSettlement)
            {
                // 回行星：把当前年份与部落带回去，接着看。
                if (_settlement != null)
                {
                    LandingState.Year = _settlement.Year;
                    LandingState.TribeId = _settlement.TribeId;
                }
                LandingState.HasYear = true;
                SceneManager.LoadScene(PlanetScene);
                return;
            }

            double year = _timeline != null ? _timeline.Year : LandingState.Year;
            string tribe = _selected;
            if (string.IsNullOrEmpty(tribe)) tribe = MostPopulous(year);

            if (string.IsNullOrEmpty(tribe))
            {
                // 这一年的世界还只有地理、没有部落（时间轴拉到最早那段）。
                _note = $"{WorldTimeline.FormatYear(year)} 还没有任何部落可降落";
                return;
            }

            LandingState.TribeId = tribe;
            LandingState.Year = year;
            LandingState.HasYear = true;
            SceneManager.LoadScene(SettlementScene);
        }

        /// <summary>当年人口最多的部落。用它当"随手一按"的落点。</summary>
        private string MostPopulous(double year)
        {
            var w = _markers != null ? _markers.World : null;
            if (w == null || !w.Loaded) return null;

            string best = null;
            double bestPop = 0;
            foreach (var t in w.File.tribes)
            {
                double p = WorldView.PopulationAt(t, year);
                if (p > bestPop) { bestPop = p; best = t.id; }
            }
            return best;
        }

        private void OnGUI()
        {
            if (!ShowHint) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = false };
                _style.normal.textColor = Color.white;
            }

            // 读各自的真实年份，不读 LandingState —— 那个只在<b>切换的那一刻</b>是准的。
            string body = InSettlement
                ? $"{SceneManager.GetActiveScene().name}（聚落）\n" +
                  $"部落 {(_settlement != null ? _settlement.TribeId : LandingState.TribeId)}\n" +
                  $"{WorldTimeline.FormatYear(_settlement != null ? _settlement.Year : LandingState.Year)}\n\n" +
                  $"[{SwitchKey}] 回行星视图"
                : $"{SceneManager.GetActiveScene().name}（行星）\n" +
                  $"{WorldTimeline.FormatYear(_timeline != null ? _timeline.Year : LandingState.Year)}\n" +
                  $"选中：{(_selected ?? "—")}\n" +
                  $"自转：{(_globe != null && _globe.AutoRotating ? "开" : "停")}（R 切换）\n\n" +
                  $"左键拖拽 → 拨地球（地表跟手）\n" +
                  $"右键拖拽 → 远近\n中键拖拽 → 转视角\n滚轮 → 远近\n" +
                  $"左键点部落 → 选中\n[{SwitchKey}] 降落看看";

            if (_sky != null)
            {
                body += $"\n{_sky.StatusLine}";
                // 月亮在不在画面里，必须能读出来。2.6R 的轨道下它只有约 27% 的时间在视锥内，
                // 剩下的时候"天上有月亮"与"没有月亮"在画面上**一模一样** ——
                // 而这正是本项目真栽过的那个跟头：图里没有月亮，却被当成有。
                // 一行字把"它在画面外"与"它没被造出来"分开。
                body += "\n月亮：" + MoonSightingText();
                // 太阳也要读得出来，而且比月亮更紧要：v0.28 起太阳在 1012R，
                // 默认第一屏的日盘**必然**不在画面里（日盘在画面里 ⟺ 蓝星是暗的，
                // 见 SunMoonSystem 类注释里的 δ + φ = 180° − ε，ε≈0.14°）。
                // 也就是说"首屏没有太阳"是**正常态**，不是故障 —— 而它跟"太阳压根没被造出来"
                // 在截图上长得一模一样，正是本项目栽过的那款跟头。所以连偏角一起报。
                body += "\n太阳：" + SunSightingText();
            }

            if (!string.IsNullOrEmpty(_note)) body += $"\n\n{_note}";

            // ⚠️ 面板高度必须**量着文字来**。原来写死 200 px，而这段文字是 12 行左右 ——
            //    最后两行（其中就有月亮相位那行）被裁在面板外面。裁掉的东西看不出来，
            //    只会让人以为"没显示"或者"没这回事"。
            Vector2 size = _style.CalcSize(new GUIContent(body));
            float h = size.y + 16f;
            float w = Mathf.Max(320f, size.x + 20f);

            // ⚠️ 画在**左下角**。WorldTimeline 的年份/阶段/时间控制画在左上角 (14,10)、宽 720；
            //    两块面板原来都挤在左上角，字直接叠在一起 ——
            //    实测截图里 "公元 2,125 年" 压着 "Planet（行星）"、
            //    "游群 1 · 村落 20 · 铁器 48 · 工业 12" 压着 "选中：3.4.2"，两个都读不了。
            //    左上角归"世界状态"，左下角归"操作说明"，各占一角就不会再撞上。
            var rect = new Rect(12f, Mathf.Max(8f, Screen.height - h - 12f), w, h);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f), body, _style);
        }

        /// <summary>
        /// 月亮此刻的处境，写成一行字。
        ///
        /// <b>四种"看不见"要分得开</b>，理由写在 <see cref="SunMoonSystem.MoonSighting"/> 上 ——
        /// 一句话：它们在截图上长得一模一样，而"分不开"正是本工程把太阳认成月亮、
        /// 并把这个结论写进报告的那条路。
        ///
        /// ⚠️ <b>"被蓝星挡住"这一态是 v0.27 才补上的，而它当场就抓到了一个真问题：</b>
        ///    新机位下开场的月亮恰好躲在蓝星背后，屏幕上没有月亮，
        ///    而旧的两态判定（只看视口）报告的是「在画面里」。
        ///    判据本身（投影到 viewport）原来是对的、注释里也写明了理由，
        ///    漏的只是"投影在视口内"→"真的看得见"之间那一步。
        /// </summary>
        private string MoonSightingText()
        {
            if (_sky == null) return "（场景里没有 SunMoonSystem）";
            switch (_sky.SightMoon(_cam))
            {
                case SunMoonSystem.MoonSighting.NotBuilt:
                    return "**根本没造出来**（Awake 没跑？—— 这不该发生，是个 bug）";
                case SunMoonSystem.MoonSighting.OffScreen:
                    return "已转到画面外（一圈约 27 分钟，会转回来）";
                case SunMoonSystem.MoonSighting.BehindPlanet:
                    return "**正躲在蓝星背后**（在视锥内，但被挡住 —— 会自己转出来）";
                default:
                    return "在画面里";
            }
        }

        /// <summary>
        /// 太阳此刻的处境，写成一行字。与月亮同一套思路，但多一态（"在镜头背后"）——
        /// v0.28 之后那是太阳的<b>常态</b>，不是异常。
        ///
        /// ⚠️ 这一行里<b>偏角比状态词重要</b>。状态词只说"看不见"，偏角则把
        /// 相机-蓝星-太阳那个三角形的内角和直接摆出来（<c>δ + φ = 180° − ε</c>，
        /// ε 是太阳的视差角 ≈0.14°，默认 φ=55° ⟹ <b>124.86°</b>）：
        /// 往夜面拖，这个数会一路减到 45.7° 以下 —— 那一刻日盘进画面，
        /// 而<b>同一批读数里蓝星应当同时变暗</b>。两者同向出现，才证明太阳真的在跟着拨；
        /// 只看"日盘进画面了"不够，因为把世界冻住也能得到一张好看的图。
        /// 角度在出画、乃至退到镜头背后之后仍然有定义，所以它是唯一能把整条轨迹读完的量。
        /// </summary>
        private string SunSightingText()
        {
            if (_sky == null) return "（场景里没有 SunMoonSystem）";
            string off = $"（离视轴 {_sky.SunOffAxisDeg(_cam):F1}°）";
            switch (_sky.SightSun(_cam))
            {
                case SunMoonSystem.SunSighting.NotBuilt:
                    return "**根本没造出来**（Awake 没跑？—— 这不该发生，是个 bug）";
                case SunMoonSystem.SunSighting.BehindCamera:
                    return $"**在镜头背后**{off} —— 默认机位的正常态，往夜面拖会升进画面";
                case SunMoonSystem.SunSighting.OffScreen:
                    return $"已转到画面外{off}（视锥横向半角 45.7°，再往夜面拖就进来了）";
                case SunMoonSystem.SunSighting.BehindPlanet:
                    return $"**正躲在蓝星背后**{off}（在视锥内，但被挡住）";
                default:
                    return $"在画面里{off} —— ⚠️ 此刻蓝星是暗的（日盘可见 ⟺ 蓝星背光）";
            }
        }
    }
}
