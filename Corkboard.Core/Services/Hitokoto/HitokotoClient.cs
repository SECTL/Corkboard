using System.Net.Http.Json;
using Corkboard.Core.Models.SubConfigs.Hitokoto;

namespace Corkboard.Core.Services.Hitokoto;

/// <summary>获取一言，最多尝试三次跳过空正文与被屏蔽的内容。</summary>
public sealed class HitokotoClient(HttpClient client)
{
    public async Task<HitokotoSentence?> GetAsync(HitokotoSettingsConfig settings,
        CancellationToken cancellationToken)
    {
        var uri = HitokotoPolicy.BuildRequestUri(settings.Categories);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            // v1 限制每秒两次请求，重取前留出间隔。
            if (attempt > 0)
                await Task.Delay(600, cancellationToken).ConfigureAwait(false);

            var sentence = await client.GetFromJsonAsync<HitokotoSentence>(uri, cancellationToken)
                .ConfigureAwait(false);
            if (sentence is not null && !string.IsNullOrWhiteSpace(sentence.Text) &&
                !HitokotoPolicy.IsBlocked(sentence, settings.BlockedWords))
                return sentence;
        }

        return null;
    }
}
