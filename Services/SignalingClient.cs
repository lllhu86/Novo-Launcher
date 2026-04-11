using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Services;

public class SignalingClient : IDisposable
{
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly StringBuilder _receiveBuffer = new();
    private bool _isConnected;
    private bool _disposed;

    public event Action<string>? Connected;
    public event Action? Disconnected;
    public event Action<SignalingMessage>? MessageReceived;
    public event Action<string>? ErrorOccurred;

    public bool IsConnected => _isConnected && _webSocket?.State == WebSocketState.Open;

    public async Task ConnectAsync(string serverUrl, CancellationToken cancellationToken = default)
    {
        if (_isConnected)
        {
            await DisconnectAsync();
        }

        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellationTokenSource.Token, cancellationToken);

            _webSocket = new ClientWebSocket();
            
            var uri = new Uri(serverUrl);
            await _webSocket.ConnectAsync(uri, linkedCts.Token);

            _isConnected = true;
            Connected?.Invoke(serverUrl);

            _ = Task.Run(() => ReceiveMessagesAsync(linkedCts.Token), linkedCts.Token);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            ErrorOccurred?.Invoke($"连接信令服务器失败：{ex.Message}");
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);
                }
            }
            catch { }

            _webSocket.Dispose();
            _webSocket = null;
        }

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        _isConnected = false;
        Disconnected?.Invoke();
    }

    public async Task SendMessageAsync(SignalingMessage message)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("未连接到信令服务器");
        }

        var json = JsonConvert.SerializeObject(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(bytes);

        await _webSocket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        try
        {
            while (_webSocket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var segment = new ArraySegment<byte>(buffer);
                var result = await _webSocket.ReceiveAsync(segment, cancellationToken);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var messageText = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    
                    if (result.EndOfMessage)
                    {
                        _receiveBuffer.Append(messageText);
                        var fullMessage = _receiveBuffer.ToString();
                        _receiveBuffer.Clear();

                        try
                        {
                            var message = JsonConvert.DeserializeObject<SignalingMessage>(fullMessage);
                            if (message != null)
                            {
                                MessageReceived?.Invoke(message);
                            }
                        }
                        catch (JsonException ex)
                        {
                            ErrorOccurred?.Invoke($"解析消息失败：{ex.Message}");
                        }
                    }
                    else
                    {
                        _receiveBuffer.Append(messageText);
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await DisconnectAsync();
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (_isConnected)
            {
                ErrorOccurred?.Invoke($"接收消息错误：{ex.Message}");
                await DisconnectAsync();
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            DisconnectAsync().Wait();
        }
    }
}
