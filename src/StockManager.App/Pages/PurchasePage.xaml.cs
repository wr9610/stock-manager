using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StockManager.Core;
using StockManager.Core.Models;

namespace StockManager.App.Pages;

public partial class PurchasePage : UserControl
{
    private readonly ObservableCollection<PurchaseLine> _lines = new();
    private Dictionary<string, Product> _byBarcode = new();
    private Dictionary<string, Product> _byName = new();

    public PurchasePage()
    {
        InitializeComponent();
        Grid.ItemsSource = _lines;

        // 供应商下拉
        ReloadSuppliers();
        LoadProducts();
        Loaded += (_, _) => ScanBox.Focus();
    }

    private void ReloadSuppliers()
    {
        var list = App.Stock.QuerySuppliers();
        SupplierCombo.Items.Clear();
        SupplierCombo.Items.Add("（不指定）");
        foreach (var s in list) SupplierCombo.Items.Add($"{s.SupplierName}");
        SupplierCombo.SelectedIndex = 0;
    }

    private void LoadProducts()
    {
        // 二条码映射：主条码 + 附加条码 → 商品
        _byBarcode.Clear();
        foreach (var p in App.Stock.QueryProducts())
        {
            _byBarcode[p.Barcode ?? ""] = p;
        }
        // 附加条码进映射
        using (var conn = App.Db.Open())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT pb.Barcode, pb.ProductId, p.ProductName FROM ProductBarcode pb JOIN Product p ON p.ProductId=pb.ProductId WHERE p.IsActive=1";
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var b = rd[0].ToString() ?? "";
                if (!_byBarcode.ContainsKey(b))
                {
                    _byBarcode[b] = new Product
                    {
                        ProductId = rd[1].ToString() ?? "",
                        ProductName = rd[2].ToString() ?? "",
                    };
                }
            }
        }
        _byName.Clear();
        foreach (var p in App.Stock.QueryProducts())
        {
            var k = p.ProductName.Trim().ToLowerInvariant();
            if (!_byName.ContainsKey(k))
                _byName[k] = p;
        }
    }

    private void ScanBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var text = ScanBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        // 先按条码，再按商品名（支持名称/简码/货号采购）
        if (_byBarcode.TryGetValue(text, out var p1) && !string.IsNullOrEmpty(p1.ProductName))
        {
            AddLine(p1, p1.Barcode ?? text);
            ScanBox.Clear();
            return;
        }

        var found = App.Stock.QueryProducts(text);
        if (found.Count == 1)
        {
            AddLine(found[0], text);
            ScanBox.Clear();
            return;
        }
        if (found.Count == 0)
        {
            ScanBox.Clear();
            MessageBox.Show($"未找到商品：{text}\n请先在商品管理中新增。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // 多个结果，取第一个名匹配
        var byName = found.FirstOrDefault(x => x.ProductName?.Trim().ToLowerInvariant() == text.ToLowerInvariant());
        if (byName != null)
        {
            AddLine(byName, text);
            ScanBox.Clear();
            return;
        }
        MessageBox.Show($"找到 {found.Count} 个商品，请输入更精确的名称或条码。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AddSelected_Click(object sender, RoutedEventArgs e)
    {
        // 弹一个选择窗口：输入名称/条码搜索
        var pick = new ProductPickWindow();
        pick.Owner = Window.GetWindow(this);
        if (pick.ShowDialog() == true && pick.Selected != null)
            AddLine(pick.Selected, pick.Selected.Barcode ?? pick.Selected.ProductName);
    }

    private void AddLine(Product p, string displayCode)
    {
        var exist = _lines.FirstOrDefault(l => l.ProductId == p.ProductId);
        if (exist != null)
        {
            exist.EditQty = (decimal.Parse(exist.EditQty) + 1).ToString();
        }
        else
        {
            _lines.Add(new PurchaseLine
            {
                ProductId = p.ProductId,
                ProductName = p.ProductName,
                Barcode = p.Barcode ?? displayCode,
                EditPrice = p.PurchasePriceCents.ToYuanString(),
                EditQty = "1",
                Unit = p.BaseUnit ?? "个",
                IsPack = false,
            });
        }
        Recalc();
    }

    private void RemoveLine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PurchaseLine line })
        {
            _lines.Remove(line);
            Recalc();
        }
    }

    // 编辑价格/数量后自动重算
    private void Recalc()
    {
        long total = 0;
        foreach (var l in _lines)
        {
            l.AmountCents = l.Qty == 0 ? 0 : (long)Math.Round(l.PriceCents * l.Qty);
            total += l.AmountCents;
        }
        Grid.Items.Refresh();
        TotalText.Text = $"合计：{total.ToYuanString()} 元";
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (_lines.Count == 0)
        {
            MessageBox.Show("请先添加进货商品", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var supSelected = SupplierCombo.SelectedIndex > 0;
        var purchase = new Purchase
        {
            SupplierName = supSelected ? SupplierCombo.SelectedItem.ToString() : null,
            Operator = App.CurrentUser,
            Remark = RemarkBox.Text.Trim(),
        };

        foreach (var l in _lines)
        {
            purchase.Items.Add(new PurchaseItem
            {
                ProductId = l.ProductId,
                ProductName = l.ProductName,
                Barcode = l.Barcode,
                PurchasePriceCents = l.PriceCents,
                Unit = l.IsPack ? "箱" : l.Unit,
                Qty = l.Qty,
                BaseQty = l.BaseQty,       // 若多单位箱，折算基本单位数量
                AmountCents = l.AmountCents,
            });
        }

        var (ok, error, _) = App.Stock.DoPurchase(purchase);
        if (!ok)
        {
            MessageBox.Show(error, "进货失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show($"进货成功！\n单号：{purchase.PurchaseNo}\n合计：{purchase.TotalAmountCents.ToYuanString()} 元",
            "✔ 入库完成", MessageBoxButton.OK, MessageBoxImage.Information);
        _lines.Clear();
        RemarkBox.Clear();
        ScanBox.Clear();
        Recalc();
        ScanBox.Focus();
    }
}

/// <summary>进货一行（价格/数量可编辑）</summary>
public class PurchaseLine : INotifyPropertyChanged
{
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string Unit { get; set; } = "个";
    public bool IsPack { get; set; }

    private string _editPrice = "0";
    public string EditPrice
    {
        get => _editPrice;
        set { _editPrice = value; OnChanged(nameof(EditPrice)); }
    }

    private string _editQty = "1";
    public string EditQty
    {
        get => _editQty;
        set { _editQty = value; OnChanged(nameof(EditQty)); }
    }

    public decimal Qty => decimal.TryParse(EditQty, out var v) ? v : 0;
    public decimal BaseQty => Qty;   // 多单位在 v1.1 完善（箱→按 PackQty 折算），当前按基本单位
    public long PriceCents => decimal.TryParse(EditPrice, out var v) ? v.ToCents() : 0;
    public long AmountCents { get; set; }
    public string AmountText => AmountCents.ToYuanString();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>商品选择窗口（进货/销售共用风格）</summary>
public class ProductPickWindow : Window
{
    public Product? Selected { get; private set; }
    private readonly System.Windows.Controls.ListBox _list = new();
    private readonly List<Product> _items = new();
    private readonly System.Windows.Controls.TextBox _search;

    public ProductPickWindow()
    {
        Title = "选择商品";
        Width = 520;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei");
        Background = System.Windows.Media.Brushes.White;

        _search = new System.Windows.Controls.TextBox { FontSize = 16, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(16, 14, 16, 6), ToolTip = "条码/名称/简码，回车搜索" };
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Refresh();
        };
        _search.TextChanged += (_, _) => Refresh();

        _list.Margin = new Thickness(16, 6, 16, 6);
        _list.MouseDoubleClick += (_, _) => Pick();

        var btn = new System.Windows.Controls.Button
        {
            Content = "✅ 确认选择", FontSize = 14, Padding = new Thickness(24, 8, 24, 8),
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 16, 14),
            Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3B82F6")),
            Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0)
        };
        btn.Click += (_, _) => Pick();

        var dock = new DockPanel();
        DockPanel.SetDock(btn, Dock.Bottom);
        dock.Children.Add(btn);
        dock.Children.Add(_search);
        dock.Children.Add(_list);
        Content = dock;

        Refresh();
    }

    private void Refresh()
    {
        _items.Clear();
        _list.Items.Clear();
        _items.AddRange(App.Stock.QueryProducts(_search.Text.Trim()));
        foreach (var p in _items)
        {
            var b = string.IsNullOrEmpty(p.Barcode) ? "" : p.Barcode + "　";
            var unit = p.BaseUnit ?? "";
            _list.Items.Add($"{b}{p.ProductName}　　{unit}　售价{p.SalePriceCents.ToYuanString()}元");
        }
    }

    private void Pick()
    {
        if (_list.SelectedIndex < 0) return;
        Selected = _items[_list.SelectedIndex];
        DialogResult = true;
    }
}