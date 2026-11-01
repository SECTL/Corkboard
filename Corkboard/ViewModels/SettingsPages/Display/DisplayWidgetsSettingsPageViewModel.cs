using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Models.SubConfigs.Widgets;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Widgets.Quote;
using Corkboard.Core.Widgets.Weather;
using Corkboard.Services.Widgets;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Display;

/// <summary>
///     「设置 → 看板模式 → 一言与天气」页。
///     <para>
///         一言只有官方 v1 一个来源（用户只要官方的），参数就是官方那几个：类别、字数上下限、
///         以及一条「附加查询串」——放行不了官方键以外的参数，库里能查到的内建键会在下面提示被忽略。
///     </para>
///     <para>
///         天气固定走小米（自定义天气本轮不支持），城市靠名称搜索拿到经纬度再存下来：
///         看板模式是全屏窗、没有输入框，搜索必须在设置里做完。
///     </para>
///     <para>
///         改的都是 <c>[ObservableProperty]</c> 平铺字段，配置 handler 自动落盘；
///         只有城市搜索是异步动作，失败只把状态写在页面上（不弹窗）。
///     </para>
/// </summary>
public partial class DisplayWidgetsSettingsPageViewModel : ViewModelBase
{
    private readonly WeatherService _weatherService;

    /// <summary>城市搜索的取消令牌：连着点两次「搜索」，前一次的结果直接作废。</summary>
    private CancellationTokenSource? _searchCancellation;

    public DisplayWidgetsSettingsPageViewModel(MainConfigHandler configHandler, WeatherService weatherService)
        : base(configHandler)
    {
        _weatherService = weatherService;

        QuoteCategoryItems = BuildCategoryItems();

        SelectedCity = null;
        RefreshDrafts();
        RefreshDerivedTexts();

        // 清空 / 填结果都会改「有没有结果」，让抽屉里那截列表自己跟着显隐。
        CityResults.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasCityResults));
    }

    /// <summary>一眼能看出配置放在哪：设置卡直接绑 <c>Widgets.xxx</c>。</summary>
    public WidgetsSettingsConfig Widgets => Config.Widgets;

    public ObservableCollection<QuoteCategoryItem> QuoteCategoryItems { get; }

    /// <summary>类别下拉框按钮上那行字（「全部类别」/「动画」/「动画 等 3 类」）。</summary>
    public string CategoriesSummary => BuildCategoriesSummary();

    public ObservableCollection<XiaomiCity> CityResults { get; } = [];

    /// <summary>搜索结果里有没有东西（抽屉里那截列表显不显示）。</summary>
    public bool HasCityResults => CityResults.Count > 0;

    /// <summary>
    ///     抽屉里一打开就摆出来的那批城市（省会和几个大城市）。
    ///     <para>
    ///         搜索要联网：没网的时候抽屉里总得有点能点的东西，不然用户只会看到一句「没搜到」。
    ///     </para>
    /// </summary>
    public IReadOnlyList<XiaomiCity> PopularCities => WeatherCityCatalog.PopularCities;

    /// <summary>附加查询串里被忽略的键（官方内建那几个），空串表示全部放行。</summary>
    public string IgnoredQueryHint { get; private set; } = string.Empty;

    public bool HasIgnoredQueryHint => IgnoredQueryHint.Length > 0;

    /// <summary>天气那一行的现状：「上次成功 12:30」或「还没成功拉到过」。</summary>
    public string WeatherStatusText { get; private set; } = string.Empty;

    public string CurrentCityText =>
        string.IsNullOrWhiteSpace(Widgets.WeatherCityName) ? CR.Settings_Widgets_WeatherCityPlaceholder : Widgets.WeatherCityName;

    /// <summary>
    ///     一言刷新间隔的草稿（NumericUpDown 只认 <see cref="decimal" />）：
    ///     微调 / 手输都只改草稿，失焦或离开页面才写配置——`ConfigHandlerBase` 每变一次属性就落一次盘。
    /// </summary>
    [ObservableProperty] private decimal _quoteRefreshDraft;

    /// <summary>天气刷新间隔的草稿，规则同上。</summary>
    [ObservableProperty] private decimal _weatherRefreshDraft;

    /// <summary>
    ///     屏蔽词草稿（每行一个词）：点「保存屏蔽词」或离开页面才落盘，不逐字写 <c>settings.json</c>。
    /// </summary>
    [ObservableProperty] private string _quoteBlockedWordsDraft = string.Empty;

    [ObservableProperty] private string _cityQuery = string.Empty;

    [ObservableProperty] private XiaomiCity? _selectedCity;

    [ObservableProperty] private string _citySearchStatus = string.Empty;

    [ObservableProperty] private bool _isSearchingCity;

    /// <summary>
    ///     用户挑定了城市（抽屉该收了）。
    ///     <para>
    ///         抽屉宿主挂在**设置壳**上（<c>DrawerHost</c>），它的开合不归本 VM 管，
    ///         所以这里只发个信号，由页面调 <c>SettingsView.Current.CloseDrawer()</c>。
    ///     </para>
    /// </summary>
    public event EventHandler? CityPicked;

    /// <summary>字数下限。NumericUpDown 只认 decimal，这里换一下并顺手把上下限拉回合法区间。</summary>
    public decimal QuoteMinLength
    {
        get => Widgets.QuoteMinLength;
        set
        {
            var length = (int)Math.Clamp(value, 0, HitokotoQuery.MaxAllowedLength);
            Widgets.QuoteMinLength = length;
            if (Widgets.QuoteMaxLength < length)
                Widgets.QuoteMaxLength = length;

            OnPropertyChanged();
            OnPropertyChanged(nameof(QuoteMaxLength));
        }
    }

    /// <summary>字数上限（官方默认 30、封顶 100）。</summary>
    public decimal QuoteMaxLength
    {
        get => Widgets.QuoteMaxLength;
        set
        {
            var length = (int)Math.Clamp(value, 0, HitokotoQuery.MaxAllowedLength);
            Widgets.QuoteMaxLength = length;
            if (Widgets.QuoteMinLength > length)
                Widgets.QuoteMinLength = length;

            OnPropertyChanged();
            OnPropertyChanged(nameof(QuoteMinLength));
        }
    }

    /// <summary>附加查询串是草稿：每敲一个字符就落一次盘不合适，失焦 / 回车才写。</summary>
    [ObservableProperty] private string _quoteExtraQueryDraft = string.Empty;

    /// <summary>
    ///     一言刷新间隔提交：夹回合法区间写进配置。
    ///     <c>0</c> 是「每次打开看板都换一条」这一档，只有一言有；其余最小 5 分钟（官方 QPS 2）。
    /// </summary>
    public void CommitQuoteRefresh()
    {
        var minutes = (int)Math.Clamp(QuoteRefreshDraft, 0, WidgetsSettingsConfig.MaxRefreshMinutes);
        if (minutes is > 0 and < WidgetsSettingsConfig.MinRefreshMinutes)
            minutes = WidgetsSettingsConfig.MinRefreshMinutes;

        Widgets.QuoteRefreshMinutes = minutes;
        QuoteRefreshDraft = minutes;
    }

    /// <summary>天气刷新间隔提交：没有「每次打开都换」这一档，一律夹在 5–1440 分钟。</summary>
    public void CommitWeatherRefresh()
    {
        var minutes = (int)Math.Clamp(WeatherRefreshDraft,
            WidgetsSettingsConfig.MinRefreshMinutes, WidgetsSettingsConfig.MaxRefreshMinutes);

        Widgets.WeatherRefreshMinutes = minutes;
        WeatherRefreshDraft = minutes;
    }

    /// <summary>
    ///     屏蔽词提交（「保存屏蔽词」按钮、失焦、离开页面三处都调这个）：
    ///     先收拾成「一行一个词」，再写配置，顺手刷新底下那句计数。
    /// </summary>
    [RelayCommand]
    private void CommitQuoteBlockedWords()
    {
        var normalized = QuoteBlocklist.Normalize(QuoteBlockedWordsDraft);
        Widgets.QuoteBlockedWords = normalized;
        QuoteBlockedWordsDraft = normalized;

        RefreshDerivedTexts();
    }

    partial void OnSelectedCityChanged(XiaomiCity? value)
    {
        if (value is null)
            return;

        Widgets.WeatherCityName = value.Name;
        Widgets.WeatherLatitude = value.Latitude;
        Widgets.WeatherLongitude = value.Longitude;
        Widgets.WeatherLocationKey = value.LocationKey ?? string.Empty;

        // 换了城市就把缓存清掉，否则下一个开板还会先显示上一个城市的天气。
        _weatherService.Invalidate();

        CitySearchStatus = string.Format(CR.Settings_Widgets_WeatherCitySelected, value.DisplayName);
        OnPropertyChanged(nameof(CurrentCityText));

        // 挑完就收抽屉：抽屉是「挑一个」用的，挑完还杵在那儿挡着设置卡没意义。
        CityResults.Clear();
        CityPicked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     打开「换城市」抽屉之前先把上一次的搜索清干净（搜索框、结果、提示语）。
    ///     真正的开抽屉动作在页面里（抽屉宿主在设置壳上）。
    /// </summary>
    public void BeginCityPick()
    {
        CityQuery = string.Empty;
        CitySearchStatus = string.Empty;
        CityResults.Clear();
        SelectedCity = null;
    }

    /// <summary>点「常去的城市」里的一个：直接写进配置，不用再搜一次。</summary>
    [RelayCommand]
    private void PickCity(XiaomiCity? city)
    {
        if (city is null)
            return;

        SelectedCity = city;
    }

    /// <summary>城市名搜索：拿到候选列表让用户挑一条，挑完才写进配置。</summary>
    [RelayCommand]
    private async Task SearchCityAsync()
    {
        var query = CityQuery?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            CitySearchStatus = CR.Settings_Widgets_WeatherSearchEmpty;
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;

        IsSearchingCity = true;
        CitySearchStatus = CR.Settings_Widgets_WeatherSearching;

        try
        {
            var cities = await _weatherService.SearchCitiesAsync(query, token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
                return;

            CityResults.Clear();
            foreach (var city in cities)
                CityResults.Add(city);

            CitySearchStatus = CityResults.Count == 0
                ? CR.Settings_Widgets_WeatherSearchEmpty
                : string.Format(CR.Settings_Widgets_WeatherSearchResult, CityResults.Count);
        }
        catch (OperationCanceledException)
        {
            // 又被点了一次，这一批结果直接丢掉。
        }
        catch (Exception)
        {
            // 搜索失败不打断设置：只把话写在页面上。
            if (!token.IsCancellationRequested)
                CitySearchStatus = CR.Settings_Widgets_WeatherSearchFailed;
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsSearchingCity = false;
        }
    }

    /// <summary>附加查询串失焦 / 回车时调用：写回配置并重算「哪些键被忽略」。</summary>
    public void CommitQuoteExtraQuery()
    {
        Widgets.QuoteExtraQuery = QuoteExtraQueryDraft.Trim();
        RefreshDerivedTexts();
    }

    /// <summary>页面每次进来都重新读一遍配置（别的页可能改过）。</summary>
    public void Reload()
    {
        QuoteExtraQueryDraft = Widgets.QuoteExtraQuery;
        RefreshDrafts();

        foreach (var item in QuoteCategoryItems)
            item.IsSelected = Widgets.QuoteCategories.HasFlag(item.Category);

        CityResults.Clear();
        CityQuery = string.Empty;
        CitySearchStatus = string.Empty;
        SelectedCity = null;

        RefreshDerivedTexts();
    }

    /// <summary>
    ///     把配置里的「间隔」与「屏蔽词」搬进草稿：构造期与每次进页面（<see cref="Reload" />）各调一次。
    ///     草稿不跟着配置变化走——别处改了配置，要重进这一页才看得到。
    /// </summary>
    private void RefreshDrafts()
    {
        QuoteRefreshDraft = Widgets.QuoteRefreshMinutes;
        WeatherRefreshDraft = Widgets.WeatherRefreshMinutes;
        QuoteBlockedWordsDraft = Widgets.QuoteBlockedWords;
    }

    /// <summary>屏蔽词草稿每变一次只重算下面那句计数——落盘要等按钮或离页，逐字写文件不合适。</summary>
    partial void OnQuoteBlockedWordsDraftChanged(string value)
    {
        OnPropertyChanged(nameof(BlockedWordsSummary));
        OnPropertyChanged(nameof(HasBlockedWords));
    }

    /// <summary>「现在屏蔽 3 个词」/「还没有屏蔽词」：让用户知道那段文本被认成了几行。</summary>
    public string BlockedWordsSummary =>
        QuoteBlocklist.Parse(QuoteBlockedWordsDraft) is { Count: > 0 } words
            ? string.Format(CR.Settings_Widgets_QuoteBlockedSummary, words.Count)
            : CR.Settings_Widgets_QuoteBlockedEmpty;

    /// <summary>有没有屏蔽词（摘要那行的强调色、清空按钮的可用性都看它）。</summary>
    public bool HasBlockedWords => QuoteBlocklist.Parse(QuoteBlockedWordsDraft).Count > 0;

    private void RefreshDerivedTexts()
    {
        var ignored = HitokotoQuery.FindIgnoredQueryKeys(QuoteExtraQueryDraft);
        IgnoredQueryHint = ignored.Count == 0
            ? string.Empty
            : string.Format(CR.Settings_Widgets_QuoteIgnored, string.Join("、", ignored));

        WeatherStatusText = _weatherService.LastSuccessAt is { } last
            ? string.Format(CR.Settings_Widgets_WeatherLastSuccess, last.ToLocalTime().ToString("HH:mm"))
            : CR.Settings_Widgets_WeatherNeverFetched;

        OnPropertyChanged(nameof(IgnoredQueryHint));
        OnPropertyChanged(nameof(HasIgnoredQueryHint));
        OnPropertyChanged(nameof(WeatherStatusText));
        OnPropertyChanged(nameof(CurrentCityText));

        // 这两行是从草稿/勾选算出来的，配置被重新读过之后也要跟着刷一遍。
        OnPropertyChanged(nameof(BlockedWordsSummary));
        OnPropertyChanged(nameof(HasBlockedWords));
        OnPropertyChanged(nameof(CategoriesSummary));
    }

    private ObservableCollection<QuoteCategoryItem> BuildCategoryItems()
    {
        var items = new ObservableCollection<QuoteCategoryItem>
        {
            new(QuoteCategory.Animation, CR.Settings_Widgets_Category_Animation),
            new(QuoteCategory.Comic, CR.Settings_Widgets_Category_Comic),
            new(QuoteCategory.Game, CR.Settings_Widgets_Category_Game),
            new(QuoteCategory.Literature, CR.Settings_Widgets_Category_Literature),
            new(QuoteCategory.Original, CR.Settings_Widgets_Category_Original),
            new(QuoteCategory.Web, CR.Settings_Widgets_Category_Web),
            new(QuoteCategory.Other, CR.Settings_Widgets_Category_Other),
            new(QuoteCategory.Movie, CR.Settings_Widgets_Category_Movie),
            new(QuoteCategory.Poetry, CR.Settings_Widgets_Category_Poetry),
            new(QuoteCategory.Philosophy, CR.Settings_Widgets_Category_Philosophy),
            new(QuoteCategory.Funny, CR.Settings_Widgets_Category_Funny)
        };

        // ⚠️ 顺序不能反：先写初始勾选、再挂监听。
        // IsSelected 由 false 变成 true 时 CommunityToolkit 会立刻发 PropertyChanged，
        // 处理器随即调 CollectCategories()——而它读的是还没有赋值的 QuoteCategoryItems（null），
        // 页面构造期就抛 NullReferenceException，整个应用跟着崩。
        foreach (var item in items)
            item.IsSelected = Widgets.QuoteCategories.HasFlag(item.Category);

        foreach (var item in items)
            item.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(QuoteCategoryItem.IsSelected))
                    return;

                Widgets.QuoteCategories = CollectCategories();

                // 下拉框按钮上那行字跟着勾选变（「全部类别」/「动画 等 3 类」）。
                OnPropertyChanged(nameof(CategoriesSummary));
            };

        return items;
    }

    /// <summary>把勾选拼回一个类别掩码；一个都没勾时按「全部」处理（服务端默认就是全部）。</summary>
    private QuoteCategory CollectCategories()
    {
        var flags = QuoteCategory.None;
        foreach (var item in QuoteCategoryItems.Where(item => item.IsSelected))
            flags |= item.Category;

        return flags == QuoteCategory.None ? QuoteCategory.All : flags;
    }

    /// <summary>
    ///     下拉框按钮上那行字：全勾（或不勾，语义都是「不限」）写「全部类别」，
    ///     只勾一个就写类别名，勾多个写「第一个 等 N 类」。
    /// </summary>
    private string BuildCategoriesSummary()
    {
        var selected = QuoteCategoryItems.Where(item => item.IsSelected).ToList();

        return selected.Count switch
        {
            0 => CR.Settings_Widgets_QuoteCategoriesAll,
            1 => selected[0].Name,
            _ when selected.Count == QuoteCategoryItems.Count => CR.Settings_Widgets_QuoteCategoriesAll,
            _ => string.Format(CR.Settings_Widgets_QuoteCategoriesSummary, selected[0].Name, selected.Count)
        };
    }
}

/// <summary>一言类别的一个勾选项（类别是 Flags，界面上拆成一行行复选框）。</summary>
public partial class QuoteCategoryItem(QuoteCategory category, string name) : ObservableObject
{
    public QuoteCategory Category { get; } = category;

    public string Name { get; } = name;

    [ObservableProperty] private bool _isSelected;
}
