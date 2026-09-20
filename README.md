# 进销存 StockManager v1.0

单机版进销存（WPF + SQLite）。扫码收银、商品/进货/退货管理、库存台账、利润报表、小票打印。
闲鱼交付，15 天试用 + 激活码买断。

## 技术栈

- .NET 10（`net10.0-windows`），WPF
- SQLite（`Microsoft.Data.Sqlite`，WAL + synchronous=FULL 断电保护）
- MiniExcel（商品批量导入）
- ESC/POS 小票打印（串口 / 网络，58mm 热敏）

## 目录

```
src/
  StockManager.Core    领域模型 + Money(分) + ActivationService(HMAC激活码)
  StockManager.Data    SQLite建表/升级 + 业务服务 + LicenseService + Excel导入
  StockManager.Print   ESC/POS 指令 + 小票模板 + 打印机
  StockManager.App     WPF 界面（登录 + 7 页面 + 激活/打印/导入弹窗）
tests/
  StockManager.Core.Tests  业务回归 15 组（金额分/移动加权/退货/报表/打印/激活码/防重置/老库升级）
  StockManager.UiSmoke     STA 冒烟（逐页实例化 + 导航遍历 + 命名控件校验）
tools/
  StockManager.LicenseGen  激活码离线生成器（作者专用，不随软件发布）
```

## 构建

```bash
dotnet build StockManager.slnx

# 业务回归 + UI 冒烟
dotnet run --project tests/StockManager.Core.Tests
dotnet run --project tests/StockManager.UiSmoke

# 发布自包含（绿色版）
dotnet publish src/StockManager.App -c Release -r win-x64 --self-contained true -o publish/
```

## 数据库升级

`DatabaseService.RunUpgrade` 按 `DBVersion` 表逐级执行 `DatabaseSchema.UpgradeScripts`。
当前 v2：Product 表新增 `CurrentCostCents`（移动加权成本持久化）。老库启动时自动升级，事务保护。

## 授权机制（P0-1 修复）

- **机器码**：Windows `MachineGuid` → SHA256 → Base32（设置页「复制机器码」）
- **激活码**：`HMACSHA256(机器码, SecretKey)` → Base32 20 位 → `XXXXX-XXXXX-XXXXX-XXXXX`
- **生成**：`dotnet run --project tools/StockManager.LicenseGen -- <机器码>`（作者专用）
- **验证**：设置页输入 → 软件内重算比对 → `LicenseService.TryActivate` → `Licensed=1`
- **边界**：密钥内嵌程序，反编译可拿 → 挡普通用户，不挡决心破解者（商业风险3 已声明）

## 试用防重置（商业风险4 修复）

- `TrialStart` 双份存储（SysConfig + 注册表 `HKCU\Software\StockManager`），取最早
- `MaxDateSeen`（最大已见日期）单调不回拨，`UsedDays` 按它计算 → 改系统日期无法重置
- 到期未激活 → 只读（所有写操作被 `LicenseService.WriteBlocked()` 拦截）

## 成本算法（P0-2 修复）

- `Product.CurrentCostCents` 是成本唯一真相，每次进货移动加权写回
- 销售/报表/退货成本全部读该列
- 回归测试 11 组复现审查者的「进100@10 → 卖90 → 进10@100」场景，验证 55 元/件

## 交付

- 客户绿色版 = `publish/` 打 zip（64MB），含 README + 闲鱼文案见 `发布包/`
- 默认账号 `admin` / `admin123`
