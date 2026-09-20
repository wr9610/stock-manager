using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using StockManager.Core;

namespace StockManager.App.Pages;

public partial class ReportPage : UserControl
{
    private readonly ObservableCollection<RankRow> _ranks = new();

    public ReportPage()
    {
        InitializeComponent();
        RankGrid.ItemsSource = _ranks;
        // 构造里 RToday 已是 IsChecked=true，Checked 事件在 InitializeComponent 前不会触发，
        // 这里手动刷新一次
        Range_Changed(this, null!);
    }

    private void Range_Changed(object sender, RoutedEventArgs? e)
    {
        // XAML 加载时 RToday(IsChecked=True) 会先触发本方法，此时后段控件未创建
        if (RangeHint == null || SalesText == null || FromDate == null) return;
        DateTime from, to;
        if (RToday.IsChecked == true)
        {
            from = DateTime.Today;
            to = DateTime.Today;
        }
        else if (RWeek.IsChecked == true)
        {
            var diff = DayOfWeek.Monday - DateTime.Today.DayOfWeek;
            if (diff > 0) diff -= 7;
            from = DateTime.Today.AddDays(diff);
            to = from.AddDays(6);
        }
        else if (RMonth.IsChecked == true)
        {
            from = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            to = from.AddMonths(1).AddDays(-1);
        }
        else
        {
            from = FromDate.SelectedDate ?? DateTime.Today.AddMonths(-1);
            to = ToDate.SelectedDate ?? DateTime.Today;
        }

        FromDate.SelectedDate = from;
        ToDate.SelectedDate = to;
        RangeHint.Text = $"统计区间：{from:yyyy-MM-dd} 00:00 ~ {to:yyyy-MM-dd} 23:59";

        var s = App.Stock.QuerySaleSummary(from.Date, to.Date.AddDays(1).AddSeconds(-1));
        SalesText.Text = s.SalesAmountCents.ToYuanString() + " 元";
        OrdersText.Text = $"{s.Orders} 笔";
        CostText.Text = s.CostAmountCents.ToYuanString() + " 元";
        DiscountText.Text = s.DiscountAmountCents.ToYuanString() + " 元";
        ProfitText.Text = s.ProfitAmountCents.ToYuanString() + " 元";

        // 利润排行
        _ranks.Clear();
        var rows = App.Stock.QueryProductProfitRanking(from.Date, to.Date.AddDays(1).AddSeconds(-1));
        var i = 1;
        foreach (var r in rows)
            _ranks.Add(new RankRow { Rank = i++, ProductName = r.ProductName, Qty = r.Qty, SalesText = r.SalesAmountCents.ToYuanString(), CostText = r.CostAmountCents.ToYuanString(), ProfitText = r.ProfitAmountCents.ToYuanString() });

        if (_ranks.Count == 0) RankHint.Text = "该区间暂无销售数据";
    }

    private void FromDate_SelectedDateChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // 切换到自定义 + 用户改了日期 → 刷新
        if (RCustom.IsChecked != true && sender != this)
            return;
        Range_Changed(sender, null!);
    }

    private void ToDate_SelectedDateChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        FromDate_SelectedDateChanged(sender, e);
    }
}

public class RankRow
{
    public int Rank { get; set; }
    public string ProductName { get; set; } = "";
    public decimal Qty { get; set; }
    public string SalesText { get; set; } = "";
    public string CostText { get; set; } = "";
    public string ProfitText { get; set; } = "";
}