namespace StockManager.Data;

/// <summary>
/// 建表脚本（A2：SQLite 原生类型；A1：金额存分 INTEGER）
/// 时间统一 TEXT 格式 yyyy-MM-dd HH:mm:ss，写库在 C# 层生成，SQL 兜底用 localtime
/// </summary>
public static class DatabaseSchema
{
    /// <summary>当前版本号</summary>
    public const int CurrentVersion = 1;

    /// <summary>建表 + 初始数据（幂等：IF NOT EXISTS）</summary>
    public const string CreateAll = @"
-- 商品分类
CREATE TABLE IF NOT EXISTS Category (
  CategoryId   TEXT PRIMARY KEY,
  CategoryName TEXT NOT NULL,
  ParentId     TEXT NULL,
  SortOrder    INTEGER DEFAULT 0,
  CreateDate   TEXT DEFAULT (datetime('now','localtime'))
);

-- 商品
CREATE TABLE IF NOT EXISTS Product (
  ProductId    TEXT PRIMARY KEY,
  CategoryId   TEXT NULL,
  ProductCode  TEXT,
  Barcode      TEXT,
  ProductName  TEXT NOT NULL,
  Spec         TEXT,
  BaseUnit     TEXT,
  PackUnit     TEXT,
  PackQty      REAL,
  PurchasePriceCents INTEGER,
  SalePriceCents     INTEGER,
  LowStockQty  REAL DEFAULT 0,
  CurrentStock REAL DEFAULT 0,
  Remark       TEXT,
  SearchCode   TEXT,
  IsSample     INTEGER DEFAULT 0,
  IsActive     INTEGER DEFAULT 1,
  CreateDate   TEXT DEFAULT (datetime('now','localtime')),
  UpdateDate   TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS UX_Product_Barcode ON Product(Barcode);
CREATE INDEX IF NOT EXISTS IX_Product_Name ON Product(ProductName);
CREATE INDEX IF NOT EXISTS IX_Product_SearchCode ON Product(SearchCode);

-- 商品附加条码
CREATE TABLE IF NOT EXISTS ProductBarcode (
  ProductBarcodeId TEXT PRIMARY KEY,
  ProductId   TEXT NOT NULL,
  Barcode     TEXT NOT NULL,
  CreateDate  TEXT DEFAULT (datetime('now','localtime'))
);
CREATE UNIQUE INDEX IF NOT EXISTS UX_ProductBarcode_Code ON ProductBarcode(Barcode);

-- 供应商
CREATE TABLE IF NOT EXISTS Supplier (
  SupplierId   TEXT PRIMARY KEY,
  SupplierName TEXT NOT NULL,
  Contact      TEXT,
  Phone        TEXT,
  Address      TEXT,
  Remark       TEXT,
  IsActive     INTEGER DEFAULT 1
);

-- 进货单
CREATE TABLE IF NOT EXISTS Purchase (
  PurchaseId    TEXT PRIMARY KEY,
  PurchaseNo    TEXT NOT NULL,
  SupplierId    TEXT NULL,
  SupplierName  TEXT,
  TotalAmountCents INTEGER DEFAULT 0,
  ItemCount     INTEGER DEFAULT 0,
  Operator      TEXT,
  Status        TEXT DEFAULT '正常',
  Remark        TEXT,
  CreateDate    TEXT DEFAULT (datetime('now','localtime'))
);
CREATE TABLE IF NOT EXISTS PurchaseItem (
  PurchaseItemId TEXT PRIMARY KEY,
  PurchaseId    TEXT NOT NULL,
  ProductId     TEXT NOT NULL,
  Barcode       TEXT,
  ProductName   TEXT,
  PurchasePriceCents INTEGER,
  Unit          TEXT,
  Qty           REAL,
  BaseQty       REAL,
  AmountCents   INTEGER
);
CREATE INDEX IF NOT EXISTS IX_PurchaseItem_Purchase ON PurchaseItem(PurchaseId);

-- 销售单
CREATE TABLE IF NOT EXISTS Sale (
  SaleId        TEXT PRIMARY KEY,
  SaleNo        TEXT NOT NULL,
  TotalAmountCents INTEGER DEFAULT 0,
  DiscountAmountCents INTEGER DEFAULT 0,
  ReceivedAmountCents INTEGER,
  CostAmountCents INTEGER DEFAULT 0,
  ProfitAmountCents INTEGER DEFAULT 0,
  ReturnAmountCents INTEGER DEFAULT 0,
  Operator      TEXT,
  Status        TEXT DEFAULT '正常',
  Remark        TEXT,
  CreateDate    TEXT DEFAULT (datetime('now','localtime'))
);
CREATE TABLE IF NOT EXISTS SaleItem (
  SaleItemId    TEXT PRIMARY KEY,
  SaleId        TEXT NOT NULL,
  ProductId     TEXT NOT NULL,
  Barcode       TEXT,
  ProductName   TEXT,
  SalePriceCents INTEGER,
  CostPriceCents INTEGER,
  Unit          TEXT,
  Qty           REAL,
  BaseQty       REAL,
  AmountCents   INTEGER,
  ReturnedQty   REAL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS IX_SaleItem_Sale ON SaleItem(SaleId);

-- 退货单（B1 部分退货 / B2 引用原行成本）
CREATE TABLE IF NOT EXISTS SaleReturn (
  SaleReturnId  TEXT PRIMARY KEY,
  SaleId        TEXT NULL,
  ReturnNo      TEXT NOT NULL,
  TotalAmountCents INTEGER DEFAULT 0,
  Operator      TEXT,
  Remark        TEXT,
  CreateDate    TEXT DEFAULT (datetime('now','localtime'))
);
CREATE TABLE IF NOT EXISTS SaleReturnItem (
  SaleReturnItemId TEXT PRIMARY KEY,
  SaleReturnId TEXT NOT NULL,
  RefSaleItemId TEXT,
  ProductId    TEXT NOT NULL,
  Barcode      TEXT,
  ProductName  TEXT,
  ReturnPriceCents INTEGER,
  CostPriceCents INTEGER,
  Qty          REAL,
  BaseQty      REAL,                    -- 折算基本单位
  AmountCents  INTEGER
);
CREATE INDEX IF NOT EXISTS IX_SaleReturnItem_Return ON SaleReturnItem(SaleReturnId);

-- 库存调整单
CREATE TABLE IF NOT EXISTS StockAdjust (
  StockAdjustId TEXT PRIMARY KEY,
  AdjustNo      TEXT NOT NULL,
  AdjustType    TEXT,
  TotalQty      REAL DEFAULT 0,
  Operator      TEXT,
  Remark        TEXT,
  CreateDate    TEXT DEFAULT (datetime('now','localtime'))
);
CREATE TABLE IF NOT EXISTS StockAdjustItem (
  StockAdjustItemId TEXT PRIMARY KEY,
  StockAdjustId TEXT NOT NULL,
  ProductId     TEXT NOT NULL,
  Barcode       TEXT,
  ProductName   TEXT,
  Unit          TEXT,
  AdjustQty     REAL,
  BeforeStock   REAL,
  AfterStock    REAL
);

-- 用户
CREATE TABLE IF NOT EXISTS SysUser (
  UserId     TEXT PRIMARY KEY,
  UserName   TEXT NOT NULL UNIQUE,
  PasswordHash TEXT,
  DisplayName TEXT,
  Role       TEXT DEFAULT '店员',
  CanChangePrice INTEGER DEFAULT 0,
  IsActive   INTEGER DEFAULT 1
);

-- 系统参数 / 备份记录 / 数据库版本 / 操作日志
CREATE TABLE IF NOT EXISTS SysConfig (
  ConfigKey   TEXT PRIMARY KEY,
  ConfigValue TEXT
);
CREATE TABLE IF NOT EXISTS BackupLog (
  BackupLogId  TEXT PRIMARY KEY,
  BackupPath   TEXT,
  FileSize     INTEGER,
  BackupType   TEXT,
  CreateDate   TEXT DEFAULT (datetime('now','localtime'))
);
CREATE TABLE IF NOT EXISTS DBVersion (
  VersionId    TEXT PRIMARY KEY,
  VersionNo    INTEGER NOT NULL,
  UpgradeSql   TEXT,
  UpgradeDate  TEXT DEFAULT (datetime('now','localtime'))
);
CREATE TABLE IF NOT EXISTS OperLog (
  OperLogId    TEXT PRIMARY KEY,
  OperType     TEXT,
  Detail       TEXT,
  Operator     TEXT,
  CreateDate   TEXT DEFAULT (datetime('now','localtime'))
);
";
}
