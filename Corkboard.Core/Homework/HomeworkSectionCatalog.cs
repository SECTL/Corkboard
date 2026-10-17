using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Notes.Legacy;

namespace Corkboard.Core.Homework;

/// <summary>
///     作业板「定义」数据（科目清单与手动排序顺序）的读写：对应 <c>data/board/board.json</c>。
///     <para>
///         和作业本身（<c>&lt;年&gt;/&lt;月&gt;/&lt;日&gt;/notes.json</c>，见
///         <c>INoteStore</c>）分开：这里是用户维护的科目表与区块先后，
///         不随作业增删而整份重写。
///     </para>
///     <para>
///         从 <c>BoardService</c> 里抽出来的原因：便签 CRUD（通用的「板子」能力）与科目管理
///         （作业独有的定义）混在同一个类里，改一边总得看另一边。抽出来之后
///         <c>BoardService</c> 只剩「内存集合 + 落盘 + 通知」，科目的事全在这里。
///     </para>
/// </summary>
public sealed class HomeworkSectionCatalog
{
    private readonly ConfigServiceBase _configService;
    private readonly ILogger<HomeworkSectionCatalog>? _logger;
    private readonly List<string> _subjectOrder;
    private readonly List<HomeworkSection> _sections;
    private readonly ReadOnlyCollection<HomeworkSection> _readOnlySections;
    private HomeworkConfig _config;

    public HomeworkSectionCatalog(ConfigServiceBase configService, ILogger<HomeworkSectionCatalog>? logger = null)
    {
        _configService = configService;
        _logger = logger;

        // 定义文件在加载前是否已存在，决定了当前定义是「用户的」还是「刚打底的默认值」。
        DefinitionExists = _configService.IsConfigExists(new HomeworkConfig());
        _config = _configService.LoadConfig(new HomeworkConfig());

        Subjects = new ObservableCollection<string>(_config.Subjects);
        _subjectOrder = [.. _config.SubjectOrder];

        // 配置里的分类可能带空白（手改过 board.json）：这里收拾一份干净的，与作业里的名字对得上。
        _sections = _config.Sections
            .Where(section => !string.IsNullOrWhiteSpace(section.Subject) && !string.IsNullOrWhiteSpace(section.Name))
            .Select(section => new HomeworkSection
            {
                Subject = section.Subject.Trim(),
                Name = section.Name.Trim()
            })
            .ToList();
        _readOnlySections = _sections.AsReadOnly();
    }

    /// <summary>
    ///     构造时定义文件是否已经存在：旧版单文件里的科目**只在它不存在时**才接管，
    ///     否则会覆盖用户自己在设置里管过的科目表（哪怕他故意清空了）。
    /// </summary>
    public bool DefinitionExists { get; }

    /// <summary>科目清单。改完调 <see cref="Save" /> 落盘。</summary>
    public ObservableCollection<string> Subjects { get; }

    /// <summary>手动排序下的科目区块先后，改它请走 <see cref="SetSubjectOrder" />。</summary>
    public IReadOnlyList<string> SubjectOrder => _subjectOrder;

    /// <summary>全部二级分类定义（按 board.json 里记的顺序）。</summary>
    public IReadOnlyList<HomeworkSection> Sections => _readOnlySections;

    /// <summary>某个科目下有哪些分类（按定义顺序）。</summary>
    public IReadOnlyList<HomeworkSection> GetSections(string subject)
    {
        var key = subject.Trim();
        return _sections
            .Where(section => string.Equals(section.Subject, key, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>某个科目下的分类名字（布置作业表单的二级下拉框用）。</summary>
    public IReadOnlyList<string> GetSectionNames(string subject)
    {
        return GetSections(subject).Select(section => section.Name).ToList();
    }

    /// <summary>
    ///     加一条分类。科目或名字是空的时候返回 <c>null</c>；同科目下已经有同名分类时
    ///     直接返回那一条（「加两次」变成「选到已有的那条」，不会出现两条一样的）。
    ///     落盘交给调用方（和作业一起写，见 <c>BoardService.Persist</c>）。
    /// </summary>
    public HomeworkSection? AddSection(string subject, string name)
    {
        var subjectKey = subject.Trim();
        var nameKey = name.Trim();
        if (subjectKey.Length == 0 || nameKey.Length == 0)
            return null;

        var existing = _sections.FirstOrDefault(section =>
            string.Equals(section.Subject, subjectKey, StringComparison.Ordinal) &&
            string.Equals(section.Name, nameKey, StringComparison.Ordinal));
        if (existing is not null)
            return existing;

        var created = new HomeworkSection { Subject = subjectKey, Name = nameKey };
        _sections.Add(created);
        return created;
    }

    /// <summary>
    ///     改一条分类的名字。找不到、或者同科目下已经有新名字时返回 <c>false</c>，什么都不改。
    ///     作业里记着的旧分类名由调用方（<c>BoardService</c>）负责跟着改。
    /// </summary>
    public bool RenameSection(string subject, string oldName, string newName)
    {
        var subjectKey = subject.Trim();
        var oldKey = oldName.Trim();
        var newKey = newName.Trim();
        if (subjectKey.Length == 0 || oldKey.Length == 0 || newKey.Length == 0)
            return false;

        if (string.Equals(oldKey, newKey, StringComparison.Ordinal))
            return true;

        if (_sections.Any(section =>
                string.Equals(section.Subject, subjectKey, StringComparison.Ordinal) &&
                string.Equals(section.Name, newKey, StringComparison.Ordinal)))
            return false;

        var target = _sections.FirstOrDefault(section =>
            string.Equals(section.Subject, subjectKey, StringComparison.Ordinal) &&
            string.Equals(section.Name, oldKey, StringComparison.Ordinal));
        if (target is null)
            return false;

        target.Name = newKey;
        return true;
    }

    /// <summary>删一条分类定义。找不到时返回 <c>false</c>。作业本身不受影响（由调用方决定怎么处理）。</summary>
    public bool RemoveSection(string subject, string name)
    {
        var subjectKey = subject.Trim();
        var nameKey = name.Trim();
        if (subjectKey.Length == 0 || nameKey.Length == 0)
            return false;

        return _sections.RemoveAll(section =>
            string.Equals(section.Subject, subjectKey, StringComparison.Ordinal) &&
            string.Equals(section.Name, nameKey, StringComparison.Ordinal)) > 0;
    }

    /// <summary>
    ///     把旧版单文件里的科目并回当前定义。只在 <see cref="DefinitionExists" /> 为假时接管：
    ///     那说明这是我们第一次为这个用户建定义，旧文件里的就是他原来的科目。
    ///     旧文件本身由存储层负责删。
    /// </summary>
    public void ImportLegacy(LegacyNoteFile legacy)
    {
        if (DefinitionExists || legacy.Subjects.Count == 0)
            return;

        _config.Subjects = legacy.Subjects;

        // 构造期建的集合是旧配置的快照，接管之后要跟着换成搬过来的那一份。
        Subjects.Clear();
        foreach (var subject in _config.Subjects)
            Subjects.Add(subject);

        _logger?.LogDebug("Adopted {Count} subjects from the legacy file", legacy.Subjects.Count);
    }

    /// <summary>
    ///     记下手动排序的区块先后：去重、去空白，并把已经从科目清单里删掉的科目清出去。
    ///     落盘交给调用方（和作业一起写，见 <c>BoardService.Persist</c>）。
    /// </summary>
    public void SetSubjectOrder(IEnumerable<string> subjects)
    {
        ArgumentNullException.ThrowIfNull(subjects);

        _subjectOrder.Clear();
        foreach (var subject in subjects)
        {
            var name = subject.Trim();

            // 重复项只留第一次出现的那个；空科目（界面上是「未分类」区块）也要占位，
            // 否则用户怎么拖它都排不到指定位置、松手就被弹回最后。
            if (!_subjectOrder.Contains(name, StringComparer.Ordinal))
            {
                _subjectOrder.Add(name);
            }
        }

        // 已经从科目清单里删掉的科目没必要占着位置；留着也不会显示，只是脏数据。
        // 「未分类」不参与这条过滤：它本来就不在科目清单里，但它是板子上真实存在的区块。
        _subjectOrder.RemoveAll(name =>
            name.Length > 0 && !Subjects.Any(s => string.Equals(s.Trim(), name, StringComparison.Ordinal)));
    }

    /// <summary>把内存里的科目表、顺序与分类定义写回 <c>board.json</c>。</summary>
    public void Save()
    {
        _config.Subjects = [.. Subjects];
        _config.SubjectOrder = [.. _subjectOrder];

        // 已经不在科目清单里的科目的分类定义没必要留着（留着也不会显示，只是脏数据）。
        _sections.RemoveAll(section =>
            !Subjects.Any(subject => string.Equals(subject.Trim(), section.Subject, StringComparison.Ordinal)));

        _config.Sections = [.. _sections];
        _configService.SaveConfig(_config);
    }

    /// <summary>从磁盘重读定义并刷新内存集合（云同步恢复设置/科目后走这里）。</summary>
    public void Reload()
    {
        _config = _configService.LoadConfig(new HomeworkConfig());

        Subjects.Clear();
        foreach (var subject in _config.Subjects)
            Subjects.Add(subject);

        _subjectOrder.Clear();
        _subjectOrder.AddRange(_config.SubjectOrder);

        _sections.Clear();
        foreach (var section in _config.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.Subject) || string.IsNullOrWhiteSpace(section.Name))
                continue;

            _sections.Add(new HomeworkSection
            {
                Subject = section.Subject.Trim(),
                Name = section.Name.Trim()
            });
        }
    }
}
