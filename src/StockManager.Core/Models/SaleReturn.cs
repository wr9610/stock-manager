namespace StockManager.Core.Models;

/// <summary>退货单头（B1 按行部分退货）</summary>
public class SaleReturn
{
    public string SaleReturnId { get; set; } = "";        // 前缀 RTR + 36进制自增
    public string? SaleId { get; set; }                   // 关联原销售单（可空=独立退货）
    public string ReturnNo { get; set; } = "";            // 单号 RT20260918001
    public long TotalAmountCents { get; set; }            // 退款金额（分）
    public string? Operator { get; set; }
    public string? Remark { get; set; }
    public DateTime CreateDate { get; set; }
    public List<SaleReturnItem> Items { get; set; } = new();
}

/// <summary>退货明细（B2：引用原销售行 + 原行成本）</summary>
public class SaleReturnItem
{
    public string SaleReturnItemId { get; set; } = "";
    public string SaleReturnId { get; set; } = "";
    public string? RefSaleItemId { get; set; }            // 关联原销售明细行
    public string ProductId { get; set; } = "";
    public string? Barcode { get; set; }
    public string? ProductName { get; set; }
    public long ReturnPriceCents { get; set; }            // 退货单价（分）
    public long CostPriceCents { get; set; }              // 原行成本（分）
    public decimal Qty { get; set; }
    public decimal BaseQty { get; set; }
    public long AmountCents { get; set; }
}
