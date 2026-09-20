using Microsoft.Data.Sqlite;
using StockManager.Core;
using StockManager.Core.Models;

namespace StockManager.Data;

/// <summary>
/// 核心业务服务：商品/进货/销售/退货/库存调整
/// 所有出入库在同一 SQLite 事务内，失败整体回滚（见 2.3）
/// </summary>
public class StockService
{
    private readonly DatabaseService _db;

    public StockService(DatabaseService db) => _db = db;

    // ========== 商品 ==========

    /// <summary>新增/更新商品（A5 条码唯一校验）</summary>
    public (bool ok, string error) SaveProduct(Product p)
    {
        var blocked = LicenseService.WriteBlocked();
        if (blocked != null) return (false, blocked);
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();

        // 条码唯一：主条码
        if (!string.IsNullOrWhiteSpace(p.Barcode))
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT COUNT(*) FROM Product WHERE Barcode=@b AND ProductId<>@id";
                cmd.Parameters.AddWithValue("@b", p.Barcode);
                cmd.Parameters.AddWithValue("@id", p.ProductId);
                if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
                    return (false, $"条码已被占用：{p.Barcode}");
            }
        }

        if (string.IsNullOrEmpty(p.ProductId))
        {
            // 新增
            p.ProductId = _db.NewId("PDT", conn, tx);
            p.CreateDate = DateTime.Now;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO Product (ProductId, CategoryId, ProductCode, Barcode, ProductName, Spec,
                    BaseUnit, PackUnit, PackQty, PurchasePriceCents, SalePriceCents, LowStockQty,
                    CurrentStock, Remark, SearchCode, IsSample, IsActive, CreateDate)
                VALUES (@id,@cat,@code,@b,@name,@spec,@bu,@pu,@pq,@pp,@sp,@ls,@cs,@rm,@sc,@is,@ia,@cd)";
            AddProductParams(cmd, p);
            cmd.ExecuteNonQuery();
        }
        else
        {
            // 更新
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                UPDATE Product SET CategoryId=@cat, ProductCode=@code, Barcode=@b, ProductName=@name, Spec=@spec,
                    BaseUnit=@bu, PackUnit=@pu, PackQty=@pq, PurchasePriceCents=@pp, SalePriceCents=@sp,
                    LowStockQty=@ls, Remark=@rm, SearchCode=@sc, IsSample=@is, IsActive=@ia,
                    UpdateDate=datetime('now','localtime')
                WHERE ProductId=@id";
            AddProductParams(cmd, p);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        return (true, "");
    }

    private static void AddProductParams(SqliteCommand cmd, Product p)
    {
        cmd.Parameters.AddWithValue("@id", p.ProductId);
        cmd.Parameters.AddWithValue("@cat", (object?)p.CategoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@code", (object?)p.ProductCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@b", (object?)p.Barcode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@name", p.ProductName);
        cmd.Parameters.AddWithValue("@spec", (object?)p.Spec ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bu", (object?)p.BaseUnit ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pu", (object?)p.PackUnit ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pq", p.PackQty);
        cmd.Parameters.AddWithValue("@pp", p.PurchasePriceCents);
        cmd.Parameters.AddWithValue("@sp", p.SalePriceCents);
        cmd.Parameters.AddWithValue("@ls", p.LowStockQty);
        cmd.Parameters.AddWithValue("@cs", p.CurrentStock);
        cmd.Parameters.AddWithValue("@rm", (object?)p.Remark ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sc", (object?)p.SearchCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@is", p.IsSample ? 1 : 0);
        cmd.Parameters.AddWithValue("@ia", p.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@cd", p.CreateDate.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    /// <summary>商品列表查询（商品管理页用），keyword 匹配条码/名称/货号/简码</summary>
    public List<Product> QueryProducts(string? keyword = null, bool includeInactive = false)
    {
        var list = new List<Product>();
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        var sql = "SELECT * FROM Product";
        if (!includeInactive) sql += " WHERE IsActive=1";
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = $"%{keyword.Trim()}%";
            sql += includeInactive ? " WHERE" : " AND";
            sql += " (Barcode LIKE @k OR ProductName LIKE @k OR ProductCode LIKE @k OR SearchCode LIKE @k)";
            cmd.Parameters.AddWithValue("@k", k);
        }
        sql += " ORDER BY ProductName, CreateDate";
        cmd.CommandText = sql;
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) list.Add(ReadProduct(rd));
        return list;
    }

    /// <summary>全部分类</summary>
    public List<Category> QueryCategories()
    {
        var list = new List<Category>();
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Category ORDER BY SortOrder, CategoryName";
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            list.Add(new Category
            {
                CategoryId = rd["CategoryId"].ToString() ?? "",
                CategoryName = rd["CategoryName"].ToString() ?? "",
                ParentId = rd["ParentId"]?.ToString(),
            });
        }
        return list;
    }

    /// <summary>按条码查商品（主条码 + 附加条码都查）</summary>
    public Product? FindByBarcode(string barcode)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.* FROM Product p WHERE p.Barcode=@b AND p.IsActive=1
            UNION
            SELECT p.* FROM Product p JOIN ProductBarcode pb ON p.ProductId=pb.ProductId
            WHERE pb.Barcode=@b AND p.IsActive=1 LIMIT 1";
        cmd.Parameters.AddWithValue("@b", barcode);
        using var rd = cmd.ExecuteReader();
        return rd.Read() ? ReadProduct(rd) : null;
    }

    private static Product ReadProduct(SqliteDataReader rd) => new()
    {
        ProductId = rd["ProductId"].ToString() ?? "",
        CategoryId = rd["CategoryId"]?.ToString(),
        ProductCode = rd["ProductCode"]?.ToString(),
        Barcode = rd["Barcode"]?.ToString(),
        ProductName = rd["ProductName"].ToString() ?? "",
        Spec = rd["Spec"]?.ToString(),
        BaseUnit = rd["BaseUnit"]?.ToString(),
        PackUnit = rd["PackUnit"]?.ToString(),
        PackQty = Convert.ToDecimal(rd["PackQty"] ?? 0),
        PurchasePriceCents = Convert.ToInt64(rd["PurchasePriceCents"] ?? 0),
        SalePriceCents = Convert.ToInt64(rd["SalePriceCents"] ?? 0),
        LowStockQty = Convert.ToDecimal(rd["LowStockQty"] ?? 0),
        CurrentStock = Convert.ToDecimal(rd["CurrentStock"] ?? 0),
        Remark = rd["Remark"]?.ToString(),
        SearchCode = rd["SearchCode"]?.ToString(),
        IsSample = Convert.ToInt32(rd["IsSample"] ?? 0) == 1,
        IsActive = Convert.ToInt32(rd["IsActive"] ?? 1) == 1,
        CreateDate = DateTime.Now,
    };

    // ========== 分类 / 附加条码 / 用户 ==========

    /// <summary>新增/更新分类</summary>
    public (bool ok, string error) SaveCategory(Category c)
    {
        var blocked = LicenseService.WriteBlocked();
        if (blocked != null) return (false, blocked);
        using var conn = _db.Open();
        if (string.IsNullOrEmpty(c.CategoryId))
        {
            c.CategoryId = _db.NewId("CAT");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Category (CategoryId, CategoryName, ParentId, SortOrder, CreateDate) VALUES (@id,@name,@pid,@so,datetime('now','localtime'))";
            cmd.Parameters.AddWithValue("@id", c.CategoryId);
            cmd.Parameters.AddWithValue("@name", c.CategoryName);
            cmd.Parameters.AddWithValue("@pid", (object?)c.ParentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@so", c.SortOrder);
            cmd.ExecuteNonQuery();
        }
        else
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Category SET CategoryName=@name, ParentId=@pid, SortOrder=@so WHERE CategoryId=@id";
            cmd.Parameters.AddWithValue("@id", c.CategoryId);
            cmd.Parameters.AddWithValue("@name", c.CategoryName);
            cmd.Parameters.AddWithValue("@pid", (object?)c.ParentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@so", c.SortOrder);
            cmd.ExecuteNonQuery();
        }
        return (true, "");
    }

    /// <summary>为商品添加附加条码（一商品多码）</summary>
    public (bool ok, string error) AddProductBarcode(string productId, string barcode)
    {
        var blocked = LicenseService.WriteBlocked();
        if (blocked != null) return (false, blocked);
        if (string.IsNullOrWhiteSpace(barcode)) return (false, "条码不能为空");
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Product WHERE Barcode=@b UNION ALL SELECT COUNT(*) FROM ProductBarcode WHERE Barcode=@b";
        cmd.Parameters.AddWithValue("@b", barcode.Trim());
        var r = cmd.ExecuteReader();
        var n = 0;
        while (r.Read()) n += Convert.ToInt32(r[0]);
        if (n > 0) return (false, $"条码已被占用：{barcode.Trim()}");
        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "INSERT INTO ProductBarcode (ProductBarcodeId, ProductId, Barcode, CreateDate) VALUES (@id,@pid,@b,datetime('now','localtime'))";
        cmd2.Parameters.AddWithValue("@id", _db.NewId("PBC"));
        cmd2.Parameters.AddWithValue("@pid", productId);
        cmd2.Parameters.AddWithValue("@b", barcode.Trim());
        cmd2.ExecuteNonQuery();
        return (true, "");
    }

    /// <summary>按工号/姓名查用户</summary>
    public List<SysUser> QueryUsers()
    {
        var list = new List<SysUser>();
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM SysUser ORDER BY UserName";
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            list.Add(new SysUser
            {
                UserId = rd["UserId"].ToString() ?? "",
                UserName = rd["UserName"].ToString() ?? "",
                PasswordHash = rd["PasswordHash"]?.ToString() ?? "",
                DisplayName = rd["DisplayName"]?.ToString(),
                Role = rd["Role"]?.ToString() ?? "店员",
                CanChangePrice = Convert.ToInt32(rd["CanChangePrice"] ?? 0) == 1,
                IsActive = Convert.ToInt32(rd["IsActive"] ?? 1) == 1,
            });
        }
        return list;
    }

    // ========== 进货 ==========

    /// <summary>进货：写单 + 明细 + 加库存 + 移动加权成本（同一事务）</summary>
    public (bool ok, string error, string? purchaseId) DoPurchase(Purchase p)
    {
        var blocked = LicenseService.WriteBlocked();
        if (blocked != null) return (false, blocked, null);
        if (p.Items.Count == 0) return (false, "进货单没有明细", null);

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();

        try
        {
            p.PurchaseId = _db.NewId("PCH", conn, tx);
            p.CreateDate = DateTime.Now;
            p.PurchaseNo = $"PO{DateTime.Now:yyyyMMdd}{p.PurchaseId[^4..]}";
            p.TotalAmountCents = 0;

            // 写单头
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO Purchase (PurchaseId, PurchaseNo, SupplierId, SupplierName, TotalAmountCents, ItemCount, Operator, Status, Remark, CreateDate)
                    VALUES (@id,@no,@sid,@sname,@amt,@cnt,@op,'正常',@rm,@cd)";
                cmd.Parameters.AddWithValue("@id", p.PurchaseId);
                cmd.Parameters.AddWithValue("@no", p.PurchaseNo);
                cmd.Parameters.AddWithValue("@sid", (object?)p.SupplierId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sname", (object?)p.SupplierName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@amt", p.TotalAmountCents);
                cmd.Parameters.AddWithValue("@cnt", p.Items.Count);
                cmd.Parameters.AddWithValue("@op", (object?)p.Operator ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@rm", (object?)p.Remark ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cd", p.CreateDate.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();
            }

            // 逐条：写明细 + 更新库存 + 移动加权成本
            foreach (var item in p.Items)
            {
                item.PurchaseItemId = _db.NewId("PIT", conn, tx);
                item.PurchaseId = p.PurchaseId;
                item.AmountCents = item.PurchasePriceCents.Amount(item.Qty);
                p.TotalAmountCents += item.AmountCents;

                // 读当前库存 + 当前成本
                var (curStock, curCost) = GetStockAndCost(conn, tx, item.ProductId);
                var newStock = curStock + item.BaseQty;
                // 移动加权：新成本分 = (旧库存×旧成本 + 本次进价×本次数量) / 新库存
                long newCost;
                if (newStock <= 0)
                    newCost = item.PurchasePriceCents;
                else
                    newCost = (long)Math.Round(
                        (curStock * curCost + item.PurchasePriceCents * item.BaseQty) / newStock,
                        0, MidpointRounding.AwayFromZero);

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        INSERT INTO PurchaseItem (PurchaseItemId, PurchaseId, ProductId, Barcode, ProductName, PurchasePriceCents, Unit, Qty, BaseQty, AmountCents)
                        VALUES (@id,@pid,@prid,@b,@name,@pp,@unit,@qty,@bq,@amt)";
                    cmd.Parameters.AddWithValue("@id", item.PurchaseItemId);
                    cmd.Parameters.AddWithValue("@pid", p.PurchaseId);
                    cmd.Parameters.AddWithValue("@prid", item.ProductId);
                    cmd.Parameters.AddWithValue("@b", (object?)item.Barcode ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", (object?)item.ProductName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@pp", item.PurchasePriceCents);
                    cmd.Parameters.AddWithValue("@unit", (object?)item.Unit ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@qty", item.Qty);
                    cmd.Parameters.AddWithValue("@bq", item.BaseQty);
                    cmd.Parameters.AddWithValue("@amt", item.AmountCents);
                    cmd.ExecuteNonQuery();
                }

                // 更新库存 + 成本（成本存在 SaleItem 用，Product 表不需要存成本列，但为查当前成本方便，从最近进货算；这里用临时查询）
                UpdateStock(conn, tx, item.ProductId, newStock);
            }

            // 回写单头金额
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE Purchase SET TotalAmountCents=@amt WHERE PurchaseId=@id";
                cmd.Parameters.AddWithValue("@amt", p.TotalAmountCents);
                cmd.Parameters.AddWithValue("@id", p.PurchaseId);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return (true, "", p.PurchaseId);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return (false, ex.Message, null);
        }
    }

    private void UpdateStock(SqliteConnection conn, SqliteTransaction tx, string productId, decimal stock)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE Product SET CurrentStock=@s WHERE ProductId=@id";
        cmd.Parameters.AddWithValue("@s", stock);
        cmd.Parameters.AddWithValue("@id", productId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>读当前库存 + 最近进货成本（成本用最近一次进价近似，精确移动加权在销售时从 PurchaseItem 累计）</summary>
    private (decimal stock, long cost) GetStockAndCost(SqliteConnection conn, SqliteTransaction tx, string productId)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT CurrentStock FROM Product WHERE ProductId=@id";
            cmd.Parameters.AddWithValue("@id", productId);
            var stock = Convert.ToDecimal(cmd.ExecuteScalar() ?? 0);

            // 最近一次进价作为当前成本（销售时用移动加权精确计算）
            long cost = 0;
            cmd.CommandText = @"
                SELECT PurchasePriceCents FROM PurchaseItem pi
                JOIN Purchase p ON pi.PurchaseId=p.PurchaseId
                WHERE pi.ProductId=@id AND p.Status='正常'
                ORDER BY p.CreateDate DESC LIMIT 1";
            using var rd = cmd.ExecuteReader();
            if (rd.Read()) cost = Convert.ToInt64(rd[0]);
            return (stock, cost);
        }
    }

    /// <summary>查询当前库存</summary>
    /// <summary>查询销售单状态</summary>
    public string GetSaleStatus(string saleId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Status FROM Sale WHERE SaleId=@id";
        cmd.Parameters.AddWithValue("@id", saleId);
        var v = cmd.ExecuteScalar();
        return v?.ToString() ?? "";
    }

    public decimal GetStock(string productId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CurrentStock FROM Product WHERE ProductId=@id";
        cmd.Parameters.AddWithValue("@id", productId);
        return Convert.ToDecimal(cmd.ExecuteScalar() ?? 0);
    }

    /// <summary>查询当前移动加权成本（分）</summary>
    public long GetCurrentCost(string productId)
    {
        using var conn = _db.Open();
        return GetCurrentCostInTx(conn, null, productId);
    }

    // ========== 销售 ==========

    /// <summary>销售：写单 + 明细 + 扣库存 + 算利润（同一事务）</summary>
    public (bool ok, string error, string? saleId) DoSale(Sale s)
    {
        var blocked = LicenseService.WriteBlocked();
        if (blocked != null) return (false, blocked, null);
        if (s.Items.Count == 0) return (false, "销售单没有明细", null);

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();

        try
        {
            s.SaleId = _db.NewId("SAL", conn, tx);
            s.CreateDate = DateTime.Now;
            s.SaleNo = $"SO{DateTime.Now:yyyyMMdd}{s.SaleId[^4..]}";
            s.TotalAmountCents = 0;
            s.CostAmountCents = 0;

            // 先校验库存是否充足
            foreach (var item in s.Items)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT CurrentStock FROM Product WHERE ProductId=@id";
                    cmd.Parameters.AddWithValue("@id", item.ProductId);
                    var stock = Convert.ToDecimal(cmd.ExecuteScalar() ?? 0);
                    if (stock < item.BaseQty)
                    {
                        tx.Rollback();
                        return (false, $"库存不足：{item.ProductName} 现有 {stock}，需 {item.BaseQty}", null);
                    }
                }
            }

            // 写单头
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO Sale (SaleId, SaleNo, TotalAmountCents, DiscountAmountCents, ReceivedAmountCents, CostAmountCents, ProfitAmountCents, ReturnAmountCents, Operator, Status, Remark, CreateDate)
                    VALUES (@id,@no,@amt,@disc,@recv,@cost,@profit,@ret,@op,'正常',@rm,@cd)";
                cmd.Parameters.AddWithValue("@id", s.SaleId);
                cmd.Parameters.AddWithValue("@no", s.SaleNo);
                cmd.Parameters.AddWithValue("@amt", 0);
                cmd.Parameters.AddWithValue("@disc", s.DiscountAmountCents);
                cmd.Parameters.AddWithValue("@recv", s.ReceivedAmountCents);
                cmd.Parameters.AddWithValue("@cost", 0);
                cmd.Parameters.AddWithValue("@profit", 0);
                cmd.Parameters.AddWithValue("@ret", 0);
                cmd.Parameters.AddWithValue("@op", (object?)s.Operator ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@rm", (object?)s.Remark ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cd", s.CreateDate.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();
            }

            // 逐条：写明细 + 扣库存 + 累计金额/成本
            foreach (var item in s.Items)
            {
                item.SaleItemId = _db.NewId("SIT", conn, tx);
                item.SaleId = s.SaleId;
                item.AmountCents = item.SalePriceCents.Amount(item.Qty);
                item.CostPriceCents = GetCurrentCostInTx(conn, tx, item.ProductId);
                s.TotalAmountCents += item.AmountCents;
                s.CostAmountCents += item.CostPriceCents.Amount(item.BaseQty);

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        INSERT INTO SaleItem (SaleItemId, SaleId, ProductId, Barcode, ProductName, SalePriceCents, CostPriceCents, Unit, Qty, BaseQty, AmountCents, ReturnedQty)
                        VALUES (@id,@sid,@prid,@b,@name,@sp,@cp,@unit,@qty,@bq,@amt,0)";
                    cmd.Parameters.AddWithValue("@id", item.SaleItemId);
                    cmd.Parameters.AddWithValue("@sid", s.SaleId);
                    cmd.Parameters.AddWithValue("@prid", item.ProductId);
                    cmd.Parameters.AddWithValue("@b", (object?)item.Barcode ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", (object?)item.ProductName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@sp", item.SalePriceCents);
                    cmd.Parameters.AddWithValue("@cp", item.CostPriceCents);
                    cmd.Parameters.AddWithValue("@unit", (object?)item.Unit ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@qty", item.Qty);
                    cmd.Parameters.AddWithValue("@bq", item.BaseQty);
                    cmd.Parameters.AddWithValue("@amt", item.AmountCents);
                    cmd.ExecuteNonQuery();
                }

                // 扣库存
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "UPDATE Product SET CurrentStock = CurrentStock - @q WHERE ProductId=@id";
                    cmd.Parameters.AddWithValue("@q", item.BaseQty);
                    cmd.Parameters.AddWithValue("@id", item.ProductId);
                    cmd.ExecuteNonQuery();
                }
            }

            // 利润 = 应收 - 折扣 - 成本
            s.ProfitAmountCents = s.TotalAmountCents - s.DiscountAmountCents - s.CostAmountCents;
            s.ReceivedAmountCents = s.ReceivedAmountCents == 0 ? s.TotalAmountCents - s.DiscountAmountCents : s.ReceivedAmountCents;

            // 回写单头
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    UPDATE Sale SET TotalAmountCents=@amt, CostAmountCents=@cost, ProfitAmountCents=@profit, ReceivedAmountCents=@recv
                    WHERE SaleId=@id";
                cmd.Parameters.AddWithValue("@amt", s.TotalAmountCents);
                cmd.Parameters.AddWithValue("@cost", s.CostAmountCents);
                cmd.Parameters.AddWithValue("@profit", s.ProfitAmountCents);
                cmd.Parameters.AddWithValue("@recv", s.ReceivedAmountCents);
                cmd.Parameters.AddWithValue("@id", s.SaleId);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return (true, "", s.SaleId);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return (false, ex.Message, null);
        }
    }

    /// <summary>当前移动加权成本（分）：从所有正常进货明细累计计算</summary>
    private long GetCurrentCostInTx(SqliteConnection conn, SqliteTransaction? tx, string productId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            SELECT COALESCE(SUM(pi.BaseQty),0) AS total_qty,
                   COALESCE(SUM(pi.BaseQty * pi.PurchasePriceCents),0) AS total_cost
            FROM PurchaseItem pi
            JOIN Purchase p ON pi.PurchaseId=p.PurchaseId
            WHERE pi.ProductId=@id AND p.Status='正常'";
        cmd.Parameters.AddWithValue("@id", productId);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return 0;
        var totalQty = Convert.ToDecimal(rd["total_qty"] ?? 0);
        var totalCost = Convert.ToDecimal(rd["total_cost"] ?? 0);
        if (totalQty <= 0) return 0;
        return (long)Math.Round(totalCost / totalQty, 0, MidpointRounding.AwayFromZero);
    }

    // ========== 退货（B1 按行部分退货 / B2 引用原行成本）==========

    /// <summary>
    /// 按行退货：
    /// - 退货明细关联原销售行（RefSaleItemId），成本取自原行 CostPriceCents（B2，防成本已变）
    /// - 库存回补（原行 BaseQty）
    /// - 原 SaleItem.ReturnedQty += 退货数；Sale.ReturnAmountCents += 退金额
    /// - 全部退完 → 原 Sale.Status = '已作废'；部分退 → 保持 '正常'
    /// </summary>
    public (bool ok, string error, string? returnId) DoReturn(SaleReturn r)
    {
        var blocked = LicenseService.WriteBlocked();
        if (blocked != null) return (false, blocked, null);
        if (r.Items.Count == 0) return (false, "退货单没有明细", null);

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();

        try
        {
            r.SaleReturnId = _db.NewId("RTR", conn, tx);
            r.CreateDate = DateTime.Now;
            r.ReturnNo = $"RT{DateTime.Now:yyyyMMdd}{r.SaleReturnId[^4..]}";
            r.TotalAmountCents = 0;

            // 写退货单头
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO SaleReturn (SaleReturnId, SaleId, ReturnNo, TotalAmountCents, Operator, Remark, CreateDate)
                    VALUES (@id,@sid,@no,@amt,@op,@rm,@cd)";
                cmd.Parameters.AddWithValue("@id", r.SaleReturnId);
                cmd.Parameters.AddWithValue("@sid", (object?)r.SaleId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@no", r.ReturnNo);
                cmd.Parameters.AddWithValue("@amt", 0);
                cmd.Parameters.AddWithValue("@op", (object?)r.Operator ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@rm", (object?)r.Remark ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cd", r.CreateDate.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();
            }

            // 逐条退货
            foreach (var item in r.Items)
            {
                // 校验退货数量 ≤ 已售 − 已退（如果关联了原销售行）
                if (string.IsNullOrEmpty(item.RefSaleItemId))
                {
                    tx.Rollback();
                    return (false, "退货必须关联原销售明细行", null);
                }

                // 从原销售行读取：可退数量 + 原行成本（B2：成本必须取自原行，不信任前端传参）
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT ProductId, Qty, ReturnedQty, CostPriceCents FROM SaleItem WHERE SaleItemId=@id";
                    cmd.Parameters.AddWithValue("@id", item.RefSaleItemId);
                    using var rd = cmd.ExecuteReader();
                    if (rd.Read())
                    {
                        var sold = Convert.ToDecimal(rd["Qty"]);
                        var returned = Convert.ToDecimal(rd["ReturnedQty"] ?? 0);
                        if (item.Qty > sold - returned)
                        {
                            tx.Rollback();
                            return (false, $"退货数量超过可退数量：{item.ProductName} 已售{sold} 已退{returned}", null);
                        }
                        item.ProductId = rd["ProductId"]?.ToString() ?? item.ProductId;
                        item.CostPriceCents = Convert.ToInt64(rd["CostPriceCents"] ?? 0);  // B2 强制取原行成本
                    }
                    else
                    {
                        tx.Rollback();
                        return (false, "原销售明细不存在", null);
                    }
                }

                item.SaleReturnItemId = _db.NewId("RIT", conn, tx);
                item.SaleReturnId = r.SaleReturnId;
                item.AmountCents = item.ReturnPriceCents.Amount(item.Qty);
                r.TotalAmountCents += item.AmountCents;

                // 写退货明细（B2：RefSaleItemId + CostPriceCents 取自原行）
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        INSERT INTO SaleReturnItem (SaleReturnItemId, SaleReturnId, RefSaleItemId, ProductId, Barcode, ProductName, ReturnPriceCents, CostPriceCents, Qty, BaseQty, AmountCents)
                        VALUES (@id,@rid,@ref,@prid,@b,@name,@rp,@cp,@qty,@bq,@amt)";
                    cmd.Parameters.AddWithValue("@id", item.SaleReturnItemId);
                    cmd.Parameters.AddWithValue("@rid", r.SaleReturnId);
                    cmd.Parameters.AddWithValue("@ref", (object?)item.RefSaleItemId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@prid", item.ProductId);
                    cmd.Parameters.AddWithValue("@b", (object?)item.Barcode ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", (object?)item.ProductName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@rp", item.ReturnPriceCents);
                    cmd.Parameters.AddWithValue("@cp", item.CostPriceCents);
                    cmd.Parameters.AddWithValue("@qty", item.Qty);
                    cmd.Parameters.AddWithValue("@bq", item.BaseQty);
                    cmd.Parameters.AddWithValue("@amt", item.AmountCents);
                    cmd.ExecuteNonQuery();
                }

                // 库存回补
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "UPDATE Product SET CurrentStock = CurrentStock + @q WHERE ProductId=@id";
                    cmd.Parameters.AddWithValue("@q", item.BaseQty);
                    cmd.Parameters.AddWithValue("@id", item.ProductId);
                    cmd.ExecuteNonQuery();
                }

                // 更新原销售行已退数量
                if (!string.IsNullOrEmpty(item.RefSaleItemId))
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "UPDATE SaleItem SET ReturnedQty = ReturnedQty + @q WHERE SaleItemId=@id";
                        cmd.Parameters.AddWithValue("@q", item.Qty);
                        cmd.Parameters.AddWithValue("@id", item.RefSaleItemId);
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            // 回写退货单金额
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE SaleReturn SET TotalAmountCents=@amt WHERE SaleReturnId=@id";
                cmd.Parameters.AddWithValue("@amt", r.TotalAmountCents);
                cmd.Parameters.AddWithValue("@id", r.SaleReturnId);
                cmd.ExecuteNonQuery();
            }

            // 更新原销售单：已退金额 + 是否全部退完
            if (!string.IsNullOrEmpty(r.SaleId))
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        UPDATE Sale SET ReturnAmountCents = ReturnAmountCents + @amt WHERE SaleId=@id";
                    cmd.Parameters.AddWithValue("@amt", r.TotalAmountCents);
                    cmd.Parameters.AddWithValue("@id", r.SaleId);
                    cmd.ExecuteNonQuery();
                }
                // 判断是否整单退完：所有行 ReturnedQty == Qty
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        SELECT COUNT(*) FROM SaleItem
                        WHERE SaleId=@id AND ReturnedQty < Qty";
                    cmd.Parameters.AddWithValue("@id", r.SaleId);
                    var notFull = Convert.ToInt32(cmd.ExecuteScalar());
                    if (notFull == 0)
                    {
                        using var up = conn.CreateCommand();
                        up.Transaction = tx;
                        up.CommandText = "UPDATE Sale SET Status='已作废' WHERE SaleId=@id";
                        up.Parameters.AddWithValue("@id", r.SaleId);
                        up.ExecuteNonQuery();
                    }
                }
            }

            tx.Commit();
            return (true, "", r.SaleReturnId);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return (false, ex.Message, null);
        }
    }

    // ========== 库存调整 ==========

    /// <summary>库存调整（盘盈/盘亏/手动），事务内写单 + 更新库存</summary>
    public (bool ok, string error, string? adjustId) DoStockAdjust(StockAdjust a)
    {
        var blocked = LicenseService.WriteBlocked();
        if (blocked != null) return (false, blocked, null);
        if (a.Items.Count == 0) return (false, "调整单没有明细", null);
        if (string.IsNullOrWhiteSpace(a.Remark)) return (false, "调整原因必填", null);

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();

        try
        {
            a.StockAdjustId = _db.NewId("ADJ", conn, tx);
            a.CreateDate = DateTime.Now;
            a.AdjustNo = $"AD{DateTime.Now:yyyyMMdd}{a.StockAdjustId[^4..]}";
            a.TotalQty = 0;

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO StockAdjust (StockAdjustId, AdjustNo, AdjustType, TotalQty, Operator, Remark, CreateDate)
                    VALUES (@id,@no,@type,@qty,@op,@rm,@cd)";
                cmd.Parameters.AddWithValue("@id", a.StockAdjustId);
                cmd.Parameters.AddWithValue("@no", a.AdjustNo);
                cmd.Parameters.AddWithValue("@type", (object?)a.AdjustType ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@qty", 0);
                cmd.Parameters.AddWithValue("@op", (object?)a.Operator ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@rm", a.Remark);
                cmd.Parameters.AddWithValue("@cd", a.CreateDate.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();
            }

            foreach (var item in a.Items)
            {
                // 读当前库存
                decimal before;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT CurrentStock FROM Product WHERE ProductId=@id";
                    cmd.Parameters.AddWithValue("@id", item.ProductId);
                    before = Convert.ToDecimal(cmd.ExecuteScalar() ?? 0);
                }
                var after = before + item.AdjustQty;
                if (after < 0)
                {
                    tx.Rollback();
                    return (false, $"调整后库存为负：{item.ProductName} 现有{before}，调整{item.AdjustQty}", null);
                }
                item.BeforeStock = before;
                item.AfterStock = after;
                a.TotalQty += item.AdjustQty;

                item.StockAdjustItemId = _db.NewId("ADI", conn, tx);
                item.StockAdjustId = a.StockAdjustId;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        INSERT INTO StockAdjustItem (StockAdjustItemId, StockAdjustId, ProductId, Barcode, ProductName, Unit, AdjustQty, BeforeStock, AfterStock)
                        VALUES (@id,@aid,@prid,@b,@name,@unit,@aq,@bf,@af)";
                    cmd.Parameters.AddWithValue("@id", item.StockAdjustItemId);
                    cmd.Parameters.AddWithValue("@aid", a.StockAdjustId);
                    cmd.Parameters.AddWithValue("@prid", item.ProductId);
                    cmd.Parameters.AddWithValue("@b", (object?)item.Barcode ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", (object?)item.ProductName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@unit", (object?)item.Unit ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@aq", item.AdjustQty);
                    cmd.Parameters.AddWithValue("@bf", before);
                    cmd.Parameters.AddWithValue("@af", after);
                    cmd.ExecuteNonQuery();
                }

                // 更新库存
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "UPDATE Product SET CurrentStock=@s WHERE ProductId=@id";
                    cmd.Parameters.AddWithValue("@s", after);
                    cmd.Parameters.AddWithValue("@id", item.ProductId);
                    cmd.ExecuteNonQuery();
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE StockAdjust SET TotalQty=@q WHERE StockAdjustId=@id";
                cmd.Parameters.AddWithValue("@q", a.TotalQty);
                cmd.Parameters.AddWithValue("@id", a.StockAdjustId);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return (true, "", a.StockAdjustId);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return (false, ex.Message, null);
        }
    }

    // ========== 供应商（B4）==========

    /// <summary>新增/更新供应商</summary>
    public (bool ok, string error) SaveSupplier(Supplier s)
    {
        var blocked = LicenseService.WriteBlocked();
        if (blocked != null) return (false, blocked);
        using var conn = _db.Open();
        if (string.IsNullOrEmpty(s.SupplierId))
        {
            s.SupplierId = _db.NewId("SUP");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Supplier (SupplierId, SupplierName, Contact, Phone, Address, Remark, IsActive)
                VALUES (@id,@name,@con,@tel,@addr,@rm,@act)";
            AddParams(cmd);
            cmd.ExecuteNonQuery();
        }
        else
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Supplier SET SupplierName=@name, Contact=@con, Phone=@tel, Address=@addr, Remark=@rm, IsActive=@act
                WHERE SupplierId=@id";
            AddParams(cmd);
            cmd.ExecuteNonQuery();
        }
        return (true, "");

        void AddParams(SqliteCommand cmd)
        {
            cmd.Parameters.AddWithValue("@id", s.SupplierId);
            cmd.Parameters.AddWithValue("@name", s.SupplierName);
            cmd.Parameters.AddWithValue("@con", (object?)s.Contact ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tel", (object?)s.Phone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@addr", (object?)s.Address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rm", (object?)s.Remark ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@act", s.IsActive ? 1 : 0);
        }
    }

    /// <summary>查询全部启用供应商</summary>
    public List<Supplier> QuerySuppliers(bool onlyActive = true)
    {
        var list = new List<Supplier>();
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = onlyActive
            ? "SELECT * FROM Supplier WHERE IsActive=1 ORDER BY SupplierName"
            : "SELECT * FROM Supplier ORDER BY SupplierName";
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            list.Add(new Supplier
            {
                SupplierId = rd["SupplierId"].ToString() ?? "",
                SupplierName = rd["SupplierName"].ToString() ?? "",
                Contact = rd["Contact"]?.ToString(),
                Phone = rd["Phone"]?.ToString(),
                Address = rd["Address"]?.ToString(),
                Remark = rd["Remark"]?.ToString(),
                IsActive = Convert.ToInt32(rd["IsActive"] ?? 1) == 1
            });
        }
        return list;
    }

    // ========== 报表 / 查询 ==========

    /// <summary>销售汇总（按日/月/自定义时间段）：笔数 / 销售额 / 折扣 / 成本 / 利润</summary>
    public SaleSummary QuerySaleSummary(DateTime from, DateTime to)
    {
        using var conn = _db.Open();

        // 第一步：正常销售单汇总
        long sales = 0, discount = 0, cost = 0, profit = 0;
        int orders = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    COUNT(*) AS Orders,
                    COALESCE(SUM(TotalAmountCents),0) AS Sales,
                    COALESCE(SUM(DiscountAmountCents),0) AS Discount,
                    COALESCE(SUM(CostAmountCents),0) AS Cost,
                    COALESCE(SUM(ProfitAmountCents),0) AS Profit
                FROM Sale
                WHERE Status='正常'
                  AND CreateDate >= @from AND CreateDate <= @to";
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));
            using var rd = cmd.ExecuteReader();
            rd.Read();
            orders = Convert.ToInt32(rd["Orders"]);
            sales = Convert.ToInt64(rd["Sales"]);
            discount = Convert.ToInt64(rd["Discount"]);
            cost = Convert.ToInt64(rd["Cost"]);
            profit = Convert.ToInt64(rd["Profit"]);
        }

        // 第二步：这些正常单的退货冲销（退回金额 + 退回成本，利润 = 退回金额 - 退回成本）
        long returnAmt = 0, returnCost = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    COALESCE(SUM(r.AmountCents),0) AS RetAmt,
                    COALESCE(SUM(r.CostPriceCents * r.Qty),0) AS RetCost
                FROM SaleReturnItem r
                JOIN SaleReturn sr ON r.SaleReturnId=sr.SaleReturnId
                JOIN Sale s ON sr.SaleId=s.SaleId
                WHERE s.Status='正常' AND sr.CreateDate >= @from AND sr.CreateDate <= @to";
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));
            using var rd = cmd.ExecuteReader();
            rd.Read();
            returnAmt = Convert.ToInt64(rd["RetAmt"]);
            returnCost = Convert.ToInt64(rd["RetCost"]);
        }

        // 净利润 = 销售利润 − 退货利润（退货利润 = 退回金额 − 退回成本）
        // 销售额 = 正常销售额 − 退回金额
        return new SaleSummary
        {
            Orders = orders,
            SalesAmountCents = sales - returnAmt,
            DiscountAmountCents = discount,
            CostAmountCents = cost - returnCost,
            ProfitAmountCents = profit - (returnAmt - returnCost)
        };
    }

    /// <summary>销售明细列表（按日期倒序）</summary>
    public List<Sale> QuerySaleList(DateTime from, DateTime to, string? keyword = null)
    {
        var list = new List<Sale>();
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT * FROM Sale
            WHERE CreateDate >= @from AND CreateDate <= @to
              AND (@k IS NULL OR SaleNo LIKE @k OR Remark LIKE @k OR Operator LIKE @k)
            ORDER BY CreateDate DESC";
        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@k", (object?)(string.IsNullOrEmpty(keyword) ? null : $"%{keyword}%") ?? DBNull.Value);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            list.Add(new Sale
            {
                SaleId = rd["SaleId"].ToString() ?? "",
                SaleNo = rd["SaleNo"].ToString() ?? "",
                TotalAmountCents = Convert.ToInt64(rd["TotalAmountCents"] ?? 0),
                DiscountAmountCents = Convert.ToInt64(rd["DiscountAmountCents"] ?? 0),
                CostAmountCents = Convert.ToInt64(rd["CostAmountCents"] ?? 0),
                ProfitAmountCents = Convert.ToInt64(rd["ProfitAmountCents"] ?? 0),
                ReturnAmountCents = Convert.ToInt64(rd["ReturnAmountCents"] ?? 0),
                Operator = rd["Operator"]?.ToString(),
                Status = rd["Status"]?.ToString() ?? "正常",
                CreateDate = DateTime.Parse(rd["CreateDate"]?.ToString() ?? ""),
            });
        }
        return list;
    }

    /// <summary>单笔销售完整（含明细）</summary>
    public Sale? GetSale(string saleId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Sale WHERE SaleId=@id";
        cmd.Parameters.AddWithValue("@id", saleId);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        var sale = new Sale
        {
            SaleId = saleId,
            SaleNo = rd["SaleNo"].ToString() ?? "",
            TotalAmountCents = Convert.ToInt64(rd["TotalAmountCents"] ?? 0),
            DiscountAmountCents = Convert.ToInt64(rd["DiscountAmountCents"] ?? 0),
            ReceivedAmountCents = Convert.ToInt64(rd["ReceivedAmountCents"] ?? 0),
            CostAmountCents = Convert.ToInt64(rd["CostAmountCents"] ?? 0),
            ProfitAmountCents = Convert.ToInt64(rd["ProfitAmountCents"] ?? 0),
            ReturnAmountCents = Convert.ToInt64(rd["ReturnAmountCents"] ?? 0),
            Operator = rd["Operator"]?.ToString(),
            Status = rd["Status"]?.ToString() ?? "正常",
            CreateDate = DateTime.Parse(rd["CreateDate"]?.ToString() ?? ""),
        };
        using (var cmd2 = conn.CreateCommand())
        {
            cmd2.CommandText = "SELECT * FROM SaleItem WHERE SaleId=@id";
            cmd2.Parameters.AddWithValue("@id", saleId);
            using var rd2 = cmd2.ExecuteReader();
            while (rd2.Read())
            {
                sale.Items.Add(new SaleItem
                {
                    SaleItemId = rd2["SaleItemId"].ToString() ?? "",
                    ProductId = rd2["ProductId"].ToString() ?? "",
                    Barcode = rd2["Barcode"]?.ToString(),
                    ProductName = rd2["ProductName"]?.ToString(),
                    SalePriceCents = Convert.ToInt64(rd2["SalePriceCents"] ?? 0),
                    CostPriceCents = Convert.ToInt64(rd2["CostPriceCents"] ?? 0),
                    Qty = Convert.ToDecimal(rd2["Qty"] ?? 0),
                    BaseQty = Convert.ToDecimal(rd2["BaseQty"] ?? 0),
                    AmountCents = Convert.ToInt64(rd2["AmountCents"] ?? 0),
                    ReturnedQty = Convert.ToDecimal(rd2["ReturnedQty"] ?? 0)
                });
            }
        }
        return sale;
    }

    /// <summary>库存台账：期初+进−销+退±调整=期末（按商品 F 组第7条）</summary>
    public List<StockLedgerRow> QueryStockLedger(DateTime from, DateTime to)
    {
        var rows = new List<StockLedgerRow>();
        using var conn = _db.Open();

        // 期末库存 = 当前 Product.CurrentStock
        // 期初 = 期末 − 区间内净变化
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.ProductId, p.ProductName, p.Barcode, p.CurrentStock,
              (SELECT COALESCE(SUM(pi.BaseQty),0) FROM PurchaseItem pi
                JOIN Purchase pu ON pi.PurchaseId=pu.PurchaseId
                WHERE pi.ProductId=p.ProductId AND pu.Status='正常' AND pu.CreateDate>=@from AND pu.CreateDate<=@to) AS PurchaseIn,
              (SELECT COALESCE(SUM(si.BaseQty),0) FROM SaleItem si
                JOIN Sale s ON si.SaleId=s.SaleId
                WHERE si.ProductId=p.ProductId AND s.Status='正常' AND s.CreateDate>=@from AND s.CreateDate<=@to) AS SaleOut,
              (SELECT COALESCE(SUM(r.BaseQty),0) FROM SaleReturnItem r
                JOIN SaleReturn sr ON r.SaleReturnId=sr.SaleReturnId
                WHERE r.ProductId=p.ProductId AND sr.CreateDate>=@from AND sr.CreateDate<=@to) AS ReturnIn,
              (SELECT COALESCE(SUM(a.AdjustQty),0) FROM StockAdjustItem a
                JOIN StockAdjust ad ON a.StockAdjustId=ad.StockAdjustId
                WHERE a.ProductId=p.ProductId AND ad.CreateDate>=@from AND ad.CreateDate<=@to) AS Adjust
            FROM Product p
            WHERE p.IsActive=1
            ORDER BY p.ProductName";
        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var cur = Convert.ToDecimal(rd["CurrentStock"] ?? 0);
            var pi = Convert.ToDecimal(rd["PurchaseIn"] ?? 0);
            var so = Convert.ToDecimal(rd["SaleOut"] ?? 0);
            var ri = Convert.ToDecimal(rd["ReturnIn"] ?? 0);
            var ad = Convert.ToDecimal(rd["Adjust"] ?? 0);
            var begin = cur - pi + so - ri - ad;   // 期末 = 期初+进−销+退±调 → 期初 = 期末−进+销−退∓调
            rows.Add(new StockLedgerRow
            {
                ProductName = rd["ProductName"]?.ToString() ?? "",
                Barcode = rd["Barcode"]?.ToString() ?? "",
                BeginStock = begin,
                PurchaseIn = pi,
                SaleOut = so,
                ReturnIn = ri,
                Adjust = ad,
                EndStock = cur
            });
        }
        return rows;
    }

    /// <summary>商品利润排行（区间内按利润降序）</summary>
    public List<ProductProfitRow> QueryProductProfitRanking(DateTime from, DateTime to)
    {
        var rows = new List<ProductProfitRow>();
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT si.ProductId, MAX(si.ProductName) AS ProductName,
                   COALESCE(SUM(si.Qty),0) AS Qty,
                   COALESCE(SUM(si.AmountCents),0) AS Sales,
                   COALESCE(SUM(si.CostPriceCents * si.BaseQty),0) AS Cost,
                   COALESCE(SUM(si.AmountCents - si.CostPriceCents * si.BaseQty),0) AS Profit
            FROM SaleItem si
            JOIN Sale s ON si.SaleId=s.SaleId
            WHERE s.Status='正常' AND s.CreateDate>=@from AND s.CreateDate<=@to
            GROUP BY si.ProductId
            ORDER BY Profit DESC";
        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new ProductProfitRow
            {
                ProductName = rd["ProductName"]?.ToString() ?? "",
                Qty = Convert.ToDecimal(rd["Qty"] ?? 0),
                SalesAmountCents = Convert.ToInt64(rd["Sales"] ?? 0),
                CostAmountCents = Convert.ToInt64(rd["Cost"] ?? 0),
                ProfitAmountCents = Convert.ToInt64(rd["Profit"] ?? 0)
            });
        }
        return rows;
    }
}

/// <summary>销售汇总</summary>
public class SaleSummary
{
    public int Orders { get; set; }
    public long SalesAmountCents { get; set; }
    public long DiscountAmountCents { get; set; }
    public long CostAmountCents { get; set; }
    public long ProfitAmountCents { get; set; }
}

/// <summary>库存台账行</summary>
public class StockLedgerRow
{
    public string ProductName { get; set; } = "";
    public string Barcode { get; set; } = "";
    public decimal BeginStock { get; set; }
    public decimal PurchaseIn { get; set; }
    public decimal SaleOut { get; set; }
    public decimal ReturnIn { get; set; }
    public decimal Adjust { get; set; }
    public decimal EndStock { get; set; }
}

/// <summary>商品利润排行行</summary>
public class ProductProfitRow
{
    public string ProductName { get; set; } = "";
    public decimal Qty { get; set; }
    public long SalesAmountCents { get; set; }
    public long CostAmountCents { get; set; }
    public long ProfitAmountCents { get; set; }
}
