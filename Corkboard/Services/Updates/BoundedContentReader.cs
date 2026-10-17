using System.IO;
using System.Net.Http;

namespace Corkboard.Services.Updates;

/// <summary>
///     有上限的 HTTP 读取器：更新元数据、清单、签名与安装包都从这里过，
///     响应长度（含声明长度与实际读到的字节数）超过上限就抛 <see cref="InvalidDataException" />。
///     更新链路上的内容全部来自网络，不能按「对方会老实报长度」来假设。
/// </summary>
internal static class BoundedContentReader
{
    public static async Task<byte[]> GetAsync(HttpClient client, Uri uri, int maximumBytes, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (client.Timeout != Timeout.InfiniteTimeSpan)
            timeout.CancelAfter(client.Timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ReadAsync(response.Content, maximumBytes, timeout.Token).ConfigureAwait(false);
    }

    public static async Task<byte[]> ReadAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        var contentLength = content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maximumBytes)
            throw new InvalidDataException("The response exceeds the configured size limit.");

        await using var input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream(Math.Min(maximumBytes, 81920));
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximumBytes - (int)output.Length + 1)), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                return output.ToArray();
            if (read > maximumBytes - output.Length)
                throw new InvalidDataException("The response exceeds the configured size limit.");
            output.Write(buffer, 0, read);
        }
    }
}
