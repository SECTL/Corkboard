using System.ComponentModel;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Models;
using Corkboard.Core.Models.SubConfigs.Hitokoto;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Services.Hitokoto;
using Microsoft.Extensions.Logging;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels;

/// <summary>一言只在主界面挂载期间刷新，关闭开关或离开界面会取消请求。</summary>
public partial class HitokotoViewModel(
    MainConfigHandler configHandler,
    HitokotoClient client,
    ILogger<HitokotoViewModel> logger) : ObservableObject
{
    private readonly DispatcherTimer _timer = new();
    private CancellationTokenSource? _request;
    private HitokotoSettingsConfig? _settings;
    private MainConfigModel? _config;
    private HitokotoSentence? _sentence;
    private bool _attached;

    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAttribution))]
    private string _attribution = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isRefreshing;

    public bool IsVisible => _attached && _settings?.Enabled == true;
    public bool CanRefresh => IsVisible && !IsRefreshing;
    public bool HasAttribution => !string.IsNullOrEmpty(Attribution);

    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _timer.Tick += OnTick;
        configHandler.Reloaded += OnReloaded;
        BindConfig();
    }

    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _timer.Stop();
        _timer.Tick -= OnTick;
        configHandler.Reloaded -= OnReloaded;
        UnbindConfig();
        CancelRequest();
        NotifyVisibility();
    }

    private void BindConfig()
    {
        UnbindConfig();
        _config = configHandler.Data;
        _settings = _config.Hitokoto;
        _config.PropertyChanged += OnConfigChanged;
        _settings.PropertyChanged += OnSettingsChanged;
        Restart();
    }

    private void UnbindConfig()
    {
        if (_config is not null) _config.PropertyChanged -= OnConfigChanged;
        if (_settings is not null) _settings.PropertyChanged -= OnSettingsChanged;
    }

    private void OnReloaded(object? sender, EventArgs e) => BindConfig();

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(MainConfigModel.Hitokoto)) BindConfig();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HitokotoSettingsConfig.ShowSource) or nameof(HitokotoSettingsConfig.ShowAuthor))
            UpdateAttribution();
        else if (e.PropertyName == nameof(HitokotoSettingsConfig.RefreshMinutes))
            ScheduleRefresh();
        else
            Restart();
    }

    private void Restart()
    {
        _timer.Stop();
        CancelRequest();
        _sentence = null;
        Text = string.Empty;
        Attribution = string.Empty;
        NotifyVisibility();
        if (IsVisible) _ = RefreshAsync();
    }

    private void CancelRequest()
    {
        _request?.Cancel();
        _request = null;
        IsRefreshing = false;
    }

    private void NotifyVisibility()
    {
        OnPropertyChanged(nameof(IsVisible));
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private void ScheduleRefresh()
    {
        _timer.Stop();
        if (!IsVisible || IsRefreshing) return;
        _timer.Interval = TimeSpan.FromMinutes(Math.Clamp(_settings!.RefreshMinutes, 1, 1440));
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e) => _ = RefreshAsync();

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (!CanRefresh) return;
        _timer.Stop();
        using var request = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        _request = request;
        IsRefreshing = true;
        if (_sentence is null) Text = CR.Hitokoto_Loading;
        // 拍下本次筛选条件，设置变化后旧请求不再发布结果。
        var settings = new HitokotoSettingsConfig
        {
            Categories = _settings!.Categories,
            BlockedWords = _settings.BlockedWords
        };
        try
        {
            var sentence = await client.GetAsync(settings, request.Token);
            if (!ReferenceEquals(_request, request)) return;
            _sentence = sentence;
            Text = sentence?.Text ?? CR.Hitokoto_Filtered;
            UpdateAttribution();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            if (!ReferenceEquals(_request, request)) return;
            logger.LogWarning(exception, "获取一言失败");
            if (_sentence is null) Text = CR.Hitokoto_Unavailable;
        }
        finally
        {
            if (ReferenceEquals(_request, request))
            {
                _request = null;
                IsRefreshing = false;
                ScheduleRefresh();
            }
        }
    }

    private void UpdateAttribution()
    {
        Attribution = _sentence is null ? string.Empty : string.Join(" · ", new[]
        {
            _settings!.ShowSource ? _sentence.Source : null,
            _settings.ShowAuthor ? _sentence.Author : null
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
