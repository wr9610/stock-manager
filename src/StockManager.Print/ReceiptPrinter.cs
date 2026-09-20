using System.IO.Ports;
using System.Net.Sockets;

namespace StockManager.Print;

/// <summary>打印机连接方式</summary>
public enum PrinterMode
{
    None,      // 不打印
    Serial,    // 串口（USB 转串口 / 并口热敏）
    Network,   // 网络打印机（端口 9100 裸打印）
}

/// <summary>打印设置</summary>
public class PrinterConfig
{
    public PrinterMode Mode { get; set; } = PrinterMode.None;
    public string PortName { get; set; } = "COM3";
    public int BaudRate { get; set; } = 9600;
    public string Host { get; set; } = "192.168.1.100";
    public int NetPort { get; set; } = 9100;
    public string ShopName { get; set; } = "进销存收银";
    public string Footer { get; set; } = "谢谢惠顾，欢迎再次光临！";

    /// <summary>兼容旧序列化：旧版可能存成 Serialize/Deserialize 的字典，这里直接给默认</summary>
    public static PrinterConfig Default => new();
}

/// <summary>
/// 小票打印机服务：串口 / 网络 二选一，发送 ESC/POS 原始指令
/// 打印失败抛出异常由 UI 层提示（不中断收银主流程）
/// </summary>
public class ReceiptPrinter
{
    public PrinterConfig Config { get; }

    public ReceiptPrinter(PrinterConfig config)
    {
        Config = config;
    }

    /// <summary>列出现有串口（设置页选口用）</summary>
    public static string[] AvailablePorts() => SerialPort.GetPortNames();

    /// <summary>发送原始字节</summary>
    public void Send(byte[] data)
    {
        switch (Config.Mode)
        {
            case PrinterMode.None:
                return;
            case PrinterMode.Serial:
                SendSerial(data);
                break;
            case PrinterMode.Network:
                SendNetwork(data);
                break;
        }
    }

    private void SendSerial(byte[] data)
    {
        using var port = new SerialPort(Config.PortName, Config.BaudRate)
        {
            WriteTimeout = 3000,
        };
        port.Open();
        port.Write(data, 0, data.Length);
        // 留一点时间让打印机消化数据
        Thread.Sleep(300);
    }

    private void SendNetwork(byte[] data)
    {
        using var client = new TcpClient();
        client.Connect(Config.Host, Config.NetPort);
        client.SendTimeout = 3000;
        var stream = client.GetStream();
        stream.Write(data, 0, data.Length);
        stream.Flush();
        Thread.Sleep(300);
    }

    /// <summary>打印测试页</summary>
    public void PrintTestPage()
    {
        var e = new EscPos();
        e.Init();
        e.AlignCenter();
        e.Bold(true);
        e.DoubleHeight(true);
        e.Line("打印机测试");
        e.DoubleHeight(false);
        e.Bold(false);
        e.Divider('=');
        e.Line($"店铺：{Config.ShopName}");
        e.Line($"模式：{Config.Mode}  端口：{(Config.Mode == PrinterMode.Serial ? Config.PortName : Config.Host + ":" + Config.NetPort)}");
        e.Line($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        e.Divider('=');
        try
        {
            e.AlignCenter();
            e.Barcode("12345678");
            e.Feed(1);
        }
        catch { }
        e.AlignCenter();
        e.Line("打印正常，配置可用 ✓");
        Send(e.ToArray());
    }
}