namespace StockManager.Core.Models;

/// <summary>商品（金额存分，库存以基本单位存储）</summary>
public class Product
{
    public string ProductId { get; set; } = "";          // 前缀 PDT + 36进制自增
    public string? CategoryId { get; set; }
    public string? ProductCode { get; set; }              // 自编码/货号
    public string? Barcode { get; set; }                  // 主条码（唯一）
    public string ProductName { get; set; } = "";
    public string? Spec { get; set; }                     // 规格
    public string? BaseUnit { get; set; }                 // 基本单位（销售单位）如"包"
    public string? PackUnit { get; set; }                 // 包装单位（进货单位）如"箱"
    public decimal PackQty { get; set; }                  // 1箱 = 24包
    public long PurchasePriceCents { get; set; }          // 进价（分）
    public long SalePriceCents { get; set; }              // 售价（分）
    public decimal LowStockQty { get; set; }              // 低库存预警（基本单位）
    public decimal CurrentStock { get; set; }             // 当前库存（基本单位）
    public string? Remark { get; set; }
    public string? SearchCode { get; set; }               // 拼音首字母简码
    public bool IsSample { get; set; }                    // 示例商品
    public bool IsActive { get; set; } = true;
    public DateTime CreateDate { get; set; }
    public DateTime? UpdateDate { get; set; }

    /// <summary>是否有包装单位（多单位商品）</summary>
    public bool HasPackUnit => !string.IsNullOrWhiteSpace(PackUnit) && PackQty > 0;

    /// <summary>按录入单位折算为基本单位数量</summary>
    public decimal ToBaseQty(decimal qty, string? unit) =>
        unit == PackUnit ? qty * PackQty : qty;
}
