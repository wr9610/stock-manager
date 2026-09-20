using System.IO;
using System.Windows;
using StockManager.Data;
using StockManager.Print;

namespace StockManager.App;

public partial class App : Application
{
    public static string DbPath { get; set; } = "";
    public static DatabaseService Db { get; set; } = null!;
    public static StockService Stock { get; set; } = null!;
    public static string CurrentUser { get; set; } = "admin";
    public static string CurrentRole { get; set; } = "老板";

    /// <summary>小票打印机（null 表示未初始化）</summary>
    public static ReceiptPrinter Printer { get; set; } = null!;

    /// <summary>供测试/冒烟初始化静态服务（生产代码只走 OnStartup）</summary>
    public static void InitForTest(string dbPath)
    {
        DbPath = dbPath;
        Db = new DatabaseService(dbPath);
        Db.Initialize();
        Stock = new StockService(Db);
        Printer = new ReceiptPrinter(PrinterConfigStore.Load(Db));
        LicenseService.Init(Db);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 数据库放在 exe 同级的 data 目录
        var baseDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
        var dataDir = Path.Combine(baseDir, "data");
        DbPath = Path.Combine(dataDir, "stock.db");

        Db = new DatabaseService(DbPath);
        Db.Initialize();

        // 断电保护：启动完整性检查
        if (!Db.CheckIntegrity(out var result))
        {
            var r = MessageBox.Show(
                $"数据库可能损坏（integrity_check = {result}）\n\n是否尝试打开？建议先从备份恢复。",
                "数据库异常", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes)
            {
                Shutdown();
                return;
            }
        }

        Stock = new StockService(Db);
        Printer = new ReceiptPrinter(PrinterConfigStore.Load(Db));
        LicenseService.Init(Db);

        // 显示登录窗口
        var login = new LoginWindow();
        login.Show();
    }
}