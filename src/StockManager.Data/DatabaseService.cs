using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace StockManager.Data;

/// <summary>
/// 数据库服务：打开连接（WAL+FULL 断电保护）、初始化建表、完整性检查、
/// VACUUM INTO 备份、SHA256 密码、本地 36 进制 ID 生成
/// </summary>
public class DatabaseService
{
    private readonly string _dbPath;

    public DatabaseService(string dbPath)
    {
        _dbPath = dbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
    }

    /// <summary>打开连接（A4：WAL + synchronous=FULL 断电保护）</summary>
    public SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    /// <summary>初始化：建表 + 默认管理员 + 版本记录</summary>
    public void Initialize()
    {
        using var conn = Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = DatabaseSchema.CreateAll;
            cmd.ExecuteNonQuery();
        }

        // 默认管理员 admin / admin123（老板角色，可改价）
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT OR IGNORE INTO SysUser
                    (UserId, UserName, PasswordHash, DisplayName, Role, CanChangePrice, IsActive)
                VALUES
                    ('USR000000001', 'admin', @hash, '管理员', '老板', 1, 1);";
            cmd.Parameters.AddWithValue("@hash", HashPassword("admin123"));
            cmd.ExecuteNonQuery();
        }

        // ID 计数器表
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS IdCounter (
                    Prefix TEXT PRIMARY KEY,
                    Counter INTEGER NOT NULL DEFAULT 0
                );";
            cmd.ExecuteNonQuery();
        }

        // 记录当前版本
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT OR IGNORE INTO DBVersion (VersionId, VersionNo, UpgradeSql)
                VALUES ('VER000000001', 1, '初始建库');";
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>启动完整性检查（A4）。true = 数据库完好</summary>
    public bool CheckIntegrity(out string result)
    {
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            result = cmd.ExecuteScalar()?.ToString() ?? "";
            return result == "ok";
        }
        catch (Exception ex)
        {
            result = ex.Message;
            return false;
        }
    }

    /// <summary>VACUUM INTO 一致快照备份（A3）</summary>
    public void BackupTo(string targetPath)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"VACUUM INTO '{targetPath.Replace("'", "''")}';";
        cmd.ExecuteNonQuery();
    }

    /// <summary>SHA256 密码哈希</summary>
    public static string HashPassword(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

    // ===== ID 生成：本地实现系统规则（前缀 + 36进制自增）=====

    /// <summary>获取下一条 ID（原子自增，表 IdCounter）。在业务事务内调用：传连接和事务，避免锁</summary>
    public string NewId(string prefix, SqliteConnection? conn = null, SqliteTransaction? tx = null)
    {
        bool ownConn = conn == null;
        conn ??= Open();
        tx ??= conn.BeginTransaction();
        try
        {
            // 确保计数器存在
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR IGNORE INTO IdCounter (Prefix, Counter) VALUES (@p, 0);";
                cmd.Parameters.AddWithValue("@p", prefix);
                cmd.ExecuteNonQuery();
            }

            long counter;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    UPDATE IdCounter SET Counter = Counter + 1 WHERE Prefix = @p;
                    SELECT Counter FROM IdCounter WHERE Prefix = @p;";
                cmd.Parameters.AddWithValue("@p", prefix);
                counter = Convert.ToInt64(cmd.ExecuteScalar());
            }

            if (ownConn) tx.Commit();
            return prefix + ToBase36(counter);
        }
        catch
        {
            if (ownConn) tx.Rollback();
            throw;
        }
        finally
        {
            if (ownConn) conn.Dispose();
        }
    }

    /// <summary>10 进制 → 36 进制字符串（补到 7 位，配合前缀共 12 位）</summary>
    public static string ToBase36(long n)
    {
        const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        if (n <= 0) return "0000001";
        var sb = new StringBuilder();
        while (n > 0)
        {
            sb.Insert(0, chars[(int)(n % 36)]);
            n /= 36;
        }
        return sb.ToString().PadLeft(7, '0');
    }
}
