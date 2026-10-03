using UnityEngine;

namespace Humen.Planet
{
    /// <summary>
    /// 让这个世界<b>动起来</b> —— 作者要的是"活生生的世界"，而一张静止的球
    /// 无论画得多细，都还是一张图。这个脚本负责把年份往前推，并让各图层跟着重画。
    ///
    /// 播放模式下：按 <b>空格</b> 开关时间流动、<b>左右方向键</b>手动拖动年份、
    /// <b>Home/End</b> 跳到起点/终点。画面上左上角有一行读数。
    ///
    /// ⚠️ 时间轴推进与 §10.6 的确定性约束无关 —— 它只改"看哪一年"，
    /// 不改任何模拟结果。模拟早在 `humen evolve` 里跑完并落库了，
    /// Unity 侧从头到尾只是<b>读文件</b>（见 <see cref="WorldView"/>）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldTimeline : MonoBehaviour
    {
        [Header("图层")]
        public TribeMarkerLayer Markers;
        public RiverLayer Rivers;
        public TerritoryLayer Territory;
        public TradeRouteLayer Routes;

        [Header("时间")]
        [Tooltip("当前年份。区间从世界快照的 timeline 读，不必手填。")]
        public double Year = 2125;

        [Tooltip("播放时每秒推进多少年。取 3000 —— 三十万年约一百秒走完，一局看得完。")]
        public double YearsPerSecond = 3000.0;

        [Tooltip("打开即自动播放。")]
        public bool Playing = true;

        [Header("读数")]
        [Tooltip("在屏幕左上角画一行年份与人口读数。")]
        public bool ShowOverlay = true;

        private GUIStyle _style;

        // 上一帧的相机-地表距离。标记尺寸随它反比缩放（见 TribeMarkerLayer.SizeZoom），
        // 故滚轮推拉之后必须重建网格，否则尺寸会停在推拉前的档位上。
        private float _lastCamToSurface = -1f;

        private void Start()
        {
            if (Markers == null) Markers = GetComponentInChildren<TribeMarkerLayer>();
            if (Rivers == null) Rivers = GetComponentInChildren<RiverLayer>();
            if (Territory == null) Territory = GetComponentInChildren<TerritoryLayer>();
            if (Routes == null) Routes = GetComponentInChildren<TradeRouteLayer>();

            if (Markers != null && Markers.Ready)
            {
                var tl = Markers.Timeline;
                if (tl != null)
                {
                    // 默认停在终点：那是快照里唯一"信息完整"的一年
                    // （材料等级、农田都是终值），往前拖是回溯。
                    Year = tl.endYear;
                }
            }
            Apply();
        }

        private void Update()
        {
            HandleInput();
            RescaleIfZoomed();

            if (Playing && Markers != null && Markers.Ready)
            {
                var tl = Markers.Timeline;
                Year += YearsPerSecond * Time.deltaTime;
                if (Year >= tl.endYear)
                {
                    // 走到终点就停住（而不是循环回起点）——
                    // 循环会让"最后一屏"永远看不清，而终点恰好是最该看的那一屏。
                    Year = tl.endYear;
                    Playing = false;
                }
                Apply();
            }
        }

        /// <summary>
        /// 滚轮推拉之后重建标记网格 —— 标记尺寸按相机到地表的距离反比缩放
        /// （见 <see cref="TribeMarkerLayer.SizeZoom"/>），相机动了尺寸就该跟着动。
        ///
        /// 用<b>相对</b>阈值（变化超过 3% 才重建）而不是逐帧重建：
        /// 逐帧重建在推拉时每帧都要重算 202 个部落的位置与尺寸，白烧 CPU；
        /// 而 3% 以下的尺寸差肉眼看不出来。
        /// </summary>
        private void RescaleIfZoomed()
        {
            if (Markers == null || !Markers.Ready) return;

            var cam = Camera.main;
            if (cam == null) return;

            float toSurface = Vector3.Distance(cam.transform.position, Markers.transform.position) - Markers.Radius;
            if (toSurface < 0.001f) return;

            if (_lastCamToSurface > 0f && Mathf.Abs(toSurface - _lastCamToSurface) / _lastCamToSurface < 0.03f)
                return;

            _lastCamToSurface = toSurface;
            Markers.Build();
            // 路的线宽同样按相机到地表的距离反比缩放（见 TradeRouteLayer.SizeZoom），
            // 故推拉之后也必须重建 —— 只重建标记的话，标记变了大小而路没变，
            // 画面上会出现"地图图钉缩了、路却还是原来那么粗"的错位。
            if (Routes != null && Routes.Ready) Routes.Build();
        }

        private void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.Space)) Playing = !Playing;

            if (Markers == null || !Markers.Ready) return;
            var tl = Markers.Timeline;

            bool changed = false;

            if (Input.GetKey(KeyCode.LeftArrow))
            {
                // 按住时的步长按"整段历史的千分之一"取，故区间无论多长手感都一致
                Year -= (tl.endYear - tl.startYear) * Time.deltaTime * 0.5;
                Playing = false;
                changed = true;
            }
            if (Input.GetKey(KeyCode.RightArrow))
            {
                Year += (tl.endYear - tl.startYear) * Time.deltaTime * 0.5;
                Playing = false;
                changed = true;
            }
            if (Input.GetKeyDown(KeyCode.Home)) { Year = tl.startYear; Playing = false; changed = true; }
            if (Input.GetKeyDown(KeyCode.End)) { Year = tl.endYear; Playing = false; changed = true; }

            if (changed)
            {
                Year = System.Math.Max(tl.startYear, System.Math.Min(tl.endYear, Year));
                Apply();
            }
        }

        /// <summary>把当前年份推给各图层。</summary>
        public void Apply()
        {
            if (Markers != null && Markers.Ready) Markers.SetYear(Year);
            if (Territory != null && Territory.Ready) Territory.SetYear(Year);
            // ⚠ 道路必须排在疆域与标记<b>之后</b>：它按相机到地表的距离反比缩放线宽
            //   （见 TradeRouteLayer.SizeZoom），而那个距离在滚轮推拉之后才变 ——
            //   这里紧跟标记重建，读到的才是这一帧真正的相机档位。
            if (Routes != null && Routes.Ready) Routes.SetYear(Year);
        }

        public void SetYear(double year)
        {
            Year = year;
            Apply();
        }

        private void OnGUI()
        {
            if (!ShowOverlay || Markers == null || !Markers.Ready) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 15,
                    alignment = TextAnchor.UpperLeft,
                    richText = false,
                };
                _style.normal.textColor = new Color(0.92f, 0.95f, 1f);
            }

            var tl = Markers.Timeline;
            string year = FormatYear(Year);
            string line2 = $"部落 {Markers.VisibleTribeCount} 个";

            // 阶段分布。这是作者最该一眼看到的东西 —— 而它此前是看不见的：
            // 早先按 materialTier 上色，而 tier 量的是地质机会（有无本地矿藏），
            // 在这个世界上恰好与发达程度负相关，画面是反的。见 WorldView.StageAt。
            var sc = Markers.StageCounts();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < sc.Length; i++)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append($"{WorldView.StageNames[i]} {sc[i]}");
            }
            string line3 = sb.ToString();

            // 疆域与道路的读数。两者都是"文明铺开到地上"的量：
            // 部落数只说明"有多少个点"，而这两个数说明"占了多少地、连了多少线" ——
            // 拖动时间轴时，它们从 0 开始涨，是画面上最该被看见的那条曲线。
            var sb2 = new System.Text.StringBuilder();
            if (Territory != null && Territory.Ready) sb2.Append($"疆域 {Territory.TerritoryCount} 块");
            if (Routes != null && Routes.Ready)
            {
                if (sb2.Length > 0) sb2.Append(" · ");
                sb2.Append($"道路 {Routes.RouteCount} 条（连着 {Routes.ConnectedTribeCount} 个聚落）");
            }
            string line4 = sb2.ToString();

            // 复位键必须写在这一行里。它是**唯一**告诉玩家"转乱了怎么回来"的地方 ——
            // v0.27 加了 F 复位，若只写在代码里，玩家转乱之后是找不到它的。
            string line5 = (Playing ? "▶ 时间流动中" : "⏸ 已暂停")
                         + "（空格播放/暂停 · ←→ 拖动 · Home/End 跳转 · F 复位地球）";

            // 描边：星球上有大片浅色地表，纯白字会糊在里面
            var shadow = new GUIStyle(_style);
            shadow.normal.textColor = new Color(0f, 0f, 0f, 0.65f);

            var rect = new Rect(14, 10, 720, 120);
            string text = year + "\n" + line2 + "\n" + line3 + "\n"
                        + (line4.Length > 0 ? line4 + "\n" : "") + line5;
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, shadow);
            GUI.Label(rect, text, _style);
        }

        /// <summary>
        /// 与 Core 侧 <c>WorldConfig.FormatYear</c> 一致的写法。
        /// 刻意不引用 Core（引用不了），但<i>结果</i>必须一致 ——
        /// 否则同一个年份在日志里与画面上会显示成两个样子。
        /// </summary>
        public static string FormatYear(double year)
        {
            if (year < 0) return $"公元前 {System.Math.Round(-year):N0} 年";
            return $"公元 {System.Math.Round(year):N0} 年";
        }
    }
}
