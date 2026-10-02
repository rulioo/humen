namespace Humen.Core;

/// <summary>
/// 气候与群系的<b>唯一入口</b> —— design.md §7.6 / §7.7。
///
/// M1 曾把温度/降水/群系判定写在 <see cref="Geography"/> 里（部落生成顺带算的），
/// M2 的渲染器需要<b>同一套</b>判据给地表上色。若各写各的，迟早出现
/// 「渲染图上是雨林、数据库里是草原」的分裂。故抽出此处，
/// <see cref="Geography"/> 与渲染器都调这里，词汇表与判定顺序只有一份。
///
/// <b>为什么分类返回的是 <c>byte</c> 码而不是字符串</b>：
/// 渲染一帧要判上千万次群系。字符串 <c>switch</c> 每次都要做长度比较与哈希，
/// 在逐像素的热路径上是实打实的瓶颈；而且字符串一旦拼错（"温带落叶林" 写成
/// "温帶落叶林"）编译器不会报错，只会静默落到 default 灰色。
/// 用码值后，热路径只剩几次浮点比较，且拼写错误在编译期就没了。
///
/// <b>M1 起是降维版，M4 补齐</b>：只有 <c>T_base(纬度) + 高度递减 + 全球偏移</c> 的那三个函数
/// （<see cref="TemperatureC"/> / <see cref="RainMm"/> / <see cref="BiomeCode"/>）保留下来当
/// 回归基准与「没有地形场时的兜底」；真正的取值走 <see cref="SampleAnnual"/> ——
/// 它把 §7.6 和式里的其余各项（洋流、大陆度、迎风坡雨影、季风）都算上，
/// 地形与海陆分布由 <see cref="ClimateGrid"/> 提供。
///
/// 降维版的可见后果是「降水只随纬度成带、沙漠与雨林是横贯整圈的直线」；
/// M4 之后这些条带会被地形打碎：副热带内陆变沙漠、迎风坡变雨林、背风坡成雨影。
/// </summary>
public static class Climate
{
    /// <summary>§7.6 的气温垂直递减率（℃/km）。</summary>
    public const double LapseRateCPerKm = 6.5;

    // ══════════════════════════════════════════════════════════════
    //  群系码表 —— ★ 唯一真源。增删改都只动这一处。
    // ══════════════════════════════════════════════════════════════

    public const byte IceCap = 0;              // 冰盖
    public const byte PolarDesert = 1;         // 极地荒漠
    public const byte Tundra = 2;              // 苔原
    public const byte Desert = 3;              // 沙漠
    public const byte Savanna = 4;             // 热带稀树草原
    public const byte Steppe = 5;              // 温带草原
    public const byte Rainforest = 6;          // 热带雨林
    public const byte MonsoonForest = 7;       // 热带季雨林
    public const byte MontaneRainforest = 8;   // 热带山地雨林
    public const byte TropicalWoodland = 9;    // 热带高地疏林
    public const byte TemperateRainforest = 10;// 温带雨林
    public const byte Mediterranean = 11;      // 地中海灌丛
    public const byte TemperateForest = 12;    // 温带落叶林
    public const byte Taiga = 13;              // 针叶林
    public const byte Alpine = 14;             // 高山

    public const int BiomeCount = 15;

    /// <summary>码 → 名称。§7.7 原表 10 个词 + M1 追加的 5 个（见 design.md §7.7 注）。</summary>
    public static readonly string[] BiomeNames =
    {
        "冰盖", "极地荒漠", "苔原", "沙漠", "热带稀树草原",
        "温带草原", "热带雨林", "热带季雨林", "热带山地雨林", "热带高地疏林",
        "温带雨林", "地中海灌丛", "温带落叶林", "针叶林", "高山",
    };

    /// <summary>码 → 绘制颜色。仅供渲染，不参与任何模拟判定。</summary>
    public static readonly (byte R, byte G, byte B)[] BiomeColors =
    {
        (238, 245, 252),   // 冰盖
        (198, 203, 209),   // 极地荒漠
        (146, 154, 133),   // 苔原
        (214, 194, 138),   // 沙漠
        (184, 172, 94),    // 热带稀树草原
        (150, 158, 88),    // 温带草原
        (24, 94, 44),      // 热带雨林
        (52, 118, 58),     // 热带季雨林
        (36, 104, 58),     // 热带山地雨林
        (120, 138, 78),    // 热带高地疏林
        (40, 100, 64),     // 温带雨林
        (140, 143, 78),    // 地中海灌丛
        (74, 122, 58),     // 温带落叶林
        (44, 79, 54),      // 针叶林
        (168, 168, 163),   // 高山
    };

    public static string BiomeName(byte code)
        => code < BiomeCount ? BiomeNames[code] : "未知";

    public static (byte R, byte G, byte B) BiomeColor(byte code)
        => code < BiomeCount ? BiomeColors[code] : ((byte)128, (byte)128, (byte)128);

    /// <summary>
    /// Whittaker 式分类（§7.7）。返回码值。
    ///
    /// <b>必须带纬度参数</b>：赤道大陆（§7.2 大陆 3）部落海拔可达近 1000 m，
    /// 按 §7.6 递减率会掉到 12~20℃。只按气温判会落进「温带落叶林」，
    /// 而它实际是低纬山地林 —— 二者对农业模型的含义完全不同。
    /// </summary>
    /// <param name="elevationM">
    /// 海拔。用来把「冷是因为纬度还是因为站得高」分开（见方法内注释）。
    /// 省略时按 0 处理 —— 那只在调用方本来就没有海拔信息时才成立
    /// （<see cref="Geography.ClassifyBiome"/> 那种纯查表的老接口）；
    /// 手上<b>有</b>海拔的调用方都该传。
    /// </param>
    public static byte BiomeCodeOf(double tempC, double rainMm, double absLat, double elevationM = 0.0)
    {
        // ── 「冷」是因为纬度，还是因为海拔？──
        //
        // 这一问必须问清楚：§7.7 的词表里「冰盖 / 极地荒漠 / 苔原」与「高山」
        // 是两组词，含义完全不同 —— 前者是<b>气候带</b>，后者是<b>地形</b>。
        // 只按温度判会把两者混为一谈：本世界的陆地整体抬在 1 500 m 以上，
        // 照温度直判，45° 的高原会得到「极地荒漠」这种在 §7.7 里根本不存在的词。
        //
        // 判据取「把海拔那一份加回去之后的海平面气温」：
        // 它若已低于 0 ℃，说明这个纬度本身就冷，冷属于气候带；
        // 否则冷是站得高换来的，属高山。全球偏移（冰期）含在内，
        // 于是冰期到来时寒带会<b>自己从高纬往低纬扩</b>，不需要另写一套逻辑。
        double seaLevelTemp = tempC + LapseRateCPerKm * (elevationM / 1000.0);
        bool coldFromLatitude = seaLevelTemp < 0;

        if (tempC < -10) return coldFromLatitude ? IceCap : Alpine;
        if (tempC < 0)
            return coldFromLatitude ? (rainMm < 250 ? PolarDesert : Tundra) : Alpine;

        bool tropical = absLat < 23.5;

        if (rainMm < 250) return Desert;
        if (rainMm < 500) return tropical ? Savanna : Steppe;

        if (tropical)
        {
            if (tempC > 20) return rainMm > 1800 ? Rainforest : MonsoonForest;
            if (tempC > 12) return rainMm > 1000 ? MontaneRainforest : TropicalWoodland;
            return Alpine;
        }

        // 温带及副极地
        if (tempC > 8 && rainMm > 1400) return TemperateRainforest;
        if (tempC > 20 && rainMm < 800) return Mediterranean;
        if (tempC > 8) return TemperateForest;
        if (tempC > 0) return Taiga;
        return Tundra;
    }

    // ══════════════════════════════════════════════════════════════
    //  气候量
    // ══════════════════════════════════════════════════════════════

    /// <summary>海平面处的纬度基准气温 + 高度订正 + 全球偏移（冰期）。</summary>
    public static double TemperatureC(double latDeg, double elevationM, double globalOffsetC = 0.0)
        => Geography.BaseTemp(Math.Abs(latDeg))
         - LapseRateCPerKm * (elevationM / 1000.0)
         + globalOffsetC;

    /// <summary>年降水（mm）。降维版：只取纬向基准带。</summary>
    public static double RainMm(double latDeg) => Geography.LatitudeRainBand(Math.Abs(latDeg));

    /// <summary>群系码（降维版：只按纬度分带 + 高度递减）。</summary>
    public static byte BiomeCode(double latDeg, double elevationM, double globalOffsetC = 0.0)
        => BiomeCodeOf(TemperatureC(latDeg, elevationM, globalOffsetC),
                       RainMm(latDeg),
                       Math.Abs(latDeg),
                       elevationM);

    /// <summary>群系名。</summary>
    public static string Biome(double latDeg, double elevationM, double globalOffsetC = 0.0)
        => BiomeName(BiomeCode(latDeg, elevationM, globalOffsetC));

    // ══════════════════════════════════════════════════════════════
    //  §7.6 的其余各项（M4）
    // ══════════════════════════════════════════════════════════════
    //
    //  上面那三个 <c>TemperatureC</c> / <c>RainMm</c> / <c>BiomeCode</c> 是<b>只按纬度</b>的
    //  降维版，M1 起就在用。它们保留下来有两个用处：
    //    ① 没有地形场时（例如 <c>derive</c> 这种纯 L2 派生）仍要有个说得过去的气候；
    //    ② 作为回归基准 —— M4 之后的数值与它们对照，一眼看得出新项各自改了多少。
    //  真正的取值走下面的 <see cref="SampleAnnual"/>。
    //
    //  这几个函数的量纲与量级都标了口径，改参数时请连注释一起改。

    /// <summary>
    /// 盛行风的上风方向在经度上的符号：<b>+1 = 风自东来</b>（上风点在东侧），
    /// <b>−1 = 风自西来</b>。
    ///
    /// 按 §7.6 的三段分带：信风带（|lat| &lt; 30°）为东风，西风带（30°~60°）为西风，
    /// 极地东风带（≥ 60°）又是东风。迎风坡与雨影全看这个符号。
    /// </summary>
    public static double WindUpwindLonSign(double absLat)
        => absLat < 30.0 ? 1.0 : absLat < 60.0 ? -1.0 : 1.0;

    /// <summary>
    /// 太阳直射点纬度（度）—— §7.6：<c>declination = 23.44° × sin(2π(dayOfYear − 81)/365.25)</c>。
    /// 取 −81 是为了让最大值落在夏至（约第 172 天）附近。
    /// </summary>
    public static double DeclinationDeg(double dayOfYear)
        => ObliquityDeg * Math.Sin(2 * Math.PI * (dayOfYear - 81.0) / DaysPerYear);

    /// <summary>回归年长度（天），§7.6 的公式里用的就是它。</summary>
    public const double DaysPerYear = 365.25;

    /// <summary>
    /// 季节温度距平（℃）—— §7.6 的 <c>T_season</c>。<b>年均值为 0</b>，
    /// 所以它<b>不参与</b> <see cref="BiomeCodeOf"/> 的判定（Whittaker 用的是年均温），
    /// 只用于「最冷月气温 / 无霜期」这类指标 —— 那才是 §6.1 农业禀赋与
    /// INV-15「寒带大陆不得解锁任何农业变体」真正依赖的量。
    ///
    /// 形状取 <c>sin(δ)·sin(φ)</c>，即日射的季节分量：它在赤道<b>连续地</b>趋近于 0，
    /// 南北半球自动反相。若改用 <c>±振幅(|φ|)</c> 那种写法，赤道两侧会留下
    /// 一个 2×振幅的台阶 —— 而赤道恰恰是大陆 3 的所在地，一个凭空的气温断层
    /// 会让它的南北两端分出两个气候带，纯属人为产物。
    ///
    /// 增益 45 是<b>标定</b>出来的，不是拍的：|φ| = 30° 处振幅约 9 ℃、60° 处约 15 ℃，
    /// 与真实的中纬 / 高纬季节差对得上。大陆度再乘上去 —— 深海性气候的年较差小，
    /// 大陆腹地的大（西伯利亚可到 ±25 ℃），故离海 1500 km 封顶 ×1.9。
    /// </summary>
    public static double SeasonalAnomalyC(double latDeg, double dayOfYear, double distToSeaKm)
    {
        double decl = DeclinationDeg(dayOfYear) * Math.PI / 180.0;
        double phi = latDeg * Math.PI / 180.0;
        double insolation = Math.Sin(decl) * Math.Sin(phi);
        return SeasonalGainC * ContinentalAmpFactor(distToSeaKm) * insolation;
    }

    /// <summary>季节振幅的基准增益（℃）。见 <see cref="SeasonalAnomalyC"/> 的标定说明。</summary>
    public const double SeasonalGainC = 45.0;

    /// <summary>黄赤交角（度）—— §7.6 的 23.44。</summary>
    public const double ObliquityDeg = 23.44;

    /// <summary>
    /// 一年之中 <c>sin(δ)</c> 的峰值 = sin(23.44°) ≈ 0.3977。
    ///
    /// 取「季节振幅」时必须乘上它：<see cref="SeasonalAnomalyC"/> 里的
    /// <c>sin(δ)</c> 全年在 ±0.3977 之间摆动，故某地一整年的温度摆幅
    /// 只有 <c>增益 × 0.3977 × |sin φ|</c>。忘了乘就会把年较差放大 2.5 倍 ——
    /// 实测会得到「60° 处 ±39 ℃」这种北极点才有的数字。
    /// </summary>
    public static readonly double SeasonalPeak = Math.Sin(ObliquityDeg * Math.PI / 180.0);

    /// <summary>大陆度对年较差的放大倍数：离海 1500 km 封顶 ×1.9。</summary>
    public static double ContinentalAmpFactor(double distToSeaKm)
        => 1.0 + 0.9 * Math.Clamp(distToSeaKm / 1500.0, 0.0, 1.0);

    /// <summary>
    /// 洋流造成的沿岸气温距平（℃）—— §7.6 的 <c>T_ocean(lon)</c>。
    ///
    /// 规则取自 §7.6 的两句：<b>西边界流暖水向高纬</b>（海在本格西侧 → 暖）、
    /// <b>东边界流冷水向低纬</b>（海在东侧 → 冷）。两条的纬度剖面是分开标定的 ——
    /// 暖流的高纬输送在 50° 上下最强（湾流、黑潮），寒流的上升流则在
    /// 20°~25° 的副热带东岸最显著（秘鲁、加利福尼亚、加那利、本格拉）。
    /// 两者离岸 600 km 后基本归零，故内陆不受影响。
    ///
    /// 极区再叠一层恒冷项：高纬终年有冷水与浮冰，东西岸都暖不起来。
    /// </summary>
    public static double OceanCurrentAnomalyC(double absLat, int coastSide, double distToSeaKm)
    {
        if (coastSide == 0) return 0.0;

        double decay = Math.Exp(-distToSeaKm / ClimateGrid.CurrentDecayKm);
        double warm = 4.5 * Gaussian(absLat, 52.0, 28.0);
        double cold = -4.5 * Gaussian(absLat, 24.0, 16.0);

        double anomaly = coastSide > 0 ? warm : cold;
        anomaly += -3.0 * Smooth01(absLat, 65.0, 85.0);      // 环极寒流 / 浮冰
        return anomaly * decay;
    }

    /// <summary>
    /// 大陆度造成的降水扣减（mm，<b>非正数</b>）—— §7.6 的 <c>−P_continentality</c>。
    ///
    /// 海岸线 120 km 以内不扣（<see cref="ClimateGrid.CoastGraceKm"/>）；再往里
    /// 每公里扣 0.55 mm。副热带基准降水只有 420 mm，扣掉内陆的 300~500 mm
    /// 之后正好落到 250 mm 的沙漠线以下 —— <b>「副热带沙」这一条验收主要就是靠它实现的</b>，
    /// 只靠纬向基准带的话副热带会是一片草原。
    /// </summary>
    public static double ContinentalityMm(double distToSeaKm)
        => -ContinentalityMmPerKm * Math.Max(0.0, distToSeaKm - ClimateGrid.CoastGraceKm);

    /// <summary>大陆度的扣减率（mm / km）。</summary>
    public const double ContinentalityMmPerKm = 0.55;

    /// <summary>
    /// 迎风坡增雨 / 背风坡雨影（mm）—— §7.6 的 <c>P_orographic</c>。
    ///
    /// <paramref name="riseM"/> = 本点高程 − 上风取样点高程。空气被地形强迫抬升
    /// 才成雨，所以<b>抬升为正才增雨</b>；下降时是背风侧，雨影的幅度比迎风坡小
    /// （水汽在迎风侧已经掉掉大半了），故系数 0.80 对 0.50。
    ///
    /// 还要乘一个「空气里有没有水」的因子：副热带的空气下沉、本来就干，
    /// 就算翻山也挤不出多少雨。用纬向基准降水的相对值来代表可降水量。
    /// </summary>
    public static double OrographicRainMm(double elevationM, double upwindElevM, double absLat)
    {
        double riseM = elevationM - upwindElevM;
        double perM = riseM >= 0 ? WindwardMmPerM : LeeMmPerM;
        double moisture = Math.Clamp(Geography.LatitudeRainBand(absLat) / 2200.0, 0.15, 1.0);
        return riseM * perM * moisture;
    }

    /// <summary>迎风坡增雨系数（mm / m 抬升）。</summary>
    public const double WindwardMmPerM = 0.80;

    /// <summary>背风坡雨影系数（mm / m 下降，取正数，结果带负号）。</summary>
    public const double LeeMmPerM = 0.50;

    /// <summary>
    /// 季风降水（mm）—— §7.6 的 <c>P_monsoon</c>。
    ///
    /// 季风源于海陆热力差造成的风向季节反转，故只出现在<b>热带海岸</b>：
    /// 纬度剖面以 15° 为中心，离岸 500 km 衰减。东岸（<c>side &lt; 0</c>）给满值 ——
    /// 东亚季风那种典型形态；西岸给 0.6 —— 印度西南季风同样成雨，
    /// 但成因里地形抬升的成分更大，这里不重复计入（迎风坡项已经在算了）。
    /// </summary>
    public static double MonsoonMm(double absLat, int coastSide, double distToSeaKm)
    {
        if (coastSide == 0) return 0.0;
        double amp = 900.0 * Gaussian(absLat, 15.0, 13.0);
        double coast = Math.Exp(-distToSeaKm / 500.0);
        double sideFactor = coastSide < 0 ? 1.0 : 0.6;
        return amp * coast * sideFactor;
    }

    /// <summary>年降水下限（mm）。无论怎么扣都不至于归零 —— 归零会让沙漠与「无数据」分不开。</summary>
    public const double MinRainMm = 15.0;

    /// <summary>迎风坡取样点距本点的角距（度）。2.5° ≈ 278 km，是一个天气尺度系统的量级。</summary>
    public const double OrographicReachDeg = 2.5;

    private static double Gaussian(double x, double mu, double sigma)
    {
        double u = (x - mu) / sigma;
        return Math.Exp(-u * u);
    }

    /// <summary>把 <paramref name="x"/> 从 [<paramref name="a"/>, <paramref name="b"/>] 平滑映射到 [0,1]。</summary>
    private static double Smooth01(double x, double a, double b)
    {
        double u = Math.Clamp((x - a) / (b - a), 0.0, 1.0);
        return u * u * (3.0 - 2.0 * u);
    }

    /// <summary>
    /// 一个采样点的完整气候。字段按「是不是 §7.6 那个和式的项」分了两组：
    /// 前三个是结果，后五个是各分项的贡献（诊断用，落库与图表都靠它们）。
    /// </summary>
    public readonly record struct ClimateSample(
        double TempC,              // 年均温（T_base + 高度递减 + 全球偏移 + 洋流）
        double RainMm,             // 年降水（基准 + 迎风坡 + 季风 + 大陆度）
        byte Biome,
        double SeasonAmpC,         // 季节振幅（正数，最冷月 ≈ Temp − 振幅）
        double DistToSeaKm,
        int CoastSide,             // +1 西岸 / −1 东岸 / 0 内陆
        double OceanAnomalyC,
        double ContinentalityMm,
        double OrographicMm,
        double MonsoonMm)
    {
        /// <summary>最冷月气温（℃）。季节项取正弦，故就是年均温减振幅。</summary>
        public double ColdestMonthC => TempC - SeasonAmpC;

        /// <summary>最热月气温（℃）。</summary>
        public double WarmestMonthC => TempC + SeasonAmpC;

        /// <summary>是否沿海（离海 ≤ 大陆度免罚距离）。</summary>
        public bool IsCoastal => DistToSeaKm <= ClimateGrid.CoastGraceKm;
    }

    /// <summary>
    /// §7.6 的完整取值。这是 M4 之后<b>唯一</b>该用来算气候的入口 ——
    /// 部落落库、星球上色、后续的农业模型全走这里，避免又一次「两处各写一份」。
    ///
    /// 和式：<c>T = T_base(|φ|) + T_ocean − 6.5×alt_km + GlobalTempOffset</c>；
    /// <c>P = P_base(|φ|) + P_orographic + P_monsoon − P_continentality</c>。
    /// （<c>T_season</c> 另算，见 <see cref="SeasonalAnomalyC"/>。）
    /// </summary>
    public static ClimateSample SampleAnnual(
        ClimateGrid grid, double latDeg, double lonDeg, double elevationM, double globalOffsetC = 0.0)
    {
        double absLat = Math.Abs(latDeg);
        double distSea = grid.DistToSeaKm(latDeg, lonDeg);
        int side = grid.CoastSide(latDeg, lonDeg);

        double ocean = OceanCurrentAnomalyC(absLat, side, distSea);

        double temp = Geography.BaseTemp(absLat)
                    - LapseRateCPerKm * (elevationM / 1000.0)
                    + globalOffsetC
                    + ocean;

        double upwindElev = grid.UpwindElevationM(latDeg, lonDeg, absLat, OrographicReachDeg);
        double oro = OrographicRainMm(elevationM, upwindElev, absLat);
        double monsoon = MonsoonMm(absLat, side, distSea);
        double cont = ContinentalityMm(distSea);

        double rain = Math.Max(MinRainMm,
            Geography.LatitudeRainBand(absLat) + oro + monsoon + cont);

        double amp = SeasonalGainC * SeasonalPeak * ContinentalAmpFactor(distSea)
                   * Math.Abs(Math.Sin(latDeg * Math.PI / 180.0));

        return new ClimateSample(
            TempC: temp, RainMm: rain, Biome: BiomeCodeOf(temp, rain, absLat, elevationM),
            SeasonAmpC: amp, DistToSeaKm: distSea, CoastSide: side,
            OceanAnomalyC: ocean, ContinentalityMm: cont,
            OrographicMm: oro, MonsoonMm: monsoon);
    }

    // ══════════════════════════════════════════════════════════════
    //  海色与壳层
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 深海颜色。
    ///
    /// ⚠️ 这两个值<b>取的是线性亮度，不是"看着像海水"</b>。原值 (10,28,66) 的线性亮度
    /// 只有 <b>0.0139</b> —— 在一张 48% 是海洋的图上，这意味着<b>大半个球面反照率接近纯黑</b>。
    /// 后果不是"颜色不好看"，而是蓝星在行星视图里<b>整个读作一颗暗球</b>：
    /// 实测受光面最亮处（含 1.15 倍平行光）折回 sRGB 只有 <b>36/255</b>，
    /// 与夜面（约 15）几乎分不开，<b>晨昏线糊成一片</b>。
    ///
    /// 换值的时候<b>不能靠调光强补救</b>：冰盖线性亮度已经是 0.90，
    /// 光强乘 1.15 就已接近 1.0，再往上推冰盖立刻过曝成死白 ——
    /// 那是一处"按下葫芦浮起瓢"。<b>问题在反照率，就只能在反照率上改。</b>
    ///
    /// 现值 (22,58,112) 线性亮度 <b>0.0457</b>（原值的 3.3 倍）。
    /// 保持的相对次序（每一条都有别处的注释依赖，改这里要一并复核）：
    /// 冰盖 0.90 &gt; 海冰 0.63 &gt; 沙漠 0.55 &gt; 大陆架 0.130 &gt; 陆地 0.12 &gt; 深海 0.046。
    /// 其中「海冰 vs 冰盖」的色差见 <see cref="SeaIceColor"/>，
    /// 「大陆架 vs 深海」的色差靠这两个值的<b>亮度比 2.8 倍</b>维持（原为 4.7 倍，
    /// 压到 2.8 是把它让给了"整颗球别太黑"，仍足以让大陆架带看得出来）。
    /// </summary>
    public static readonly (byte R, byte G, byte B) DeepOceanColor = (22, 58, 112);

    /// <summary>浅海 / 大陆架颜色。与 <see cref="DeepOceanColor"/> 成对调亮，理由见那里。</summary>
    public static readonly (byte R, byte G, byte B) ShelfColor = (40, 104, 164);

    /// <summary>
    /// 海冰颜色。
    ///
    /// ⚠️ 必须与陆地上的「冰盖」（238,245,252）<b>拉开色差</b>。两者曾是
    /// (232,242,250) 与 (238,245,252) —— 肉眼几乎不可分，后果是南北两块寒带大陆
    /// 整年压在冰盖色底下、与海冰糊成一片，行星图上「5 块大陆」只数得出 3 块。
    /// 真实的多年海冰也确实比冰盖暗且偏蓝（雪面反照率高、海冰薄处透出海水色）。
    /// </summary>
    public static readonly (byte R, byte G, byte B) SeaIceColor = (186, 212, 232);

    /// <summary>海水冰点（℃）。</summary>
    public const double SeaWaterFreezingC = -1.8;

    /// <summary>§7.4 表：大气壳半径倍率。</summary>
    public const double AtmosphereShellFactor = 1.02;

    /// <summary>§7.4 表：云层半径倍率。</summary>
    public const double CloudShellFactor = 1.03;
}
