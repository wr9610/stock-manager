namespace StockManager.Core.Models;

/// <summary>库存调整单</summary>
public class StockAdjust
{
    public string StockAdjustId { get; set; } = "";       // 前缀 ADJ + 36进制自增
    public string AdjustNo { get; set; } = "";            // 单号 AD20260918001
    public string AdjustType { get; set; } = "";          // 盘盈/盘亏/手动调整
    public decimal TotalQty { get; set; }
    public string? Operator { get; set; }
    public string? Remark { get; set; }                   // 调整原因（必填）
    public DateTime CreateDate { get; set; }
    public List<StockAdjustItem> Items { get; set; } = new();
}

/// <summary>调整明细</summary>
public class StockAdjustItem
{
    public string StockAdjustItemId { get; set; } = "";
    public string StockAdjustId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string? Barcode { get; set; }
    public string? ProductName { get; set; }
    public string? Unit { get; set; }                     // 录入时单位
    public decimal AdjustQty { get; set; }                // 正=盘盈/加，负=盘亏/减
    public decimal BeforeStock { get; set; }
    public decimal AfterStock { get; set; }
}
