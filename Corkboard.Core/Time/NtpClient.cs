using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Corkboard.Core.Time;

/// <summary>
///     真正发 UDP 的 NTP 客户端：解析主机名 → 发一个 48 字节请求 → 解析回应 → 算偏差。
///     <para>
///         接口只有「问一台服务器要一次时间」，重试、换源、要不要写回系统时钟都由上层决定：
///         这个类不知道配置、不知道界面，也不知道失败之后该干什么。
///     </para>
///     <para>
///         <b>任何异常都不许抛出去</b>（除了调用方自己的取消）：校时是个后台动作，
///         网络抖动、DNS 挂了、对端不回包都是常态，这些必须以
///         <see cref="NtpSyncResult.Failure" /> 的形状回到调用方，让它按「这次没校上」处理，
///         而不是让一个 <see cref="SocketException" /> 顺着 Task 冒到界面线程去。
///     </para>
/// </summary>
public sealed class NtpClient
{
    /// <summary>NTP 固定端口。</summary>
    private const int NtpPort = 123;

    /// <summary>调用方没给（或给了个非正数）超时时的兜底值。</summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    private readonly ILogger<NtpClient>? _logger;

    /// <summary>
    ///     <paramref name="logger" /> 可以不给：单测与宿主之外的工具里没有 DI 容器，
    ///     而日志只用于排障（<c>LogDebug</c>），缺了不影响功能。
    /// </summary>
    public NtpClient(ILogger<NtpClient>? logger = null) => _logger = logger;

    /// <summary>
    ///     向 <paramref name="server" /> 要一次时间。失败一律以
    ///     <see cref="NtpSyncResult.Failure" /> 返回（文案是英文短语，方便直接进日志）。
    ///     <para>
    ///         超时与调用方取消要分开：超时是「这次没校上」，取消是「调用方不要了」。
    ///         所以超时走自己的 <see cref="CancellationTokenSource" />，只有
    ///         <paramref name="cancellationToken" /> 真的被触发时才把
    ///         <see cref="OperationCanceledException" /> 往外抛。
    ///     </para>
    /// </summary>
    public async Task<NtpSyncResult> QueryAsync(string server, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(server)) return NtpSyncResult.Failure(server ?? string.Empty, "server is empty");

        UdpClient? socket = null;
        try
        {
            var address = await ResolveAsync(server, cancellationToken).ConfigureAwait(false);
            if (address is null) return NtpSyncResult.Failure(server, "could not resolve host");

            var effectiveTimeout = timeout > TimeSpan.Zero ? timeout : DefaultTimeout;
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(effectiveTimeout);

            // 只用 IPv4 套接字：默认源清单里都是双栈的域名，解析出来的 IPv6 地址在国内网络上
            // 经常一条路都不通，与其在 v6 上耗掉整个超时，不如明确走 v4。
            socket = new UdpClient(AddressFamily.InterNetwork);

            var sentUtc = DateTimeOffset.UtcNow;
            var request = NtpPacket.CreateRequest(sentUtc);
            await socket.SendAsync(request, new IPEndPoint(address, NtpPort), timeoutSource.Token)
                .ConfigureAwait(false);

            var response = await socket.ReceiveAsync(timeoutSource.Token).ConfigureAwait(false);
            var receivedUtc = DateTimeOffset.UtcNow;

            if (!NtpPacket.TryParse(response.Buffer, out var packet) || packet is null)
            {
                _logger?.LogDebug("NTP 回应无法解析：{Server} 回了 {Length} 字节", server, response.Buffer.Length);
                return NtpSyncResult.Failure(server, "invalid response packet");
            }

            if (!NtpPacket.TryComputeOffset(packet, sentUtc, receivedUtc, out var offset, out var roundTrip))
            {
                _logger?.LogDebug("NTP 结果不可信：{Server} stratum={Stratum}", server, packet.Stratum);
                return NtpSyncResult.Failure(server, "unreliable response");
            }

            _logger?.LogDebug("NTP 校时成功：{Server} offset={Offset}ms rtt={RoundTrip}ms stratum={Stratum}",
                server, offset.TotalMilliseconds, roundTrip.TotalMilliseconds, packet.Stratum);

            return NtpSyncResult.Success(server, offset, roundTrip, receivedUtc, packet.Stratum);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 调用方主动取消：这是它要的，别吞掉，否则上层没法区分「取消了」和「超时了」。
            throw;
        }
        catch (OperationCanceledException)
        {
            // 走到这里是自己的 CancelAfter 触发的：对端没在 timeout 内回包。
            _logger?.LogDebug("NTP 请求超时：{Server}", server);
            return NtpSyncResult.Failure(server, "timeout");
        }
        catch (Exception ex)
        {
            // DNS 失败、套接字错误、对端端口不可达……都按「这次没校上」处理。
            _logger?.LogDebug(ex, "NTP 请求失败：{Server}", server);
            return NtpSyncResult.Failure(server, ex.Message);
        }
        finally
        {
            socket?.Dispose();
        }
    }

    /// <summary>
    ///     把主机名解析成地址：优先 IPv4（套接字就是 v4 的），没有 IPv4 才退回第一个地址。
    ///     解析不出来时返回 <c>null</c>，由调用方转成失败结果。
    /// </summary>
    private async Task<IPAddress?> ResolveAsync(string server, CancellationToken cancellationToken)
    {
        // 用户可能直接填了 IP，也可能按 URI 的写法填了 [::1]，先按字面量认一遍，省一次 DNS。
        var literal = server;
        if (literal.Length > 2 && literal[0] == '[' && literal[^1] == ']') literal = literal[1..^1];
        if (IPAddress.TryParse(literal, out var parsed)) return parsed;

        var addresses = await Dns.GetHostAddressesAsync(server, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0) return null;

        foreach (var address in addresses)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork) return address;
        }

        return addresses[0];
    }
}
