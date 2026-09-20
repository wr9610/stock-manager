using StockManager.Core;

namespace StockManager.LicenseGen;

/// <summary>
/// 激活码离线生成器（老王专用，不随软件发给客户）
/// 用法：
///   dotnet run --project tools/StockManager.LicenseGen -- <机器码>
/// 客户在设置页抄「机器码」发给你，你输入后得到激活码回给客户。
/// </summary>
internal static class Program
{
    private static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("══════════════════════════════════════");
        Console.WriteLine("  进销存 激活码生成器 v1.0（作者专用）");
        Console.WriteLine("══════════════════════════════════════");

        string machine;
        if (args.Length >= 1)
        {
            machine = args[0].Trim();
        }
        else
        {
            Console.Write("输入客户的机器码：");
            machine = Console.ReadLine()?.Trim() ?? "";
        }

        if (string.IsNullOrEmpty(machine))
        {
            Console.WriteLine("机器码不能为空");
            return;
        }

        var code = ActivationService.GenerateActivationCode(machine);
        Console.WriteLine($"\n机器码：{machine}");
        Console.WriteLine($"激活码：{code}");
        Console.WriteLine("\n把激活码发给客户，在设置页「激活码」框里输入即可。");

        Console.WriteLine("\n验证一下（自检）…");
        Console.WriteLine($"  Validate = {ActivationService.Validate(machine, code)}");
    }
}