using StockManager.Core;

namespace StockManager.Data;

/// <summary>
/// 试用授权（C1 + 商业风险4 加固）：
/// - 首次启动记 TrialStart（SysConfig + 注册表双份，取最早）
/// - 记录 MaxDateSeen（最大已见日期，单调递增）—— 防止改系统日期回拨重置试用
/// - 试用 15 天全功能，到期后进入只读（写操作被业务层拦截）
/// - Licensed=1 永久授权（激活码激活，P0-1）
/// </summary>
public static class LicenseService
{
    public const int TrialDays = 15;

    private const string KeyStart = "TrialStart";
    private const string KeyLicensed = "Licensed";
    private const string KeyMaxSeen = "MaxDateSeen";

    private static DatabaseService? _db;
    private static DateOnly? _trialStart;
    private static DateOnly _maxSeen;
    private static bool _licensed;

    /// <summary>应用启动时调用一次（幂等）。registryStart/registryMaxSeen 由 App 层从注册表读出传入。</summary>
    public static void Init(DatabaseService db, string? registryStart = null, string? registryMaxSeen = null)
    {
        _db = db;
        var today = DateOnly.FromDateTime(DateTime.Today);
        using var conn = db.Open();

        // 读 Licensed
        _licensed = GetConfigInt(conn, KeyLicensed) == 1;

        // TrialStart：DB + 注册表双份，取最早（防删任一处重置）
        DateOnly? start = null;
        if (DateOnly.TryParse(GetConfig(conn, KeyStart), out var d1)) start = d1;
        if (DateOnly.TryParse(registryStart, out var d2) && (start == null || d2 < start)) start = d2;
        _trialStart = start ?? today;
        SetConfig(conn, KeyStart, _trialStart.Value.ToString("yyyy-MM-dd"));

        // MaxDateSeen：取 DB、注册表、今天三者的最大值（单调不后退，防改日期回拨）
        DateOnly maxSeen = today;
        if (DateOnly.TryParse(GetConfig(conn, KeyMaxSeen), out var m1) && m1 > maxSeen) maxSeen = m1;
        if (DateOnly.TryParse(registryMaxSeen, out var m2) && m2 > maxSeen) maxSeen = m2;
        _maxSeen = maxSeen;
        SetConfig(conn, KeyMaxSeen, _maxSeen.ToString("yyyy-MM-dd"));
    }

    /// <summary>App 层写注册表用：应持久化的 TrialStart</summary>
    public static string RegistryTrialStart => _trialStart?.ToString("yyyy-MM-dd") ?? "";

    /// <summary>App 层写注册表用：应持久化的 MaxDateSeen</summary>
    public static string RegistryMaxDateSeen => _maxSeen.ToString("yyyy-MM-dd");

    /// <summary>正式授权（老王手动执行 / 注册成功时调用）</summary>
    public static void Activate()
    {
        if (_db == null) return;
        using var conn = _db.Open();
        SetConfig(conn, KeyLicensed, "1");
        _licensed = true;
    }

    /// <summary>
    /// 用激活码激活（P0-1）：先 HMAC 验签，匹配才写 Licensed=1。
    /// machineCode 由 App 层从本机指纹计算传入。
    /// </summary>
    public static (bool ok, string msg) TryActivate(string activationCode, string machineCode)
    {
        if (IsLicensed) return (true, "已经是正式版，无需重复激活");
        if (string.IsNullOrWhiteSpace(activationCode))
            return (false, "请输入激活码");

        if (!ActivationService.Validate(machineCode, activationCode))
            return (false, "激活码无效，请核对后重新输入（注意区分数字 0 和字母 O）");

        Activate();
        return (true, "✔ 激活成功！感谢购买支持");
    }

    /// <summary>当前机器码（App 层设置，设置页显示给客户抄给作者）</summary>
    public static string? MachineCode { get; set; }

    /// <summary>是否已正式授权</summary>
    public static bool IsLicensed => _licensed;

    /// <summary>
    /// 已使用天数（用 MaxDateSeen 而非"今天"，单调不后退 → 改系统日期回拨无法重置试用）
    /// </summary>
    public static int UsedDays
    {
        get
        {
            if (_trialStart == null) return 0;
            var start = _trialStart.Value.ToDateTime(TimeOnly.MinValue);
            var seen = _maxSeen.ToDateTime(TimeOnly.MinValue);
            return Math.Max(0, (seen - start).Days);
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