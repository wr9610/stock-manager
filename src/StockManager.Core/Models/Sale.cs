namespace StockManager.Core.Models;

/// <summary>销售单头（金额存分；赊账字段预留 V1.1）</summary>
public class Sale
{
    public string SaleId { get; set; } = "";              // 前缀 SAL + 36进制自增
    public string SaleNo { get; set; } = "";              // 单号 SO20260918001
    public long TotalAmountCents { get; set; }            // 应收合计
    public long DiscountAmountCents { get; set; }         // 抹零/折扣
    public long ReceivedAmountCents { get; set; }         // 已收金额（默认=应收）
    public long CostAmountCents { get; set; }             // 成本合计
    public long ProfitAmountCents { get; set; }           // 利润 = 应收 - 折扣 - 成本
    public long ReturnAmountCents { get; set; }           // 已退金额
    public string? Operator { get; set; }
    public string Status { get; set; } = "正常";           // 正常/已作废（全额退货）
    public string? Remark { get; set; }
    public DateTime CreateDate { get; set; }
    public List<SaleItem> Items { get; set; } = new();
}

/// <summary>销售明细</summary>
public class SaleItem
{
    public string SaleItemId { get; set; } = "";
    public string SaleId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string? Barcode { get; set; }
    public string? ProductName { get; set; }
    public long SalePriceCents { get; set; }              // 售价（分，含临时改价）
    public long CostPriceCents { get; set; }              // 卖出时移动平均成本（分）
    public string? Unit { get; set; }                     // 销售单位
    public decimal Qty { get; set; }
    public decimal BaseQty { get; set; }
    public long AmountCents { get; set; }                 // 售价×数量
    public decimal ReturnedQty { get; set; }              // 已退数量（部分退货）
}
