namespace Humen.Core;

/// <summary>性别。<b>数值即 ID 尾号</b>（req.txt 第 5 行：尾号 1 为男性，尾号 0 为女性）。</summary>
public enum Gender
{
    Female = 0,
    Male = 1,
}

/// <summary>
/// ID 的来源。★ <b>刻意做成显式枚举，而不是靠 <c>seq ≤ 1000</c> 推断</b> ——
/// 因为部落会分裂（<c>parent_tribe_id</c>），新部落的序号<b>从 1 重新开始</b>，
/// 于是「序号小」绝不等于「元年成员」。id.md §7 ID-5 专门立了这条规矩。
/// </summary>
public enum IdSource
{
    /// <summary>元年成员（公元前 30 万年那 10 万人）。</summary>
    Genesis = 0,

    /// <summary>后世繁衍新增，由 <see cref="Identity.Derive"/> 按需派生。</summary>
    Derived = 1,
}

/// <summary>农业潜力（design.md §6.1 环境禀赋表）。进化分叉的第一推动力。</summary>
public enum AgriculturePotential
{
    /// <summary>❌ 不能农耕。只能走渔猎—游牧路线。</summary>
    None = 0,
    /// <summary>⚠️ 农业稀少。有，但不足以独自撑起文明。</summary>
    Marginal = 1,
    /// <summary>✅ 可以农耕。</summary>
    Good = 2,
    /// <summary>✅ 且条件优越（更早进入农业社会）。</summary>
    Excellent = 3,
}

/// <summary>大陆（design.md §7.2 大陆布局 + §6.1 环境禀赋）。共 5 块，硬编码，不随机生成。</summary>
public sealed record Continent(
    int Id,
    string Name,
    string ClimateZone,
    double LatMin, double LatMax,
    double LonMin, double LonMax,
    double CenterLat, double CenterLon,
    double LandFraction,
    AgriculturePotential Agriculture,
    string[] Domesticates,
    string[] Minerals)
{
    /// <summary>中心点的球面坐标（R = 1000 单位，design.md §7.1）。</summary>
    public (double X, double Y, double Z) CenterOnSphere(double radius)
        => Sphere.ToXyz(CenterLat, CenterLon, radius);

    /// <summary>
    /// 点是否落在 §7.2 的包围盒内。
    /// 经度按最短弧处理，故能正确处理跨 180° 的大陆 3（LonMin = 140，LonMax = −160）。
    /// </summary>
    public bool ContainsBBox(double lat, double lon)
    {
        if (lat < LatMin || lat > LatMax) return false;

        double span = LonMax - LonMin;
        if (span < 0) span += 360;                  // 跨 180°

        double d = lon - LonMin;
        if (d < 0) d += 360;
        return d <= span;
    }

    /// <summary>
    /// 把一个点夹回 §7.2 的包围盒内。经度同样按最短弧处理（跨 180° 的大陆 3 不会夹错）。
    ///
    /// <b>为什么需要这个：</b><see cref="PlanetField.PlaceSeeds"/> 的「叶」与「海湾」
    /// 是相对脊线随机偏移的，偏移量按 <c>rBase</c>（= 短轴 × 0.42）缩放 ——
    /// 北温带大陆短轴 32°，叶就能甩出 **±11°**，越过 44°N 的北边界，
    /// 一头扎进北寒带大陆的地盘；北寒带大陆的叶再往南甩几度，两块就**连上了**。
    ///
    /// 后果不是难看，是两条硬约束同时破：
    ///   ① INV-8（陆桥）—— 栅格连通性报「跨陆分量 C1+C4」；
    ///   ② §6.1 环境禀赋 —— 寒带大陆（<c>AgriculturePotential.None</c>）的陆地
    ///      长到了温带，一块本该无法农耕的大陆变成可农耕的。
    ///
    /// 夹回去之后，种子圆心一律落在盒内；陆地边缘仍会溢出种子半径那一点点，
    /// 海岸线的锯齿感（"非规则形状"）不受影响 —— 被约束的只是**圆心**。
    /// </summary>
    public (double Lat, double Lon) ClampToBBox(double lat, double lon)
    {
        double clat = Math.Clamp(lat, LatMin, LatMax);

        double span = LonMax - LonMin;
        if (span < 0) span += 360;                  // 跨 180°

        double d = lon - LonMin;
        while (d < 0) d += 360;
        while (d >= 360) d -= 360;
        if (d > span) d = span;

        return (clat, Sphere.WrapLon(LonMin + d));
    }
}

/// <summary>主河。每大陆一条，Catmull-Rom 由 12~20 个控制点插值（design.md §7.5）。</summary>
public sealed record River(
    int Id,
    int ContinentId,
    string Name,
    IReadOnlyList<(double Lat, double Lon)> ControlPoints);

/// <summary>一级支流。在主河参数 t ∈ {0.15, 0.32, 0.50, 0.68, 0.85} 处汇入。</summary>
/// <param name="TribeCapacity">
/// 该支流的养育能力 = 能承载几个部落（design.md §7.5）。
/// 由长度与流域面积推导，见 <see cref="Geography.AllocateCapacity"/>。
/// <b>M3 起不再恒为 4</b> —— 恒为 4 时 INV-9b 会退化成永远成立的恒等式。
/// </param>
public sealed record Tributary(
    int Id,
    int ContinentId,
    int RiverId,
    int Index,          // 该主河下的第几条支流，1..5
    string Name,
    double JunctionT,   // 汇入主河处的参数
    double LengthKm,
    double HeadingDeg,  // 自汇入点向外的方位角
    IReadOnlyList<(double Lat, double Lon)> ControlPoints,
    int TribeCapacity);

/// <summary>
/// 部落。<c>Code</c> 是 req.txt 的「大陆.部落」写法，如 <c>"1.1"</c>。
/// 全局 100 个，按 (大陆, 支流, 沿岸位) 三重循环确定，<b>无随机</b>。
/// </summary>
public sealed record Tribe(
    int GlobalIndex,        // 0..99
    int ContinentId,        // 1..5
    int LocalIndex,         // 该大陆内编号 1..20
    string Code,            // "1.1" .. "5.20"
    int TributaryId,
    int SlotOnTributary,    // 沿岸第几处，0..3
    string Name,
    double Lat, double Lon,
    double ElevationM,
    double MeanTempC,
    double AnnualRainMm,
    string Biome,
    // ── M4（v0.13）新增：气候与土壤 ──
    // 全部来自 Climate.SampleAnnual / Soil.Profile，与渲染器用的是<b>同一套</b>判据。
    double SeasonAmpC,        // 季节振幅（℃）：最冷月 ≈ MeanTempC − SeasonAmpC
    double ColdestMonthC,     // 最冷月气温（℃）。INV-15「寒带不得解锁农业」判的就是它
    double DistToSeaKm,       // 到最近海洋的大圆距离（km）。海岸线附近为 0
    double TopsoilM,          // §7.8 的 A 层厚度（m）
    string SoilType,          // §7.8 的土壤大类名
    double AgricultureFactor  // §7.8：由 A 层厚度 + 最冷月 + 降水算出的农业产能因子 ∈ [0,1]
);

/// <summary>技术节点状态（design.md §5.4 / tech_tree.md）。M1 只写初始状态。</summary>
public sealed record TechState(
    int ContinentId,
    string TechId,
    bool Discovered,
    double? DiscoveredYear);

/// <summary>
/// 一个「人」。
///
/// ⚠️ <b>这个记录同时服务 L1 与 L2</b>（id.md §3.2）：
///   L1 实体层 —— 它会真的落进 <c>members</c> 表；
///   L2 程序层 —— 它只是 <see cref="Identity.Derive"/> 的返回值，<b>用完即弃，绝不落盘</b>。
/// 两者字段完全相同，这是刻意的：跨层观感必须一致（ID-15）。
/// </summary>
public sealed record Member(
    string Id,              // "1.1-103-1"
    int ContinentId,
    int TribeId,            // 全局 0..99
    string TribeCode,       // "1.1"
    int Seq,                // 部落内序号，1 起
    Gender Gender,
    string FullName,
    string Surname,
    string GivenName,
    double BirthYear,
    int AgeAtGenesis,       // 仅元年成员有意义；L2 为 -1
    IdSource Source)
{
    /// <summary>ID 尾号（req.txt 第 5 行：1=男 0=女）。</summary>
    public int GenderCode => (int)Gender;
}

/// <summary>球面 ↔ 经纬度换算（design.md §7.1，R = 1000 单位）。</summary>
public static class Sphere
{
    /// <summary>星球半径，单位 = Unity 世界单位。</summary>
    public const double Radius = 1000.0;

    /// <summary>经纬度 → 三维直角坐标。Y 轴为地轴北极。</summary>
    public static (double X, double Y, double Z) ToXyz(double latDeg, double lonDeg, double radius = Radius)
    {
        double lat = latDeg * Math.PI / 180.0;
        double lon = lonDeg * Math.PI / 180.0;
        double cl = Math.Cos(lat);
        return (radius * cl * Math.Cos(lon),
                radius * Math.Sin(lat),
                radius * cl * Math.Sin(lon));
    }

    /// <summary>三维直角坐标 → 经纬度。</summary>
    public static (double Lat, double Lon) ToLatLon(double x, double y, double z)
    {
        double r = Math.Sqrt(x * x + y * y + z * z);
        if (r < 1e-9) return (0, 0);
        double lat = Math.Asin(Math.Clamp(y / r, -1.0, 1.0)) * 180.0 / Math.PI;
        double lon = Math.Atan2(z, x) * 180.0 / Math.PI;
        return (lat, lon);
    }

    /// <summary>两点大圆距离（单位 = Unity 世界单位）。</summary>
    public static double Distance(double lat1, double lon1, double lat2, double lon2, double radius = Radius)
    {
        var a = ToXyz(lat1, lon1, radius);
        var b = ToXyz(lat2, lon2, radius);
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>把经度规整到 (−180, 180]。</summary>
    public static double WrapLon(double lon)
    {
        lon %= 360.0;
        if (lon > 180.0) lon -= 360.0;
        if (lon <= -180.0) lon += 360.0;
        return lon;
    }
}
