using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using StockManager.Core;
using StockManager.Data;

namespace StockManager.App;

public partial class ExcelImportWindow : Window
{
    private readonly ObservableCollection<ImportRowView> _rows = new();
    private string? _currentPath;

    public ExcelImportWindow()
    {
        InitializeComponent();
        PreviewGrid.ItemsSource = _rows;
    }

    private void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var open = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择商品 Excel",
            Filter = "Excel 文件 (*.xlsx;*.xls)|*.xlsx;*.xls",
        };
        if (open.ShowDialog() != true) return;

        try
        {
            var rows = ExcelImportService.Parse(open.FileName);
            if (rows.Count == 0)
            {
                FileInfoText.Text = "文件里没有数据";
                return;
            }
            _currentPath = open.FileName;
            _rows.Clear();
            foreach (var r in rows) _rows.Add(ImportRowView.From(r));

            var ok = rows.Count(r => r.Valid);
            var bad = rows.Count - ok;
            FileInfoText.Text = $"已读取 {rows.Count} 行，可导入 {ok}，待修正 {bad}";
            SummaryText.Text = $"共 {rows.Count} 行：有效 {ok} / 无效 {bad}";
            ImportBtn.IsEnabled = ok > 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"解析失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DownloadTemplate_Click(object sender, RoutedEventArgs e)
    {
        var save = new Microsoft.Win32.SaveFileDialog
        {
            Title = "保存导入模板",
            Filter = "Excel 文件 (*.xlsx)|*.xlsx",
            FileName = "商品导入模板.xlsx",
        };
        if (save.ShowDialog() != true) return;

        try
        {
            var example = new[]
            {
                new { 条码 = "6901234567001", 商品名 = "示例矿泉水 550ml", 规格 = "550ml", 单位 = "件", 进价 = 1.20m, 售价 = 2.50m, 库存 = 100, 低库存预警 = 10 },
                new { 条码 = "6901234567002", 商品名 = "示例可乐 330ml", 规格 = "330ml", 单位 = "件", 进价 = 1.80m, 售价 = 3.50m, 库存 = 80, 低库存预警 = 10 },
            };
            MiniExcelLibs.MiniExcel.SaveAs(save.FileName, example);
            MessageBox.Show("模板已保存，请按列头填写后导入", "模板", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0) return;
        var validRows = _rows.Where(r => r.Valid).Select(v => (ImportRow)v).ToList();
        var r = MessageBox.Show(
            $"确认导入 {validRows.Count} 条商品？\n\n条码重复的行会被跳过并提示。",
            "确认导入", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        var (ok, fails) = ExcelImportService.Import(App.Stock, validRows);

        var msg = $"✔ 成功导入 {ok} 条";
        if (fails.Count > 0)
            msg += $"\n跳过 {fails.Count} 条：\n" + string.Join("\n", fails.Take(15));
        if (fails.Count > 15) msg += $"\n…共 {fails.Count} 条";
        MessageBox.Show(msg, "导入完成", MessageBoxButton.OK,
            fails.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

        if (ok > 0) DialogResult = true;
    }
}

/// <summary>预览行显示辅助（加展示属性，不污染业务 ImportRow）</summary>
public class ImportRowView : ImportRow
{
    public string PurchaseText => PurchaseCents.ToYuanString();
    public string SaleText => SaleCents.ToYuanString();
    public string? ErrorBrush => Valid ? "#16A34A" : "#DC2626";
    public string? ShowError => Valid ? "可导入" : Error;

    public static ImportRowView From(ImportRow r) => new()
    {
        Barcode = r.Barcode,
        ProductName = r.ProductName,
        Spec = r.Spec,
        Unit = r.Unit,
        PurchaseCents = r.PurchaseCents,
        SaleCents = r.SaleCents,
        Stock = r.Stock,
        LowStock = r.LowStock,
        Error = r.Error,
    };
}