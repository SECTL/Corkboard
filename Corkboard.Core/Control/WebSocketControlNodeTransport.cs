using System.Net.WebSockets;
using System.Text;

namespace Corkboard.Core.ControlCenter;

/// <summary>
///     用 <see cref="ClientWebSocket" /> 连集控中心。.NET 10 的共享框架自带这个类型，
///     不需要（也不许）再引 <c>System.Net.WebSockets.Client</c> 包。
///     <para>
///         单帧上限 64 KiB（协议 §2.3），收方向要自己拼分片：<see cref="ClientWebSocket" /> 不保证
///         一次 <c>ReceiveAsync</c> 就收到一整帧。
///     </para>
/// </summary>
public sealed class WebSocketControlNodeTransportFactory : IControlNodeTransportFactory
{
    private readonly ControlNodeConnectOptions _options;

    /// <summary>建工厂。</summary>
    /// <param name="options">连接参数（建连超时）。</param>
    public WebSocketControlNodeTransportFactory(ControlNodeConnectOptions? options = null) =>
        _options = options ?? new ControlNodeConnectOptions();

    /// <inheritdoc />
    public async Task<IControlNodeTransport> ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var socket = new ClientWebSocket();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.ConnectTimeout);
        try
        {
            await socket.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new WebSocketControlNodeTransport(socket);
    }
}

/// <summary>一条 <see cref="ClientWebSocket" /> 长连接（收发都已序列化好的 JSON 文本帧）。</summary>
public sealed class WebSocketControlNodeTransport : IControlNodeTransport
{
    private const int ReceiveBufferSize = 16 * 1024;

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ClientWebSocket _socket;
    private int _disposed;

    /// <summary>包一条已经连上的 socket（一般通过 <see cref="WebSocketControlNodeTransportFactory" /> 拿）。</summary>
    public WebSocketControlNodeTransport(ClientWebSocket socket)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
    }

    /// <inheritdoc />
    public string? CloseReason => _socket.CloseStatusDescription;

    /// <inheritdoc />
    public int? CloseCode => _socket.CloseStatus is { } status ? (int)status : null;

    /// <inheritdoc />
    public async Task SendAsync(string frameJson, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frameJson);

        var bytes = Encoding.UTF8.GetBytes(frameJson);
        if (bytes.Length > ControlV4Protocol.MaxFrameBytes)
            throw new InvalidOperationException($"集控帧超过 {ControlV4Protocol.MaxFrameBytes} 字节上限");

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_socket.State != WebSocketState.Open)
                throw new IOException($"集控长连接已关闭（{_socket.State}）");

            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferSize];
        using var assembled = new MemoryStream();

        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            assembled.Write(buffer, 0, result.Count);
            if (assembled.Length > ControlV4Protocol.MaxFrameBytes)
                throw new InvalidOperationException($"集控帧超过 {ControlV4Protocol.MaxFrameBytes} 字节上限");

            if (!result.EndOfMessage)
                continue;

            return assembled.Length == 0 ? string.Empty : Encoding.UTF8.GetString(assembled.ToArray());
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closing", timeout.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException
                                       or IOException)
        {
            // 收尾失败无所谓：下面直接丢掉 socket（对端迟早会看到断线并清理会话）。
        }
        finally
        {
            _socket.Dispose();
            _sendLock.Dispose();
        }
    }
}
