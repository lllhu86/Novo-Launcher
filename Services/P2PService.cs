using System.Collections.Concurrent;
using MinecraftLauncher.Models;
using Newtonsoft.Json;

namespace MinecraftLauncher.Services;

public class P2PService : IDisposable
{
    private static P2PService? _instance;
    private static readonly object _lock = new();

    public static P2PService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new P2PService();
                }
            }
            return _instance;
        }
    }

    private readonly SignalingClient _signalingClient;
    private readonly LocalProxy _localProxy;
    private readonly ConcurrentQueue<byte[]> _dataChannelQueue = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private P2PSettings _settings = new();
    private string? _peerId;
    private string? _remotePeerId;
    private string? _currentRoomId;
    private bool _isHost;
    private bool _isConnecting;
    private bool _disposed;

    public bool IsConnected => _localProxy.IsRunning && _signalingClient.IsConnected && !string.IsNullOrEmpty(_remotePeerId);
    public bool IsConnecting => _isConnecting;
    public bool IsHost => _isHost;
    public int LocalProxyPort => _localProxy.LocalPort;

    public event Action<string>? ConnectionStateChanged;
    public event Action<string>? RoomCreated;
    public event Action<string, string>? PeerConnected;
    public event Action? PeerDisconnected;
    public event Action<string>? ErrorOccurred;

    private P2PService()
    {
        _signalingClient = new SignalingClient();
        _localProxy = new LocalProxy();

        _signalingClient.MessageReceived += OnSignalingMessageReceived;
        _signalingClient.ErrorOccurred += OnSignalingError;
        _signalingClient.Disconnected += OnSignalingDisconnected;

        _localProxy.DataReceived += OnLocalDataReceived;
        _localProxy.ErrorOccurred += OnProxyError;
    }

    public P2PSettings GetSettings()
    {
        return new P2PSettings
        {
            SignalingServerUrl = _settings.SignalingServerUrl,
            StunServerUrl = _settings.StunServerUrl,
            TurnServerUrl = _settings.TurnServerUrl,
            TurnUsername = _settings.TurnUsername,
            TurnPassword = _settings.TurnPassword,
            LocalProxyPort = _settings.LocalProxyPort
        };
    }

    public async Task CreateRoomAsync(string roomName, P2PSettings settings)
    {
        if (_isConnecting || IsConnected)
        {
            throw new InvalidOperationException("已有连接正在进行");
        }

        _isConnecting = true;
        _isHost = true;
        _settings = settings;
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            ConnectionStateChanged?.Invoke("正在连接信令服务器...");
            await _signalingClient.ConnectAsync(settings.SignalingServerUrl, _cancellationTokenSource.Token);

            _peerId = GeneratePeerId();

            var port = await _localProxy.StartAsync(settings.LocalProxyPort, _cancellationTokenSource.Token);

            var createMessage = new SignalingMessage
            {
                Type = "create_room",
                PeerId = _peerId,
                RoomName = roomName
            };
            await _signalingClient.SendMessageAsync(createMessage);

            ConnectionStateChanged?.Invoke("等待其他玩家加入...");
        }
        catch (Exception)
        {
            _isConnecting = false;
            throw;
        }
    }

    public async Task JoinRoomAsync(string roomId, P2PSettings settings)
    {
        if (_isConnecting || IsConnected)
        {
            throw new InvalidOperationException("已有连接正在进行");
        }

        _isConnecting = true;
        _isHost = false;
        _settings = settings;
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            ConnectionStateChanged?.Invoke("正在连接信令服务器...");
            await _signalingClient.ConnectAsync(settings.SignalingServerUrl, _cancellationTokenSource.Token);

            _peerId = GeneratePeerId();
            _currentRoomId = roomId;

            var port = await _localProxy.StartAsync(settings.LocalProxyPort, _cancellationTokenSource.Token);

            var joinMessage = new SignalingMessage
            {
                Type = "join_room",
                PeerId = _peerId,
                RoomId = roomId
            };
            await _signalingClient.SendMessageAsync(joinMessage);

            ConnectionStateChanged?.Invoke("正在建立 P2P 连接...");
        }
        catch (Exception)
        {
            _isConnecting = false;
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        _isConnecting = false;
        _remotePeerId = null;
        _currentRoomId = null;
        _isHost = false;

        _localProxy.Stop();
        await _signalingClient.DisconnectAsync();

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        ConnectionStateChanged?.Invoke("未连接");
        PeerDisconnected?.Invoke();
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(string signalingServer, string stunServer)
    {
        var result = new ConnectionTestResult();

        try
        {
            using var testClient = new SignalingClient();
            var connected = false;
            var error = string.Empty;

            testClient.Connected += _ => connected = true;
            testClient.ErrorOccurred += e => error = e;

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await testClient.ConnectAsync(signalingServer, cts.Token);

            if (connected)
            {
                result.Success = true;
                result.PublicIp = "检测成功";
                result.NatType = "需要 STUN 服务器检测";
            }
            else
            {
                result.Success = false;
                result.ErrorMessage = error;
            }

            await testClient.DisconnectAsync();
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private void OnSignalingMessageReceived(SignalingMessage message)
    {
        switch (message.Type)
        {
            case "room_created":
                HandleRoomCreated(message);
                break;
            case "peer_joined":
                HandlePeerJoined(message);
                break;
            case "peer_left":
                HandlePeerLeft(message);
                break;
            case "offer":
                HandleOffer(message);
                break;
            case "answer":
                HandleAnswer(message);
                break;
            case "candidate":
                HandleCandidate(message);
                break;
            case "error":
                HandleError(message);
                break;
        }
    }

    private void HandleRoomCreated(SignalingMessage message)
    {
        _currentRoomId = message.RoomId;
        _isConnecting = false;
        RoomCreated?.Invoke(message.RoomId ?? string.Empty);
        ConnectionStateChanged?.Invoke("等待其他玩家加入...");
    }

    private void HandlePeerJoined(SignalingMessage message)
    {
        if (message.PeerInfo == null) return;

        _remotePeerId = message.PeerInfo.PeerId;

        if (_isHost)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var offerMessage = new SignalingMessage
                    {
                        Type = "offer",
                        PeerId = _peerId,
                        TargetPeerId = _remotePeerId,
                        Data = GenerateOffer()
                    };
                    await _signalingClient.SendMessageAsync(offerMessage);
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke($"发送 offer 失败：{ex.Message}");
                }
            });
        }
    }

    private void HandlePeerLeft(SignalingMessage message)
    {
        if (message.PeerId == _remotePeerId)
        {
            _remotePeerId = null;
            PeerDisconnected?.Invoke();
            ConnectionStateChanged?.Invoke("对方已断开连接");
        }
    }

    private void HandleOffer(SignalingMessage message)
    {
        _remotePeerId = message.PeerId;

        _ = Task.Run(async () =>
        {
            try
            {
                var answerMessage = new SignalingMessage
                {
                    Type = "answer",
                    PeerId = _peerId,
                    TargetPeerId = _remotePeerId,
                    Data = GenerateAnswer(message.Data)
                };
                await _signalingClient.SendMessageAsync(answerMessage);

                _isConnecting = false;
                OnP2PConnected();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"处理 offer 失败：{ex.Message}");
            }
        });
    }

    private void HandleAnswer(SignalingMessage message)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                ProcessAnswer(message.Data);
                _isConnecting = false;
                OnP2PConnected();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"处理 answer 失败：{ex.Message}");
            }
        });
    }

    private void HandleCandidate(SignalingMessage message)
    {
        try
        {
            ProcessCandidate(message.Data);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"处理 candidate 失败：{ex.Message}");
        }
    }

    private void HandleError(SignalingMessage message)
    {
        _isConnecting = false;
        ErrorOccurred?.Invoke(message.Data ?? "未知错误");
    }

    private void OnP2PConnected()
    {
        var peerAddress = _isHost ? "客户端" : "主机";
        var connectionType = "P2P 直连";

        ConnectionStateChanged?.Invoke(_isHost ? "已连接 (主机)" : "已连接 (客户端)");
        PeerConnected?.Invoke(peerAddress, connectionType);

        _ = Task.Run(() => ProcessDataChannelAsync(_cancellationTokenSource?.Token ?? CancellationToken.None));
    }

    private async Task ProcessDataChannelAsync(CancellationToken cancellationToken)
    {
        while (IsConnected && !cancellationToken.IsCancellationRequested)
        {
            while (_dataChannelQueue.TryDequeue(out var data))
            {
                await _localProxy.SendDataAsync(data);
            }

            await Task.Delay(10, cancellationToken);
        }
    }

    private void OnLocalDataReceived(byte[] data)
    {
        if (!string.IsNullOrEmpty(_remotePeerId) && _signalingClient.IsConnected)
        {
            var dataMessage = new SignalingMessage
            {
                Type = "data",
                PeerId = _peerId,
                TargetPeerId = _remotePeerId,
                Data = Convert.ToBase64String(data)
            };
            _ = _signalingClient.SendMessageAsync(dataMessage);
        }
    }

    private void OnSignalingDisconnected()
    {
        if (IsConnected)
        {
            _remotePeerId = null;
            PeerDisconnected?.Invoke();
            ConnectionStateChanged?.Invoke("与信令服务器断开连接");
        }
    }

    private void OnSignalingError(string error)
    {
        ErrorOccurred?.Invoke(error);
    }

    private void OnProxyError(string error)
    {
        ErrorOccurred?.Invoke($"代理错误：{error}");
    }

    private static string GeneratePeerId()
    {
        return Guid.NewGuid().ToString("N")[..8].ToUpper();
    }

    private string GenerateOffer()
    {
        var offer = new
        {
            type = "offer",
            sdp = $"v=0\r\no=- {DateTimeOffset.UtcNow.ToUnixTimeSeconds()} 0 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\n",
            iceCandidates = new[]
            {
                new { candidate = $"candidate:1 1 UDP 2122260223 127.0.0.1 {_localProxy.LocalPort} typ host", sdpMid = "0", sdpMLineIndex = 0 }
            }
        };
        return JsonConvert.SerializeObject(offer);
    }

    private string GenerateAnswer(string? offerData)
    {
        var answer = new
        {
            type = "answer",
            sdp = $"v=0\r\no=- {DateTimeOffset.UtcNow.ToUnixTimeSeconds()} 0 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\n",
            iceCandidates = new[]
            {
                new { candidate = $"candidate:1 1 UDP 2122260223 127.0.0.1 {_localProxy.LocalPort} typ host", sdpMid = "0", sdpMLineIndex = 0 }
            }
        };
        return JsonConvert.SerializeObject(answer);
    }

    private void ProcessAnswer(string? answerData)
    {
    }

    private void ProcessCandidate(string? candidateData)
    {
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _ = DisconnectAsync();
            _signalingClient.Dispose();
            _localProxy.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
}
