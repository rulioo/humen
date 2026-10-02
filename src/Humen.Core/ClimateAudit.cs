namespace Humen.Core;

/// <summary>
/// §13 气候验收的量算口径：<b>赤道绿、副热带沙、极地白；冰期可驱动</b>。
///
/// 这一层被两处调用，且必须是<b>同一份实现</b>：
/// <list type="bullet">
///   <item><c>humen climate</c> —— 给人看的完整报告（分带、分项、双列对照）；</item>
///   <item><see cref="Invariants"/> —— 给 CI 看的判定（pass/fail 进不变式表）。</item>
/// </list>
/// 两处各写一份扫描是这类项目最经典的腐烂方式：报告说「达标」、验收说「不达标」，
/// 然后没人知道该信哪个。所以扫描只有一个实现，就在下面。
///
/// <b>面积权重必须带 cos(纬度)</b>：等距圆柱格网上每一格看着一样大，
/// 实际 80° 上的一格只有赤道的 17 %。不加权会把极区放大五倍，
/// 于是「极地白」这种按占比判的验收会失真。
/// </summary>
public static class ClimateAudit
{
    /// <summary>每带 10°，两半球合并 —— 0~10° 那一带同时代表北半球与南半球。</summary>
    public const int Bands = 18;

    /// <summary>「绿」的群系集合：§13 验收第一条「赤道绿」判的就是它们的占比。</summary>
    public static readonly byte[] GreenBiomes =
    {
        Climate.Rainforest, Climate.MonsoonForest, Climate.MontaneRainforest,
        Climate.TropicalWoodland, Climate.TemperateForest, Climate.TemperateRainforest,
        Climate.Taiga, Climate.Savanna,
    };

    // ── §13 的四个判据。数字写在这里，好让报告与验收不可能对不上。──
    public const double EquatorGreenNeed = 0.40;    // 0°~20° 森林占比
    public const double SubtropDesertNeed = 0.25;   // 20°~40° 沙漠占比
    public const double PolarIceNeed = 0.50;        // 70°~90° 冰盖占比
    public const double GlacialRatioNeed = 1.50;    // 冰盛期冰盖 / 当代冰盖

    /// <summary>
    /// 一次全星球扫描的结果。
    /// </summary>
    /// <param name="ShareByBiome">长度 <see cref="Climate.BiomeCount"/>，按 cos(纬度) 加权，和为 1。</param>
    /// <param name="ShareByBand">[带][群系]，带内占比。</param>
    /// <param name="BandWeight">每带的面积权重（带内所有陆地格之和），用于合并两半球时加权。</param>
    /// <param name="BandCount">[带][群系] 的格数。</param>
    public sealed record ClimateScan(
        double[] ShareByBiome,
        double[][] ShareByBand,
        double[] BandWeight,
        int[][] BandCount,
        double MeanOceanAnomalyC, double MeanContinentalityMm,
        double MeanOrographicMm, double MeanMonsoonMm, double MeanSeasonAmpC,
        double MeanRainMm, double MeanTempC, int LandCells);

    /// <summary>
    /// 扫全星球。
    /// </summary>
    /// <param name="seaLevelM">当前海平面相对基准的偏移（冰期 −120 m）。</param>
    /// <param name="tempOffsetC">全球温度偏移（冰期 −8 ℃）。</param>
    /// <param name="zonal">
    /// <c>true</c> = M1 的降维版（只按纬度分带 + 高度递减），作对照列用。
    /// 这条分支的存在只为「把 M4 的增量摆出来看」，不参与任何验收判定。
    /// </param>
    public static ClimateScan Scan(ClimateGrid grid, double seaLevelM, double tempOffsetC, bool zonal)
    {
        var share = new double[Climate.BiomeCount];
        var bandShare = new double[Bands][];
        var bandCount = new int[Bands][];
        var bandW = new double[Bands];
        for (int b = 0; b < Bands; b++)
        {
            bandShare[b] = new double[Climate.BiomeCount];
            bandCount[b] = new int[Climate.BiomeCount];
        }

        double wSum = 0;
        double sumOcean = 0, sumCont = 0, sumOro = 0, sumMon = 0, sumAmp = 0, sumRain = 0, sumTemp = 0;
        int land = 0;

        for (int iy = 0; iy < ClimateGrid.Ny; iy++)
        {
            double lat = ClimateGrid.LatAt(iy);
            double w = Math.Cos(lat * Math.PI / 180.0);
            int b = Math.Min(Bands - 1, (int)(Math.Abs(lat) / 10.0));

            for (int ix = 0; ix < ClimateGrid.Nx; ix++)
            {
                double lon = ClimateGrid.LonAt(ix);
                double elev = grid.GridElevationM(lat, lon);
                if (elev <= seaLevelM) continue;              // 与渲染器同一条判据

                double rain, temp, amp;
                byte code;
                if (zonal)
                {
                    // M1 的降维版：只按纬度分带 + 高度递减。作对照列。
                    temp = Climate.TemperatureC(lat, elev, tempOffsetC);
                    rain = Climate.RainMm(lat);
                    amp = 0;
                    code = Climate.BiomeCodeOf(temp, rain, Math.Abs(lat));
                }
                else
                {
                    var s = Climate.SampleAnnual(grid, lat, lon, elev, tempOffsetC);
                    temp = s.TempC; rain = s.RainMm; amp = s.SeasonAmpC; code = s.Biome;
                    sumOcean += s.OceanAnomalyC * w;
                    sumCont += s.ContinentalityMm * w;
                    sumOro += s.OrographicMm * w;
                    sumMon += s.MonsoonMm * w;
                }
                sumAmp += amp * w;
                sumRain += rain * w;
                sumTemp += temp * w;

                share[code] += w;
                bandShare[b][code] += w;
                bandCount[b][code] += 1;
                bandW[b] += w;
                wSum += w;
                land++;
            }
        }

        for (int i = 0; i < Climate.BiomeCount; i++) share[i] /= wSum;
        for (int b = 0; b < Bands; b++)
            for (int i = 0; i < Climate.BiomeCount; i++)
                bandShare[b][i] = bandW[b] > 0 ? bandShare[b][i] / bandW[b] : 0;

        return new ClimateScan(share, bandShare, bandW, bandCount,
            sumOcean / wSum, sumCont / wSum, sumOro / wSum, sumMon / wSum,
            sumAmp / wSum, sumRain / wSum, sumTemp / wSum, land);
    }

    /// <summary>某一族群系在某纬度带（10° 一带）上的占比。</summary>
    public static double ShareOf(ClimateScan s, int band, params byte[] codes)
    {
        double v = 0;
        foreach (byte c in codes) v += s.ShareByBand[band][c];
        return v;
    }

    /// <summary>
    /// 一组纬度带合并后的占比（<b>按面积加权</b>，不是把各带占比相加）。
    ///
    /// 相加是个很容易犯的错：占比之和会超过 100 %，验算时却因为
    /// 「看着都比阈值大」而蒙混过关。各带的面积本来就不同
    /// （带重 <see cref="ClimateScan.BandWeight"/>），必须加权平均。
    /// </summary>
    public static double BandShare(ClimateScan s, int bandFrom, int bandTo, params byte[] codes)
    {
        double num = 0, den = 0;
        for (int b = bandFrom; b <= bandTo; b++)
        {
            if (s.BandWeight[b] <= 0) continue;
            double w = s.BandWeight[b];
            den += w;
            foreach (byte c in codes) num += s.ShareByBand[b][c] * w;
        }
        return den > 0 ? num / den : 0;
    }

    /// <summary>全球（陆地）占比。</summary>
    public static double ShareOfAll(ClimateScan s, params byte[] codes)
    {
        double v = 0;
        foreach (byte c in codes) v += s.ShareByBiome[c];
        return v;
    }

    /// <summary>
    /// 某纬度带上占比最大的前 <paramref name="n"/> 个群系，形如「冰盖 57 % · 针叶林 31 %」。
    ///
    /// 只打「占比最大的那个」会掩盖真实的组成：一片 57 % 冰盖 + 43 % 针叶林的地带，
    /// 与一片纯冰盖的地带在「主导群系」一栏里长得一模一样，而两者的含义天差地别。
    /// </summary>
    public static string TopBiomes(ClimateScan s, int band, int n)
    {
        var idx = Enumerable.Range(0, Climate.BiomeCount)
                            .OrderByDescending(i => s.ShareByBand[band][i])
                            .Take(n)
                            .Where(i => s.ShareByBand[band][i] >= 0.005)
                            .Select(i => $"{Climate.BiomeName((byte)i)} {s.ShareByBand[band][i]:P0}");
        return string.Join(" · ", idx);
    }

    // ══════════════════════════════════════════════════════════════════
    //  §13 四条验收
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 一条验收的实测值与判据。
    /// </summary>
    /// <param name="Baseline">
    /// <b>同一口径</b>下 M1 降维版（只按纬度分带）的实测值，用于回答
    /// 「这一条到底是 M4 挣来的，还是纬度分带本来就够」。
    /// <see cref="double.NaN"/> = 该条没有降维对照（降维模型里没有它的对应量）。
    /// </param>
    public sealed record Verdict(string What, double Got, double Need, string Priority,
                                 double Baseline = double.NaN)
    {
        public bool Pass => Got >= Need;
        public string Glyph => Pass ? "✅" : "❌";
        public bool HasBaseline => !double.IsNaN(Baseline);
    }

    /// <summary>§13 四条验收的完整结论。</summary>
    public sealed record Acceptance(
        ClimateScan Now, ClimateScan Zonal, ClimateScan Glacial,
        double GlacialYear, double GlacialSeaM, double GlacialOffsetC,
        Verdict EquatorGreen, Verdict SubtropDesert, Verdict PolarIce, Verdict GlacialDrive)
    {
        public bool AllPass => EquatorGreen.Pass && SubtropDesert.Pass
                            && PolarIce.Pass && GlacialDrive.Pass;

        public IEnumerable<Verdict> Verdicts
        {
            get
            {
                yield return EquatorGreen;
                yield return SubtropDesert;
                yield return PolarIce;
                yield return GlacialDrive;
            }
        }
    }

    /// <summary>
    /// 跑完整的 §13 验收。当代 + 降维对照 + 末次冰盛期，共三次扫描。
    /// </summary>
    /// <param name="year">当代年份，默认 2025（本书「现在」）。</param>
    public static Acceptance Evaluate(ClimateGrid grid, long seed, double year = WorldConfig.PresentYear)
    {
        double sea = GlacialCycle.SeaLevelOffsetM(year, seed);
        double off = GlacialCycle.GlobalTempOffsetC(year, seed);

        var now = Scan(grid, sea, off, zonal: false);
        var zonal = Scan(grid, sea, off, zonal: true);

        // 末次冰盛期：同一张场，只换全球温度偏移与海平面
        double lgmYear = GlacialCycle.LastGlacialMaxYear;
        var lgm = Scan(grid, GlacialCycle.SeaLevelOffsetM(lgmYear, seed),
                             GlacialCycle.GlobalTempOffsetC(lgmYear, seed), zonal: false);

        // 纬度带按 10° 一带：0~20° = 带 0~1；副热带 20~40° = 带 2~3；极地 70~90° = 带 7~8
        double eqGreen = BandShare(now, 0, 1, GreenBiomes);
        double subDesert = BandShare(now, 2, 3, Climate.Desert);
        double polIce = BandShare(now, 7, 8, Climate.IceCap);

        // 降维对照必须用<b>同一个口径</b>（同样是 0~20° 带上的森林占比），
        // 而不是拿全球占比来比 —— 两者不是一回事，摆在一起只会误导。
        double eqGreenZ = BandShare(zonal, 0, 1, GreenBiomes);
        double subDesertZ = BandShare(zonal, 2, 3, Climate.Desert);
        double polIceZ = BandShare(zonal, 7, 8, Climate.IceCap);

        double iceNow = ShareOfAll(now, Climate.IceCap);
        double iceLgm = ShareOfAll(lgm, Climate.IceCap);
        // 当代冰盖为 0 时比值无意义 —— 报告成「无穷大」会让判定凭空通过。
        double ratio = iceNow > 1e-9 ? iceLgm / iceNow : 0.0;

        return new Acceptance(now, zonal, lgm,
            lgmYear, GlacialCycle.SeaLevelOffsetM(lgmYear, seed),
            GlacialCycle.GlobalTempOffsetC(lgmYear, seed),
            new Verdict("赤道绿   0°~20° 森林占比 ≥ 40 %", eqGreen, EquatorGreenNeed, "P1", eqGreenZ),
            new Verdict("副热带沙 20°~40° 沙漠占比 ≥ 25 %", subDesert, SubtropDesertNeed, "P1", subDesertZ),
            new Verdict("极地白   70°~90° 冰盖占比 ≥ 50 %", polIce, PolarIceNeed, "P1", polIceZ),
            new Verdict("冰期可驱动 冰盛期冰盖 / 当代冰盖 ≥ ×1.5", ratio, GlacialRatioNeed, "P1"));
    }
}
