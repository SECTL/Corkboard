using System.Text.Json;
using Corkboard.Core.ControlCenter;

namespace Corkboard.Core.Tests;

/// <summary><c>node-state.json</c>（已应用 revision + 上次选的班）的往返与坏文件兜底。</summary>
public class FileControlNodeStateStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsState()
    {
        using var temp = new TempDirectory();
        var path = temp.File("node-state.json");
        var updatedAt = new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);
        var store = new FileControlNodeStateStore(path: path);

        store.Save(new ControlNodeState(1234, "class-9", updatedAt));
        var reloaded = new FileControlNodeStateStore(path: path).Load();

        Assert.Equal(1234, reloaded.AppliedStateRevision);
        Assert.Equal("class-9", reloaded.SelectedClassId);
        Assert.Equal(updatedAt, reloaded.UpdatedAt);
    }

    [Fact]
    public void SavedFile_UsesSnakeCaseAndFormatVersion()
    {
        using var temp = new TempDirectory();
        var path = temp.File("node-state.json");
        new FileControlNodeStateStore(path: path).Save(new ControlNodeState(7, "class-1"));

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        Assert.Equal(FileControlNodeStateStore.FormatVersion, root.GetProperty("format_version").GetInt32());
        Assert.Equal(7, root.GetProperty("applied_state_revision").GetInt64());
        Assert.Equal("class-1", root.GetProperty("selected_class_id").GetString());
    }

    [Fact]
    public void MissingFile_ReturnsEmptyState()
    {
        using var temp = new TempDirectory();

        var state = new FileControlNodeStateStore(path: temp.File("missing.json")).Load();

        Assert.Equal(0, state.AppliedStateRevision);
        Assert.Null(state.SelectedClassId);
    }

    [Fact]
    public void CorruptFile_ReturnsEmptyStateWithoutThrowing()
    {
        using var temp = new TempDirectory();
        var path = temp.File("node-state.json");
        File.WriteAllText(path, "not-a-json");

        var state = new FileControlNodeStateStore(path: path).Load();

        Assert.Same(ControlNodeState.Empty, state);
    }

    [Fact]
    public void UnknownFormatVersion_ReturnsEmptyState()
    {
        using var temp = new TempDirectory();
        var path = temp.File("node-state.json");
        File.WriteAllText(path, "{\"format_version\":99,\"applied_state_revision\":42}");

        var state = new FileControlNodeStateStore(path: path).Load();

        Assert.Equal(0, state.AppliedStateRevision);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFiles()
    {
        using var temp = new TempDirectory();
        var path = temp.File("node-state.json");
        var store = new FileControlNodeStateStore(path: path);

        store.Save(new ControlNodeState(1, null));
        store.Save(new ControlNodeState(2, "class-2"));

        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(temp.Root, "*.tmp"));
    }
}
