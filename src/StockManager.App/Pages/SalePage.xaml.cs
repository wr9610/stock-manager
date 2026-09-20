using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StockManager.Core;
using StockManager.Core.Models;
using StockManager.Print;

namespace StockManager.App.Pages;

public partial class SalePage : UserControl
{
    private readonly ObservableCollection<CartItem> _cart = new();
    private DateTime _lastScanTime;

    public SalePage()
    {
        InitializeComponent();
        CartGrid.ItemsSource = _cart;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ScanBox.Focus();
    }

    // ========== 扫码录入 ==========
    private void ScanBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // 两种扫码枪兼容：
        // 1) 逐字符快击（间隔 <30ms）→ 自动解析
        // 2) 一次粘贴整串条码 → 等回车（扫码枪标配回车）也能解析
        var text = ScanBox.Text.Trim();
        if (text.Length < 8)
        {
            _lastScanTime = DateTime.MinValue;
            return;
        }
        var now = DateTime.Now;
        if ((now - _lastScanTime).TotalMilliseconds < 30)
        {
            ResolveScan(text);
        }
        _lastScanTime = now;
    }

    private void ScanBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ResolveScan(ScanBox.Text.Trim());
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            Checkout();
        }
        else if (e.Key == Key.F4)
        {
            Hold();
        }
        else if (e.Key == Key.F5)
        {
            Resume();
        }
    }

    private void ResolveScan(string code)
    {
        if (string.IsNullOrEmpty(code)) return;

        // 先按条码查
        var product = App.Stock.FindByBarcode(code);
        if (product == null && IsNumericOrCode(code))
        {
            // 数字条码没查到给提示，不弹建商品（销售页）
            ResolvedInfo.Text = $"⚠ 条码未找到：{code}";
            ScanBox.Clear();
            return;
        }

        if (product == null)
        {
            // 用商品名搜（拼音/名称）
            var list = SearchProduct(code);
            if (list.Count == 0)
            {
                ResolvedInfo.Text = $"⚠ 未找到商品：{code}";
                ScanBox.Clear();
                return;
            }
            if (list.Count == 1)
            {
                AddToCart(list[0]);
                ScanBox.Clear();
                return;
            }
            ResolvedInfo.Text = $"找到 {list.Count} 个，请精确输入";
            ScanBox.Clear();
            return;
        }

        AddToCart(product);
        ScanBox.Clear();
    }

    private void AddToCart(Product p)
    {
        // 已在购物车 → 数量+1（扫码连扫同一商品）
        var exist = _cart.FirstOrDefault(c => c.Barcode == p.Barcode);
        if (exist != null)
        {
            exist.Qty++;
            exist.AmountCents = exist.SalePriceCents.Amount(exist.Qty);
            CartGrid.Items.Refresh();
            Recalc();
            return;
        }

        _cart.Add(new CartItem
        {
            Barcode = p.Barcode ?? "",
            ProductName = p.ProductName,
            ProductId = p.ProductId,
            SalePriceCents = p.SalePriceCents,
            Qty = 1,
            AmountCents = p.SalePriceCents,
        });
        Recalc();
    }

    private List<Product> SearchProduct(string keyword)
    {
        var list = new List<Product>();
        using var conn = App.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT * FROM Product
            WHERE IsActive=1 AND (ProductName LIKE @k OR SearchCode LIKE @k)
            ORDER BY ProductName LIMIT 20";
        cmd.Parameters.AddWithValue("@k", $"%{keyword}%");
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            list.Add(new Product
            {
                ProductId = rd["ProductId"].ToString() ?? "",
                Barcode = rd["Barcode"]?.ToString(),
                ProductName = rd["ProductName"].ToString() ?? "",
                SalePriceCents = Convert.ToInt64(rd["SalePriceCents"] ?? 0),
                BaseUnit = rd["BaseUnit"]?.ToString(),
            });
        }
        return list;
    }

    private static bool IsNumericOrCode(string s) => s.All(char.IsDigit) || s.Length >= 10;

    // ========== 金额计算 ==========
    private void Recalc(object? sender = null, TextChangedEventArgs? e = null)
    {
        // XAML 加载时 DiscountBox 默认 Text="0" 会先触发本方法，
        // 此刻后面的控件可能还没创建，做空保护
        if (ReceivedBox == null || TotalText == null || ChangeText == null) return;
        long total = _cart.Sum(c => c.AmountCents);
        long discount = ParseCents(DiscountBox.Text);
        long received = ParseCents(ReceivedBox.Text);
        long pay = total - discount;
        TotalText.Text = pay.ToYuanString() + " 元";
        ChangeText.Text = (received - pay > 0 ? (received - pay) : 0).ToYuanString() + " 元";
    }

    private static long ParseCents(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return decimal.TryParse(text, out var v) ? v.ToCents() : 0;
    }

    // ========== 结算 ==========
    private void Checkout_Click(object sender, RoutedEventArgs e) => Checkout();

    private void Checkout()
    {
        if (_cart.Count == 0)
        {
            MessageBox.Show("购物车是空的", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        long discount = ParseCents(DiscountBox.Text);

        var sale = new Sale
        {
            Operator = App.CurrentUser,
            DiscountAmountCents = discount,
            ReceivedAmountCents = ParseCents(ReceivedBox.Text),
        };
        foreach (var c in _cart)
        {
            sale.Items.Add(new SaleItem
            {
                ProductId = c.ProductId,
                Barcode = c.Barcode,
                ProductName = c.ProductName,
                SalePriceCents = c.SalePriceCents,
                Qty = c.Qty,
                BaseQty = c.Qty,
                Unit = c.Unit,
            });
        }

        var (ok, error, saleId) = App.Stock.DoSale(sale);
        if (!ok)
        {
            MessageBox.Show(error, "结算失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 结算成功后 DoSale 已回填单号/应收/实收，弹小票信息
        var pay = sale.TotalAmountCents - sale.DiscountAmountCents;
        MessageBox.Show($"结算成功！\n销售单号：{sale.SaleNo}\n应收：{pay.ToYuanString()} 元\n实收：{sale.ReceivedAmountCents.ToYuanString()} 元\n找零：{ChangeText.Text}",
            "✔ 结算完成", MessageBoxButton.OK, MessageBoxImage.Information);

        // 自动打印小票（打印失败只提示，不阻塞收银）
        try
        {
            var cfg = App.Printer.Config;
            if (cfg.Mode != PrinterMode.None)
            {
                var bytes = ReceiptBuilder.BuildSale(sale, cfg.ShopName, cfg.Footer);
                App.Printer.Send(bytes);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"小票打印失败（不影响收款）：\n{ex.Message}", "打印", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _cart.Clear();
        DiscountBox.Text = "0";
        ReceivedBox.Text = "";
        ResolvedInfo.Text = "";
        Recalc();
        ScanBox.Focus();
    }

    // ========== 挂单 / 取单 ==========
    private static readonly ObservableCollection<CartItem> _holdCart = new();

    private void Hold_Click(object sender, RoutedEventArgs e) => Hold();
    private void Hold()
    {
        _holdCart.Clear();
        foreach (var c in _cart) _holdCart.Add(c);
        _cart.Clear();
        Recalc();
        ScanBox.Focus();
        ResolvedInfo.Text = $"已挂单（{_holdCart.Count} 件），点 取单(F5) 恢复";
    }

    private void Resume_Click(object sender, RoutedEventArgs e) => Resume();
    private void Resume()
    {
        if (_holdCart.Count == 0)
        {
            ResolvedInfo.Text = "没有挂起的单";
            return;
        }
        _cart.Clear();
        foreach (var c in _holdCart) _cart.Add(c);
        _holdCart.Clear();
        Recalc();
        ScanBox.Focus();
        ResolvedInfo.Text = "已取单，继续收银";
    }

    // 删除行/改数量快捷键
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Delete && CartGrid.SelectedItem is CartItem sel)
        {
            _cart.Remove(sel);
            Recalc();
            e.Handled = true;
        }
        else if (e.Key == Key.Add || e.Key == Key.OemPlus)
        {
            if (CartGrid.SelectedItem is CartItem s1) { s1.Qty++; s1.AmountCents = s1.SalePriceCents.Amount(s1.Qty); CartGrid.Items.Refresh(); Recalc(); }
            e.Handled = true;
        }
        else if (e.Key == Key.Subtract || e.Key == Key.OemMinus)
        {
            if (CartGrid.SelectedItem is CartItem s2 && s2.Qty > 1) { s2.Qty--; s2.AmountCents = s2.SalePriceCents.Amount(s2.Qty); CartGrid.Items.Refresh(); Recalc(); }
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }
}

/// <summary>购物车行</summary>
public class CartItem
{
    public string ProductId { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public long SalePriceCents { get; set; }
    public string SalePriceText => SalePriceCents.ToYuanString();
    public decimal Qty { get; set; } = 1;
    public string Unit { get; set; } = "";
    public long AmountCents { get; set; }
    public string AmountText => AmountCents.ToYuanString();
}