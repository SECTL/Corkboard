using System.Security.Cryptography;
using Corkboard.Core.ControlCenter;

namespace Corkboard.Core.Tests;

/// <summary>设备凭证的 AES-GCM 加解密往返与「坏文件当未接入」。</summary>
public class NodeEnrollmentStoreTests
{
    private static ControlNodeCredential SampleCredential() => new(
        "dev-123",
        "s3cret-value",
        "SC-6M34RQ",
        "site-a",
        "class-1",
        "class-name")
    {
        EnrolledAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)
    };

    [Fact]
    public async Task SaveAndLoad_RoundTripsCredential()
    {
        using var temp = new TempDirectory();
        var store = new NodeEnrollmentStore(credentialPath: temp.File("control-node.json"));

        await store.SaveAsync(SampleCredential(), TestContext.Current.CancellationToken);

        var reloaded = await new NodeEnrollmentStore(credentialPath: temp.File("control-node.json")).LoadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(reloaded);
        Assert.Equal("dev-123", reloaded!.DeviceId);
        Assert.Equal("s3cret-value", reloaded.Secret);
        Assert.Equal("SC-6M34RQ", reloaded.SiteId);
        Assert.Equal("site-a", reloaded.SiteName);
        Assert.Equal("class-1", reloaded.ClassId);
        Assert.True(reloaded.IsComplete);
        Assert.True(File.Exists(temp.File("control-node.key")));
    }

    [Fact]
    public async Task CredentialFile_DoesNotContainPlaintextSecret()
    {
        using var temp = new TempDirectory();
        var store = new NodeEnrollmentStore(credentialPath: temp.File("control-node.json"));
        await store.SaveAsync(SampleCredential(), TestContext.Current.CancellationToken);

        var cipherText = await File.ReadAllTextAsync(temp.File("control-node.json"), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("s3cret-value", cipherText);
        Assert.DoesNotContain("SC-6M34RQ", cipherText);
        Assert.DoesNotContain("dev-123", cipherText);
    }

    [Fact]
    public async Task MissingFile_ReturnsNullWithoutThrowing()
    {
        using var temp = new TempDirectory();
        var store = new NodeEnrollmentStore(credentialPath: temp.File("control-node.json"));

        Assert.Null(await store.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task CorruptCredentialFile_IsTreatedAsNotEnrolled()
    {
        using var temp = new TempDirectory();
        var path = temp.File("control-node.json");
        var store = new NodeEnrollmentStore(credentialPath: path);
        await store.SaveAsync(SampleCredential(), TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(path, "{ this is not json", TestContext.Current.CancellationToken);

        Assert.Null(await store.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Null(store.Current);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(temp.File("control-node.key")));
    }

    [Fact]
    public async Task KeyMismatch_IsTreatedAsNotEnrolledAndFilesAreDiscarded()
    {
        using var temp = new TempDirectory();
        var credentialPath = temp.File("control-node.json");
        var keyPath = temp.File("control-node.key");
        var store = new NodeEnrollmentStore(credentialPath: credentialPath);
        await store.SaveAsync(SampleCredential(), TestContext.Current.CancellationToken);

        // 换一把同格式的密钥：密文还在，但再也解不开了。
        var replacement = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await File.WriteAllTextAsync(keyPath, $"{{\"format_version\":1,\"key\":\"{replacement}\"}}", TestContext.Current.CancellationToken);

        Assert.Null(await store.LoadAsync(TestContext.Current.CancellationToken));
        Assert.False(File.Exists(credentialPath));
        Assert.False(File.Exists(keyPath));
    }

    [Fact]
    public async Task ClearAsync_RemovesBothFilesAndMemory()
    {
        using var temp = new TempDirectory();
        var store = new NodeEnrollmentStore(credentialPath: temp.File("control-node.json"));
        await store.SaveAsync(SampleCredential(), TestContext.Current.CancellationToken);

        await store.ClearAsync(TestContext.Current.CancellationToken);

        Assert.Null(store.Current);
        Assert.Null(await store.LoadAsync(TestContext.Current.CancellationToken));
        Assert.False(File.Exists(temp.File("control-node.json")));
    }

    [Fact]
    public async Task SaveTwice_KeepsLatestCredential()
    {
        using var temp = new TempDirectory();
        var store = new NodeEnrollmentStore(credentialPath: temp.File("control-node.json"));
        await store.SaveAsync(SampleCredential(), TestContext.Current.CancellationToken);
        await store.SaveAsync(SampleCredential() with { Secret = "second-secret" }, TestContext.Current.CancellationToken);

        var reloaded = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("second-secret", reloaded!.Secret);
    }
}
