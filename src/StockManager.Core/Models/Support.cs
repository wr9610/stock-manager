namespace StockManager.Core.Models;

/// <summary>商品分类（支持二级）</summary>
public class Category
{
    public string CategoryId { get; set; } = "";       // 前缀 CAT
    public string CategoryName { get; set; } = "";
    public string? ParentId { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreateDate { get; set; }
}

/// <summary>商品条码（一商品多码）</summary>
public class ProductBarcode
{
    public string ProductBarcodeId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string Barcode { get; set; } = "";         // 附加条码（唯一）
    public DateTime CreateDate { get; set; }
}

/// <summary>供应商</summary>
public class Supplier
{
    public string SupplierId { get; set; } = "";       // 前缀 SUP
    public string SupplierName { get; set; } = "";
    public string? Contact { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Remark { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>本地用户</summary>
public class SysUser
{
    public string UserId { get; set; } = "";           // 前缀 USR
    public string UserName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? DisplayName { get; set; }
    public string Role { get; set; } = "店员";         // 老板/店员
    public bool CanChangePrice { get; set; }           // 收银能否临时改价/抹零
    public bool IsActive { get; set; } = true;
}

/// <summary>系统参数</summary>
public class SysConfig
{
    public string ConfigKey { get; set; } = "";
    public string? ConfigValue { get; set; }
}

/// <summary>备份记录</summary>
public class BackupLog
{
    public string BackupLogId { get; set; } = "";
    public string BackupPath { get; set; } = "";
    public long FileSize { get; set; }
    public string BackupType { get; set; } = "";       // 自动/手动
    public DateTime CreateDate { get; set; }
}

/// <summary>数据库版本（升级用）</summary>
public class DBVersion
{
    public string VersionId { get; set; } = "";
    public int VersionNo { get; set; }
    public string UpgradeSql { get; set; } = "";
    public DateTime UpgradeDate { get; set; }
}

/// <summary>操作日志</summary>
public class OperLog
{
    public string OperLogId { get; set; } = "";
    public string OperType { get; set; } = "";
    public string? Detail { get; set; }
    public string? Operator { get; set; }
    public DateTime CreateDate { get; set; }
}
