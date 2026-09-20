using StockManager.Print;

namespace StockManager.Data;

/// <summary>
/// 打印机配置持久化（存 SysConfig 表，JSON 序列化整段）
/// 一个键 PrintConfig 存完整配置，避免多行写入
/// </summary>
public static class PrinterConfigStore
{
    private const string Key = "PrintConfig";

    /// <summary>读取配置（无记录返回默认）</summary>
    public static PrinterConfig Load(DatabaseService db)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ConfigValue FROM SysConfig WHERE ConfigKey=@k";
        cmd.Parameters.AddWithValue("@k", Key);
        var v = cmd.ExecuteScalar()?.ToString();
        if (string.IsNullOrEmpty(v)) return PrinterConfig.Default;
        try
        {
            var c = System.Text.Json.JsonSerializer.Deserialize<PrinterConfig>(v);
            return c ?? PrinterConfig.Default;
        }
        catch
        {
            return PrinterConfig.Default;
        }
    }

    /// <summary>保存配置</summary>
    public static void Save(DatabaseService db, PrinterConfig cfg)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(cfg);
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO SysConfig (ConfigKey, ConfigValue) VALUES (@k, @v)
            ON CONFLICT(ConfigKey) DO UPDATE SET ConfigValue=@v";
        cmd.Parameters.AddWithValue("@k", Key);
        cmd.Parameters.AddWithValue("@v", json);
        cmd.ExecuteNonQuery();
    }
}