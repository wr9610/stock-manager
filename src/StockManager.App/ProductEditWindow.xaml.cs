using System.Windows;
using StockManager.App.Pages;
using StockManager.Core;
using StockManager.Core.Models;

namespace StockManager.App;

public partial class ProductEditWindow : Window
{
    private readonly ProductRow? _existing;

    public ProductEditWindow(ProductRow? existing)
    {
        InitializeComponent();
        _existing = existing;

        // 分类下拉
        foreach (var c in App.Stock.QueryCategories())
            CategoryCombo.Items.Add($"{c.CategoryName}");
        if (CategoryCombo.Items.Count > 0) CategoryCombo.SelectedIndex = 0;

        if (existing != null)
        {
            TitleText.Text = "编辑商品";
            NameBox.Text = existing.ProductName;
            BarcodeBox.Text = existing.Barcode;
            SpecBox.Text = existing.Spec;
            BaseUnitBox.Text = existing.UnitText;
            PurchaseBox.Text = existing.PurchasePriceText;
            PriceBox.Text = existing.SalePriceText;
            LowBox.Text = existing.LowStockQty.ToString();
            SampleBox.Visibility = Visibility.Collapsed;
            SampleBox.IsChecked = false;
        }
        else
        {
            TitleText.Text = "新增商品";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ErrorText.Text = "商品名不能为空";
            return;
        }
        var barcode = BarcodeBox.Text.Trim();

        var p = new Product
        {
            ProductId = _existing?.ProductId ?? "",
            Barcode = barcode,
            ProductName = name,
            Spec = SpecBox.Text.Trim(),
            BaseUnit = BaseUnitBox.Text.Trim(),
            PackUnit = null,
            ProductCode = "",
            PurchasePriceCents = ParseCents(PurchaseBox.Text),
            SalePriceCents = ParseCents(PriceBox.Text),
            LowStockQty = ParseDec(LowBox.Text),
            SearchCode = SearchCodeBox.Text.Trim(),
            Remark = RemarkBox.Text.Trim(),
            IsSample = SampleBox.IsChecked == true,
            IsActive = true,
        };

        // 附加条码（只对新增处理，编辑沿用 ProductPage 顶部显示）
        var extra = ExtraBarcodeBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (extra.Any(x => x == barcode))
        {
            ErrorText.Text = "附加条码不能与主条码重复";
            return;
        }

        var (ok, error) = App.Stock.SaveProduct(p);
        if (!ok)
        {
            ErrorText.Text = error;
            return;
        }
        foreach (var x in extra)
        {
            var (ok2, err2) = App.Stock.AddProductBarcode(p.ProductId, x);
            if (!ok2) { MessageBox.Show(err2, "附加条码", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        DialogResult = true;
    }

    private static long ParseCents(string t) =>
        decimal.TryParse(t, out var v) ? v.ToCents() : 0;

    private static decimal ParseDec(string t) =>
        decimal.TryParse(t, out var v) ? v : 0;
}