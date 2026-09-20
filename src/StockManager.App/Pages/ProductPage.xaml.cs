using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StockManager.Core;
using StockManager.Core.Models;
using StockManager.Data;

namespace StockManager.App.Pages;

public partial class ProductPage : UserControl
{
    private readonly ObservableCollection<ProductRow> _rows = new();

    public ProductPage()
    {
        InitializeComponent();
        Grid.ItemsSource = _rows;
        LoadData();
    }

    private void LoadData()
    {
        var keyword = SearchBox.Text.Trim();
        _rows.Clear();
        var list = App.Stock.QueryProducts(keyword, ShowInactive.IsChecked == true);
        foreach (var p in list)
            _rows.Add(ProductRow.From(p));

        var total = _rows.Count;
        var lowStock = _rows.Count(r => r.CurrentStock <= r.LowStockQty && r.LowStockQty > 0);
        var lowNames = string.Join("、", _rows.Where(r => r.CurrentStock <= r.LowStockQty && r.LowStockQty > 0)
                                              .Take(5).Select(r => r.ProductName));
        SummaryText.Text = $"共 {total} 种商品";
        if (lowStock > 0)
        {
            SummaryText.Text += $"　⚠ {lowStock} 种库存低于预警：{lowNames}";
            SummaryText.Foreground = Brush("#DC2626");
        }
        else
        {
            SummaryText.Foreground = Brush("#64748B");
        }
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) LoadData();
    }

    private void Search_Click(object sender, RoutedEventArgs e) => LoadData();

    private void ShowInactive_Changed(object sender, RoutedEventArgs e) => LoadData();

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var w = new ProductEditWindow(null) { Owner = Window.GetWindow(this) };
        if (w.ShowDialog() == true) LoadData();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not ProductRow row) { MessageBox.Show("请先选中商品", "提示"); return; }
        var w = new ProductEditWindow(row) { Owner = Window.GetWindow(this) };
        if (w.ShowDialog() == true) LoadData();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Edit_Click(sender, e);

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 显示该商品的附加条码
        if (Grid.SelectedItem is not ProductRow row)
        {
            ExtraBarcodeText.Text = "";
            return;
        }
        using var conn = App.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Barcode FROM ProductBarcode WHERE ProductId=@id ORDER BY CreateDate";
        cmd.Parameters.AddWithValue("@id", row.ProductId);
        using var rd = cmd.ExecuteReader();
        var list = new List<string>();
        while (rd.Read()) list.Add(rd[0].ToString() ?? "");
        ExtraBarcodeText.Text = list.Count == 0
            ? $"商品 {row.ProductName}　无附加条码"
            : $"商品 {row.ProductName}　附加条码：{string.Join("　", list)}";
    }

    private void ImportExcel_Click(object sender, RoutedEventArgs e)
    {
        var blocked = LicenseService.WriteBlocked();
        if (blocked != null)
        {
            MessageBox.Show(blocked, "只读模式", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var w = new ExcelImportWindow { Owner = Window.GetWindow(this) };
        if (w.ShowDialog() == true) LoadData();
    }

    private void ToggleActive_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not ProductRow row) { MessageBox.Show("请先选中商品", "提示"); return; }
        var target = !row.IsActive;
        var r = MessageBox.Show(
            $"确定{(target ? "启用" : "停用")}商品「{row.ProductName}」？\n停用后该商品不再出现在收银扫码里。",
            "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        using var conn = App.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Product SET IsActive=@a, UpdateDate=datetime('now','localtime') WHERE ProductId=@id";
        cmd.Parameters.AddWithValue("@a", target ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", row.ProductId);
        cmd.ExecuteNonQuery();
        LoadData();
    }
}

/// <summary>商品列表行（含显示格式化）</summary>
public class ProductRow
{
    public string ProductId { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string? Spec { get; set; }
    public string PurchasePriceText { get; set; } = "";
    public string SalePriceText { get; set; } = "";
    public string UnitText { get; set; } = "";
    public decimal CurrentStock { get; set; }
    public decimal LowStockQty { get; set; }
    public bool IsActive { get; set; } = true;
    public string StatusText => IsActive ? "正常" : "已停用";
    public string StatusBrush => IsActive ? "#16A34A" : "#9CA3AF";

    public static ProductRow From(Product p) => new()
    {
        ProductId = p.ProductId,
        Barcode = p.Barcode ?? "",
        ProductName = p.ProductName,
        Spec = p.Spec,
        PurchasePriceText = p.PurchasePriceCents.ToYuanString(),
        SalePriceText = p.SalePriceCents.ToYuanString(),
        UnitText = p.BaseUnit ?? "",
        CurrentStock = p.CurrentStock,
        LowStockQty = p.LowStockQty,
        IsActive = p.IsActive,
    };

    public Product ToEntity() => new()
    {
        ProductId = ProductId,
        Barcode = Barcode,
        ProductName = ProductName,
        Spec = Spec,
        PurchasePriceCents = ParseCents(PurchasePriceText),
        SalePriceCents = ParseCents(SalePriceText),
        BaseUnit = UnitText,
        CurrentStock = CurrentStock,
        LowStockQty = LowStockQty,
        IsActive = IsActive,
    };

    private static long ParseCents(string t) =>
        decimal.TryParse(t, out var v) ? v.ToCents() : 0;
}