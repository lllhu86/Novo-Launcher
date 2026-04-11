using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MinecraftLauncher.Models;

namespace MinecraftLauncher
{
    public partial class MicrosoftLoginDialog : Window
    {
        private readonly AccountService _accountService;
        private CancellationTokenSource? _cancellationTokenSource;
        public Account? Account { get; private set; }

        public MicrosoftLoginDialog()
        {
            InitializeComponent();
            Owner = Application.Current.MainWindow;
            _accountService = AccountService.Instance;
            _ = StartLoginFlowAsync();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private async Task StartLoginFlowAsync()
        {
            try
            {
                StatusTextBlock.Text = "正在获取设备代码...";
                
                // 获取设备代码
                var deviceCode = await _accountService.GetDeviceCodeAsync();
                
                if (string.IsNullOrEmpty(deviceCode.UserCode))
                {
                    throw new Exception("未能获取用户代码");
                }
                
                // 复制到剪贴板
                Clipboard.SetText(deviceCode.UserCode);
                UserCodeTextBox.Text = deviceCode.UserCode;
                
                StatusTextBlock.Text = "正在打开浏览器...";
                var verificationUrl = deviceCode.VerificationUriComplete ?? deviceCode.VerificationUri;
                if (string.IsNullOrEmpty(verificationUrl))
                {
                    // 如果都没有，尝试从 Message 中提取
                    verificationUrl = "https://www.microsoft.com/link";
                }
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = verificationUrl,
                    UseShellExecute = true
                };
                
                if (string.IsNullOrEmpty(startInfo.FileName))
                {
                    throw new Exception("验证 URL 为空");
                }
                
                Process.Start(startInfo);
                
                // 开始轮询验证
                _cancellationTokenSource = new CancellationTokenSource();
                await PollForTokenAsync(deviceCode.DeviceCode, _cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"登录失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
            }
        }

        private async Task PollForTokenAsync(string deviceCode, CancellationToken cancellationToken)
        {
            var interval = TimeSpan.FromSeconds(5);
            var expiresAt = DateTime.UtcNow.AddSeconds(900); // 15 分钟过期
            
            while (DateTime.UtcNow < expiresAt)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    var account = await _accountService.VerifyDeviceCodeAsync(deviceCode);
                    if (account != null)
                    {
                        Account = account;
                        StatusTextBlock.Text = "登录成功！";
                        LoginProgressBar.IsIndeterminate = false;
                        LoginProgressBar.Value = 100;
                        
                        await Task.Delay(1000);
                        DialogResult = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // 继续轮询
                    StatusTextBlock.Text = $"等待确认... ({ex.Message})";
                }
                
                StatusTextBlock.Text = "请在浏览器中完成登录验证...";
                await Task.Delay(interval, cancellationToken);
            }
            
            throw new TimeoutException("登录超时，请重试");
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            DialogResult = false;
        }

        private async void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            RetryButton.Visibility = Visibility.Collapsed;
            StatusTextBlock.Text = "正在重新获取设备代码...";
            await StartLoginFlowAsync();
        }

        protected override void OnClosed(EventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            base.OnClosed(e);
        }
    }
}
