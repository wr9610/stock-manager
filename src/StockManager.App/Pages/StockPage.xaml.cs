using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using StockManager.Data;

namespace StockManager.App.Pages;

public partial class StockPage : UserControl
{
    private readonly ObservableCollection<object> _rows = new();

    public StockPage()
    {
        InitializeComponent();
        Grid.ItemsSource = _rows;
        FromDate.SelectedDate = DateTime.Today.AddMonths(-1);
        ToDate.SelectedDate = DateTime.Today;
        LoadNow();
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (PeriodPanel == null) return;   // InitializeComponent 未完成时忽略
        if (ModeNow.IsChecked == true) LoadNow();
        else LoadLedger();
    }

    private void QueryLedger_Click(object sender, RoutedEventArgs e) => LoadLedger();

    private static DataGridTextColumn Col(string header, string path, double width, bool right = true)
    {
        var c = new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(path) { StringFormat = right ? "{0:N2}" : "{0}" }, Width = width };
        return c;
    }

    private void LoadNow()
    {
        SetupColumns(new[] { Col("条码", "Barcode", 140, false), Col("商品名", "ProductName", 200, false), Col("规格", "Spec", 90, false), Col("单位", "Unit", 70, false), Col("当前库存", "Stock", 90), Col("预警线", "LowStock", 90), Col("状态", "StatusText", 80, false) });
        _rows.Clear();
        foreach (var p in App.Stock.QueryProducts())
        {
            _rows.Add(new NowRow
            {
                Barcode = p.Barcode ?? "",
                ProductName = p.ProductName,
                Spec = p.Spec ?? "",
                Unit = p.BaseUnit ?? "",
                Stock = p.CurrentStock,
                LowStock = p.LowStockQty,
                StatusText = p.CurrentStock <= p.LowStockQty && p.LowStockQty > 0 ? "⚠ 低库存" : "",
            });
        }
    }

    private void LoadLedger()
    {
        var from = FromDate.SelectedDate ?? DateTime.Today.AddMonths(-1);
        var to = (ToDate.SelectedDate ?? DateTime.Today).AddDays(1).AddSeconds(-1);
        if (from > to)
        {
            MessageBox.Show("起始日期不能晚于结束日期", "提示");
            return;
        }
        SetupColumns(new[] {
            Col("商品名", "ProductName", 160, false),
            Col("期初", "BeginStock", 90),
            Col("进货+", "PurchaseIn", 80),
            Col("销售-", "SaleOut", 80),
            Col("退货+", "ReturnIn", 80),
            Col("调整", "Adjust", 80),
            Col("期末", "EndStock", 90),
        });
        _rows.Clear();
        var rows = App.Stock.QueryStockLedger(from, to);
        foreach (var r in rows) _rows.Add(r);
    }

    private void SetupColumns(DataGridColumn[] cols)
    {
        Grid.Columns.Clear();
        foreach (var c in cols) Grid.Columns.Add(c);
    }
}

public class NowRow
{
    public string Barcode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Spec { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Stock { get; set; }
    public decimal LowStock { get; set; }
    public string StatusText { get; set; } = "";
}