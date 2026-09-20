using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StockManager.App;
using StockManager.App.Pages;

namespace StockManager.UiSmoke;

/// <summary>
/// UI 冒烟测试：在真实 WPF 调度器上逐个实例化所有页面，
/// 强制布局 + 触发 Loaded，验证 XAML 控件名/绑定/事件不会炸。
/// 用法：dotnet run --project tests/StockManager.UiSmoke
/// </summary>
internal static class Program
{
    private static readonly List<string> _fails = new();
    private static bool _ok;

    [STAThread]
    private static int Main()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"stock_smoke_{Guid.NewGuid():N}.db");
        var app = new Application();
        app.DispatcherUnhandledException += (_, e) =>
        {
            _fails.Add($"Dispatcher异常：{e.Exception.Message}");
            e.Handled = true;
        };
        app.Startup += (_, _) =>
        {
            try
            {
                global::StockManager.App.App.InitForTest(dbPath);
                AddSampleProduct();

                // 逐个页面实例化 + 布局 + Loaded
                P("SalePage", new SalePage(), out var sale);
                P("ProductPage", new ProductPage(), out _);
                P("PurchasePage", new PurchasePage(), out _);
                P("ReturnPage", new ReturnPage(), out _);
                P("StockPage", new StockPage(), out _);
                P("ReportPage", new ReportPage(), out _);
                P("SettingsPage", new SettingsPage(), out _);

                // 主窗口整体
                try
                {
                    var main = new MainWindow();
                    ForceLayoutAndLoaded(main);
                    // 走一遍导航，覆盖全部页面切换
                    foreach (var key in new[] { "sale", "product", "purchase", "return", "stock", "report", "settings" })
                    {
                        var rb = new System.Windows.Controls.RadioButton { Tag = key };
                        main.GetType().GetMethod("OnNav", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                            ?.Invoke(main, new object?[] { rb, new RoutedEventArgs() });
                    }
                    ForceLayoutAndLoaded(main);
                    main.Close();
                }
                catch (Exception ex)
                {
                    _fails.Add($"MainWindow：{ex}");
                }

                _ok = _fails.Count == 0;
                app.Shutdown();
            }
            catch (Exception ex)
            {
                _fails.Add($"致命异常：{ex}");
                _ok = false;
                app.Shutdown();
            }
        };

        app.Run();

        Console.WriteLine(_ok ? "== UI 冒烟全部通过 ==" : "== UI 冒烟存在失败 ==");
        foreach (var f in _fails) Console.WriteLine("  ✗ " + f);

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var f = dbPath + suffix;
            if (File.Exists(f)) { try { File.Delete(f); } catch { } }
        }
        Console.WriteLine("临时数据库已清理");
        return _ok ? 0 : 1;
    }

    private static void P(string name, FrameworkElement el, out FrameworkElement box)
    {
        box = el;
        try
        {
            // 验证关键命名控件存在（防 XAML 改名漏绑）
            var mustHave = name switch
            {
                "SalePage" => new[] { "ScanBox", "CartGrid", "DiscountBox", "ReceivedBox", "ChangeText" },
                "ProductPage" => new[] { "SearchBox", "Grid" },
                "PurchasePage" => new[] { "ScanBox", "Grid", "SupplierCombo" },
                "ReturnPage" => new[] { "FindBox", "Grid" },
                "StockPage" => new[] { "Grid", "FromDate", "ToDate", "PeriodPanel" },
                "ReportPage" => new[] { "RankGrid", "SalesText", "ProfitText" },
                "SettingsPage" => new[] { "UserGrid", "AboutText", "BackupStatus" },
                _ => Array.Empty<string>()
            };
            foreach (var n in mustHave)
            {
                if (el.FindName(n) == null)
                {
                    _fails.Add($"{name} 缺少命名控件 {n}");
                    return;   // 提前返回，布局本身没意义
                }
            }
            ForceLayoutAndLoaded(el);
        }
        catch (Exception ex)
        {
            _fails.Add($"{name} 抛异常：{ex.Message}");
        }
    }

    private static void AddSampleProduct()
    {
        var p = new StockManager.Core.Models.Product
        {
            Barcode = "6901234567891",
            ProductName = "冒烟测试商品",
            BaseUnit = "件",
            PurchasePriceCents = 100,
            SalePriceCents = 200,
            CurrentStock = 10,
            IsActive = true,
        };
        global::StockManager.App.App.Stock.SaveProduct(p);
    }

    private static void ForceLayoutAndLoaded(FrameworkElement el)
    {
        el.Measure(new Size(1200, 800));
        el.Arrange(new Rect(new Size(1200, 800)));
        el.UpdateLayout();
        el.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        el.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
}