using System.IO;
using System.Windows;
using System.Windows.Controls;
using StockManager.Core;
using StockManager.Core.Models;
using StockManager.Data;
using StockManager.Print;

namespace StockManager.App.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        LoadUsers();
        LoadPrinterConfig();
        var info = new FileInfo(App.DbPath);

        // 试用 / 授权状态
        string licenseText;
        if (LicenseService.IsLicensed)
        {
            licenseText = "✅ 已授权（正式版）";
        }
        else if (LicenseService.IsReadOnly)
        {
            licenseText = $"⛔ 试用已到期（已进入只读模式）\n已用 {LicenseService.TrialDays} 天，请联系作者购买激活码";
        }
        else
        {
            licenseText = $"🎯 试用中：剩余 {LicenseService.DaysLeft} 天\n（到期后自动进入只读，数据不受影响）";
        }

        AboutText.Text = $"进销存 StockManager v1.0\n\n{licenseText}\n\n" +
            $"数据库：{App.DbPath}\n" +
            $"文件大小：{info.Length / 1024.0 / 1024.0:N1} MB\n" +
            $"当前用户：{App.CurrentUser}（{App.CurrentRole}）";

        // 激活码区
        var machine = LicenseService.MachineCode ?? App.GetMachineCode();
        LicenseService.MachineCode = machine;
        MachineCodeText.Text = $"你的机器码：{machine}\n（复制发给作者，换取激活码）";
        if (LicenseService.IsLicensed)
        {
            ActivationBox.IsEnabled = false;
            ActivationStatus.Text = "✅ 已激活正式版";
        }
    }

    // ========== 激活码 ==========
    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        var (ok, msg) = LicenseService.TryActivate(ActivationBox.Text.Trim(), LicenseService.MachineCode ?? "");
        ActivationStatus.Text = msg;
        ActivationStatus.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ok ? "#16A34A" : "#DC2626"));
        if (ok)
        {
            ActivationBox.IsEnabled = false;
            MessageBox.Show(msg, "激活", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CopyMachine_Click(object sender, RoutedEventArgs e)
    {
        var machine = LicenseService.MachineCode ?? "";
        if (machine.Length == 0) return;
        System.Windows.Clipboard.SetText(machine);
        ActivationStatus.Text = "✔ 机器码已复制，粘贴发给作者";
    }

    // ========== 打印机设置 ==========
    private void LoadPrinterConfig()
    {
        var cfg = PrinterConfigStore.Load(App.Db);
        PrintModeCombo.SelectedIndex = (int)cfg.Mode;
        PortBox.Text = cfg.PortName;
        HostBox.Text = cfg.Host;
        NetPortBox.Text = cfg.NetPort.ToString();
        ShopNameBox.Text = cfg.ShopName;
        RefreshPortList();
    }

    private void RefreshPorts_Click(object sender, RoutedEventArgs e) => RefreshPortList();

    private void RefreshPortList()
    {
        var ports = ReceiptPrinter.AvailablePorts();
        PrinterStatus.Text = ports.Length == 0
            ? "未检测到串口（USB 转串口需先装驱动）"
            : "可用串口：" + string.Join("  ", ports);
    }

    private void SavePrinter_Click(object sender, RoutedEventArgs e)
    {
        var cfg = new PrinterConfig
        {
            Mode = (PrinterMode)PrintModeCombo.SelectedIndex,
            PortName = PortBox.Text.Trim(),
            Host = HostBox.Text.Trim(),
            NetPort = int.TryParse(NetPortBox.Text, out var np) ? np : 9100,
            ShopName = string.IsNullOrWhiteSpace(ShopNameBox.Text) ? "进销存收银" : ShopNameBox.Text.Trim(),
        };
        PrinterConfigStore.Save(App.Db, cfg);
        // 立即生效
        App.Printer = new ReceiptPrinter(cfg);
        PrinterStatus.Text = $"✔ 已保存：{(cfg.Mode == PrinterMode.None ? "不打印" : cfg.Mode == PrinterMode.Serial ? $"串口 {cfg.PortName}" : $"网络 {cfg.Host}:{cfg.NetPort}")}";
    }

    private void TestPrinter_Click(object sender, RoutedEventArgs e)
    {
        // 未保存时先用界面上的临时配置测试
        var cfg = new PrinterConfig
        {
            Mode = (PrinterMode)PrintModeCombo.SelectedIndex,
            PortName = PortBox.Text.Trim(),
            Host = HostBox.Text.Trim(),
            NetPort = int.TryParse(NetPortBox.Text, out var np) ? np : 9100,
            ShopName = ShopNameBox.Text.Trim(),
        };
        if (cfg.Mode == PrinterMode.None)
        {
            PrinterStatus.Text = "请先选择打印方式";
            return;
        }
        try
        {
            new ReceiptPrinter(cfg).PrintTestPage();
            PrinterStatus.Text = "✔ 测试页已发送，请查看打印机出纸";
        }
        catch (Exception ex)
        {
            PrinterStatus.Text = $"✗ 打印失败：{ex.Message}";
        }
    }

    // ========== 用户管理 ==========
    private void LoadUsers()
    {
        var list = App.Stock.QueryUsers().Select(u => new UserRow
        {
            UserName = u.UserName,
            DisplayName = u.DisplayName ?? "",
            Role = u.Role,
            CanChangePrice = u.CanChangePrice,
            ActiveText = u.IsActive ? "正常" : "停用",
        });
        UserGrid.ItemsSource = list;
    }

    private void AddUser_Click(object sender, RoutedEventArgs e)
    {
        var win = new AddUserWindow();
        win.Owner = Window.GetWindow(this);
        if (win.ShowDialog() == true) LoadUsers();
    }

    private void ResetPwd_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show($"确定重置「{(UserGrid.SelectedItem as UserRow)?.UserName}」的密码为 123456 ？", "重置密码", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        // 简化：重置为 123456
        string? user = (UserGrid.SelectedItem as UserRow)?.UserName;
        if (user == null) { MessageBox.Show("请先选中用户", "提示"); return; }
        using var conn = App.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE SysUser SET PasswordHash=@h WHERE UserName=@u AND IsActive=1";
        cmd.Parameters.AddWithValue("@h", DatabaseService.HashPassword("123456"));
        cmd.Parameters.AddWithValue("@u", user);
        cmd.ExecuteNonQuery();
        MessageBox.Show("已重置为 123456", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ========== 备份 / 恢复 ==========
    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var save = new Microsoft.Win32.SaveFileDialog
            {
                Title = "备份数据库到",
                Filter = "SQLite 备份 (*.db)|*.db",
                FileName = $"stock_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db",
            };
            if (save.ShowDialog() != true) return;
            App.Db.BackupTo(save.FileName);
            BackupStatus.Text = $"✔ 备份成功：{save.FileName}\n（大小 {new FileInfo(save.FileName).Length / 1024.0:N1} KB）";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"备份失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(
            "恢复会覆盖当前所有数据！\n请确认已经备份过，且库里没有需要保留的新记录。\n\n继续？",
            "危险操作", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;

        var open = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择备份文件",
            Filter = "SQLite 备份 (*.db)|*.db",
        };
        if (open.ShowDialog() != true) return;

        try
        {
            // 关闭可能的写句柄前，先完整复制到一个临时文件再覆盖（WAL 下直接覆盖可能残留 -wal）
            var tmp = App.DbPath + ".restore_tmp";
            File.Copy(open.FileName, tmp, true);
            if (File.Exists(App.DbPath + "-wal")) File.Delete(App.DbPath + "-wal");
            if (File.Exists(App.DbPath + "-shm")) File.Delete(App.DbPath + "-shm");
            File.Copy(tmp, App.DbPath, true);
            File.Delete(tmp);
            MessageBox.Show("恢复成功！\n建议重新启动程序以加载恢复后的数据。",
                "✔ 完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"恢复失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ========== 完整性检查 ==========
    private void Integrity_Click(object sender, RoutedEventArgs e)
    {
        var ok = App.Db.CheckIntegrity(out var result);
        MessageBox.Show(ok
            ? "数据库完整性检查通过 ✅"
            : $"数据库可能损坏：{result}", "完整性检查", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    // ========== 示例数据 ==========
    private void ImportSample_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show("导入 10 个示例商品、1 笔进货、1 笔销售（方便试用）？\n示例商品会被标记，之后可一键清除。", "导入示例数据", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        try
        {
            // 示例分类
            App.Stock.SaveCategory(new Category { CategoryName = "示例分类", SortOrder = 99, ParentId = null });

            long sampleCost = 0;
            var samples = new[]
            {
                ("6901234567891", "示例矿泉水 550ml", 1.20m, 2.50m, 100),
                ("6901234567892", "示例可乐 330ml", 1.80m, 3.50m, 80),
                ("6901234567893", "示例牛奶 250ml", 2.50m, 5.00m, 50),
                ("6901234567894", "示例面包", 3.80m, 6.50m, 40),
                ("6901234567895", "示例火腿肠", 1.50m, 3.00m, 90),
                ("6901234567896", "示例方便面", 2.20m, 4.50m, 60),
                ("6901234567897", "示例纸巾 3包", 4.00m, 7.00m, 45),
                ("6901234567898", "示例洗衣液 2kg", 12.00m, 19.90m, 20),
                ("6901234567899", "示例毛巾", 6.00m, 12.50m, 30),
                ("6901234567890", "示例筷子 10双", 1.00m, 2.00m, 70),
            };

            foreach (var (bar, name, cost, price, stock) in samples)
            {
                var p = new Product
                {
                    Barcode = bar,
                    ProductName = name,
                    BaseUnit = "件",
                    PurchasePriceCents = cost.ToCents(),
                    SalePriceCents = price.ToCents(),
                    LowStockQty = 10,
                    CurrentStock = stock,
                    SearchCode = "示例",
                    IsSample = true,
                    IsActive = true,
                };
                var (okb, _) = App.Stock.SaveProduct(p);
                if (!okb) { SampleStatus.Text = "导入中断：商品与现有条码冲突"; return; }
            }

            // 示例进货（第一个商品 20 件）
            var first = App.Stock.FindByBarcode("6901234567891");
            if (first != null)
            {
                var purchase = new Purchase
                {
                    SupplierName = null,
                    Operator = App.CurrentUser,
                    Remark = "示例进货",
                };
                purchase.Items.Add(new PurchaseItem
                {
                    ProductId = first.ProductId,
                    ProductName = first.ProductName,
                    Barcode = first.Barcode,
                    PurchasePriceCents = first.PurchasePriceCents,
                    Qty = 20,
                    BaseQty = 20,
                });
                App.Stock.DoPurchase(purchase);
            }

            // 示例销售
            var sold = App.Stock.FindByBarcode("6901234567892");
            if (sold != null)
            {
                var sale = new Sale
                {
                    Operator = App.CurrentUser,
                    Remark = "示例销售-试用",
                };
                sale.Items.Add(new SaleItem
                {
                    ProductId = sold.ProductId,
                    ProductName = sold.ProductName,
                    Barcode = sold.Barcode,
                    SalePriceCents = sold.SalePriceCents,
                    Qty = 3,
                    BaseQty = 3,
                });
                App.Stock.DoSale(sale);
            }

            SampleStatus.Text = $"✔ 示例数据导入完成（示例成本合计约 {sampleCost.ToYuanString()} 元）";
            _ = sampleCost;
        }
        catch (Exception ex)
        {
            SampleStatus.Text = $"导入失败：{ex.Message}";
        }
    }

    private void ClearSample_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show("清除所有被标记为「示例商品」的数据？\n会连同它们的进货/销售/流水一起删除，恢复初始干净状态。", "清除示例数据", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;

        try
        {
            using var conn = App.Db.Open();
            using var tx = conn.BeginTransaction();

            // 找到示例商品
            var productIds = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT ProductId FROM Product WHERE IsSample=1";
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) productIds.Add(rd[0].ToString() ?? "");
            }

            if (productIds.Count == 0)
            {
                MessageBox.Show("没有示例数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 示例销售的 SaleId
            var sampleSaleIds = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT SaleId FROM SaleItem WHERE ProductId IN (SELECT ProductId FROM Product WHERE IsSample=1) GROUP BY SaleId";
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) sampleSaleIds.Add(rd[0].ToString() ?? "");
            }

            foreach (var sid in sampleSaleIds)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM SaleItem WHERE SaleId=@id; DELETE FROM Sale WHERE SaleId=@id;";
                cmd.Parameters.AddWithValue("@id", sid);
                cmd.ExecuteNonQuery();
            }

            // 删除进货（示例商品的进货行 + 其所在采购单若无其它商品则整单删）
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    DELETE FROM PurchaseItem WHERE ProductId IN (SELECT ProductId FROM Product WHERE IsSample=1);
                    DELETE FROM Purchase WHERE PurchaseId NOT IN (SELECT DISTINCT PurchaseId FROM PurchaseItem);";
                cmd.ExecuteNonQuery();
            }

            // 退货、调整里若引用示例商品也删
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    DELETE FROM SaleReturnItem WHERE ProductId IN (SELECT ProductId FROM Product WHERE IsSample=1);
                    DELETE FROM SaleReturn WHERE SaleReturnId NOT IN (SELECT DISTINCT SaleReturnId FROM SaleReturnItem);
                    DELETE FROM StockAdjustItem WHERE ProductId IN (SELECT ProductId FROM Product WHERE IsSample=1);
                    DELETE FROM StockAdjust WHERE StockAdjustId NOT IN (SELECT DISTINCT StockAdjustId FROM StockAdjustItem);
                    DELETE FROM ProductBarcode WHERE ProductId IN (SELECT ProductId FROM Product WHERE IsSample=1);
                    DELETE FROM Product WHERE IsSample=1;";
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            SampleStatus.Text = $"✔ 已清除 {productIds.Count} 个示例商品及其流水";
        }
        catch (Exception ex)
        {
            SampleStatus.Text = $"清除失败：{ex.Message}";
        }
    }
}

/// <summary>用户行</summary>
public class UserRow
{
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "店员";
    public bool CanChangePrice { get; set; }
    public string ActiveText { get; set; } = "正常";
}