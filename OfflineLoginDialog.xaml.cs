using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using MinecraftLauncher.Models;

namespace MinecraftLauncher
{
    public partial class OfflineLoginDialog : Window
    {
        public Account? Account { get; private set; }

        public OfflineLoginDialog()
        {
            InitializeComponent();
            Owner = Application.Current.MainWindow;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var name = NameTextBox.Text.Trim();
            var uuid = UuidTextBox.Text.Trim();

            // 验证玩家名称
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("玩家名称不能为空", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (name.Length < 3 || name.Length > 16)
            {
                MessageBox.Show("玩家名称长度必须在 3-16 个字符之间", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!Regex.IsMatch(name, @"^[A-Za-z0-9]+$"))
            {
                MessageBox.Show("玩家名称只能包含字母和数字", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 验证 UUID（如果提供）
            if (!string.IsNullOrEmpty(uuid))
            {
                uuid = uuid.ToLower().Replace("-", "").Replace("{", "").Replace("}", "");
                if (!Regex.IsMatch(uuid, @"^[a-f0-9]{32}$"))
                {
                    MessageBox.Show("UUID 格式不正确，请输入 32 位无符号 UUID", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else
            {
                // 自动生成 UUID
                uuid = Guid.NewGuid().ToString("N");
            }

            Account = new Account
            {
                Type = "offline",
                Name = name,
                Uuid = uuid,
                PlayerType = "Legacy",
                AccessToken = uuid,
                RefreshToken = "",
                Auth = ""
            };

            DialogResult = true;
        }
    }
}
