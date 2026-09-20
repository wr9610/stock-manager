using System.Windows;
using System.Windows.Controls;
using StockManager.App.Pages;

namespace StockManager.App;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = new();

    public MainWindow()
    {
        InitializeComponent();
        UserInfoText.Text = $"{App.CurrentUser}（{App.CurrentRole}）";
        // XAML 解析时 IsChecked=True 的导航钮比 ContentHost 早触发 OnNav，
        // 这里在初始化完成后手动切一次页（幂等，不会重复创建页实例）
        SwitchPage("sale");
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        // XAML 加载中 ContentHost 尚未创建时，忽略由默认选中触发的早到事件
        if (ContentHost == null) return;
        if (sender is RadioButton rb && rb.Tag is string key)
        {
            SwitchPage(key);
        }
    }

    private void SwitchPage(string key)
    {
        if (ContentHost == null) return;
        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "sale" => new SalePage(),
                "product" => new ProductPage(),
                "purchase" => new PurchasePage(),
                "return" => new ReturnPage(),
                "stock" => new StockPage(),
                "report" => new ReportPage(),
                "settings" => new SettingsPage(),
                _ => new SalePage()
            };
            _pages[key] = page;
        }
        ContentHost.Content = page;
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show("确定退出登录？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r == MessageBoxResult.Yes)
        {
            var login = new LoginWindow();
            login.Show();
            Close();
        }
    }
}