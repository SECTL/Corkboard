using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Auth;

/// <summary>创建桌面 loopback 代理；它本身没有依赖。</summary>
public sealed class LoopbackAuthRedirectBrokerFactory(ILogger<LoopbackAuthRedirectBroker> logger)
    : IAuthRedirectBrokerFactory
{
    public IAuthRedirectBroker Create() => new LoopbackAuthRedirectBroker(logger);
}

/// <summary>
///     桌面重定向代理：在空闲端口上开一个 loopback HTTP 监听，收下系统浏览器里的回调，
///     并回一个简短的确认页面。
/// </summary>
/// <remarks>
///     端口每次尝试重新挑选：授权服务为同一个客户端 ID 登记的是 loopback 动态端口重定向。
/// </remarks>
public sealed class LoopbackAuthRedirectBroker(ILogger<LoopbackAuthRedirectBroker> logger) : IAuthRedirectBroker
{
    private HttpListener? _listener;

    /// <summary>一次浏览器往返的等待上限。</summary>
    public TimeSpan WaitTimeout { get; } = TimeSpan.FromMinutes(5);

    public string CreateRedirectUri() => $"http://localhost:{GetFreePort()}/callback";

    public Task PrepareAsync(AuthRedirectAttempt attempt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        _listener = new HttpListener();
        _listener.Prefixes.Add($"{attempt.RedirectUri}/");
        _listener.Start();
        logger.LogDebug("SECTL 授权回调已在 {RedirectUri} 上监听。", attempt.RedirectUri);
        return Task.CompletedTask;
    }

    public async Task<AuthRedirectResult> WaitForRedirectAsync(AuthRedirectAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        var listener = _listener ?? throw new InvalidOperationException("授权回调监听尚未启动。");
        // 超时属于代理自身，因此以 TimeoutException 浮出，上层可以据此给出明确结论而不是无限等待。
        var context = await listener.GetContextAsync().WaitAsync(WaitTimeout, cancellationToken)
            .ConfigureAwait(false);
        var request = context.Request;
        var response = context.Response;
        var code = request.QueryString["code"];
        var returnedState = request.QueryString["state"];
        var error = request.QueryString["error_description"] ?? request.QueryString["error"];
        var html = string.IsNullOrWhiteSpace(code)
            ? "<h1>Authorization failed</h1><p>You can close this window.</p>"
            : "<h1>Authorization successful</h1><p>You can close this window.</p>";
        var bytes = Encoding.UTF8.GetBytes($"<html><meta charset='utf-8'><body>{html}</body></html>");
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        response.Close();

        return new AuthRedirectResult(attempt.RedirectUri, returnedState ?? string.Empty, attempt.CodeVerifier,
            code, error);
    }

    /// <summary>loopback 流程总在本次尝试内完成，没有可恢复的重定向。</summary>
    public Task<AuthRedirectResult?> TakePendingRedirectAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<AuthRedirectResult?>(null);

    public void Dispose()
    {
        try
        {
            _listener?.Close();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (HttpListenerException)
        {
        }

        _listener = null;
    }

    private static int GetFreePort()
    {
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        return ((IPEndPoint)tcp.LocalEndpoint).Port;
    }
}
