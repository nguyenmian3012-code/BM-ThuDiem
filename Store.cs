using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BMThuDiem;

public sealed class ThuRow
{
    public long Id { get; set; }
    public string Stt { get; set; } = "";
    public string When { get; set; } = "";
    public string Diem { get; set; } = "";
    public string Operator { get; set; } = "";
    public string Plate { get; set; } = "";
    public string Impurity { get; set; } = "";
    public string Price { get; set; } = "";
    public DateTime Day { get; set; }
}

public sealed class TrialStore
{
    readonly SqliteConnection _cx;
    public string DbPath { get; }

    public TrialStore()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BmCancomp");
        var data = Path.Combine(root, "data");
        try { Directory.CreateDirectory(data); }
        catch
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BmCancomp");
            data = Path.Combine(root, "data");
            Directory.CreateDirectory(data);
        }
        DbPath = Path.Combine(data, "thudiem.db");
        _cx = new SqliteConnection("Data Source=" + DbPath);
        _cx.Open();
    }

    public string DataDir => Path.GetDirectoryName(DbPath)!;

    public void Init()
    {
        using var cmd = _cx.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS trials (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              ts TEXT NOT NULL,
              diem REAL,
              operator TEXT,
              plate TEXT,
              impurity TEXT,
              price TEXT,
              dry REAL,
              wet REAL,
              equiv REAL,
              listed INTEGER,
              raw TEXT,
              note TEXT,
              sync TEXT
            );
            CREATE TABLE IF NOT EXISTS shifts (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              start_min INTEGER NOT NULL,
              name TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
        Try("ALTER TABLE trials ADD COLUMN equiv REAL");
        Try("ALTER TABLE trials ADD COLUMN listed INTEGER");
        Try("ALTER TABLE trials ADD COLUMN note TEXT");
        RelaxDiem();
        using var n = _cx.CreateCommand();
        n.CommandText = "SELECT COUNT(*) FROM shifts";
        if (Convert.ToInt32(n.ExecuteScalar()) == 0)
            SaveShifts("07:00 a Phước\n07:00 a Đấu");
        Trim();
    }

    void RelaxDiem()
    {
        using var probe = _cx.CreateCommand();
        probe.CommandText = "SELECT \"notnull\" FROM pragma_table_info('trials') WHERE name = 'diem'";
        var flag = probe.ExecuteScalar();
        if (flag is null || Convert.ToInt32(flag) == 0) return;
        var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cols = _cx.CreateCommand())
        {
            cols.CommandText = "SELECT name FROM pragma_table_info('trials')";
            using var rd = cols.ExecuteReader();
            while (rd.Read()) have.Add(rd.GetString(0));
        }
        var want = new[] { "id", "ts", "diem", "operator", "plate", "impurity", "price", "dry", "wet", "raw", "sync", "equiv", "listed", "note" };
        var keep = want.Where(have.Contains).ToArray();
        var list = string.Join(", ", keep);
        using var tx = _cx.BeginTransaction();
        Exec(tx, "DROP TABLE IF EXISTS trials_new");
        Exec(tx, "CREATE TABLE trials_new (id INTEGER PRIMARY KEY AUTOINCREMENT, ts TEXT NOT NULL, diem REAL, operator TEXT, plate TEXT, impurity TEXT, price TEXT, dry REAL, wet REAL, equiv REAL, listed INTEGER, raw TEXT, note TEXT, sync TEXT)");
        Exec(tx, "INSERT INTO trials_new (" + list + ") SELECT " + list + " FROM trials");
        if (have.Contains("delta"))
            Exec(tx, "UPDATE trials_new SET equiv = (SELECT delta FROM trials o WHERE o.id = trials_new.id) WHERE equiv IS NULL");
        Exec(tx, "DROP TABLE trials");
        Exec(tx, "ALTER TABLE trials_new RENAME TO trials");
        tx.Commit();
    }

    void Exec(SqliteTransaction tx, string sql)
    {
        using var cmd = _cx.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    void Try(string sql)
    {
        try
        {
            using var cmd = _cx.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException) { }
    }

    public long Add(double? diem, string who, double dry, double wet, double equiv, int listed, string raw, string note)
    {
        using var cmd = _cx.CreateCommand();
        cmd.CommandText = """
            INSERT INTO trials(ts, diem, operator, plate, impurity, price, dry, wet, equiv, listed, raw, note, sync)
            VALUES($ts,$d,$w,'','','',$dry,$wet,$eq,$listed,$raw,$note,$sync)
            """;
        cmd.Parameters.AddWithValue("$ts", DateTimeOffset.Now.ToString("o"));
        cmd.Parameters.AddWithValue("$d", (object?)diem ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$w", who);
        cmd.Parameters.AddWithValue("$dry", dry);
        cmd.Parameters.AddWithValue("$wet", wet);
        cmd.Parameters.AddWithValue("$eq", equiv);
        cmd.Parameters.AddWithValue("$listed", listed);
        cmd.Parameters.AddWithValue("$raw", raw);
        cmd.Parameters.AddWithValue("$note", note);
        cmd.Parameters.AddWithValue("$sync", diem is null ? "ngoài bảng" : "chờ");
        cmd.ExecuteNonQuery();
        using var id = _cx.CreateCommand();
        id.CommandText = "SELECT last_insert_rowid()";
        var n = Convert.ToInt64(id.ExecuteScalar());
        Trim();
        return n;
    }

    public void HandOff(long id, double diem, string who, double dry, double wet, double equiv, int listed)
    {
        var payload = new
        {
            id = "td" + id.ToString(CultureInfo.InvariantCulture),
            source = "starch",
            starchPct = diem,
            stable = true,
            ts = DateTimeOffset.Now.ToString("o"),
            customer = who,
            raw = $"kho {dry:0} g | uot {wet:0} g | doi {equiv:0} g | moc {listed} g | {who}",
        };
        var json = JsonSerializer.Serialize(payload);
        var name = payload.id + ".json";
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BmCancomp", "data", "starch_out");
        var common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BmCancomp", "data", "starch_out");
        if (!TryDrop(local, name, json) && !TryDrop(common, name, json))
            throw new IOException("Không thả được phiếu thử điểm cho AutoCan.");
    }

    static bool TryDrop(string dir, string name, string json)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, name);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Mark(long id, string sync)
    {
        using var cmd = _cx.CreateCommand();
        cmd.CommandText = "UPDATE trials SET sync=$s WHERE id=$id";
        cmd.Parameters.AddWithValue("$s", sync);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void Trim()
    {
        const long cap = 500L * 1024 * 1024;
        long Size()
        {
            long n = File.Exists(DbPath) ? new FileInfo(DbPath).Length : 0;
            var wal = DbPath + "-wal";
            if (File.Exists(wal)) n += new FileInfo(wal).Length;
            return n;
        }
        if (Size() < cap) return;
        while (Size() > cap * 4 / 5)
        {
            using var del = _cx.CreateCommand();
            del.CommandText = "DELETE FROM trials WHERE id IN (SELECT id FROM trials ORDER BY id LIMIT 500)";
            if (del.ExecuteNonQuery() == 0) break;
        }
        using var ck = _cx.CreateCommand();
        ck.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        ck.ExecuteNonQuery();
        using var vac = _cx.CreateCommand();
        vac.CommandText = "VACUUM";
        vac.ExecuteNonQuery();
    }

    public List<ThuRow> Recent()
    {
        var rows = new List<ThuRow>();
        using var cmd = _cx.CreateCommand();
        cmd.CommandText = "SELECT id, ts, diem, IFNULL(operator,''), IFNULL(plate,''), IFNULL(impurity,''), IFNULL(price,''), IFNULL(note,'') FROM trials ORDER BY id DESC LIMIT 200";
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var note = rd.GetString(7);
            var diem = rd.IsDBNull(2) ? note : rd.GetDouble(2).ToString("0.00");
            var when = DateTime.Now;
            if (DateTimeOffset.TryParse(rd.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                when = dto.LocalDateTime;
            rows.Add(new ThuRow
            {
                Id = rd.GetInt64(0),
                When = when.ToString("HH:mm:ss"),
                Day = WorkDay(when),
                Diem = diem,
                Operator = rd.GetString(3),
                Plate = rd.GetString(4),
                Impurity = rd.GetString(5),
                Price = rd.GetString(6),
            });
        }
        rows.Reverse();
        for (var i = 0; i < rows.Count; i++) rows[i].Stt = i.ToString();
        rows.Reverse();
        var shown = new List<ThuRow>();
        DateTime? prev = null;
        foreach (var row in rows)
        {
            if (prev != null && row.Day != prev.Value)
                shown.Add(new ThuRow
                {
                    Id = 0,
                    When = "Hết " + row.Day.ToString("dd/MM") + " Bắt đầu " + prev.Value.ToString("dd/MM"),
                    Day = row.Day,
                });
            shown.Add(row);
            prev = row.Day;
        }
        return shown;
    }

    public sealed record AvgBox(string Name, double Avg, DateTime Day, int Count);

    public List<AvgBox> Averages()
    {
        var names = ShiftLines()
            .Select(line => line.Length > 6 ? line[6..].Trim() : "")
            .Where(name => name.Length > 0)
            .Distinct()
            .Take(2)
            .ToList();
        if (names.Count == 0) names.AddRange(["a Phước", "a Đấu"]);
        var rows = new List<(string Who, DateTime Day, double Diem)>();
        using (var cmd = _cx.CreateCommand())
        {
            cmd.CommandText = "SELECT ts, diem, IFNULL(operator,'') FROM trials WHERE diem IS NOT NULL";
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                if (!DateTimeOffset.TryParse(rd.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                    continue;
                rows.Add((rd.GetString(2).Trim(), WorkDay(dto.LocalDateTime), rd.GetDouble(1)));
            }
        }
        var list = new List<AvgBox>();
        foreach (var name in names)
        {
            var mine = rows.Where(r => string.Equals(r.Who, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (mine.Count == 0)
            {
                list.Add(new AvgBox(name, 0, DateTime.MinValue, 0));
                continue;
            }
            var day = mine.Max(r => r.Day);
            var today = mine.Where(r => r.Day == day).Select(r => r.Diem).ToList();
            list.Add(new AvgBox(name, today.Average(), day, today.Count));
        }
        return list;
    }

    public static DateTime WorkDay(DateTime local)
    {
        var day = local.Date;
        if (local.Hour < 7) day = day.AddDays(-1);
        return day;
    }

    public void Update(ThuRow row)
    {
        if (row.Id == 0) return;
        using var cmd = _cx.CreateCommand();
        cmd.CommandText = "UPDATE trials SET operator=$o, plate=$p, impurity=$i, price=$g WHERE id=$id";
        cmd.Parameters.AddWithValue("$o", row.Operator ?? "");
        cmd.Parameters.AddWithValue("$p", row.Plate ?? "");
        cmd.Parameters.AddWithValue("$i", row.Impurity ?? "");
        cmd.Parameters.AddWithValue("$g", row.Price ?? "");
        cmd.Parameters.AddWithValue("$id", row.Id);
        cmd.ExecuteNonQuery();
    }

    public List<string> ShiftLines()
    {
        var list = new List<string>();
        using var cmd = _cx.CreateCommand();
        cmd.CommandText = "SELECT start_min, name FROM shifts ORDER BY start_min, id";
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var min = rd.GetInt32(0);
            list.Add($"{min / 60:00}:{min % 60:00} {rd.GetString(1)}");
        }
        return list;
    }

    public void SaveShifts(string text)
    {
        using var del = _cx.CreateCommand();
        del.CommandText = "DELETE FROM shifts";
        del.ExecuteNonQuery();
        foreach (var line in text.Split('\n', '\r'))
        {
            var bit = line.Trim();
            if (bit.Length < 6) continue;
            var parts = bit.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var hm = parts[0].Split(':');
            if (hm.Length < 2 || !int.TryParse(hm[0], out var h) || !int.TryParse(hm[1], out var m)) continue;
            using var cmd = _cx.CreateCommand();
            cmd.CommandText = "INSERT INTO shifts(start_min, name) VALUES($m,$n)";
            cmd.Parameters.AddWithValue("$m", h * 60 + m);
            cmd.Parameters.AddWithValue("$n", parts[1].Trim());
            cmd.ExecuteNonQuery();
        }
    }

    public string OperatorNow()
    {
        var shifts = new List<(int Min, string Name)>();
        using (var cmd = _cx.CreateCommand())
        {
            cmd.CommandText = "SELECT start_min, name FROM shifts ORDER BY id";
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) shifts.Add((rd.GetInt32(0), rd.GetString(1)));
        }
        if (shifts.Count == 0) return "";
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
        catch { tz = TimeZoneInfo.Local; }
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var minute = now.Hour * 60 + now.Minute;
        var times = shifts.Select(s => s.Min).Distinct().OrderBy(x => x).ToList();
        var active = times[0];
        foreach (var t in times)
            if (t <= minute) active = t;
        if (minute < times[0]) active = times[^1];
        var group = shifts.Where(s => s.Min == active).Select(s => s.Name).ToList();
        if (group.Count <= 1) return group.FirstOrDefault() ?? "";
        var day = now.Date;
        if (minute < times.Min()) day = day.AddDays(-1);
        var n = (int)(day - new DateTime(2026, 11, 9)).TotalDays;
        if (n < 0) n = 0;
        return group[n % group.Count];
    }

    public string AdminPin()
    {
        var path = Path.Combine(Path.GetDirectoryName(DataDir)!, "appsettings.json");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("adminPin", out var pin))
            {
                var text = pin.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }
        catch { }
        return "2580";
    }
}
