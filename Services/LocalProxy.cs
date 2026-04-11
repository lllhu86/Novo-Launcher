using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;

namespace MinecraftLauncher.Services;

public class LocalProxy : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly List<TcpClient> _connectedClients = new();
    private readonly ConcurrentQueue<byte[]> _outgoingDataQueue = new();
    private bool _isRunning;
    private bool _disposed;

    public int LocalPort { get; private set; }
    public bool IsRunning => _isRunning;

    public event Action<byte[]>? DataReceived;
    public event Action<TcpClient>? ClientConnected;
    public event Action<TcpClient>? ClientDisconnected;
    public event Action<string>? ErrorOccurred;

    public async Task<int> StartAsync(int preferredPort = 25565, CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return LocalPort;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            _cancellationTokenSource.Token, cancellationToken);

        for (int port = preferredPort; port < preferredPort + 100; port++)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
                LocalPort = port;
                _isRunning = true;

                _ = Task.Run(() => AcceptClientsAsync(linkedCts.Token), linkedCts.Token);

                return port;
            }
            catch (SocketException)
            {
                _listener?.Stop();
                _listener = null;
            }
        }

        throw new InvalidOperationException("无法找到可用端口");
    }

    public void Stop()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _cancellationTokenSource?.Cancel();

        foreach (var client in _connectedClients)
        {
            try
            {
                client.Close();
            }
            catch { }
        }
        _connectedClients.Clear();

        try
        {
            _listener?.Stop();
        }
        catch { }

        _listener = null;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (_isRunning && !cancellationToken.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _connectedClients.Add(client);
                ClientConnected?.Invoke(client);

                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_isRunning)
                {
                    ErrorOccurred?.Invoke($"接受客户端连接失败：{ex.Message}");
                }
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            var stream = client.GetStream();
            var buffer = new byte[8192];

            while (client.Connected && !cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0) break;

                var data = new byte[bytesRead];
                Array.Copy(buffer, data, bytesRead);
                DataReceived?.Invoke(data);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (client.Connected)
            {
                ErrorOccurred?.Invoke($"客户端数据读取错误：{ex.Message}");
            }
        }
        finally
        {
            _connectedClients.Remove(client);
            ClientDisconnected?.Invoke(client);
            try
            {
                client.Close();
            }
            catch { }
        }
    }

    public async Task SendDataAsync(byte[] data)
    {
        var clientsCopy = _connectedClients.ToList();
        foreach (var client in clientsCopy)
        {
            try
            {
                if (client.Connected)
                {
                    var stream = client.GetStream();
                    await stream.WriteAsync(data, CancellationToken.None);
                }
            }
            catch { }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Stop();
        }
    }
}
