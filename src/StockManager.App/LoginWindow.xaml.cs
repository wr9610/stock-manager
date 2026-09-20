using System.Windows;
using System.Windows.Input;
using StockManager.Data;

namespace StockManager.App;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        UserBox.Focus();
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        DoLogin();
    }

    private void PassBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) DoLogin();
    }

    private void DoLogin()
    {
        var user = UserBox.Text.Trim();
        var pass = PassBox.Password;

        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            ErrorText.Text = "请输入用户名和密码";
            return;
        }

        using var conn = App.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DisplayName, Role FROM SysUser WHERE UserName=@u AND PasswordHash=@p AND IsActive=1";
        cmd.Parameters.AddWithValue("@u", user);
        cmd.Parameters.AddWithValue("@p", DatabaseService.HashPassword(pass));
        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            ErrorText.Text = "用户名或密码错误";
            return;
        }

        App.CurrentUser = user;
        App.CurrentRole = rd["Role"]?.ToString() ?? "店员";

        var main = new MainWindow();
        main.Show();
        Close();
    }
}