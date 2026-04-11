using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using MinecraftLauncher.Models;

namespace MinecraftLauncher
{
    public partial class AccountPage : Page
    {
        private readonly AccountService _accountService;
        private ObservableCollection<AccountViewModel> _accounts;

        public AccountPage()
        {
            InitializeComponent();
            _accountService = AccountService.Instance;
            LoadAccounts();
        }

        private void LoadAccounts()
        {
            _accounts = new ObservableCollection<AccountViewModel>();
            var accounts = _accountService.GetAccounts();
            
            foreach (var account in accounts)
            {
                _accounts.Add(new AccountViewModel
                {
                    Name = account.Name,
                    Type = account.Type,
                    TypeText = account.Type == "microsoft" ? "微软正版" : "离线账号",
                    Uuid = account.Uuid,
                    AccessToken = account.AccessToken,
                    RefreshToken = account.RefreshToken,
                    PlayerType = account.PlayerType,
                    Auth = account.Auth
                });
            }

            AccountListBox.ItemsSource = _accounts;
            
            // 选中之前的账号
            var selectedIndex = _accountService.GetSelectedIndex();
            if (selectedIndex >= 0 && selectedIndex < _accounts.Count)
            {
                AccountListBox.SelectedIndex = selectedIndex;
            }
        }

        private void AccountListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AccountListBox.SelectedIndex >= 0)
            {
                _accountService.SetSelectedIndex(AccountListBox.SelectedIndex);
                UpdateMainWindowAccountDisplay();
            }
        }

        private void UpdateMainWindowAccountDisplay()
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.UpdateAccountDisplayFromAccountPage();
            }
        }

        private void AddAccountButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OfflineLoginDialog();
            if (dialog.ShowDialog() == true)
                {
                    var account = dialog.Account;
                    _accountService.AddAccount(account);
                    _accounts.Add(new AccountViewModel
                    {
                        Name = account.Name,
                        Type = account.Type,
                        TypeText = "离线账号",
                        Uuid = account.Uuid,
                        AccessToken = account.AccessToken,
                        RefreshToken = account.RefreshToken,
                        PlayerType = account.PlayerType,
                        Auth = account.Auth
                    });
                    
                    // 如果是第一个账号，自动选中
                    if (_accounts.Count == 1)
                    {
                        AccountListBox.SelectedIndex = 0;
                        UpdateMainWindowAccountDisplay();
                    }
                }
        }

        private async void MicrosoftLoginButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new MicrosoftLoginDialog();
                if (dialog.ShowDialog() == true && dialog.Account != null)
                {
                    var account = dialog.Account;
                    _accountService.AddAccount(account);
                    _accounts.Add(new AccountViewModel
                    {
                        Name = account.Name,
                        Type = account.Type,
                        TypeText = "微软正版",
                        Uuid = account.Uuid,
                        AccessToken = account.AccessToken,
                        RefreshToken = account.RefreshToken,
                        PlayerType = account.PlayerType,
                        Auth = account.Auth
                    });
                    
                    // 如果是第一个账号，自动选中
                    if (_accounts.Count == 1)
                    {
                        AccountListBox.SelectedIndex = 0;
                        UpdateMainWindowAccountDisplay();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"微软登录失败：{ex.Message}", "登录失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (AccountListBox.SelectedIndex >= 0)
            {
                var result = MessageBox.Show("确定要删除这个账号吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _accountService.RemoveAccount(AccountListBox.SelectedIndex);
                    _accounts.RemoveAt(AccountListBox.SelectedIndex);
                    
                    // 如果删除后还有账号，自动选中第一个
                    if (_accounts.Count > 0)
                    {
                        AccountListBox.SelectedIndex = 0;
                    }
                }
            }
            else
            {
                MessageBox.Show("请先选择一个账号", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    public class AccountViewModel
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "offline";
        public string TypeText { get; set; } = "";
        public string Uuid { get; set; } = "";
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string PlayerType { get; set; } = "Legacy";
        public string Auth { get; set; } = "";
    }
}
