using System.Windows;
using System.Windows.Controls;
using MinecraftLauncher.Services;
using MinecraftLauncher.Models;
using System.Threading.Tasks;

namespace MinecraftLauncher;

public partial class MultiplayerPage : Page
{
    private readonly P2PService _p2pService;
    private string? _currentRoomId;

    public MultiplayerPage()
    {
        InitializeComponent();
        _p2pService = P2PService.Instance;
        _p2pService.ConnectionStateChanged += OnConnectionStateChanged;
        _p2pService.RoomCreated += OnRoomCreated;
        _p2pService.PeerConnected += OnPeerConnected;
        _p2pService.PeerDisconnected += OnPeerDisconnected;
        _p2pService.ErrorOccurred += OnErrorOccurred;
        
        LoadSettings();
        UpdateUI();
    }

    private void LoadSettings()
    {
        var settings = _p2pService.GetSettings();
        SignalingServerTextBox.Text = settings.SignalingServerUrl;
        StunServerTextBox.Text = settings.StunServerUrl;
    }

    private void OnConnectionStateChanged(string state)
    {
        Dispatcher.Invoke(() =>
        {
            ConnectionStatusText.Text = state;
            UpdateUI();
        });
    }

    private void OnRoomCreated(string roomId)
    {
        Dispatcher.Invoke(() =>
        {
            _currentRoomId = roomId;
            RoomIdText.Text = roomId;
            RoomInfoPanel.Visibility = Visibility.Visible;
            WaitingText.Visibility = Visibility.Visible;
            UpdateUI();
        });
    }

    private void OnPeerConnected(string peerAddress, string connectionType)
    {
        Dispatcher.Invoke(() =>
        {
            RemoteAddressText.Text = peerAddress;
            ConnectionTypeText.Text = connectionType;
            LocalProxyPortText.Text = _p2pService.LocalProxyPort.ToString();
            ServerAddressText.Text = $"localhost:{_p2pService.LocalProxyPort}";
            DisconnectButton.Visibility = Visibility.Visible;
            UpdateUI();
        });
    }

    private void OnPeerDisconnected()
    {
        Dispatcher.Invoke(() =>
        {
            RemoteAddressText.Text = "-";
            ConnectionTypeText.Text = "-";
            LatencyText.Text = "-";
            DisconnectButton.Visibility = Visibility.Collapsed;
            UpdateUI();
        });
    }

    private void OnErrorOccurred(string error)
    {
        Dispatcher.Invoke(() =>
        {
            MessageBox.Show(error, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        });
    }

    private void UpdateUI()
    {
        var isConnected = _p2pService.IsConnected;
        var isConnecting = _p2pService.IsConnecting;
        var isHost = _p2pService.IsHost;

        CreateRoomButton.IsEnabled = !isConnected && !isConnecting;
        JoinRoomButton.IsEnabled = !isConnected && !isConnecting;
        DisconnectButton.Visibility = isConnected ? Visibility.Visible : Visibility.Collapsed;
        
        if (isConnected)
        {
            ConnectionStatusText.Text = isHost ? "已连接 (主机)" : "已连接 (客户端)";
        }
        else if (isConnecting)
        {
            ConnectionStatusText.Text = "连接中...";
        }
        else
        {
            ConnectionStatusText.Text = "未连接";
        }
    }

    private async void CreateRoomButton_Click(object sender, RoutedEventArgs e)
    {
        var roomName = RoomNameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(roomName))
        {
            MessageBox.Show("请输入房间名称", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CreateRoomButton.IsEnabled = false;
        ConnectionStatusText.Text = "正在创建房间...";

        try
        {
            var settings = new P2PSettings
            {
                SignalingServerUrl = SignalingServerTextBox.Text.Trim(),
                StunServerUrl = StunServerTextBox.Text.Trim()
            };
            
            await _p2pService.CreateRoomAsync(roomName, settings);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"创建房间失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            CreateRoomButton.IsEnabled = true;
            ConnectionStatusText.Text = "未连接";
        }
    }

    private async void JoinRoomButton_Click(object sender, RoutedEventArgs e)
    {
        var roomId = JoinRoomIdTextBox.Text.Trim();
        if (string.IsNullOrEmpty(roomId))
        {
            MessageBox.Show("请输入房间 ID", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (roomId.Length != 6)
        {
            MessageBox.Show("房间 ID 必须是 6 位", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        JoinRoomButton.IsEnabled = false;
        ConnectionStatusText.Text = "正在加入房间...";

        try
        {
            var settings = new P2PSettings
            {
                SignalingServerUrl = SignalingServerTextBox.Text.Trim(),
                StunServerUrl = StunServerTextBox.Text.Trim()
            };
            
            await _p2pService.JoinRoomAsync(roomId, settings);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"加入房间失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            JoinRoomButton.IsEnabled = true;
            ConnectionStatusText.Text = "未连接";
        }
    }

    private void CopyRoomIdButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentRoomId))
        {
            Clipboard.SetText(_currentRoomId);
            MessageBox.Show("房间 ID 已复制到剪贴板", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CopyServerAddressButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(ServerAddressText.Text);
        MessageBox.Show("服务器地址已复制到剪贴板", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        await _p2pService.DisconnectAsync();
        RoomInfoPanel.Visibility = Visibility.Collapsed;
        WaitingText.Visibility = Visibility.Collapsed;
        _currentRoomId = null;
        UpdateUI();
    }

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        var signalingServer = SignalingServerTextBox.Text.Trim();
        var stunServer = StunServerTextBox.Text.Trim();

        if (string.IsNullOrEmpty(signalingServer))
        {
            MessageBox.Show("请输入信令服务器地址", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        TestConnectionButton.IsEnabled = false;
        TestConnectionButton.Content = "测试中...";

        try
        {
            var result = await _p2pService.TestConnectionAsync(signalingServer, stunServer);
            if (result.Success)
            {
                MessageBox.Show($"连接测试成功！\n公共 IP: {result.PublicIp}\nNAT 类型: {result.NatType}", 
                    "测试成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"连接测试失败：{result.ErrorMessage}", "测试失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"测试失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
            TestConnectionButton.Content = "测试连接";
        }
    }
}
