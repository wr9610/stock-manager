namespace StockManager.Core.Models;

/// <summary>进货单头</summary>
public class Purchase
{
    public string PurchaseId { get; set; } = "";          // 前缀 PCH + 36进制自增
    public string PurchaseNo { get; set; } = "";          // 单号 PO20260918001
    public string? SupplierId { get; set; }
    public string? SupplierName { get; set; }             // 冗余存名
    public long TotalAmountCents { get; set; }
    public int ItemCount { get; set; }
    public string? Operator { get; set; }
    public string Status { get; set; } = "正常";           // 正常/已作废（红冲）
    public string? Remark { get; set; }
    public DateTime CreateDate { get; set; }
    public List<PurchaseItem> Items { get; set; } = new();
}

/// <summary>进货明细</summary>
public class PurchaseItem
{
    public string PurchaseItemId { get; set; } = "";
    public string PurchaseId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string? Barcode { get; set; }
    public string? ProductName { get; set; }
    public long PurchasePriceCents { get; set; }          // 进价（分）
    public string? Unit { get; set; }                     // 录入时单位（基本/包装）
    public decimal Qty { get; set; }                      // 数量（按所选单位）
    public decimal BaseQty { get; set; }                  // 折算基本单位数量
    public long AmountCents { get; set; }                 // 金额（分）= 进价×数量
}
