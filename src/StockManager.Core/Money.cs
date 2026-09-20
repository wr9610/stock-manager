namespace StockManager.Core;

/// <summary>
/// 金额工具（A1：金额一律存分，避免 SQLite 浮点精度问题）
/// 领域内部统一用 long（分），只在 UI 显示和 Excel 导入导出处做分↔元转换
/// </summary>
public static class Money
{
    /// <summary>元 → 分（四舍五入）</summary>
    public static long ToCents(this decimal yuan) => (long)Math.Round(yuan * 100m, 0, MidpointRounding.AwayFromZero);

    /// <summary>元 → 分</summary>
    public static long ToCents(this double yuan) => ((decimal)Math.Round(yuan, 2)).ToCents();

    /// <summary>分 → 元（用于 UI 显示 / Excel 导出）</summary>
    public static decimal ToYuan(this long cents) => cents / 100m;

    /// <summary>分 → 元（decimal 版，避免整数除法）</summary>
    public static decimal ToYuan(this int cents) => cents / 100m;

    /// <summary>单价分 × 数量（数量可能为小数，如 1.5 斤）→ 金额分（四舍五入）</summary>
    public static long Amount(this long priceCents, decimal qty) =>
        (long)Math.Round(priceCents * qty, 0, MidpointRounding.AwayFromZero);

    /// <summary>格式化：分 → "1,234.56 元" 字符串（UI 用）</summary>
    public static string ToYuanString(this long cents) => (cents / 100m).ToString("N2");
}
