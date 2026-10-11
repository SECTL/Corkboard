namespace Corkboard.Core.Services.Hitokoto;

/// <summary>一言请求参数与屏蔽词的纯逻辑。</summary>
public static class HitokotoPolicy
{
    public static Uri BuildRequestUri(string? categories)
    {
        var categoryQuery = string.Concat((categories ?? string.Empty)
            .Where(category => category is >= 'a' and <= 'l')
            .Distinct()
            .Select(category => $"&c={category}"));

        return new Uri($"https://v1.hitokoto.cn/?encode=json&max_length=120{categoryQuery}");
    }

    public static bool IsBlocked(HitokotoSentence sentence, string? words)
    {
        foreach (var word in (words ?? string.Empty).Split(['\r', '\n'],
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (sentence.Text?.Contains(word, StringComparison.OrdinalIgnoreCase) == true ||
                sentence.Source?.Contains(word, StringComparison.OrdinalIgnoreCase) == true ||
                sentence.Author?.Contains(word, StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }

        return false;
    }
}
