using System.Text.Json;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Models;
using Corkboard.Core.Models.Board;
using Corkboard.Core.Services.Board;
using Corkboard.Shared.Abstraction;
using Microsoft.Extensions.Logging.Abstractions;

namespace Corkboard.Core.Tests;

/// <summary>
///     端到端跑一遍**真实文件读写**（真实 <see cref="BoardNoteStore" />）：覆盖旧文件搬迁、按创建日期归档、
///     定义与作业分文件存放。
///     <para>
///     归档根、旧文件、配置文件全部指向临时沙箱，绝不碰开发机上的真实数据根
///     （开发时通常有应用实例正在跑，写真实数据根会互相打架）。
///     </para>
/// </summary>
public class BoardStorageEndToEndTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(
        Path.GetTempPath(), "corkboard-e2e", Guid.NewGuid().ToString("N"));

    private string BoardRoot => Path.Combine(_sandbox, "board");

    private string LegacyPath => Path.Combine(_sandbox, "config", "board.json");

    /// <summary>定义文件，位置照生产布局：归档根下的 <c>board.json</c>。</summary>
    private string ConfigPath => Path.Combine(BoardRoot, "board.json");

    private BoardNoteStore CreateStore() =>
        new(NullLogger<BoardNoteStore>.Instance, BoardRoot, LegacyPath);

    /// <summary>把配置写到沙箱里的 JSON 文件：真实序列化，但不碰 <c>Utils</c> 解析出的真实路径。</summary>
    private sealed class SandboxConfigService(string filePath) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => File.Exists(filePath);

        public override T LoadConfig<T>(T fallback)
        {
            if (!File.Exists(filePath))
                return fallback;

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
        }

        public override void SaveConfig<T>(T config)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(config, JsonOptions));
        }

        public override void DeleteConfig<T>(T config)
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    private (BoardService Service, SandboxConfigService Config) CreateService()
    {
        var config = new SandboxConfigService(ConfigPath);
        var service = new BoardService(config, CreateStore(), NullLogger<BoardService>.Instance);
        return (service, config);
    }

    private static DateTimeOffset LocalTime(int year, int month, int day, int hour = 10)
    {
        var local = new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    [Fact]
    public void LegacyFile_MigratesIntoDatedFoldersOnDisk()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LegacyPath)!);
        var first = LocalTime(2026, 10, 6, 20);
        File.WriteAllText(LegacyPath, $$"""
        {
          "notes": [
            { "id": "{{Guid.NewGuid()}}", "content": "哈哈哈哈", "subject": "语文",
              "created_at": "{{first:O}}", "values": {} },
            { "id": "{{Guid.NewGuid()}}", "content": "低调低调", "subject": "数学",
              "created_at": "{{first.AddMinutes(1):O}}", "values": {} },
            { "id": "{{Guid.NewGuid()}}", "content": "隔天的", "subject": "数学",
              "created_at": "{{first.AddDays(1):O}}", "values": {} }
          ],
          "types": [ { "name": "练习册" } ],
          "subjects": [ "语文", "数学" ]
        }
        """);

        var (service, _) = CreateService();

        // 三条作业都读回来了，并按各自创建日期摊到「年/月/日」目录：第二天单独一个文件。
        Assert.Equal(3, service.Notes.Count);
        Assert.True(File.Exists(Path.Combine(BoardRoot, "2026", "10", "06", BoardNoteStore.NotesFileName)));
        Assert.True(File.Exists(Path.Combine(BoardRoot, "2026", "10", "07", BoardNoteStore.NotesFileName)));
        Assert.Equal(2, CreateStore().LoadAll().Count(note => note.CreatedAt.Day == 6));

        // 旧文件已删掉，重启不会再搬一次。
        Assert.False(File.Exists(LegacyPath));
        Assert.Equal(3, CreateStore().LoadAll().Count);
    }

    [Fact]
    public void LegacyFile_DoesNotOverrideUserEditedDefinition()
    {
        // 用户已经删光了类型与科目：旧文件里的定义不该被搬回来覆盖用户的选择。
        var config = new SandboxConfigService(ConfigPath);
        config.SaveConfig(new BoardConfig { Types = [], Subjects = [] });

        Directory.CreateDirectory(Path.GetDirectoryName(LegacyPath)!);
        File.WriteAllText(LegacyPath, """
        { "notes": [], "types": [ { "name": "练习册" } ], "subjects": [ "语文" ] }
        """);

        var service = new BoardService(config, CreateStore(), NullLogger<BoardService>.Instance);

        Assert.Empty(service.Types);
        Assert.Empty(service.Subjects);
    }

    [Fact]
    public void Assignment_IsWrittenIntoDatedFolderWhileDefinitionFileStaysClean()
    {
        var (service, _) = CreateService();

        Assert.Empty(service.Notes);
        // 没有任何作业时不该先建出一堆空目录。
        Assert.False(Directory.Exists(BoardRoot));

        service.Add(new BoardNote { Content = "第一条", Subject = "语文" });

        var segments = BoardNoteStore.GetDateSegments(DateTimeOffset.Now);
        var file = Path.Combine(BoardRoot, segments[0], segments[1], segments[2], BoardNoteStore.NotesFileName);
        Assert.True(File.Exists(file));
        Assert.Single(CreateStore().LoadAll());

        // 作业只进日期目录；定义文件里不含作业。
        var definition = File.ReadAllText(Path.Combine(BoardRoot, "board.json"));        Assert.Contains("\"types\"", definition);
        Assert.DoesNotContain("\"notes\"", definition);
    }

    [Fact]
    public void SameDayAssignments_ShareOneFileAndDeletionCleansItUp()
    {
        var (service, _) = CreateService();

        var first = service.Add(new BoardNote { Content = "一", Subject = "语文", CreatedAt = DateTimeOffset.Now });
        var second = service.Add(new BoardNote { Content = "二", Subject = "语文", CreatedAt = DateTimeOffset.Now });

        var segments = BoardNoteStore.GetDateSegments(DateTimeOffset.Now);
        var directory = Path.Combine(BoardRoot, segments[0], segments[1], segments[2]);
        var file = Path.Combine(directory, BoardNoteStore.NotesFileName);

        // 同一天的两条作业合并在同一个文件里。
        Assert.Equal(2, CreateStore().LoadAll().Count);
        Assert.Contains("一", File.ReadAllText(file));

        // 删光之后文件与空掉的日期目录都不该留下。
        service.Remove(first.Id);
        service.Remove(second.Id);

        Assert.Empty(CreateStore().LoadAll());
        Assert.False(Directory.Exists(directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_sandbox))
            Directory.Delete(_sandbox, recursive: true);
    }
}
