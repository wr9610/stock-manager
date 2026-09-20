namespace StockManager.Data;

/// <summary>
/// 试用授权（C1）：
/// - 首次启动记 TrialStart（SysConfig.TrialStart = yyyy-MM-dd）
/// - 试用 15 天全功能，到期后进入只读（写操作被业务层拦截）
/// - Licensed=1 永久授权（交付/自用时由老王设置，也可做成注册码）
/// </summary>
public static class LicenseService
{
    public const int TrialDays = 15;

    private const string KeyStart = "TrialStart";
    private const string KeyLicensed = "Licensed";

    private static DatabaseService? _db;
    private static DateOnly? _trialStart;
    private static bool _licensed;

    /// <summary>应用启动时调用一次（幂等）</summary>
    public static void Init(DatabaseService db)
    {
        _db = db;
        using var conn = db.Open();

        // 读 Licensed
        _licensed = GetConfigInt(conn, KeyLicensed) == 1;

        // 读/建 TrialStart
        var startStr = GetConfig(conn, KeyStart);
        if (string.IsNullOrWhiteSpace(startStr))
        {
            _trialStart = DateOnly.FromDateTime(DateTime.Now);
            SetConfig(conn, KeyStart, _trialStart.Value.ToString("yyyy-MM-dd"));
        }
        else if (DateOnly.TryParse(startStr, out var d))
        {
            _trialStart = d;
        }
        else
        {
            // 脏数据兜底
            _trialStart = DateOnly.FromDateTime(DateTime.Now);
            SetConfig(conn, KeyStart, _trialStart.Value.ToString("yyyy-MM-dd"));
        }
    }

    /// <summary>正式授权（老王手动执行 / 注册成功时调用）</summary>
    public static void Activate()
    {
        if (_db == null) return;
        using var conn = _db.Open();
        SetConfig(conn, KeyLicensed, "1");
        _licensed = true;
    }

    /// <summary>是否已正式授权</summary>
    public static bool IsLicensed => _licensed;

    /// <summary>已使用天数</summary>
    public static int UsedDays
    {
        get
        {
            if (_trialStart == null) return 0;
            var today = DateTime.Today;
            var start = _trialStart.Value.ToDateTime(TimeOnly.MinValue);
            return Math.Max(0, (today - start).Days);
        }
    }

    /// <summary>剩余试用天数</summary>
    public static int DaysLeft => IsLicensed ? -1 : Math.Max(0, TrialDays - UsedDays);

    /// <summary>是否只读（到期未授权）</summary>
    public static bool IsReadOnly => !IsLicensed && UsedDays >= TrialDays;

    /// <summary>只读拦截：写操作入口统一调用，返回错误信息（空=可写）</summary>
    public static string? WriteBlocked() => IsReadOnly
        ? "试用期已结束，系统进入只读模式。\n请先备份数据，再联系作者购买激活码。"
        : null;

    // ===== SysConfig helpers =====
    private static string? GetConfig(Microsoft.Data.Sqlite.SqliteConnection conn, string key)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ConfigValue FROM SysConfig WHERE ConfigKey=@k";
        cmd.Parameters.AddWithValue("@k", key);
        return cmd.ExecuteScalar()?.ToString();
    }

    private static int GetConfigInt(Microsoft.Data.Sqlite.SqliteConnection conn, string key)
        => int.TryParse(GetConfig(conn, key), out var v) ? v : 0;

    private static void SetConfig(Microsoft.Data.Sqlite.SqliteConnection conn, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO SysConfig (ConfigKey, ConfigValue) VALUES (@k, @v)
            ON CONFLICT(ConfigKey) DO UPDATE SET ConfigValue=@v";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }
}