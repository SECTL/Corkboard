using System.Text.Json.Serialization;

namespace Corkboard.Core.Services.Hitokoto;

/// <summary>一言接口的正文、来源与作者。</summary>
public sealed record HitokotoSentence(
    [property: JsonPropertyName("hitokoto")] string Text,
    [property: JsonPropertyName("from")] string? Source = null,
    [property: JsonPropertyName("from_who")] string? Author = null);
