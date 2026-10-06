using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Shared;
using Corkboard.Shared.Abstraction;

namespace Corkboard.Core.Models.Board;

/// <summary>便签数据文件 <c>data/config/board.json</c>。</summary>
public partial class BoardConfig : ConfigBase
{
    [ObservableProperty] private List<BoardNote> _notes = [];

    [JsonIgnore] public override string ConfigFilePath => Utils.GetFilePath("config", "board.json");
}
