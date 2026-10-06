using Corkboard.Core.Abstraction;
using Corkboard.Core.Models.Board;
using Corkboard.Core.Services.Board;
using Corkboard.Shared.Abstraction;
using Microsoft.Extensions.Logging.Abstractions;

namespace Corkboard.Core.Tests;

/// <summary>
///     Board 是骨架里的占位领域，但它是「新模块如何接进配置管道」的样板：
///     这里用内存配置服务替换磁盘实现，验证增删改置顶都会落盘一次。
/// </summary>
public class BoardServiceTests
{
    private sealed class InMemoryConfigService : ConfigServiceBase
    {
        public Dictionary<string, ConfigBase> Saved { get; } = [];

        public int SaveCount { get; private set; }

        public override bool IsConfigExists<T>(T fallback) => Saved.ContainsKey(fallback.ConfigFilePath);

        public override T LoadConfig<T>(T fallback)
        {
            return Saved.TryGetValue(fallback.ConfigFilePath, out var stored) ? (T)stored : fallback;
        }

        public override void SaveConfig<T>(T config)
        {
            SaveCount++;
            Saved[config.ConfigFilePath] = config;
        }

        public override void DeleteConfig<T>(T config)
        {
            Saved.Remove(config.ConfigFilePath);
        }
    }

    private static (BoardService Service, InMemoryConfigService Config) CreateService()
    {
        var config = new InMemoryConfigService();
        var service = new BoardService(config, NullLogger<BoardService>.Instance);
        return (service, config);
    }

    [Fact]
    public void Add_PersistsNoteAndRaisesChanged()
    {
        var (service, config) = CreateService();
        var changed = 0;
        service.Changed += (_, _) => changed++;

        var note = service.Add("标题", "正文", "#FFFFFF");

        Assert.Single(service.Notes);
        Assert.Equal("标题", note.Title);
        Assert.Equal("#FFFFFF", note.ColorHex);
        Assert.Equal(1, config.SaveCount);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Add_WithoutColor_UsesFallbackColor()
    {
        var (service, _) = CreateService();

        var note = service.Add("标题", "正文");

        Assert.False(string.IsNullOrWhiteSpace(note.ColorHex));
    }

    [Fact]
    public void Remove_UnknownId_ReturnsFalseWithoutSaving()
    {
        var (service, config) = CreateService();

        Assert.False(service.Remove(Guid.NewGuid()));
        Assert.Equal(0, config.SaveCount);
    }

    [Fact]
    public void TogglePin_FlipsFlag()
    {
        var (service, _) = CreateService();
        var note = service.Add("标题", "正文");

        Assert.True(service.TogglePin(note.Id));
        Assert.True(service.Notes.Single().IsPinned);

        service.TogglePin(note.Id);
        Assert.False(service.Notes.Single().IsPinned);
    }

    [Fact]
    public void Update_ReplacesNoteAndRefreshesTimestamp()
    {
        var (service, _) = CreateService();
        var note = service.Add("标题", "正文");
        var before = note.UpdatedAt;

        var edited = note.Clone();
        edited.Title = "改过的标题";
        edited.UpdatedAt = before.AddMinutes(-5);

        Assert.True(service.Update(edited));
        var stored = service.Notes.Single();
        Assert.Equal("改过的标题", stored.Title);
        Assert.True(stored.UpdatedAt >= before);
    }

    [Fact]
    public void Reload_ReadsBackWhatWasPersisted()
    {
        var (service, config) = CreateService();
        service.Add("标题", "正文");

        var reloaded = new BoardService(config, NullLogger<BoardService>.Instance);

        Assert.Single(reloaded.Notes);
        Assert.Equal("标题", reloaded.Notes[0].Title);
    }
}
