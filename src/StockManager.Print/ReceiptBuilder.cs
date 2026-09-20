using StockManager.Core;
using StockManager.Core.Models;

namespace StockManager.Print;

/// <summary>
/// 销售小票 / 退货小票模板（58mm）
/// 调用方只需传 Sale/SaleReturn，本类负责排版
/// </summary>
public static class ReceiptBuilder
{
    /// <summary>构建销售小票指令流</summary>
    public static byte[] BuildSale(Sale s, string shopName = "进销存收银", string footer = "谢谢惠顾，欢迎再次光临！")
    {
        var e = new EscPos();
        e.Init();

        // 店名
        e.AlignCenter();
        e.Bold(true);
        e.DoubleHeight(true);
        e.Line(shopName);
        e.DoubleHeight(false);
        e.Bold(false);
        e.FontB();
        e.Line($"销售单号：{s.SaleNo}");
        e.Line($"时间：{s.CreateDate:yyyy-MM-dd HH:mm:ss}");
        e.Line($"收银员：{s.Operator}");
        e.FontA();
        e.Divider('=');

        // 明细
        e.Bold(true);
        e.ItemLine("品名", "单价×数量", "金额");
        e.Bold(false);
        e.Divider('-');
        foreach (var item in s.Items)
        {
            var pq = $"{item.SalePriceCents.ToYuanString()}×{item.Qty}";
            e.ItemLine(item.ProductName ?? "", pq, item.AmountCents.ToYuanString());
        }
        e.Divider('-');

        // 金额区
        e.TwoCol("应收", s.TotalAmountCents.ToYuanString());
        if (s.DiscountAmountCents > 0)
            e.TwoCol("抹零/折扣", "-" + s.DiscountAmountCents.ToYuanString());
        e.Bold(true);
        e.TwoCol("实收", s.ReceivedAmountCents.ToYuanString());
        e.Bold(false);
        var change = s.ReceivedAmountCents - (s.TotalAmountCents - s.DiscountAmountCents);
        if (change > 0)
            e.TwoCol("找零", change.ToYuanString());

        e.Divider('=');
        e.Feed(1);

        // 二维码（单号，方便查单）
        try
        {
            e.AlignCenter();
            e.QRCode(s.SaleNo);
        }
        catch
        {
            // 个别打印机不支持二维码，忽略
        }

        e.AlignCenter();
        e.FontB();
        e.Line(footer);
        e.Line(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        return e.ToArray();
    }

    /// <summary>构建退货小票指令流</summary>
    public static byte[] BuildReturn(SaleReturn r, Sale? origin = null, string shopName = "进销存收银", string footer = "退货凭证，请妥善保管")
    {
        var e = new EscPos();
        e.Init();

        e.AlignCenter();
        e.Bold(true);
        e.DoubleHeight(true);
        e.Line("退 货");
        e.DoubleHeight(false);
        e.Bold(false);
        e.FontB();
        e.Line($"退货单号：{r.ReturnNo}");
        e.Line($"时间：{r.CreateDate:yyyy-MM-dd HH:mm:ss}");
        if (origin != null) e.Line($"原销售单：{origin.SaleNo}");
        e.FontA();
        e.Divider('=');

        e.Bold(true);
        e.ItemLine("品名", "退款单价×数量", "金额");
        e.Bold(false);
        e.Divider('-');
        foreach (var item in r.Items)
        {
            var pq = $"{item.ReturnPriceCents.ToYuanString()}×{item.Qty}";
            e.ItemLine(item.ProductName ?? "", pq, item.AmountCents.ToYuanString());
        }
        e.Divider('-');
        e.Bold(true);
        e.TwoCol("退款合计", r.TotalAmountCents.ToYuanString());
        e.Bold(false);

        e.Divider('=');
        e.AlignCenter();
        e.FontB();
        e.Line(footer);
        e.Line(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        return e.ToArray();
    }
}