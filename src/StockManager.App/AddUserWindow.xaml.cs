using System.Windows;
using StockManager.Data;

namespace StockManager.App;

public partial class AddUserWindow : Window
{
    public AddUserWindow()
    {
        InitializeComponent();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var username = UserNameBox.Text.Trim();
        var pwd = PwdBox.Text;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(pwd))
        {
            ErrorText.Text = "账号和密码不能为空";
            return;
        }
        if (pwd.Length < 6)
        {
            ErrorText.Text = "密码至少 6 位";
            return;
        }

        using var conn = App.Db.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM SysUser WHERE UserName=@u";
            cmd.Parameters.AddWithValue("@u", username);
            if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
            {
                ErrorText.Text = $"账号 {username} 已存在";
                return;
            }
        }

        var role = (RoleCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "店员";
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO SysUser (UserId, UserName, PasswordHash, DisplayName, Role, CanChangePrice, IsActive)
                VALUES (@id, @u, @h, @dn, @r, @cp, 1)";
            cmd.Parameters.AddWithValue("@id", App.Db.NewId("USR"));
            cmd.Parameters.AddWithValue("@u", username);
            cmd.Parameters.AddWithValue("@h", DatabaseService.HashPassword(pwd));
            cmd.Parameters.AddWithValue("@dn", (object?)DisplayBox.Text.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@r", role);
            cmd.Parameters.AddWithValue("@cp", PriceBox.IsChecked == true ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
        DialogResult = true;
    }
}