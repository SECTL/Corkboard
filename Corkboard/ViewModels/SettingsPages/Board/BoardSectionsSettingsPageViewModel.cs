using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Board;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Board;

/// <summary>
///     「科目与分类」设置页：上面是科目清单，每个科目下面可以再建二级分类（小标题）。
///     <para>
///         科目与分类都是<b>业务数据</b>（存在 <c>board.json</c>），由 <see cref="IBoardService" /> 显式落盘，
///         不走配置管道，所以这里改完直接喊一声 <see cref="IBoardService.Save" /> 就行，没有防抖那一套。
///         科目清单与分类名都绑草稿属性：输入框只在失焦 / 回车 / 离开页面时提交，
///         不会每敲一个字符就写一次盘。
///     </para>
///     <para>
///         分类的增删改在服务里已经落盘并通知 <see cref="IBoardService.Changed" />，
///         页面订阅它重建行；作业板主页也订阅同一个事件，改完回主页面就是新的分层。
///     </para>
/// </summary>
public partial class BoardSectionsSettingsPageViewModel : ViewModelBase
{
    private readonly IBoardService _boardService;

    /// <summary>页面订阅的是单例服务，离开可视树时必须断开（见 <see cref="Detach" />）。</summary>
    public BoardSectionsSettingsPageViewModel(MainConfigHandler configHandler, IBoardService boardService)
        : base(configHandler)
    {
        _boardService = boardService;

        _boardService.Changed += OnBoardChanged;
        ReloadSubjectItems();
    }

    public BoardSettingsConfig Settings => Config.BoardSettings;

    /// <summary>科目列表：左边当导航用，右上角下拉框也绑同一份（两处选中态联动）。</summary>
    public ObservableCollection<BoardSectionsSubjectItem> SubjectItems { get; } = [];

    /// <summary>上一次选中的科目名，重建列表后按它把选中项找回来。</summary>
    private string? _selectedSubjectName;

    /// <summary>
    ///     当前在右半边编辑的科目。左边 <c>ListBox</c> 与右上角 <c>ComboBox</c> 都双向绑它，
    ///     所以「左边点一下」和「下拉框换一个」是同一个状态。
    /// </summary>
    [ObservableProperty] private BoardSectionsSubjectItem? _selectedSubjectItem;

    /// <summary>有没有选中的科目；没选时右半边显示一句提示，不显示空表单。</summary>
    public bool HasSelectedSubjectItem => SelectedSubjectItem is not null;

    partial void OnSelectedSubjectItemChanged(BoardSectionsSubjectItem? value)
    {
        _selectedSubjectName = value?.Subject;
        OnPropertyChanged(nameof(HasSelectedSubjectItem));
    }

    /// <summary>
    ///     新建科目的输入草稿。**不直接绑业务数据**：<see cref="IBoardService.Subjects" /> 是
    ///     「改集合再 Save」的用法，每敲一个字符改一次会很脏；现在是「输入框 + 添加按钮/回车」，
    ///     一次加一个（用户明确要求不要逗号分隔的那一套）。
    /// </summary>
    [ObservableProperty] private string _newSubjectDraft = string.Empty;

    /// <summary>输入框里有字才能点「添加」。</summary>
    public bool CanAddSubject => NewSubjectDraft.Trim().Length > 0;

    partial void OnNewSubjectDraftChanged(string value) => OnPropertyChanged(nameof(CanAddSubject));

    /// <summary>加科目失败（重名之类）时的一句话提示。</summary>
    [ObservableProperty] private string _subjectHint = string.Empty;

    /// <summary>提示有没有内容，模板据此显隐那一行。</summary>
    [ObservableProperty] private bool _hasSubjectHint;

    /// <summary>科目列表是不是空的（空的时候显示一句「还没有科目」）。</summary>
    public bool HasNoSubjects => SubjectItems.Count == 0;

    /// <summary>分类增删改失败时的一句话提示。成功时清空，不弹窗。</summary>
    [ObservableProperty] private string _sectionHint = string.Empty;

    /// <summary>提示有没有内容，模板据此显隐那一行（省得写值转换器）。</summary>
    [ObservableProperty] private bool _hasSectionHint;

    /// <summary>页面离开可视树时调用：断开对单例服务的订阅。</summary>
    public void Detach()
    {
        _boardService.Changed -= OnBoardChanged;
    }

    /// <summary>消息总线那边的作业板变动：分类定义可能被别处改了，重建行。</summary>
    private void OnBoardChanged(object? sender, EventArgs e) => ReloadSubjectItems();

    /// <summary>
    ///     重建「科目 → 分类」两张表。只保留「科目清单里有的」与「定义过分类的」科目：
    ///     分类定义跟着科目走，没定义过分类的科目在这页没什么可编辑的。
    ///     正在输入的新分类草稿按科目名带过去，重建不把用户敲了一半的字弄丢。
    /// </summary>
    public void ReloadSubjectItems()
    {
        var pendingDrafts = SubjectItems.ToDictionary(
            item => item.Subject, item => item.NewCategoryDraft, StringComparer.Ordinal);

        SubjectItems.Clear();
        foreach (var subject in EnumerateSubjects())
        {
            var item = new BoardSectionsSubjectItem(subject);
            if (pendingDrafts.TryGetValue(subject, out var draft))
                item.NewCategoryDraft = draft;

            foreach (var section in _boardService.GetSections(subject))
                item.Categories.Add(new BoardSectionsCategoryItem(subject, section.Name));

            item.RefreshHasCategories();
            SubjectItems.Add(item);
        }

        // 重建后把选中项找回来（按科目名；第一次进入就选第一个），
        // 否则每改一次分类都会跳回第一个科目。
        SelectedSubjectItem = SubjectItems.FirstOrDefault(item => item.Subject == _selectedSubjectName)
                              ?? SubjectItems.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoSubjects));
    }

    /// <summary>不重名地列出要编辑的科目：先按科目清单的顺序，再补上只在分类定义里出现的科目。</summary>
    private IEnumerable<string> EnumerateSubjects()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var subject in _boardService.Subjects)
        {
            var name = subject.Trim();
            if (name.Length > 0 && seen.Add(name))
                yield return name;
        }

        foreach (var section in _boardService.Sections)
        {
            var name = section.Subject.Trim();
            if (name.Length > 0 && seen.Add(name))
                yield return name;
        }
    }

    /// <summary>
    ///     把输入框里的科目加进清单并落盘。重名时给一句提示、不重复加。
    ///     落盘会通知 <see cref="IBoardService.Changed" />，列表跟着重建。
    /// </summary>
    public bool AddSubject()
    {
        var name = NewSubjectDraft.Trim();
        if (name.Length == 0)
            return false;

        if (_boardService.Subjects.Any(subject => string.Equals(subject.Trim(), name, StringComparison.CurrentCultureIgnoreCase)))
        {
            ShowSubjectHint(CR.Settings_Board_Subjects_Duplicate);
            return false;
        }

        _boardService.Subjects.Add(name);
        _boardService.Save();
        NewSubjectDraft = string.Empty;
        ClearSubjectHint();
        return true;
    }

    /// <summary>
    ///     删掉一个科目（调用方已经确认过）：先把它下面的分类定义删掉，再从清单里移除并落盘。
    ///     删的是定义，已经布置的作业不会被删。
    /// </summary>
    public void RemoveSubject(BoardSectionsSubjectItem item)
    {
        foreach (var section in _boardService.GetSections(item.Subject).ToList())
            _boardService.RemoveSection(item.Subject, section.Name);

        _boardService.Subjects.Remove(item.Subject);
        _boardService.Save();
        ClearSubjectHint();
    }

    /// <summary>提交分类标题字号草稿。手输越界值时夹回 9–32，并把夹过的值同步回界面。</summary>

    /// <summary>设置页所有编辑的收口：草稿提交（输入框失焦与离开页面时都会调）。</summary>


    /// <summary>在某个科目下新建一条分类。重名时给一句话提示，不重复建。</summary>
    public bool AddCategory(BoardSectionsSubjectItem item)
    {
        var name = item.NewCategoryDraft.Trim();
        if (name.Length == 0)
            return false;

        if (HasCategory(item.Subject, name))
        {
            ShowSectionHint(CR.Settings_Board_Sections_DuplicateName);
            return false;
        }

        // 服务里已经落盘并通知（订阅会重建本页的行），这里只清掉输入框。
        _boardService.AddSection(item.Subject, name);
        item.NewCategoryDraft = string.Empty;
        ClearSectionHint();
        return true;
    }

    /// <summary>
    ///     提交某一行的重命名草稿。空白或没改就原样退回；撞上已有分类名时不改，给一句提示。
    ///     改名成功时服务会把记着旧名字的作业一起改过来。
    /// </summary>
    public void CommitCategoryRename(BoardSectionsCategoryItem item)
    {
        var name = item.NameDraft.Trim();
        if (name.Length == 0 || string.Equals(name, item.Name, StringComparison.Ordinal))
        {
            item.NameDraft = item.Name;
            return;
        }

        if (HasCategory(item.Subject, name))
        {
            item.NameDraft = item.Name;
            ShowSectionHint(CR.Settings_Board_Sections_DuplicateName);
            return;
        }

        _boardService.RenameSection(item.Subject, item.Name, name);

        // 服务不返回「成功与否」（返回的是跟着改名的作业条数，0 条也可能是成功），
        // 所以以「新名字真的在定义里了」为准。
        if (HasCategory(item.Subject, name))
        {
            item.MarkRenamed(name);
            ClearSectionHint();
        }
        else
        {
            item.NameDraft = item.Name;
            ShowSectionHint(CR.Settings_Board_Sections_DuplicateName);
        }
    }

    /// <summary>
    ///     删掉一条分类（调用方已经确认过）。删的是分类定义：里面的作业由服务退回科目的「未分类」，
    ///     不会连作业一起删掉。
    /// </summary>
    public void RemoveCategory(BoardSectionsCategoryItem item)
    {
        _boardService.RemoveSection(item.Subject, item.Name);
        ClearSectionHint();
    }

    private bool HasCategory(string subject, string name)
        => _boardService.GetSections(subject)
            .Any(section => string.Equals(section.Name, name, StringComparison.Ordinal));

    private void ShowSectionHint(string text)
    {
        SectionHint = text;
        HasSectionHint = text.Length > 0;
    }

    private void ClearSectionHint() => ShowSectionHint(string.Empty);

    private void ShowSubjectHint(string text)
    {
        SubjectHint = text;
        HasSubjectHint = text.Length > 0;
    }

    private void ClearSubjectHint() => ShowSubjectHint(string.Empty);
}

/// <summary>
///     「科目与分类」页里的一个科目：科目名、它下面的分类行，以及新建分类的输入草稿。
/// </summary>
public partial class BoardSectionsSubjectItem : ObservableObject
{
    public BoardSectionsSubjectItem(string subject) => Subject = subject;

    /// <summary>落盘用的科目名（显示名就是它；空串表示「未分类」，正常情况下不会出现在这里）。</summary>
    public string Subject { get; }

    /// <summary>显示名：科目为空时退回资源里的「未分类」，免得卡片标题是空的。</summary>
    public string DisplayName => Subject.Length > 0 ? Subject : CR.Board_Uncategorized;

    public ObservableCollection<BoardSectionsCategoryItem> Categories { get; } = [];

    /// <summary>这个科目还没有任何分类时显示一句说明，而不是留一块空白。</summary>
    [ObservableProperty] private bool _hasCategories;

    /// <summary>「新建分类」输入框的草稿。</summary>
    [ObservableProperty] private string _newCategoryDraft = string.Empty;

    /// <summary>输入框里有字才能点「新建分类」。</summary>
    public bool CanAddCategory => NewCategoryDraft.Trim().Length > 0;

    public void RefreshHasCategories()
    {
        HasCategories = Categories.Count > 0;
        OnPropertyChanged(nameof(CategoryCount));
        OnPropertyChanged(nameof(CategoryCountText));
    }

    /// <summary>这个科目下有几条分类（左边导航与下拉框里显示用）。</summary>
    public int CategoryCount => Categories.Count;

    /// <summary>「N 个分类」的文案，空分类时说「还没有分类」。</summary>
    public string CategoryCountText => CategoryCount > 0
        ? string.Format(CR.Settings_Board_Sections_CategoryCount, CategoryCount)
        : CR.Settings_Board_Sections_NoCategories;

    partial void OnNewCategoryDraftChanged(string value) => OnPropertyChanged(nameof(CanAddCategory));
}

/// <summary>
///     「科目与分类」页里的一行分类：落盘用的旧名字 + 输入框里的新名字草稿。
/// </summary>
public partial class BoardSectionsCategoryItem : ObservableObject
{
    public BoardSectionsCategoryItem(string subject, string name)
    {
        Subject = subject;
        Name = name;
        _nameDraft = name;
    }

    /// <summary>这条分类属于哪个科目（重命名 / 删除都要带上它）。</summary>
    public string Subject { get; }

    /// <summary>服务里记着的分类名，提交重命名时当旧名字用。</summary>
    public string Name { get; private set; }

    /// <summary>重命名输入框的草稿。</summary>
    [ObservableProperty] private string _nameDraft;

    /// <summary>重命名成功后把旧名字也更新掉，免得同一行再失焦一次又拿旧名字去改。</summary>
    public void MarkRenamed(string name)
    {
        Name = name;
        NameDraft = name;
    }
}
