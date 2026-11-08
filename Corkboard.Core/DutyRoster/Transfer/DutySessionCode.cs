using System.Security.Cryptography;
using System.Text;

namespace Corkboard.Core.DutyRoster.Transfer;

/// <summary>
///     云会话码：12 位、去掉 I/O/0/1 的大写字母数字。
///     <para>
///         与上游 SecRandom 的 <c>RosterSyncTransferService</c> 规则一致——生成时先取 12 字节随机数，
///         再对字母表取模（32 个字符，模 32 无偏差）；用户输入允许带空格、短横线，归一化后必须恰好 12 位。
///         60 bit 熵只够配合 PBKDF2(600k) 使用，<b>不要为了好念再缩短</b>。
///     </para>
/// </summary>
public static class DutySessionCode
{
    public const int Length = 12;

    /// <summary>剔除了 I、O、0、1，手抄时不容易看错。</summary>
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>生成一个新的会话码（密码学随机）。</summary>
    public static string Create()
    {
        Span<byte> random = stackalloc byte[Length];
        RandomNumberGenerator.Fill(random);
        Span<char> code = stackalloc char[Length];
        for (var index = 0; index < code.Length; index++)
            code[index] = Alphabet[random[index] % Alphabet.Length];
        return new string(code);
    }

    /// <summary>只保留字母数字并转大写；用户输入里的空格、短横线会被吃掉。</summary>
    public static string Normalize(string? code) =>
        new(code?.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray() ?? []);

    public static bool IsValid(string? code) =>
        code is { Length: Length } && code.All(character => Alphabet.Contains(character));

    /// <summary>上送服务端的只有哈希，会话码本身不过网。</summary>
    public static string Sha256Hex(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}