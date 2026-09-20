using StockManager.Core;
using StockManager.Data;
using StockManager.Core.Models;
using StockManager.Print;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var tmp = Path.Combine(Path.GetTempPath(), "stock_test.db");
if (File.Exists(tmp)) File.Delete(tmp);

var db = new DatabaseService(tmp);
db.Initialize();
var svc = new StockService(db);

string Mark(bool b) => b ? "OK" : "FAIL";

// ========== 1. 商品 + 条码唯一 ==========
Console.WriteLine("=== 1. 商品 + 条码唯一 ===");
var cola = new Product { ProductName = "可乐", Barcode = "6900001", BaseUnit = "包", PackUnit = "箱", PackQty = 24, PurchasePriceCents = 200, SalePriceCents = 300 };
var (ok1, e1) = svc.SaveProduct(cola);
Console.WriteLine($"  新增商品: {Mark(ok1)} {e1}");
var dup = new Product { ProductName = "山寨可乐", Barcode = "6900001", PurchasePriceCents = 100, SalePriceCents = 200 };
var (ok2, e2) = svc.SaveProduct(dup);
Console.WriteLine($"  重复条码被拦: {Mark(!ok2)} ({e2})");

// ========== 2. 进货 + 移动加权成本 ==========
Console.WriteLine("\n=== 2. 进货 + 移动加权成本 ===");
var p1 = new Purchase { Operator = "admin" };
p1.Items.Add(new PurchaseItem { ProductId = cola.ProductId, Barcode = "6900001", ProductName = "可乐", PurchasePriceCents = 200, Qty = 10, BaseQty = 10, Unit = "包" });
var (pk1, pe1, pid1) = svc.DoPurchase(p1);
Console.WriteLine($"  进货1: 10包@2元 {Mark(pk1)} {pe1} 单号 {pid1}");

var p2 = new Purchase { Operator = "admin" };
p2.Items.Add(new PurchaseItem { ProductId = cola.ProductId, Barcode = "6900001", ProductName = "可乐", PurchasePriceCents = 400, Qty = 10, BaseQty = 10, Unit = "包" });
var (pk2, pe2, pid2) = svc.DoPurchase(p2);
Console.WriteLine($"  进货2: 10包@4元 {Mark(pk2)} {pe2} 单号 {pid2}");
// 移动加权成本 = (10*200 + 10*400)/20 = 300分
var cost = svc.GetCurrentCost(cola.ProductId);
var stock = svc.GetStock(cola.ProductId);
Console.WriteLine($"  成本: {cost}分 (期望300) {Mark(cost == 300)} | 库存: {stock} (期望20) {Mark(stock == 20)}");

// ========== 3. 多单位进货 ==========
Console.WriteLine("\n=== 3. 多单位（1箱=24包） ===");
var p3 = new Purchase { Operator = "admin" };
p3.Items.Add(new PurchaseItem { ProductId = cola.ProductId, Barcode = "6900001", ProductName = "可乐", PurchasePriceCents = 2400, Qty = 1, BaseQty = 24, Unit = "箱" });
var (pk3, pe3, _) = svc.DoPurchase(p3);
var stock3 = svc.GetStock(cola.ProductId);
Console.WriteLine($"  进1箱(24包): 库存={stock3} (期望44) {Mark(pk3 && stock3 == 44)}");

// ========== 4. 销售 + 扣库存 ==========
Console.WriteLine("\n=== 4. 销售 + 扣库存 ===");
var sale = new Sale { Operator = "admin" };
sale.Items.Add(new SaleItem { ProductId = cola.ProductId, Barcode = "6900001", ProductName = "可乐", SalePriceCents = 300, Qty = 5, BaseQty = 5 });
var (sk1, se1, saleId) = svc.DoSale(sale);
var stockAfterSale = svc.GetStock(cola.ProductId);
Console.WriteLine($"  卖5包: {Mark(sk1)} {se1} | 库存={stockAfterSale} (期望39) {Mark(stockAfterSale == 39)}");
// 移动加权成本: (10*200 + 10*400 + 24*2400)/44 = (2000+4000+57600)/44 = 63600/44 ≈ 1445分
// 卖5包利润 = 5*300 - 5*1445 = 1500-7225 = -5725分（成本高于售价，进货价4元/包导致）
var expectProfit = 1500L - 5 * sale.Items[0].CostPriceCents;
Console.WriteLine($"  利润={sale.ProfitAmountCents}分 (期望 {expectProfit}) {Mark(sale.ProfitAmountCents == expectProfit)}");

// ========== 5. 库存不足拦截 ==========
Console.WriteLine("\n=== 5. 库存不足拦截 ===");
var bigSale = new Sale { Operator = "admin" };
bigSale.Items.Add(new SaleItem { ProductId = cola.ProductId, Barcode = "6900001", ProductName = "可乐", SalePriceCents = 300, Qty = 999, BaseQty = 999 });
var (bk1, be1, _) = svc.DoSale(bigSale);
Console.WriteLine($"  卖999包(库存39): 被拦 {Mark(!bk1)} ({be1})");

// ========== 6. 按行部分退货（B1/B2）==========
Console.WriteLine("\n=== 6. 按行部分退货 ===");
// 先卖 10 包（记录销售行ID）
var sale2 = new Sale { Operator = "admin" };
sale2.Items.Add(new SaleItem { ProductId = cola.ProductId, Barcode = "6900001", ProductName = "可乐", SalePriceCents = 300, Qty = 10, BaseQty = 10 });
var (s2k, s2e, s2id) = svc.DoSale(sale2);
var soldStock = svc.GetStock(cola.ProductId);  // 39 - 10 = 29
Console.WriteLine($"  卖10包: {Mark(s2k)} 库存={soldStock} (期望29) {Mark(soldStock == 29)}");
var saleItemId = sale2.Items[0].SaleItemId;

// 退 4 包（部分退）
var rt1 = new SaleReturn { SaleId = s2id, Operator = "admin", Remark = "顾客退4包" };
rt1.Items.Add(new SaleReturnItem
{
    RefSaleItemId = saleItemId,
    ProductId = cola.ProductId,
    Barcode = "6900001",
    ProductName = "可乐",
    ReturnPriceCents = 300,
    CostPriceCents = sale2.Items[0].CostPriceCents,  // 原行成本
    Qty = 4,
    BaseQty = 4
});
var (r1k, r1e, r1id) = svc.DoReturn(rt1);
var stockAfterReturn = svc.GetStock(cola.ProductId);  // 29 + 4 = 33
Console.WriteLine($"  退4包(部分退): {Mark(r1k)} {r1e} | 库存={stockAfterReturn} (期望33) {Mark(stockAfterReturn == 33)}");

// 原单状态：部分退 → 保持"正常"
var statusAfterPartial = svc.GetSaleStatus(s2id!);
Console.WriteLine($"  部分退后原单状态: {statusAfterPartial} (期望正常) {Mark(statusAfterPartial == "正常")}");

// 退超拦截：再退 8 包（可退 6 包）
var rt2 = new SaleReturn { SaleId = s2id, Operator = "admin", Remark = "退超了" };
rt2.Items.Add(new SaleReturnItem { RefSaleItemId = saleItemId, ProductId = cola.ProductId, ProductName = "可乐", ReturnPriceCents = 300, Qty = 8, BaseQty = 8 });
var (r2k, r2e, _) = svc.DoReturn(rt2);
Console.WriteLine($"  退8包(可退6): 被拦 {Mark(!r2k)} ({r2e})");

// 再退 6 包 → 整单退完 → 原单"已作废"
var rt3 = new SaleReturn { SaleId = s2id, Operator = "admin", Remark = "全退" };
rt3.Items.Add(new SaleReturnItem { RefSaleItemId = saleItemId, ProductId = cola.ProductId, ProductName = "可乐", ReturnPriceCents = 300, Qty = 6, BaseQty = 6 });
var (r3k, r3e, _) = svc.DoReturn(rt3);
Console.WriteLine($"  再退6包(整单退完): {Mark(r3k)} {r3e}");
var statusFull = svc.GetSaleStatus(s2id!);
Console.WriteLine($"  整单退完后状态: {statusFull} (期望已作废) {Mark(statusFull == "已作废")}");
var stockFinal = svc.GetStock(cola.ProductId);  // 33 + 6 = 39
Console.WriteLine($"  全部退完库存: {stockFinal} (期望39回原) {Mark(stockFinal == 39)}");

// ========== 7. 库存调整 ==========
Console.WriteLine("\n=== 7. 库存调整 ===");
var adj = new StockAdjust { Operator = "admin", AdjustType = "盘亏", Remark = "盘点损耗" };
adj.Items.Add(new StockAdjustItem { ProductId = cola.ProductId, ProductName = "可乐", AdjustQty = -3 });
var (ak, ae, aid) = svc.DoStockAdjust(adj);
var stockAdj = svc.GetStock(cola.ProductId);  // 39 - 3 = 36
Console.WriteLine($"  盘亏3包: {Mark(ak)} {ae} | 库存={stockAdj} (期望36) {Mark(stockAdj == 36)}");

// 调整成负数拦截
var adj2 = new StockAdjust { Operator = "admin", AdjustType = "盘亏", Remark = "多盘了" };
adj2.Items.Add(new StockAdjustItem { ProductId = cola.ProductId, ProductName = "可乐", AdjustQty = -999 });
var (ak2, ae2, _) = svc.DoStockAdjust(adj2);
Console.WriteLine($"  盘亏999(库存36): 被拦 {Mark(!ak2)} ({ae2})");

// ========== 8. 供应商 ==========
Console.WriteLine("\n=== 8. 供应商 ===");
var sup = new Supplier { SupplierName = "永旺批发", Contact = "张老板", Phone = "13800000000" };
var (supk, supe) = svc.SaveSupplier(sup);
var sups = svc.QuerySuppliers();
Console.WriteLine($"  新增供应商: {Mark(supk)} {supe} | 列表 {sups.Count} 家 {Mark(sups.Count == 1)}");

// ========== 9. 报表查询 ==========
Console.WriteLine("\n=== 9. 报表查询（今天全范围）===");
var today = DateTime.Now.Date;
var tomorrow = today.AddDays(1).AddSeconds(-1);
var sum = svc.QuerySaleSummary(today, tomorrow);
Console.WriteLine($"  销售汇总: 笔数={sum.Orders} 销售额={sum.SalesAmountCents}分 成本={sum.CostAmountCents}分 利润={sum.ProfitAmountCents}分");
Console.WriteLine($"    笔数=1 {Mark(sum.Orders == 1)} (卖10包整单退=已作废不计，只有卖5包那单正常)");
// 利润 = (卖5包利润) + (卖10包利润)，退掉的已扣回
var saleList = svc.QuerySaleList(today, tomorrow);
Console.WriteLine($"    销售单数=2 {Mark(saleList.Count == 2)}");
var sale1rd = svc.GetSale(saleId!);
Console.WriteLine($"    单笔查询: {Mark(sale1rd != null && sale1rd.Items.Count == 1)}");

var ledger = svc.QueryStockLedger(today, tomorrow);
Console.WriteLine($"  库存台账行数={ledger.Count} {Mark(ledger.Count >= 1)}");
if (ledger.Count > 0)
{
    var row = ledger[0];
    Console.WriteLine($"    {row.ProductName}: 期初={row.BeginStock} 进={row.PurchaseIn} 销={row.SaleOut} 退={row.ReturnIn} 调={row.Adjust} 期末={row.EndStock}");
    Console.WriteLine($"    台账平衡(期初+进−销+退±调=期末): {Mark(row.BeginStock + row.PurchaseIn - row.SaleOut + row.ReturnIn + row.Adjust == row.EndStock)}");
}

var ranking = svc.QueryProductProfitRanking(today, tomorrow);
Console.WriteLine($"  利润排行行数={ranking.Count} {Mark(ranking.Count >= 1)}");
if (ranking.Count > 0)
    Console.WriteLine($"    第一名: {ranking[0].ProductName} 利润={ranking[0].ProfitAmountCents}分");

// ========== 打印模板（ESC/POS 字节流构建）==========
Console.WriteLine("\n=== 10. 小票打印模板 ===");
try
{
    var saleForPrint = new Sale
    {
        SaleNo = "SO202609180001",
        Operator = "admin",
        CreateDate = DateTime.Now,
        TotalAmountCents = 2000,
        DiscountAmountCents = 50,
        ReceivedAmountCents = 2000,
    };
    saleForPrint.Items.Add(new SaleItem
    {
        ProductName = "示例矿泉水",
        SalePriceCents = 250,
        Qty = 2,
        AmountCents = 500,
    });
    var saleBytes = ReceiptBuilder.BuildSale(saleForPrint);
    Console.WriteLine($"  销售小票字节数={saleBytes.Length} >0 {Mark(saleBytes.Length > 50)}");
    // 应包含 ESC @ 初始化指令
    Console.WriteLine($"  含初始化指令(ESC@): {Mark(saleBytes[0] == 0x1B && saleBytes[1] == 0x40)}");
    // 含切纸指令 GS V
    Console.WriteLine($"  含切纸指令(GS V): {Mark(saleBytes.Any(x => x == 0x1D))}");

    var retBytes = ReceiptBuilder.BuildReturn(new SaleReturn
    {
        ReturnNo = "RT202609180001",
        CreateDate = DateTime.Now,
        TotalAmountCents = 300,
    });
    Console.WriteLine($"  退货小票字节数={retBytes.Length} >0 {Mark(retBytes.Length > 30)}");

    // 极长商品名不崩（截断处理）
    var longName = new Sale { SaleNo = "SOX", CreateDate = DateTime.Now, TotalAmountCents = 1 };
    longName.Items.Add(new SaleItem { ProductName = new string('长', 80), SalePriceCents = 1, Qty = 1, AmountCents = 1 });
    var lb = ReceiptBuilder.BuildSale(longName);
    Console.WriteLine($"  超长商品名截断: {Mark(lb.Length > 30)}");
}
catch (Exception ex)
{
    Console.WriteLine($"  ✗ 打印模板异常：{ex.Message}");
}

// ========== 试用授权（C1）==========
// ========== P0-2 移动加权成本回归（审查者 55 元例子）==========
Console.WriteLine("\n=== 11. 移动加权成本（穿插销售）===");
try
{
    // 场景：进100件@10元 → 卖90件 → 进10件@100元
    // 正确移动加权 = (10×10 + 10×100)/20 = 55元/件（旧实现把已售90也算进 → 18元/件）
    var pCost = new Product
    {
        Barcode = "8000000000001",
        ProductName = "成本测试",
        BaseUnit = "件",
        PurchasePriceCents = 1000,
        SalePriceCents = 2000,
        IsActive = true,
    };
    svc.SaveProduct(pCost);
    pCost = svc.FindByBarcode("8000000000001")!;

    // 第一次进 100 件 @10元
    var pc1 = new Purchase { Operator = "t" };
    pc1.Items.Add(new PurchaseItem { ProductId = pCost.ProductId, ProductName = pCost.ProductName, Barcode = pCost.Barcode, PurchasePriceCents = 1000, Qty = 100, BaseQty = 100 });
    svc.DoPurchase(pc1);

    // 卖 90 件 @20元
    var s1 = new Sale { Operator = "t" };
    s1.Items.Add(new SaleItem { ProductId = pCost.ProductId, ProductName = pCost.ProductName, Barcode = pCost.Barcode, SalePriceCents = 2000, Qty = 90, BaseQty = 90 });
    var (sok, serr, _) = svc.DoSale(s1);
    Console.WriteLine($"  卖90件成功 {Mark(sok)}");
    if (!sok) Console.WriteLine($"    error: {serr}");

    // 第二次进 10 件 @100元
    var pc2 = new Purchase { Operator = "t" };
    pc2.Items.Add(new PurchaseItem { ProductId = pCost.ProductId, ProductName = pCost.ProductName, Barcode = pCost.Barcode, PurchasePriceCents = 10000, Qty = 10, BaseQty = 10 });
    svc.DoPurchase(pc2);

    var nowCost = svc.GetCurrentCost(pCost.ProductId);
    Console.WriteLine($"  当前成本={nowCost}分 期望5500分 {Mark(nowCost == 5500)}");
    if (nowCost != 5500) Console.WriteLine($"    旧实现会给 18元/件=1818分，利润虚高");

    // 再卖 5 件，成本应取 55 元/件
    var s2 = new Sale { Operator = "t" };
    s2.Items.Add(new SaleItem { ProductId = pCost.ProductId, ProductName = pCost.ProductName, Barcode = pCost.Barcode, SalePriceCents = 2000, Qty = 5, BaseQty = 5 });
    var (s2ok, _, saleId2) = svc.DoSale(s2);
    Console.WriteLine($"  再卖5件 {Mark(s2ok)}");
    if (s2ok)
    {
        var full = svc.GetSale(saleId2!);
        var itemCost = full!.Items[0].CostPriceCents;
        Console.WriteLine($"  本次销售行成本={itemCost}分 期望5500分 {Mark(itemCost == 5500)}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  ✗ 成本回归异常：{ex.Message}");
}

Console.WriteLine("\n=== 12. 试用授权 ===");
try
{
    // 全新库 → 首次启动，剩余 15 天
    LicenseService.Init(db);
    Console.WriteLine($"  全新库剩余天数=15 {Mark(LicenseService.DaysLeft == 15)}");
    Console.WriteLine($"  全新库非只读 {Mark(!LicenseService.IsReadOnly)}");

    // 模拟用了 15 天后：把 TrialStart 改为 16 天前
    using (var conn = db.Open())
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "UPDATE SysConfig SET ConfigValue=@d WHERE ConfigKey='TrialStart'";
        cmd.Parameters.AddWithValue("@d", DateTime.Today.AddDays(-16).ToString("yyyy-MM-dd"));
        cmd.ExecuteNonQuery();
    }
    LicenseService.Init(db);
    Console.WriteLine($"  超期剩余天数=0 {Mark(LicenseService.DaysLeft == 0)}");
    Console.WriteLine($"  超期进入只读 {Mark(LicenseService.IsReadOnly)}");

    // 只读拦截写操作
    var saleBlocked = svc.DoSale(new Sale());
    Console.WriteLine($"  只读拦截销售 {Mark(!saleBlocked.ok && saleBlocked.error.Contains("只读"))}");
    var pBlocked = svc.SaveProduct(new Product { ProductName = "x" });
    Console.WriteLine($"  只读拦截商品 {Mark(!pBlocked.ok)}");

    // 激活后恢复写
    LicenseService.Activate();
    Console.WriteLine($"  激活后非只读 {Mark(!LicenseService.IsReadOnly)}");
    var pOk = svc.SaveProduct(new Product { ProductName = "激活后商品", Barcode = "999", SalePriceCents = 100, IsActive = true });
    Console.WriteLine($"  激活后可写 {Mark(pOk.ok)}");
}
catch (Exception ex)
{
    Console.WriteLine($"  ✗ 试用授权异常：{ex.Message}");
}

// ========== Excel 导入（C2）==========
Console.WriteLine("\n=== 12. Excel 商品导入 ===");
try
{
    // 生成样例 xlsx（用 MiniExcel，与生产一致）
    var xlsx = Path.Combine(Path.GetTempPath(), $"stock_import_{Guid.NewGuid():N}.xlsx");
    var sample = new[]
    {
        new { 条码 = "6010000000001", 商品名 = "导入商品A", 规格 = "500ml", 单位 = "瓶", 进价 = 2.00m, 售价 = 4.50m, 库存 = 30, 低库存预警 = 5 },
        new { 条码 = "6010000000002", 商品名 = "导入商品B", 规格 = "1L", 单位 = "桶", 进价 = 5.50m, 售价 = 9.90m, 库存 = 12, 低库存预警 = 3 },
        new { 条码 = "", 商品名 = "坏行-无条码", 规格 = "", 单位 = "件", 进价 = 1.00m, 售价 = 2.00m, 库存 = 5, 低库存预警 = 0 },
    };
    MiniExcelLibs.MiniExcel.SaveAs(xlsx, sample);

    var parsed = ExcelImportService.Parse(xlsx);
    Console.WriteLine($"  解析行数=3 {Mark(parsed.Count == 3)}");
    Console.WriteLine($"  有效行=2 {Mark(parsed.Count(r => r.Valid) == 2)}");
    Console.WriteLine($"  无效行识别(条码空) {Mark(parsed.Any(r => !r.Valid && r.Error != null))}");
    Console.WriteLine($"  金额解析(进价2.00→200分) {Mark(parsed[0].PurchaseCents == 200)}");
    Console.WriteLine($"  库存解析(30) {Mark(parsed[0].Stock == 30)}");

    var (ok, fails) = ExcelImportService.Import(svc, parsed);
    Console.WriteLine($"  导入成功=2 {Mark(ok == 2)}");

    // 重复条码：再导一次，应跳过 2 条
    var (okDup, failsDup) = ExcelImportService.Import(svc, parsed.Where(r => r.Valid).ToList());
    Console.WriteLine($"  重复导入成功=0 {Mark(okDup == 0)}");
    Console.WriteLine($"  重复导入跳过=2 {Mark(failsDup.Count == 2)}");
    Console.WriteLine($"  重复提示含已存在 {Mark(failsDup.Any(f => f.Contains("已存在")))}");

    // 已落库商品可查
    var impA = svc.FindByBarcode("6010000000001");
    Console.WriteLine($"  导入后可按条码查 {Mark(impA != null)}");
    if (impA != null) Console.WriteLine($"    售价={impA.SalePriceCents}分 {Mark(impA.SalePriceCents == 450)}");

    File.Delete(xlsx);
}
catch (Exception ex)
{
    Console.WriteLine($"  ✗ Excel 导入异常：{ex.Message}");
}

// ========== 老库 v1 → v2 升级路径 ==========
Console.WriteLine("\n=== 13. 老库升级 ===");
try
{
    // 手工造一个 v1 库：用真实 v1 schema（CreateAll 去掉 CurrentCostCents 行），DBVersion 记 v1
    var tmp2 = Path.Combine(Path.GetTempPath(), $"stock_upgrade_{Guid.NewGuid():N}.db");
    var v1Schema = DatabaseSchema.CreateAll.Replace("  CurrentCostCents   INTEGER DEFAULT 0,   -- 移动加权成本（v2 起持久化，销售成本唯一真相）\n", "");
    using (var raw = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={tmp2}"))
    {
        raw.Open();
        using var cmd = raw.CreateCommand();
        cmd.CommandText = v1Schema + @"
            INSERT INTO Product (ProductId, ProductName, Barcode, CurrentStock) VALUES ('PDT1','旧商品','111',5);
            INSERT INTO DBVersion (VersionId, VersionNo, UpgradeSql) VALUES ('VER000000001', 1, '初始建库');";
        cmd.ExecuteNonQuery();
    }

    var upDb = new DatabaseService(tmp2);
    upDb.Initialize();

    // 升级后应存在 CurrentCostCents 列 + 版本号 = 2
    using (var c2 = upDb.Open())
    {
        bool hasCol;
        using (var cmd2 = c2.CreateCommand())
        {
            cmd2.CommandText = "PRAGMA table_info(Product)";
            using var rd2 = cmd2.ExecuteReader();
            hasCol = false;
            while (rd2.Read()) if (rd2[1].ToString() == "CurrentCostCents") hasCol = true;
        }
        Console.WriteLine($"  升级后含 CurrentCostCents 列 {Mark(hasCol)}");

        int v;
        using (var cmd2 = c2.CreateCommand())
        {
            cmd2.CommandText = "SELECT MAX(VersionNo) FROM DBVersion";
            v = Convert.ToInt32(cmd2.ExecuteScalar());
        }
        Console.WriteLine($"  版本号升至 {v} {Mark(v == 2)}");

        using (var cmd2 = c2.CreateCommand())
        {
            cmd2.CommandText = "SELECT CurrentCostCents FROM Product WHERE ProductId='PDT1'";
            var oldCost = Convert.ToInt64(cmd2.ExecuteScalar() ?? 0);
            Console.WriteLine($"  旧数据成本默认 0 {Mark(oldCost == 0)}");
        }
    }
    GC.Collect(); GC.WaitForPendingFinalizers();   // 释放 Open() 遗留连接句柄
    foreach (var f in new[] { tmp2, tmp2 + "-wal", tmp2 + "-shm" })
        if (File.Exists(f)) { try { File.Delete(f); } catch { } }
}
catch (Exception ex)
{
    Console.WriteLine($"  ✗ 升级测试异常：{ex.Message}");
}

// ========== 激活码（P0-1）==========
Console.WriteLine("\n=== 14. 激活码 ===");
try
{
    var mc = ActivationService.MachineCode("test-machine-guid-123");
    Console.WriteLine($"  机器码生成 {Mark(mc.Length >= 10)} ({mc})");
    var code = ActivationService.GenerateActivationCode(mc);
    Console.WriteLine($"  激活码格式(4组5位) {Mark(System.Text.RegularExpressions.Regex.IsMatch(code, @"^[A-Z2-7]{5}-[A-Z2-7]{5}-[A-Z2-7]{5}-[A-Z2-7]{5}$"))}");
    Console.WriteLine($"  验签正确 {Mark(ActivationService.Validate(mc, code))}");
    Console.WriteLine($"  验签容错(小写/去横线) {Mark(ActivationService.Validate(mc, code.ToLowerInvariant().Replace("-", "")))}");
    Console.WriteLine($"  错误码拒绝 {Mark(!ActivationService.Validate(mc, "AAAAA-BBBBB-CCCCC-DDDDD"))}");
    Console.WriteLine($"  不同机器码拒绝 {Mark(!ActivationService.Validate(ActivationService.MachineCode("other-guid"), code))}");
}
catch (Exception ex)
{
    Console.WriteLine($"  ✗ 激活码异常：{ex.Message}");
}

// ========== 试用防重置（商业风险4）==========
Console.WriteLine("\n=== 15. 试用防重置 ===");
try
{
    // 场景：注册表已有 10 天前的 TrialStart，DB 全新 → 应取最早（10 天前），剩余 5 天
    var tmp3 = Path.Combine(Path.GetTempPath(), $"stock_trial_{Guid.NewGuid():N}.db");
    var db3 = new DatabaseService(tmp3);
    db3.Initialize();
    var regStart = DateTime.Today.AddDays(-10).ToString("yyyy-MM-dd");
    LicenseService.Init(db3, registryStart: regStart);
    Console.WriteLine($"  注册表最早生效(剩余5天) {Mark(LicenseService.DaysLeft == 5)}");

    // 场景：拨回系统日期（DB MaxDateSeen 被改成远古，注册表仍记录今天）→ 试用不重置
    using (var c = db3.Open())
    using (var cmd = c.CreateCommand())
    {
        cmd.CommandText = "UPDATE SysConfig SET ConfigValue='2026-08-01' WHERE ConfigKey='MaxDateSeen'";
        cmd.ExecuteNonQuery();
    }
    // 用户把系统日期拨回 8 月 → 但注册表已记录今天（最大已见日期），单调不回拨
    LicenseService.Init(db3, registryStart: regStart, registryMaxSeen: DateTime.Today.ToString("yyyy-MM-dd"));
    Console.WriteLine($"  单调不回拨(剩余仍5天) {Mark(LicenseService.DaysLeft == 5)}");
    Console.WriteLine($"  回拨后已用仍10天 {Mark(LicenseService.UsedDays == 10)}");
    GC.Collect(); GC.WaitForPendingFinalizers();
    foreach (var f in new[] { tmp3, tmp3 + "-wal", tmp3 + "-shm" })
        if (File.Exists(f)) { try { File.Delete(f); } catch { } }
}
catch (Exception ex)
{
    Console.WriteLine($"  ✗ 防重置异常：{ex.Message}");
}

Console.WriteLine("\n=== 全部业务验证完成 ===");
