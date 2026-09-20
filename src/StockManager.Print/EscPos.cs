using System.Text;

namespace StockManager.Print;

/// <summary>
/// ESC/POS 热敏小票指令构建器（58mm 纸，宽度 32 半角字符）
/// 遵循常见 58mm 打印机指令集（兼容 EPSON / 佳博 / 芯烨等国产机）
/// </summary>
public class EscPos
{
    private readonly MemoryStream _buf = new();

    static EscPos()
    {
        // .NET Core 需显式注册代码页（GBK 等）才能按 936 编码中文
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // ===== 基础控制 =====
    public void Init() => Write(0x1B, 0x40);                       // ESC @
    public void Feed(int lines) => Write(0x1B, 0x64, (byte)lines); // ESC d n 进纸n行
    public void Cut() => Write(0x1D, 0x56, 0x41, 0x00);            // GS V A 切纸
    public void Beep() => Write(0x1B, 0x42, 0x02, 0x02);           // ESC B n t 蜂鸣

    // ===== 样式 =====
    public void Bold(bool on) => Write(0x1B, 0x45, (byte)(on ? 1 : 0));       // ESC E
    public void Underline(bool on) => Write(0x1B, 0x2D, (byte)(on ? 1 : 0));  // ESC -
    public void DoubleHeight(bool on) => Write(0x1D, 0x21, (byte)(on ? 0x11 : 0x00)); // GS !
    public void AlignLeft() => Write(0x1B, 0x61, 0x00);
    public void AlignCenter() => Write(0x1B, 0x61, 0x01);
    public void AlignRight() => Write(0x1B, 0x61, 0x02);
    public void FontA() => Write(0x1B, 0x4D, 0x00);   // 12×24
    public void FontB() => Write(0x1B, 0x4D, 0x01);   // 9×17 小字

    // ===== 文本 =====
    /// <summary>按 GBK 编码写文本（国产打印机默认 GBK，中文 2 字节）</summary>
    public void Text(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        var bytes = Encoding.GetEncoding("GBK").GetBytes(s);
        _buf.Write(bytes, 0, bytes.Length);
    }

    public void Line(string s) { Text(s); Text("\n"); }

    // ===== 条码 / 二维码 =====
    /// <summary>CODE128 条码</summary>
    public void Barcode(string code)
    {
        if (string.IsNullOrEmpty(code)) return;
        Write(0x1D, 0x48, 0x02);   // HRI 字符在条码下方
        Write(0x1D, 0x66, 0x01);   // HRI 字体 B
        Write(0x1D, 0x68, 0x60);   // 高度 96
        Write(0x1D, 0x77, 0x02);   // 宽度
        Write(0x1D, 0x6B, 0x49);   // GS k m=73 (CODE128)
        var bytes = Encoding.ASCII.GetBytes(code);
        Write((byte)code.Length);  // 长度
        Write(bytes);
    }

    /// <summary>QR 二维码（QRCode Model 2）</summary>
    public void QRCode(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        // 设置模块大小
        Write(0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, 0x06);
        // 纠错等级 M
        Write(0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, 0x31);
        // 存数据
        var bytes = Encoding.UTF8.GetBytes(text);
        var n = bytes.Length + 3;
        Write(0x1D, 0x28, 0x6B, (byte)(n & 0xFF), (byte)((n >> 8) & 0xFF), 0x31, 0x50, 0x30);
        Write(bytes);
        // 打印
        Write(0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30);
    }

    // ===== 布局辅助 =====
    /// <summary>左右两列对齐（左侧文本，右侧数字右对齐）</summary>
    public void TwoCol(string left, string right, int width = 32)
    {
        var l = Truncate(left, width - 2);
        var r = Truncate(right, Math.Max(1, width - l.Length - 1));
        Text(l);
        Text(new string(' ', Math.Max(1, width - l.Length - r.Length)));
        Text(r);
        Text("\n");
    }

    /// <summary>四列：名称 / 单价×数量 / 金额</summary>
    public void ItemLine(string name, string priceQty, string amount, int width = 32)
    {
        var n = Truncate(name, width / 2);
        var pq = Truncate(priceQty, width / 2 - 2);
        var a = Truncate(amount, 8);
        Text(n);
        Text(new string(' ', Math.Max(1, width / 2 - n.Length)));
        Text(pq);
        Text(new string(' ', Math.Max(1, width / 2 - 2 - pq.Length)));
        Text(a);
        Text("\n");
    }

    /// <summary>分隔线</summary>
    public void Divider(char ch = '-')
    {
        Text(new string(ch, 32) + "\n");
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // 一个中文字符占 2 个半角宽度
        var count = 0;
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            var w = ch > 0x7F ? 2 : 1;
            if (count + w > max) break;
            count += w;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private void Write(params byte[] data) => _buf.Write(data, 0, data.Length);
    private void Write(byte b) => _buf.WriteByte(b);

    /// <summary>取出完整指令字节流</summary>
    public byte[] ToArray()
    {
        // 结尾：换行 + 进纸 3 行 + 切纸 + 初始化
        Write(0x0A);
        Feed(3);
        Cut();
        return _buf.ToArray();
    }
}