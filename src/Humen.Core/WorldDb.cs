using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Humen.Core;

/// <summary>
/// SQLite 落盘（design.md §8.4 建表语句 + id.md §6.3 <c>id_ledger</c>）。
///
/// <b>确定性纪律</b>：本类<b>绝不写入任何时间戳 / 机器名 / 路径</b>。
/// 整库内容只由 <c>seed</c> 决定 —— 这是 INV-34 能在 CI 里逐字节比对的前提。
/// （想记构建时间就写进 <c>world_summary.json</c>，那是产物不是库。）
/// </summary>
public static class WorldDb
{
    public const int SchemaVersion = 1;

    /// <summary>矿产深度分带（design.md §4.4，字符串与 DDL 注释一致）。</summary>
    private static readonly (string Band, int DepthM, string[] Materials)[] DepthProfile =
    {
        ("surface",   20, new[] { "铜", "铁", "锡", "金", "银" }),
        ("shallow",  300, new[] { "铜", "铁", "锡", "煤", "金", "银" }),
        ("deep",    1200, new[] { "铜", "铁", "煤", "石油", "稀有金属" }),
        ("ultra",   3000, new[] { "铁", "煤", "石油", "铀" }),
        ("beyond",  6000, new[] { "铀", "稀土" }),
    };

    // ══════════════════════════════════════════════════════════════════
    //  建库
    // ══════════════════════════════════════════════════════════════════

    /// <summary>把已经生成好的世界写进库。返回写库统计。</summary>
    /// <remarks>
    /// v0.11（M3）起<b>由调用方传入 <paramref name="world"/></b>，不再在内部重建。
    /// 原因：M3 的河流要走地形梯度下降、部落要逐点采样地形场，单次生成代价比 M1 的
    /// 包围盒正弦摆动高几个数量级；而调用方本来就已经建过一份（要写 summary.json），
    /// 在库里再重建一次等于白花一倍时间。
    /// </remarks>
    public static DbStats Build(string path, long seed, Geography.World world)
    {
        if (File.Exists(path)) File.Delete(path);

        var members = MemberBuilder.BuildGenesis(seed, world.Tribes);

        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        };

        using var conn = new SqliteConnection(csb.ToString());
        conn.Open();

        Exec(conn, "PRAGMA journal_mode = OFF;");
        Exec(conn, "PRAGMA synchronous = OFF;");

        CreateSchema(conn);

        using var tx = conn.BeginTransaction();

        WriteMeta(conn, tx, seed, world, members.Count);
        WriteContinents(conn, tx);
        WriteRivers(conn, tx, world.Rivers);
        WriteTributaries(conn, tx, world.Tributaries, seed);
        WriteMinerals(conn, tx, seed);
        WriteTribes(conn, tx, world.Tribes, world.Tributaries);
        WriteMembers(conn, tx, members);
        WriteTechState(conn, tx, seed);
        WriteIdLedger(conn, tx, world.Tribes);

        tx.Commit();

        return Summarize(path, seed, world, members);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Schema
    // ══════════════════════════════════════════════════════════════════

    private static void CreateSchema(SqliteConnection conn)
    {
        Exec(conn, """
            CREATE TABLE world_meta (key TEXT PRIMARY KEY, value TEXT);

            CREATE TABLE continents (
              continent_id INTEGER PRIMARY KEY, name TEXT, climate_zone TEXT,
              min_lat REAL, max_lat REAL, min_lon REAL, max_lon REAL,
              center_lat REAL, center_lon REAL, area_ratio REAL,
              agriculture_feasible INTEGER,
              domesticable_species INTEGER,
              mineral_richness TEXT
            );

            CREATE TABLE rivers (
              river_id TEXT PRIMARY KEY,
              continent_id INTEGER, name TEXT,
              source_lat REAL, source_lon REAL, mouth_lat REAL, mouth_lon REAL,
              spline_json TEXT, length_km REAL, avg_discharge_m3s REAL
            );

            CREATE TABLE tributaries (
              tributary_id TEXT PRIMARY KEY,
              river_id TEXT NOT NULL, name TEXT,
              confluence_t REAL,
              spline_json TEXT, length_km REAL,
              basin_area_km2 REAL,
              tribe_capacity INTEGER
            );

            CREATE TABLE mineral_deposits (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              continent_id INTEGER NOT NULL,
              material TEXT NOT NULL,
              depth_band TEXT NOT NULL,
              depth_m INTEGER NOT NULL,
              richness REAL NOT NULL,
              human_extractable INTEGER NOT NULL
            );

            CREATE TABLE tribes (
              tribe_id TEXT PRIMARY KEY,
              continent_id INTEGER,
              river_id TEXT,
              tributary_id TEXT,
              name TEXT,
              latitude REAL, longitude REAL, elevation_m REAL,
              biome TEXT, mean_temp_c REAL, annual_rain_mm REAL,
              population INTEGER, parent_tribe_id TEXT, founded_year REAL,
              -- v0.13 M4 新增：气候与土壤（§7.6 / §7.8）。
              -- season_amp_c 是季节振幅，coldest_month_c = mean_temp_c − season_amp_c；
              -- INV-15「寒带大陆不得解锁任何农业变体」判的是 coldest_month_c，不是年均温。
              season_amp_c REAL, coldest_month_c REAL, dist_to_sea_km REAL,
              topsoil_m REAL, soil_type TEXT, agriculture_factor REAL
            );

            CREATE TABLE members (
              member_id TEXT PRIMARY KEY,
              tribe_id TEXT NOT NULL, continent_id INTEGER NOT NULL,
              member_no INTEGER NOT NULL,
              gender_code INTEGER NOT NULL,
              gender TEXT NOT NULL,
              full_name TEXT NOT NULL, surname TEXT, given_name TEXT,
              age INTEGER NOT NULL, birth_year REAL NOT NULL,
              -- v0.10 新增：ID 来源。id.md ID-5 明令不得由 seq ≤ 1000 推断，
              -- 必须显式落库（M2 部落分裂后 seq 会从头开始，序号小 ≠ 元年成员）。
              id_source TEXT NOT NULL DEFAULT 'genesis'
            );
            CREATE INDEX idx_members_tribe ON members(tribe_id);
            CREATE INDEX idx_members_tribe_no ON members(tribe_id, member_no);

            CREATE TABLE tech_state (
              tribe_id TEXT, tech_id TEXT, acquired_year REAL,
              variant TEXT,
              discovered_by_member TEXT,
              PRIMARY KEY (tribe_id, tech_id)
            );

            CREATE TABLE events (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              year REAL NOT NULL, tribe_id TEXT, continent_id INTEGER,
              event_type TEXT NOT NULL,
              tech_id TEXT, magnitude REAL, description TEXT
            );
            CREATE INDEX idx_events_year ON events(year);
            CREATE INDEX idx_events_tribe ON events(tribe_id);

            CREATE TABLE tribe_history (
              tribe_id TEXT, year REAL, population INTEGER,
              energy_per_capita REAL, life_quality REAL,
              material_tier INTEGER, tool_multiplier REAL,
              PRIMARY KEY (tribe_id, year)
            );

            CREATE TABLE members_archive (
              member_id TEXT PRIMARY KEY, tribe_id TEXT,
              full_name TEXT, birth_year REAL, death_year REAL,
              notable_for TEXT
            );

            -- id.md §6.3：L3 计数层。三层架构中唯一随人口增长的状态，而它只有 ~10² 行。
            CREATE TABLE id_ledger (
              continent     INTEGER NOT NULL,
              tribe         INTEGER NOT NULL,
              next_seq      INTEGER NOT NULL,
              minted_total  INTEGER NOT NULL,
              PRIMARY KEY (continent, tribe)
            );
            """);
    }

    // ══════════════════════════════════════════════════════════════════
    //  各表写入
    // ══════════════════════════════════════════════════════════════════

    private static void WriteMeta(SqliteConnection c, SqliteTransaction tx, long seed, Geography.World w, int memberCount)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO world_meta(key, value) VALUES ($k, $v);";
        var pk = cmd.CreateParameter(); pk.ParameterName = "$k"; cmd.Parameters.Add(pk);
        var pv = cmd.CreateParameter(); pv.ParameterName = "$v"; cmd.Parameters.Add(pv);

        void Put(string k, string v) { pk.Value = k; pv.Value = v; cmd.ExecuteNonQuery(); }
        void PutN(string k, double v) => Put(k, v.ToString("R", CultureInfo.InvariantCulture));

        Put("schema_version", SchemaVersion.ToString(CultureInfo.InvariantCulture));
        Put("seed", seed.ToString(CultureInfo.InvariantCulture));
        PutN("start_year", WorldConfig.StartYear);
        PutN("end_year", WorldConfig.EndYear);
        PutN("total_span_years", WorldConfig.TotalSpanYears);
        Put("start_year_display", WorldConfig.FormatYear(WorldConfig.StartYear));
        Put("end_year_display", WorldConfig.FormatYear(WorldConfig.EndYear));
        PutN("id_window_start", WorldConfig.IdWindowStart);
        PutN("id_window_end", WorldConfig.IdWindowEnd);
        Put("id_window_note", WorldConfig.IdWindowNote);
        Put("continents", WorldConfig.ContinentCount.ToString(CultureInfo.InvariantCulture));
        Put("rivers", w.Rivers.Count.ToString(CultureInfo.InvariantCulture));
        Put("tributaries", w.Tributaries.Count.ToString(CultureInfo.InvariantCulture));
        Put("tribes", w.Tribes.Count.ToString(CultureInfo.InvariantCulture));
        Put("members_per_tribe", WorldConfig.MembersPerTribe.ToString(CultureInfo.InvariantCulture));
        Put("genesis_members", memberCount.ToString(CultureInfo.InvariantCulture));
        Put("initial_tech", string.Join(",", WorldConfig.InitialTechSet));
        Put("identity_architecture", "three-layer v1.3: L1 rows + L2 Derive() + L3 id_ledger");
        Put("sphere_radius", Sphere.Radius.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void WriteContinents(SqliteConnection c, SqliteTransaction tx)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO continents VALUES
            ($id,$name,$zone,$minLat,$maxLat,$minLon,$maxLon,$cLat,$cLon,$area,$agri,$dom,$min);
            """;
        string[] names = { "$id", "$name", "$zone", "$minLat", "$maxLat", "$minLon", "$maxLon",
                           "$cLat", "$cLon", "$area", "$agri", "$dom", "$min" };
        foreach (var n in names) cmd.Parameters.Add(new SqliteParameter(n, null));

        foreach (var ct in Geography.Continents)
        {
            cmd.Parameters["$id"].Value = ct.Id;
            cmd.Parameters["$name"].Value = ct.Name;
            cmd.Parameters["$zone"].Value = ct.ClimateZone;
            cmd.Parameters["$minLat"].Value = ct.LatMin;
            cmd.Parameters["$maxLat"].Value = ct.LatMax;
            cmd.Parameters["$minLon"].Value = ct.LonMin;
            cmd.Parameters["$maxLon"].Value = ct.LonMax;
            cmd.Parameters["$cLat"].Value = ct.CenterLat;
            cmd.Parameters["$cLon"].Value = ct.CenterLon;
            cmd.Parameters["$area"].Value = ct.LandFraction;
            cmd.Parameters["$agri"].Value = (int)ct.Agriculture;
            cmd.Parameters["$dom"].Value = ct.Domesticates.Length;
            cmd.Parameters["$min"].Value = JsonSerializer.Serialize(ct.Minerals);
            cmd.ExecuteNonQuery();
        }
    }

    private static void WriteRivers(SqliteConnection c, SqliteTransaction tx, IReadOnlyList<River> rivers)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO rivers VALUES ($id,$c,$name,$slat,$slon,$mlat,$mlon,$spline,$len,$disch);
            """;
        foreach (var n in new[] { "$id", "$c", "$name", "$slat", "$slon", "$mlat", "$mlon", "$spline", "$len", "$disch" })
            cmd.Parameters.Add(new SqliteParameter(n, null));

        foreach (var r in rivers)
        {
            double len = Geography.PolylineLengthKm(r.ControlPoints);
            cmd.Parameters["$id"].Value = RiverId(r);
            cmd.Parameters["$c"].Value = r.ContinentId;
            cmd.Parameters["$name"].Value = r.Name;
            cmd.Parameters["$slat"].Value = r.ControlPoints[0].Lat;
            cmd.Parameters["$slon"].Value = r.ControlPoints[0].Lon;
            cmd.Parameters["$mlat"].Value = r.ControlPoints[^1].Lat;
            cmd.Parameters["$mlon"].Value = r.ControlPoints[^1].Lon;
            cmd.Parameters["$spline"].Value = SplineJson(r.ControlPoints);
            cmd.Parameters["$len"].Value = Math.Round(len, 3);
            cmd.Parameters["$disch"].Value = DBNull.Value;   // 出流量待 §7.6 水文模型（M2）
            cmd.ExecuteNonQuery();
        }
    }

    private static void WriteTributaries(SqliteConnection c, SqliteTransaction tx, IReadOnlyList<Tributary> tris, long seed)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO tributaries VALUES ($id,$r,$name,$t,$spline,$len,$basin,$cap);
            """;
        foreach (var n in new[] { "$id", "$r", "$name", "$t", "$spline", "$len", "$basin", "$cap" })
            cmd.Parameters.Add(new SqliteParameter(n, null));

        foreach (var t in tris)
        {
            cmd.Parameters["$id"].Value = TributaryId(t);
            cmd.Parameters["$r"].Value = $"R{t.ContinentId}";
            cmd.Parameters["$name"].Value = t.Name;
            cmd.Parameters["$t"].Value = t.JunctionT;
            cmd.Parameters["$spline"].Value = SplineJson(t.ControlPoints);
            cmd.Parameters["$len"].Value = Math.Round(t.LengthKm, 3);

            // 一阶流域估计：把支流近似为长 L、宽 0.35L 的矩形汇水区。
            // ⚠️ 仅供养育能力排序用，不是水文学结论。与 Geography.BasinAreaKm2 同口径。
            double basin = Geography.BasinAreaKm2(t.LengthKm);
            cmd.Parameters["$basin"].Value = Math.Round(basin, 1);
            // INV-9b：真实养育能力，由长度/流域面积分配而来（M3 起不再恒为 4）
            cmd.Parameters["$cap"].Value = t.TribeCapacity;
            cmd.ExecuteNonQuery();
        }
    }

    private static void WriteMinerals(SqliteConnection c, SqliteTransaction tx, long seed)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO mineral_deposits
              (continent_id, material, depth_band, depth_m, richness, human_extractable)
            VALUES ($c,$m,$b,$d,$r,$h);
            """;
        foreach (var n in new[] { "$c", "$m", "$b", "$d", "$r", "$h" })
            cmd.Parameters.Add(new SqliteParameter(n, null));

        foreach (var ct in Geography.Continents)
        {
            var deposits = new List<(string Material, string Band, int Depth, double Rich)>();
            foreach (var (band, depth, materials) in DepthProfile)
                foreach (var mat in materials)
                    if (Array.IndexOf(ct.Minerals, mat) >= 0)
                        deposits.Add((mat, band, depth, 0));

            // 用 (seed, 大陆, 矿种序号) 派生丰度，避免"每块大陆的铜一样多"
            for (int i = 0; i < deposits.Count; i++)
            {
                var d = deposits[i];
                // 子域 60+i：同一大陆的不同矿种得到不相关丰度
                ulong h = Hashing.Hash64(seed, 60 + i, ct.Id, d.Depth, 0);
                double richness = 0.15 + Hashing.ToUnit(h) * 0.85;

                cmd.Parameters["$c"].Value = ct.Id;
                cmd.Parameters["$m"].Value = d.Material;
                cmd.Parameters["$b"].Value = d.Band;
                cmd.Parameters["$d"].Value = d.Depth;
                cmd.Parameters["$r"].Value = Math.Round(richness, 4);
                // INV-20：depth_m > 4000 的行 human_extractable 恒为 0
                cmd.Parameters["$h"].Value = d.Depth > 4000 ? 0 : 1;
                cmd.ExecuteNonQuery();
            }
        }
    }

    private static void WriteTribes(SqliteConnection c, SqliteTransaction tx,
                                    IReadOnlyList<Tribe> tribes, IReadOnlyList<Tributary> tributaries)
    {
        var keyByTributaryId = tributaries.ToDictionary(t => t.Id, TributaryId);

        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO tribes VALUES
            ($id,$c,$r,$t,$name,$lat,$lon,$elev,$biome,$temp,$rain,$pop,NULL,$founded,
             $amp,$cold,$sea,$topsoil,$soil,$agri);
            """;
        foreach (var n in new[] { "$id", "$c", "$r", "$t", "$name", "$lat", "$lon",
                                  "$elev", "$biome", "$temp", "$rain", "$pop", "$founded",
                                  "$amp", "$cold", "$sea", "$topsoil", "$soil", "$agri" })
            cmd.Parameters.Add(new SqliteParameter(n, null));

        foreach (var t in tribes)
        {
            cmd.Parameters["$id"].Value = t.Code;
            cmd.Parameters["$c"].Value = t.ContinentId;
            cmd.Parameters["$r"].Value = $"R{t.ContinentId}";
            cmd.Parameters["$t"].Value = TributaryKeyOf(t, keyByTributaryId);
            cmd.Parameters["$name"].Value = t.Name;
            cmd.Parameters["$lat"].Value = t.Lat;
            cmd.Parameters["$lon"].Value = t.Lon;
            cmd.Parameters["$elev"].Value = t.ElevationM;
            cmd.Parameters["$biome"].Value = t.Biome;
            cmd.Parameters["$temp"].Value = t.MeanTempC;
            cmd.Parameters["$rain"].Value = t.AnnualRainMm;
            cmd.Parameters["$pop"].Value = WorldConfig.MembersPerTribe;
            cmd.Parameters["$founded"].Value = WorldConfig.StartYear;
            cmd.Parameters["$amp"].Value = t.SeasonAmpC;
            cmd.Parameters["$cold"].Value = t.ColdestMonthC;
            cmd.Parameters["$sea"].Value = t.DistToSeaKm;
            cmd.Parameters["$topsoil"].Value = t.TopsoilM;
            cmd.Parameters["$soil"].Value = t.SoilType;
            cmd.Parameters["$agri"].Value = t.AgricultureFactor;
            cmd.ExecuteNonQuery();
        }
    }

    private static void WriteMembers(SqliteConnection c, SqliteTransaction tx, IReadOnlyList<Member> members)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO members
              (member_id, tribe_id, continent_id, member_no, gender_code, gender,
               full_name, surname, given_name, age, birth_year, id_source)
            VALUES ($id,$tribe,$c,$no,$gc,$g,$name,$sur,$giv,$age,$by,$src);
            """;
        foreach (var n in new[] { "$id", "$tribe", "$c", "$no", "$gc", "$g", "$name",
                                  "$sur", "$giv", "$age", "$by", "$src" })
            cmd.Parameters.Add(new SqliteParameter(n, null));

        foreach (var m in members)
        {
            cmd.Parameters["$id"].Value = m.Id;
            cmd.Parameters["$tribe"].Value = m.TribeCode;
            cmd.Parameters["$c"].Value = m.ContinentId;
            cmd.Parameters["$no"].Value = m.Seq;
            cmd.Parameters["$gc"].Value = m.GenderCode;
            cmd.Parameters["$g"].Value = m.Gender == Gender.Male ? "male" : "female";
            cmd.Parameters["$name"].Value = m.FullName;
            cmd.Parameters["$sur"].Value = m.Surname.Length == 0 ? DBNull.Value : m.Surname;
            cmd.Parameters["$giv"].Value = m.GivenName;
            cmd.Parameters["$age"].Value = m.AgeAtGenesis;
            cmd.Parameters["$by"].Value = m.BirthYear;
            cmd.Parameters["$src"].Value = m.Source == IdSource.Genesis ? "genesis" : "derived";
            cmd.ExecuteNonQuery();
        }
    }

    private static void WriteTechState(SqliteConnection c, SqliteTransaction tx, long seed)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO tech_state(tribe_id, tech_id, acquired_year) VALUES ($t,$tech,$y);";
        cmd.Parameters.Add(new SqliteParameter("$t", null));
        cmd.Parameters.Add(new SqliteParameter("$tech", null));
        cmd.Parameters.Add(new SqliteParameter("$y", null));

        for (int gi = 0; gi < WorldConfig.TribeCount; gi++)
        {
            var (cont, local) = Identity.SplitTribeIndex(gi);
            string code = Identity.TribeCode(cont, local);
            foreach (var tech in WorldConfig.InitialTechSet)
            {
                cmd.Parameters["$t"].Value = code;
                cmd.Parameters["$tech"].Value = tech;
                // 初始技术视为"有史以来就有"，年份记模拟起点
                cmd.Parameters["$y"].Value = WorldConfig.StartYear;
                cmd.ExecuteNonQuery();
            }
        }
    }

    /// <summary>写 <c>id_ledger</c>：每部落水位 = 元年 1000（id.md §6.3）。</summary>
    private static void WriteIdLedger(SqliteConnection c, SqliteTransaction tx, IReadOnlyList<Tribe> tribes)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO id_ledger(continent, tribe, next_seq, minted_total) VALUES ($c,$t,$n,$m);";
        foreach (var n in new[] { "$c", "$t", "$n", "$m" })
            cmd.Parameters.Add(new SqliteParameter(n, null));

        foreach (var t in tribes)
        {
            cmd.Parameters["$c"].Value = t.ContinentId;
            cmd.Parameters["$t"].Value = t.LocalIndex;         // PK 是 (大陆, 大陆内部落号)
            cmd.Parameters["$n"].Value = WorldConfig.MembersPerTribe + 1;   // 下一个可用
            cmd.Parameters["$m"].Value = WorldConfig.MembersPerTribe;
            cmd.ExecuteNonQuery();
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  演化结果落库（§9.1 时钟跑完之后）
    // ══════════════════════════════════════════════════════════════════

    /// <summary>演化统计。</summary>
    public sealed record EvolutionStats(
        int TribesTotal, int TribesAlive, int TribesExtinct, int TribesBorn,
        int Discoveries, int Forgottens, int Fissions, int Extinctions, int Shocks,
        int TechsEverKnown, double TotalPopulation, double MaxEnergy,
        string TopTribe, int TopTribeTechs, string TopTribeTech,
        double FirstAgricultureYear, double FirstIronYear, double FirstIndustrialYear);

    /// <summary>
    /// 把 <see cref="EvolutionEngine"/> 跑出来的历史写进库。
    ///
    /// <b>幂等</b>：先清空上一轮的演化产物（<c>tech_state</c> / <c>events</c> / <c>tribe_history</c>、
    /// 以及上一轮分裂出来、<c>parent_tribe_id</c> 非空的部落行），再重写。
    /// 于是"同一 seed 跑两遍"不会把事件翻倍 —— 这是 INV-34 能逐字节比对的前提。
    ///
    /// 元年 100 个部落的 <c>tribes</c> 行<b>原地 UPDATE</b> 人口，不删不插：
    /// 它们连着 <c>members</c> 表（元年初代 10 万人），删了外键就悬空了。
    /// </summary>
    public static EvolutionStats WriteEvolution(
        string path,
        EvolutionEngine eng,
        IReadOnlyList<(double Year, int TribeId, double Pop, double Energy, double Lq, int Tier, double Prod)> history)
    {
        var csb = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite };
        using var conn = new SqliteConnection(csb.ToString());
        conn.Open();
        Exec(conn, "PRAGMA journal_mode = OFF;");
        Exec(conn, "PRAGMA synchronous = OFF;");

        using var tx = conn.BeginTransaction();

        // ── 幂等清理 ──
        Exec(conn, tx, "DELETE FROM tech_state;");
        Exec(conn, tx, "DELETE FROM events;");
        Exec(conn, tx, "DELETE FROM tribe_history;");
        Exec(conn, tx, "DELETE FROM tribes WHERE parent_tribe_id IS NOT NULL;");

        // ── 分裂出的新部落 ──
        InsertBornTribes(conn, tx, eng);
        UpdateGenesisPopulations(conn, tx, eng);

        // ── tech_state：每个部落的最终技术集 + 首次掌握年份 ──
        WriteTechStateFromEngine(conn, tx, eng);

        // ── events ──
        WriteEvents(conn, tx, eng);

        // ── tribe_history ──
        WriteHistory(conn, tx, eng, history);

        var stats = SummarizeEvolution(conn, tx, eng, history);
        WriteEvolutionMeta(conn, tx, stats);

        tx.Commit();
        return stats;
    }

    private static void InsertBornTribes(SqliteConnection c, SqliteTransaction tx, EvolutionEngine eng)
    {
        // 新部落继承母部落的河/支流/土壤/季节振幅 —— 分家分的是同一片地方
        var parentRow = new Dictionary<string, (string? River, string? Trib, double Amp, string? Soil)>(StringComparer.Ordinal);
        using (var q = c.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT tribe_id, river_id, tributary_id, season_amp_c, soil_type FROM tribes;";
            using var r = q.ExecuteReader();
            while (r.Read())
                parentRow[r.GetString(0)] = (
                    r.IsDBNull(1) ? null : r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    r.IsDBNull(3) ? 0.0 : r.GetDouble(3),
                    r.IsDBNull(4) ? null : r.GetString(4));
        }

        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT OR REPLACE INTO tribes VALUES
            ($id,$c,$r,$t,$name,$lat,$lon,$elev,$biome,$temp,$rain,$pop,$parent,$founded,
             $amp,$cold,$sea,$topsoil,$soil,$agri);
            """;
        foreach (var n in new[] { "$id", "$c", "$r", "$t", "$name", "$lat", "$lon", "$elev", "$biome",
                                  "$temp", "$rain", "$pop", "$parent", "$founded", "$amp", "$cold",
                                  "$sea", "$topsoil", "$soil", "$agri" })
            cmd.Parameters.Add(new SqliteParameter(n, null));

        var byId = eng.Tribes.ToDictionary(t => t.Id);

        foreach (var t in eng.Tribes)
        {
            if (t.ParentId < 0) continue;                    // 元年部落已在库里
            if (!byId.TryGetValue(t.ParentId, out var parent)) continue;
            parentRow.TryGetValue(parent.Code, out var prow);

            cmd.Parameters["$id"].Value = t.Code;
            cmd.Parameters["$c"].Value = t.ContinentId;
            cmd.Parameters["$r"].Value = (object?)prow.River ?? DBNull.Value;
            cmd.Parameters["$t"].Value = (object?)prow.Trib ?? DBNull.Value;
            cmd.Parameters["$name"].Value = t.Code;          // 新部落尚无正式名（§7.3 命名待接）
            cmd.Parameters["$lat"].Value = t.Lat;
            cmd.Parameters["$lon"].Value = t.Lon;
            cmd.Parameters["$elev"].Value = t.ElevationM;
            cmd.Parameters["$biome"].Value = t.Biome;
            cmd.Parameters["$temp"].Value = t.MeanTempC;
            cmd.Parameters["$rain"].Value = t.AnnualRainMm;
            cmd.Parameters["$pop"].Value = (long)Math.Round(t.Population);
            cmd.Parameters["$parent"].Value = parent.Code;   // §6.3：母部落
            cmd.Parameters["$founded"].Value = t.FoundedYear;
            cmd.Parameters["$amp"].Value = prow.Amp;
            cmd.Parameters["$cold"].Value = t.ColdestMonthC;
            cmd.Parameters["$sea"].Value = t.DistToSeaKm;
            cmd.Parameters["$topsoil"].Value = t.TopsoilM;
            cmd.Parameters["$soil"].Value = (object?)prow.Soil ?? DBNull.Value;
            cmd.Parameters["$agri"].Value = t.AgricultureFactor;
            cmd.ExecuteNonQuery();
        }
    }

    private static void UpdateGenesisPopulations(SqliteConnection c, SqliteTransaction tx, EvolutionEngine eng)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE tribes SET population = $p WHERE tribe_id = $id;";
        cmd.Parameters.Add(new SqliteParameter("$p", null));
        cmd.Parameters.Add(new SqliteParameter("$id", null));
        foreach (var t in eng.Tribes)
        {
            if (t.ParentId >= 0) continue;
            cmd.Parameters["$p"].Value = (long)Math.Round(t.Population);
            cmd.Parameters["$id"].Value = t.Code;
            cmd.ExecuteNonQuery();
        }
    }

    private static void WriteTechStateFromEngine(SqliteConnection c, SqliteTransaction tx, EvolutionEngine eng)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR REPLACE INTO tech_state(tribe_id, tech_id, acquired_year) VALUES ($t,$tech,$y);";
        cmd.Parameters.Add(new SqliteParameter("$t", null));
        cmd.Parameters.Add(new SqliteParameter("$tech", null));
        cmd.Parameters.Add(new SqliteParameter("$y", null));

        foreach (var t in eng.Tribes)
        {
            foreach (string tech in t.Techs)
            {
                if (!t.AcquiredYear.TryGetValue(tech, out double y)) y = WorldConfig.StartYear;
                cmd.Parameters["$t"].Value = t.Code;
                cmd.Parameters["$tech"].Value = tech;
                cmd.Parameters["$y"].Value = y;
                cmd.ExecuteNonQuery();
            }
        }
    }

    private static void WriteEvents(SqliteConnection c, SqliteTransaction tx, EvolutionEngine eng)
    {
        var codeById = eng.Tribes.ToDictionary(t => t.Id, t => t.Code);

        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO events(year, tribe_id, continent_id, event_type, tech_id, magnitude, description)
            VALUES ($y,$t,$c,$e,$tech,$m,$d);
            """;
        foreach (var n in new[] { "$y", "$t", "$c", "$e", "$tech", "$m", "$d" })
            cmd.Parameters.Add(new SqliteParameter(n, null));

        foreach (var ev in eng.Events)
        {
            cmd.Parameters["$y"].Value = ev.Year;
            cmd.Parameters["$t"].Value = codeById.TryGetValue(ev.TribeId, out var code) ? code : null;
            cmd.Parameters["$c"].Value = ev.ContinentId;
            cmd.Parameters["$e"].Value = ev.Type;
            cmd.Parameters["$tech"].Value = (object?)ev.TechId ?? DBNull.Value;
            cmd.Parameters["$m"].Value = ev.Magnitude;
            cmd.Parameters["$d"].Value = ev.Description;
            cmd.ExecuteNonQuery();
        }
    }

    private static void WriteHistory(
        SqliteConnection c, SqliteTransaction tx, EvolutionEngine eng,
        IReadOnlyList<(double Year, int TribeId, double Pop, double Energy, double Lq, int Tier, double Prod)> history)
    {
        var codeById = eng.Tribes.ToDictionary(t => t.Id, t => t.Code);

        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT OR REPLACE INTO tribe_history
            (tribe_id, year, population, energy_per_capita, life_quality, material_tier, tool_multiplier)
            VALUES ($t,$y,$p,$e,$l,$m,$prod);
            """;
        foreach (var n in new[] { "$t", "$y", "$p", "$e", "$l", "$m", "$prod" })
            cmd.Parameters.Add(new SqliteParameter(n, null));

        foreach (var h in history)
        {
            cmd.Parameters["$t"].Value = codeById.TryGetValue(h.TribeId, out var hc) ? hc : null;
            cmd.Parameters["$y"].Value = h.Year;
            cmd.Parameters["$p"].Value = (long)Math.Round(h.Pop);
            cmd.Parameters["$e"].Value = h.Energy;
            cmd.Parameters["$l"].Value = h.Lq;
            cmd.Parameters["$m"].Value = h.Tier;
            cmd.Parameters["$prod"].Value = h.Prod;
            cmd.ExecuteNonQuery();
        }
    }

    private static EvolutionStats SummarizeEvolution(
        SqliteConnection c, SqliteTransaction tx, EvolutionEngine eng,
        IReadOnlyList<(double Year, int TribeId, double Pop, double Energy, double Lq, int Tier, double Prod)> history)
    {
        int disc = 0, forget = 0, fiss = 0, ext = 0, shock = 0;
        foreach (var e in eng.Events)
        {
            switch (e.Type)
            {
                case "discover": disc++; break;
                case "forget": forget++; break;
                case "fission": fiss++; break;
                case "extinct": ext++; break;
                default: if (e.Type.StartsWith("shock:", StringComparison.Ordinal)) shock++; break;
            }
        }

        var alive = eng.Tribes.Where(t => t.Alive).ToList();
        var top = alive.OrderByDescending(t => t.EnergyPerCapita).FirstOrDefault();

        double firstAgri = double.NaN, firstIron = double.NaN, firstInd = double.NaN;
        foreach (var e in eng.Events)
        {
            if (e.Type != "discover" || e.TechId is null) continue;
            if (double.IsNaN(firstAgri) && e.TechId is "plow" or "animal_herd" or "ox_dom") firstAgri = e.Year;
            if (double.IsNaN(firstIron) && e.TechId == "iron") firstIron = e.Year;
            if (double.IsNaN(firstInd) && e.TechId == "coal_fuel") firstInd = e.Year;
        }

        return new EvolutionStats(
            TribesTotal: eng.Tribes.Count,
            TribesAlive: alive.Count,
            TribesExtinct: eng.Tribes.Count - alive.Count,
            TribesBorn: eng.Tribes.Count(t => t.ParentId >= 0),
            Discoveries: disc, Forgottens: forget, Fissions: fiss, Extinctions: ext, Shocks: shock,
            TechsEverKnown: eng.TechFrontierUnion.Count,
            TotalPopulation: alive.Sum(t => t.Population),
            MaxEnergy: top?.EnergyPerCapita ?? 0,
            TopTribe: top?.Code ?? "—",
            TopTribeTechs: top?.Techs.Count ?? 0,
            TopTribeTech: top is null ? "—" : HighestTechOf(eng, top),
            FirstAgricultureYear: firstAgri, FirstIronYear: firstIron, FirstIndustrialYear: firstInd);
    }

    private static string HighestTechOf(EvolutionEngine eng, TribeState t)
    {
        string best = "—"; int bestD = -1;
        foreach (string id in t.Techs)
            if (eng.Tree.TryGet(id, out var n))
            {
                int d = eng.Tree.DepthOf(id);
                if (d > bestD) { bestD = d; best = n.Name; }
            }
        return best;
    }

    private static void WriteEvolutionMeta(SqliteConnection c, SqliteTransaction tx, EvolutionStats s)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR REPLACE INTO world_meta(key, value) VALUES ($k, $v);";
        var pk = cmd.CreateParameter(); pk.ParameterName = "$k"; cmd.Parameters.Add(pk);
        var pv = cmd.CreateParameter(); pv.ParameterName = "$v"; cmd.Parameters.Add(pv);
        void Put(string k, string v) { pk.Value = k; pv.Value = v; cmd.ExecuteNonQuery(); }
        void PutN(string k, double v) => Put(k, v.ToString("R", CultureInfo.InvariantCulture));

        Put("evolved", "1");
        Put("evolved_tribes_total", s.TribesTotal.ToString(CultureInfo.InvariantCulture));
        Put("evolved_tribes_alive", s.TribesAlive.ToString(CultureInfo.InvariantCulture));
        Put("evolved_tribes_born", s.TribesBorn.ToString(CultureInfo.InvariantCulture));
        Put("evolved_tribes_extinct", s.TribesExtinct.ToString(CultureInfo.InvariantCulture));
        Put("evolved_techs_known", s.TechsEverKnown.ToString(CultureInfo.InvariantCulture));
        Put("evolved_top_tribe", s.TopTribe);
        PutN("evolved_total_population", s.TotalPopulation);
        PutN("evolved_max_energy", s.MaxEnergy);
        if (!double.IsNaN(s.FirstAgricultureYear)) PutN("evolved_first_agriculture_year", s.FirstAgricultureYear);
        if (!double.IsNaN(s.FirstIronYear)) PutN("evolved_first_iron_year", s.FirstIronYear);
        if (!double.IsNaN(s.FirstIndustrialYear)) PutN("evolved_first_industrial_year", s.FirstIndustrialYear);
    }

    /// <summary>库级 <c>Exec</c>（带事务）。</summary>
    private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ══════════════════════════════════════════════════════════════════
    //  辅助
    // ══════════════════════════════════════════════════════════════════

    public static string RiverId(River r) => $"R{r.ContinentId}";

    public static string TributaryId(Tributary t) => $"R{t.ContinentId}-T{t.Index}";

    /// <summary>
    /// 部落所属支流的键，形如 <c>R1-T3</c>。
    ///
    /// v0.11（M3）起<b>按真实归属查表</b>，不再由 <see cref="Tribe.LocalIndex"/> 反推。
    /// 旧写法 <c>(LocalIndex−1)/4+1</c> 只在「每条支流恒 4 个部落」时才成立；
    /// M3 的养育能力由长度与流域面积决定（见 <see cref="Geography.AllocateCapacity"/>），
    /// 各支流的部落数不再相等，反推会给出<b>错误的支流</b>（且错得很隐蔽：
    /// 仍然落在 1..5 范围内，INV-9b 照样通过）。
    ///
    /// 查不到就抛：悬空引用必须响亮地失败，不能退回一个"看起来合理"的序号。
    /// </summary>
    public static string TributaryKeyOf(Tribe t, IReadOnlyDictionary<int, string> keyByTributaryId)
        => keyByTributaryId.TryGetValue(t.TributaryId, out var k)
            ? k
            : throw new InvalidOperationException(
                $"部落 {t.Code} 指向支流 id={t.TributaryId}，但该支流不在世界模型里。");

    private static string SplineJson(IReadOnlyList<(double Lat, double Lon)> pts)
    {
        var sb = new StringBuilder(pts.Count * 18);
        sb.Append('[');
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('[')
              .Append(pts[i].Lat.ToString("0.######", CultureInfo.InvariantCulture)).Append(',')
              .Append(pts[i].Lon.ToString("0.######", CultureInfo.InvariantCulture)).Append(']');
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ══════════════════════════════════════════════════════════════════
    //  摘要
    // ══════════════════════════════════════════════════════════════════

    public sealed record DbStats(
        string Path, long SizeBytes, long Seed,
        int Continents, int Rivers, int Tributaries, int Tribes, int Members,
        double MinElevationM, double MaxElevationM,
        string StartYearDisplay, string EndYearDisplay);

    private static DbStats Summarize(string path, long seed, Geography.World w, IReadOnlyList<Member> members)
    {
        double minE = double.MaxValue, maxE = double.MinValue;
        foreach (var t in w.Tribes)
        {
            if (t.ElevationM < minE) minE = t.ElevationM;
            if (t.ElevationM > maxE) maxE = t.ElevationM;
        }

        return new DbStats(
            Path: path,
            SizeBytes: new FileInfo(path).Length,
            Seed: seed,
            Continents: Geography.Continents.Length,
            Rivers: w.Rivers.Count,
            Tributaries: w.Tributaries.Count,
            Tribes: w.Tribes.Count,
            Members: members.Count,
            MinElevationM: Math.Round(minE, 1),
            MaxElevationM: Math.Round(maxE, 1),
            StartYearDisplay: WorldConfig.FormatYear(WorldConfig.StartYear),
            EndYearDisplay: WorldConfig.FormatYear(WorldConfig.EndYear));
    }
}
