using StockManager.Core;
using StockManager.Core.Models;

namespace StockManager.Data;

/// <summary>Excel 导入的一行（预览用）</summary>
public class ImportRow
{
    public string Barcode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Spec { get; set; } = "";
    public string Unit { get; set; } = "";
    public long PurchaseCents { get; set; }
    public long SaleCents { get; set; }
    public decimal Stock { get; set; }
    public decimal LowStock { get; set; }
    public string? Error { get; set; }
    public bool Valid => string.IsNullOrEmpty(Error);
}

/// <summary>
/// Excel 商品批量导入（C2）
/// 支持列头：条码/货号/商品名/名称/规格/单位/基本单位/进价/成本价/售价/库存/低库存预警
/// 数字类型兼容文本单元格；金额按"元"解析
/// </summary>
public static class ExcelImportService
{
    /// <summary>解析 Excel 为导入行（不落库，供预览）</summary>
    public static List<ImportRow> Parse(string path)
    {
        var rows = new List<ImportRow>();

        // useHeaderRow: true → 第一行即表头，返回行的 key 就是表头名
        // 兼容真实 Excel（可能混入标题行/空行）——用动态列名匹配
        using var stream = File.OpenRead(path);
        var sheet = MiniExcelLibs.MiniExcel.Query(stream, useHeaderRow: true).ToList();
        if (sheet.Count == 0)
        {
            throw new InvalidDataException("Excel 里没有数据（第一行应为表头：条码/商品名/进价/售价等）");
        }

        // 表头：取第一行 key（useHeaderRow=true 时即表头单元格文本）
        var headers = new List<string>();
        if (sheet[0] is IDictionary<string, object?> first)
        {
            foreach (var k in first.Keys) headers.Add(k.Trim());
        }
        else
        {
            throw new InvalidDataException("Excel 第一行必须是表头（条码/商品名/进价/售价等）");
        }

        int Col(string name) => headers.FindIndex(h =>
            h.Equals(name, StringComparison.OrdinalIgnoreCase) || h.Contains(name, StringComparison.OrdinalIgnoreCase));

        var barcodeIdx = Col("条码");
        var nameIdx = Col("商品名");
        if (nameIdx < 0) nameIdx = Col("名称");
        var specIdx = Col("规格");
        var unitIdx = Col("单位");
        if (unitIdx < 0) unitIdx = Col("基本单位");
        var purchaseIdx = Col("进价");
        if (purchaseIdx < 0) purchaseIdx = Col("成本价");
        var saleIdx = Col("售价");
        var stockIdx = Col("库存");
        var lowIdx = Col("低库存");
        if (lowIdx < 0) lowIdx = Col("预警");

        if (nameIdx < 0)
        {
            throw new InvalidDataException($"Excel 缺少「商品名」列。\n识别到的表头：{string.Join(" / ", headers)}");
        }

        // 遍历数据行
        foreach (var rowObj in sheet)
        {
            if (rowObj is not IDictionary<string, object?> row) continue;
            var values = row.Values.ToList();
            string Get(int idx) => idx >= 0 && idx < values.Count ? values[idx]?.ToString()?.Trim() ?? "" : "";

            var r = new ImportRow
            {
                Barcode = Get(barcodeIdx),
                ProductName = Get(nameIdx),
                Spec = Get(specIdx),
                Unit = Get(unitIdx),
                PurchaseCents = ParseMoney(Get(purchaseIdx)),
                SaleCents = ParseMoney(Get(saleIdx)),
                Stock = ParseDec(Get(stockIdx)),
                LowStock = ParseDec(Get(lowIdx)),
            };

            // 空行跳过
            if (string.IsNullOrEmpty(r.ProductName) && string.IsNullOrEmpty(r.Barcode)) continue;

            if (string.IsNullOrEmpty(r.ProductName))
            {
                r.Error = "商品名空";
            }
            else if (string.IsNullOrEmpty(r.Barcode))
            {
                r.Error = "条码空";
            }
            rows.Add(r);
        }
        return rows;
    }

    /// <summary>批量落库：返回 (成功数, 失败清单)。重复条码/非法行跳过并记失败</summary>
    public static (int ok, List<string> fails) Import(StockService stock, List<ImportRow> rows)
    {
        var fails = new List<string>();
        var okCount = 0;
        foreach (var r in rows)
        {
            if (!r.Valid)
            {
                fails.Add($"{r.ProductName}：{r.Error}");
                continue;
            }
            // 条码唯一（主条码/附加条码都查）
            if (!string.IsNullOrEmpty(r.Barcode) && stock.FindByBarcode(r.Barcode) != null)
            {
                fails.Add($"{r.ProductName}：条码 {r.Barcode} 已存在，跳过");
                continue;
            }
            var p = new Product
            {
                Barcode = r.Barcode,
                ProductName = r.ProductName,
                Spec = r.Spec,
                BaseUnit = string.IsNullOrEmpty(r.Unit) ? "个" : r.Unit,
                PurchasePriceCents = r.PurchaseCents,
                SalePriceCents = r.SaleCents,
                LowStockQty = r.LowStock,
                CurrentStock = r.Stock,
                IsActive = true,
            };
            var (ok, error) = stock.SaveProduct(p);
            if (ok)
            {
                okCount++;
            }
            else
            {
                fails.Add($"{r.ProductName}：{error}");
            }
        }
        return (okCount, fails);
    }

    private static long ParseMoney(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        return decimal.TryParse(s, out var v) ? v.ToCents() : 0;
    }

    private static decimal ParseDec(string s)
        => decimal.TryParse(s, out var v) ? v : 0;
}