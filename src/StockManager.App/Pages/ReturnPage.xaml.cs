using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StockManager.Core;
using StockManager.Core.Models;
using StockManager.Print;

namespace StockManager.App.Pages;

public partial class ReturnPage : UserControl
{
    private readonly ObservableCollection<ReturnLine> _lines = new();
    private Sale? _sale;
    private string? _saleId;

    public ReturnPage()
    {
        InitializeComponent();
        Grid.ItemsSource = _lines;
        Loaded += (_, _) => FindBox.Focus();
    }

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) LoadSale_Click(sender, e);
    }

    private void LoadSale_Click(object sender, RoutedEventArgs e)
    {
        var q = FindBox.Text.Trim();
        if (string.IsNullOrEmpty(q))
        {
            MessageBox.Show("请输入销售单号（如 SO20260918001）或日期（2026-09-18）", "提示");
            return;
        }

        DateTime from, to;
        if (DateTime.TryParse(q, out var d))
        {
            from = d.Date;
            to = d.Date.AddDays(1).AddSeconds(-1);
        }
        else
        {
            // 单号：支持完整或后4位尾号
            var list = App.Stock.QuerySaleList(DateTime.MinValue, DateTime.MaxValue, q);
            if (list.Count == 1)
            {
                LoadSale(list[0]);
                return;
            }
            if (list.Count > 1)
            {
                MessageBox.Show($"找到 {list.Count} 张销售单，请用完整单号", "提示");
                return;
            }
            // 最后试试后四位
            list = App.Stock.QuerySaleList(DateTime.MinValue, DateTime.MaxValue, "%" + q);
            if (list.Count == 1) { LoadSale(list[0]); return; }
            MessageBox.Show($"未找到销售单：{q}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var range = App.Stock.QuerySaleList(from, to);
        if (range.Count == 0)
        {
            MessageBox.Show($"{q} 没有销售记录", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (range.Count > 1)
        {
            // 弹选择
            var pick = new SalePickWindow(range);
            pick.Owner = Window.GetWindow(this);
            if (pick.ShowDialog() == true && pick.Selected != null) LoadSale(pick.Selected);
            return;
        }
        LoadSale(range[0]);
    }

    private void LoadSale(Sale sale)
    {
        _saleId = sale.SaleId;
        _sale = App.Stock.GetSale(sale.SaleId);
        if (_sale == null) return;

        SaleInfoText.Text = $"销售单：{_sale.SaleNo}　｜　时间：{_sale.CreateDate:yyyy-MM-dd HH:mm}　｜　金额：{_sale.TotalAmountCents.ToYuanString()} 元　｜　已退：{_sale.ReturnAmountCents.ToYuanString()} 元　｜　录入：{_sale.Operator}";

        _lines.Clear();
        foreach (var item in _sale.Items)
        {
            var can = item.Qty - item.ReturnedQty;
            _lines.Add(new ReturnLine
            {
                SaleItemId = item.SaleItemId,
                ProductId = item.ProductId,
                Barcode = item.Barcode ?? "",
                ProductName = item.ProductName ?? "",
                SalePriceCents = item.SalePriceCents,
                Qty = item.Qty,
                ReturnedQty = item.ReturnedQty,
                CanReturn = can,
            });
        }
        QtyBox.Text = "1";
        UpdateReturnTotal();
    }

    private void AddReturn_Click(object sender, RoutedEventArgs e)
    {
        if (_sale == null)
        {
            MessageBox.Show("请先加载销售单", "提示");
            return;
        }
        if (Grid.SelectedItem is not ReturnLine line)
        {
            MessageBox.Show("请先在上方选中要退货的商品行", "提示");
            return;
        }
        if (!decimal.TryParse(QtyBox.Text, out var qty) || qty <= 0)
        {
            MessageBox.Show("请输入正确的退货数量", "提示");
            return;
        }
        if (line.Qty - line.ReturnedQty - line.SelectQty < qty)
        {
            MessageBox.Show($"可退数量不足：{line.ProductName} 还可退 {line.Qty - line.ReturnedQty - line.SelectQty}", "提示");
            return;
        }
        line.SelectQty += qty;
        Grid.Items.Refresh();
        UpdateReturnTotal();
    }

    private void UpdateReturnTotal()
    {
        long total = 0;
        foreach (var l in _lines)
            total += l.SalePriceCents.Amount(l.SelectQty);
        ReturnTotalText.Text = $"待退金额：{total.ToYuanString()} 元";
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (_sale == null)
        {
            MessageBox.Show("请先加载销售单", "提示");
            return;
        }
        var items = _lines.Where(l => l.SelectQty > 0).ToList();
        if (items.Count == 0)
        {
            MessageBox.Show("请选择要退货的商品并输入数量", "提示");
            return;
        }

        var r = MessageBox.Show($"确认对销售单 {_sale.SaleNo} 退货 {items.Count} 项，总金额 {items.Sum(i => i.SalePriceCents.Amount(i.SelectQty)).ToYuanString()} 元？\n\n退货后库存会加回，允许部分退货。",
            "确认退货", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        var ret = new SaleReturn
        {
            SaleId = _sale.SaleId,
            Operator = App.CurrentUser,
            Remark = $"销售单 {_sale.SaleNo} 部分退货",
        };
        foreach (var l in items)
        {
            ret.Items.Add(new SaleReturnItem
            {
                RefSaleItemId = l.SaleItemId,
                ProductId = l.ProductId,
                ProductName = l.ProductName,
                Barcode = l.Barcode,
                ReturnPriceCents = l.SalePriceCents,
                Qty = l.SelectQty,
                BaseQty = l.SelectQty,
            });
        }

        var (ok, error, _) = App.Stock.DoReturn(ret);
        if (!ok)
        {
            MessageBox.Show(error, "退货失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show($"退货成功！\n退货单号：{ret.ReturnNo}\n退款：{ret.TotalAmountCents.ToYuanString()} 元",
            "✔ 退货完成", MessageBoxButton.OK, MessageBoxImage.Information);

        // 打印退货凭证（失败不阻塞）
        try
        {
            var cfg = App.Printer.Config;
            if (cfg.Mode != PrinterMode.None)
            {
                var bytes = ReceiptBuilder.BuildReturn(ret, _sale, cfg.ShopName, cfg.Footer);
                App.Printer.Send(bytes);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"退货小票打印失败：\n{ex.Message}", "打印", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        LoadSale(_sale);   // 刷新可数量
        QtyBox.Text = "1";
    }
}

/// <summary>退货行（可退数量 & 本次退数量）</summary>
public class ReturnLine
{
    public string SaleItemId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public long SalePriceCents { get; set; }
    public string SalePriceText => SalePriceCents.ToYuanString();
    public decimal Qty { get; set; }
    public decimal ReturnedQty { get; set; }
    public decimal CanReturn { get; set; }
    public decimal SelectQty { get; set; }
}

/// <summary>销售单选择窗口</summary>
public class SalePickWindow : Window
{
    public Sale? Selected { get; private set; }
    private readonly List<Sale> _sales;
    private readonly ListBox _list = new();

    public SalePickWindow(List<Sale> sales)
    {
        _sales = sales;
        Title = "选择销售单";
        Width = 560;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei");
        Background = System.Windows.Media.Brushes.White;

        foreach (var s in sales)
            _list.Items.Add($"{s.SaleNo}　{s.CreateDate:yyyy-MM-dd HH:mm}　{s.TotalAmountCents.ToYuanString()}元　{s.Operator}");
        _list.Margin = new Thickness(16);
        _list.MouseDoubleClick += (_, _) => Pick();

        var btn = new Button
        {
            Content = "✅ 选择该单", FontSize = 14, Padding = new Thickness(24, 8, 24, 8),
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 16, 16),
            Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3B82F6")),
            Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0)
        };
        btn.Click += (_, _) => Pick();

        var dock = new DockPanel();
        DockPanel.SetDock(btn, Dock.Bottom);
        dock.Children.Add(btn);
        dock.Children.Add(_list);
        Content = dock;
    }

    private void Pick()
    {
        if (_list.SelectedIndex < 0) return;
        Selected = _sales[_list.SelectedIndex];
        DialogResult = true;
    }
}