using System.Security.Cryptography;
using System.Text;

namespace StockManager.Core;

/// <summary>
/// 激活码机制（P0-1，商业风险3 加固）：
/// - 机器码：客户本机指纹的 SHA256 摘要（设备号）
/// - 激活码：HMACSHA256(机器码, 秘密密钥)，Base32 编码为 4 组×5 位大写字母数字
/// - 软件内验签：输入激活码 → 重新计算比对，通过才 Activate
/// - 离线生成器 LicenseGen（tools/）用同一算法，输入机器码产出激活码
///
/// 安全边界：密钥内嵌在程序里，能反编译拿到 → 挡不住决心破解的人；
/// 但挡得住"打开 DB 工具就改一行"的绝大多数（商业风险3 的目标）。
/// </summary>
public static class ActivationService
{
    // 签名密钥：老王卖的时候换一个随机串编译进程序即可
    // （旧客户已发的激活码会失效，统一换新码即可）
    public const string SecretKey = "StockManager-2026-S1-TaoBao-Key-v1";

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>机器码（设备号）：Windows MachineGuid 的 SHA256 前 10 字节 → Base32</summary>
    public static string MachineCode(string machineGuid)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(machineGuid));
        return ToBase32(hash.Take(10).ToArray());
    }

    /// <summary>生成激活码：HMACSHA256(机器码, 密钥) → 取前 12 字节(96bit=20字符 Base32) → 4组×5位</summary>
    public static string GenerateActivationCode(string machineCode, string secret = SecretKey)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(machineCode));
        var b32 = ToBase32(hash.Take(12).ToArray());
        // 20 字符 Base32 → 4 组 5 位
        return $"{b32[..5]}-{b32.Substring(5, 5)}-{b32.Substring(10, 5)}-{b32.Substring(15, 5)}";
    }

    /// <summary>验证激活码是否匹配机器码（容错：忽略大小写和短横线）</summary>
    public static bool Validate(string machineCode, string activationCode, string secret = SecretKey)
    {
        if (string.IsNullOrWhiteSpace(machineCode) || string.IsNullOrWhiteSpace(activationCode))
            return false;
        var normalized = activationCode.Trim().Replace("-", "").ToUpperInvariant();
        var expected = GenerateActivationCode(machineCode, secret).Replace("-", "").ToUpperInvariant();
        return normalized == expected;
    }

    private static string ToBase32(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32Alphabet[(buffer >> (bits - 5)) & 0x1F]);
                bits -= 5;
            }
        }
        if (bits > 0)
            sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 0x1F]);
        return sb.ToString();
    }
}