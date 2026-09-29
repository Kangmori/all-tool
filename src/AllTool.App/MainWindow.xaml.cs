using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using AllTool.Core.Diagnostics;
using AllTool.Core.Discovery;
using AllTool.Core.Execution;
using AllTool.Core.Manifest;
using AllTool.Core.Settings;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AllTool.App;

/// <summary>
/// 宿主主窗口。
///
/// 界面完全是"清单驱动"的：左侧列出工具包与动作，中间的表单由 <see cref="BuildForm"/>
/// 按字段定义动态生成，执行前把将要运行的命令原样显示出来（硬规则 R6）。
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>输出框最多保留多少行（避免超长输出把界面拖慢）。完整输出会写进日志。</summary>
    private const int MaxOutputLines = 1000;

    /// <summary>界面上最多攒多久刷一次输出（毫秒）。见 <see cref="AppendOutput"/> 的说明。</summary>
    private const int OutputFlushIntervalMs = 150;

    private readonly ProcessRunner _runner = new();
    private readonly LastValuesStore _lastValues = LastValuesStore.OpenDefault();
    private readonly GroupingStore _grouping = GroupingStore.OpenDefault();
    private readonly List<PackageEntry> _packages = [];
    private readonly List<ActionEntry> _actions = [];
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly List<(string Id, Func<object?> Get)> _valueSync = [];
    private readonly List<string> _outputLines = [];

    /// <summary>
    /// 界面输出**攒到一定量或一定时间**再刷新的开关。
    ///
    /// 为什么必须这么做：原来的写法是"每来一行就 <c>string.Join</c> 出整段文本再赋给 TextBox"，
    /// 一行一次就等于 O(行数²) 的字符串拷贝 + 一次完整重排。`ipconfig /displaydns` 有 1.6 MB 输出，
    /// 结果就是界面直接无响应（用户实测反馈）。
    /// 现在：行只往列表里塞，真正的文本重建由定时器按 <see cref="OutputFlushIntervalMs"/> 触发。
    /// </summary>
    private bool _outputDirty;

    private DispatcherQueueTimer? _outputTimer;

    /// <summary>等待写进日志的原始输出行（清单声明的命令输出，完整版）。</summary>
    private readonly List<string> _pendingLogLines = [];

    private readonly FileLogger _logger = FileLogger.OpenDefault();

    /// <summary>字段 id → 生成的控件，用于必填校验时高亮与聚焦。</summary>
    private readonly Dictionary<string, FrameworkElement> _fieldControls = new(StringComparer.Ordinal);

    /// <summary>字段 id → 可接受拖放的输入框（用于"拖到窗口空白处"时找第一个空的路径字段）。</summary>
    private readonly Dictionary<string, TextBox> _pathBoxes = new(StringComparer.Ordinal);

    private ToolManifest? _manifest;
    private ManifestAction? _action;
    private string? _executablePath;
    private CancellationTokenSource? _cancellation;

    /// <summary>「下一步」按钮点了之后要先填进表单、再执行，所以值在这里中转一次。</summary>
    private IReadOnlyDictionary<string, object?>? _pendingPresets;

    public MainWindow()
    {
        InitializeComponent();

        // 「工具包」/「动作」标题：右键新建分组、展开/收起、恢复默认分组，并接收"拖到标题上恢复默认"
        WireSectionTitle(PackageSectionTitle, forPackages: true, PackageGroupsPanel);
        WireSectionTitle(ActionSectionTitle, forPackages: false, ActionGroupsPanel);

        // 把工具包（文件夹 / .zip / manifest.yaml）直接拖到「工具包」区域即可安装
        AllowPackageFileDrop(PackageSectionTitle);
        AllowPackageFileDrop(PackageGroupsPanel);

        StartOutputTimer();
        _logger.Info($"程序启动：{typeof(MainWindow).Assembly.GetName().Version}，"
                     + $"运行环境 .NET {Environment.Version} / {Environment.OSVersion.VersionString}");

        LoadPackages();
    }

    /// <summary>
    /// 输出刷新的定时器：把"每行都重建一次界面文本"改成"最多每 150 ms 重建一次"。
    /// 输出再快也不会让 UI 线程忙死；同时顺便把攒下的原始行批写进日志。
    /// </summary>
    private void StartOutputTimer()
    {
        _outputTimer = DispatcherQueue.CreateTimer();
        _outputTimer.Interval = TimeSpan.FromMilliseconds(OutputFlushIntervalMs);
        _outputTimer.IsRepeating = true;
        _outputTimer.Tick += (_, _) => FlushOutput(force: false);
        _outputTimer.Start();
    }

    /// <summary>把攒下的输出真正写到界面与日志上。</summary>
    private void FlushOutput(bool force)
    {
        if (!_outputDirty && _pendingLogLines.Count == 0)
        {
            return;
        }

        if (!force && !_outputDirty)
        {
            // 只有日志待写时不碰界面
            FlushLogLines();
            return;
        }

        if (_outputDirty)
        {
            try
            {
                var text = string.Join(Environment.NewLine, _outputLines);
                OutputBox.Text = text;
                OutputBox.SelectionStart = text.Length;   // 注意：不要写 OutputBox.Text.Length，那会再拷一次整串
            }
            catch (Exception ex)
            {
                _logger.Exception("刷新输出区时", ex);
            }

            _outputDirty = false;
        }

        FlushLogLines();
    }

    private void FlushLogLines()
    {
        if (_pendingLogLines.Count == 0)
        {
            return;
        }

        _logger.WriteLines(LogLevel.Info, _pendingLogLines);
        _pendingLogLines.Clear();
    }

    // ------------------------------------------------------------------ 左栏：工具包与动作（按分组呈现）

    /// <summary>记住上次选中的工具包/动作，下次打开直接回到那里。</summary>
    private const string UiStateScope = "__ui__";

    /// <summary>实际使用的工具包目录（安装/卸载都作用在这里）。</summary>
    private string _pluginsRoot = string.Empty;

    /// <summary>
    /// 当前展开着的分组（键形如 <c>pkg:包管理</c> / <c>act:应用管理</c>）。
    /// 列表会因为"选中条目/筛选/改分组"而重建，展开状态必须跨重建保留，
    /// 否则用户刚点开的分组会立刻被收起来（实测被反馈过）。启动时是空的 = 全部收起。
    /// </summary>
    private readonly HashSet<string> _expandedGroups = new(StringComparer.Ordinal);

    /// <summary>
    /// 接收从资源管理器拖来的**工具包**：文件夹（含 manifest.yaml）、.zip、或单个清单文件。
    /// 与"拖动条目改分组"是两种不同的数据格式，所以互不干扰。
    /// </summary>
    private void AllowPackageFileDrop(UIElement target)
    {
        target.AllowDrop = true;

        target.DragOver += (_, e) =>
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "松手即安装这个工具包";
                e.DragUIOverride.IsCaptionVisible = true;
                e.Handled = true;
            }
        };

        target.Drop += async (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            e.Handled = true;
            var items = await e.DataView.GetStorageItemsAsync();

            foreach (var item in items)
            {
                if (!string.IsNullOrWhiteSpace(item.Path))
                {
                    InstallFrom(item.Path);
                }
            }
        };
    }

    private void ReloadPackages()
    {
        _packages.Clear();
        _actions.Clear();
        _manifest = null;
        _action = null;
        _executablePath = null;

        FormPanel.Children.Clear();
        CommandLineBox.Text = string.Empty;
        RunButton.IsEnabled = false;
        NextStepsPanel.Children.Clear();
        NextStepsPanel.Visibility = Visibility.Collapsed;

        LoadPackages();
    }

    private void LoadPackages()
    {
        try
        {
            _pluginsRoot = RepoPaths.FindPluginsDirectory();

            foreach (var (directory, manifest) in ManifestLoader.LoadAll(_pluginsRoot))
            {
                _packages.Add(new PackageEntry(
                    $"{manifest.Name}（{manifest.Id}）",
                    directory,
                    manifest));
            }

            RebuildPackageList();

            StatusText.Text = _packages.Count == 0
                ? $"在 {_pluginsRoot} 里没找到任何工具包（可以把工具包文件夹拖进来安装）"
                : $"已载入 {_packages.Count} 个工具包，请选择";

            _logger.Info($"载入工具包：{_packages.Count} 个（目录 {_pluginsRoot}）");

            // 恢复上次的选择；没有记录就选第一个，省一次点击。
            // 自动化冒烟可以用 ALLTOOL_SELECT_PACKAGE 钉住工具包——只靠 ALLTOOL_SELECT_ACTION 不够，
            // 因为"恢复上次选择"会让那个序号套用到别的包上（脚本会静默失灵，踩过）。
            var forcedPackage = Environment.GetEnvironmentVariable("ALLTOOL_SELECT_PACKAGE");
            var lastPackage = _lastValues.Get(UiStateScope, "state", "lastPackage");

            var target = _packages.FirstOrDefault(p => p.Manifest.Id == forcedPackage)
                ?? _packages.FirstOrDefault(p => p.Manifest.Id == lastPackage)
                ?? _packages.FirstOrDefault();

            if (target is not null)
            {
                SelectPackage(target, restoreAction: true);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "载入工具包失败：" + ex.Message;
            _logger.Exception("载入工具包时", ex);
        }
    }

    private string PackageGroupOf(PackageEntry entry) =>
        _grouping.GetPackageGroup(entry.Manifest.Id ?? string.Empty)
        ?? (string.IsNullOrWhiteSpace(entry.Manifest.Category) ? "未分组" : entry.Manifest.Category!);

    private string ActionGroupOf(ManifestAction action) =>
        _grouping.GetActionGroup(_manifest?.Id ?? string.Empty, action.Id ?? string.Empty)
        ?? (string.IsNullOrWhiteSpace(action.Category) ? "未分组" : action.Category!);

    /// <summary>
    /// 工具包条目的悬停说明：优先用清单里的 <c>summary</c>（一句话），
    /// 没有就退回 <c>description</c> 的第一句——总比什么都不显示强。
    /// </summary>
    private static string? TooltipFor(ToolManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.Summary))
        {
            return $"{manifest.Name}：{manifest.Summary}";
        }

        var description = manifest.Description;

        if (string.IsNullOrWhiteSpace(description))
        {
            return manifest.Name;
        }

        var firstSentence = description.Split(['。', '.', '；', ';'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        return $"{manifest.Name}：{firstSentence?.Trim()}";
    }

    /// <summary>按分组重建左侧工具包列表。</summary>
    private void RebuildPackageList()
    {
        var filter = FilterBox?.Text?.Trim() ?? string.Empty;

        var visible = _packages
            .Where(p => Matches(filter, p.Display, p.Manifest.Description, p.Manifest.Id))
            .ToList();

        PackageGroupsPanel.Children.Clear();
        _packageVisuals.Clear();

        foreach (var group in GroupBy(visible, PackageGroupOf, _grouping.GetCustomGroups(forPackages: true)))
        {
            var groupName = group.Key;

            PackageGroupsPanel.Children.Add(BuildGroup(
                groupName,
                group.Items,
                entry => new GroupItem(
                    entry.Manifest.Id ?? string.Empty,
                    entry.Display,
                    ReferenceEquals(entry.Manifest, _manifest),
                    () => SelectPackage(entry, restoreAction: false),
                    menu => BuildPackageMenu(menu, entry),
                    TooltipFor(entry.Manifest)),
                isPackageScope: true,
                _packageVisuals));
        }
    }

    private void RebuildActionList()
    {
        var filter = FilterBox?.Text?.Trim() ?? string.Empty;

        var visible = _actions
            .Where(a => Matches(filter, a.Display, a.Action.Title, a.Action.Command, a.Action.Description))
            .ToList();

        ActionGroupsPanel.Children.Clear();
        _actionVisuals.Clear();

        if (_manifest is null)
        {
            return;
        }

        // 动作的分组**完全由清单定义**（作者最清楚自己的命令该怎么归类），
        // 所以这里不接收用户新建的分组、也不允许拖动改分组——避免"清单说一套、界面另一套"的矛盾。
        foreach (var group in GroupBy(visible, a => ActionGroupOf(a.Action)))
        {
            var groupName = group.Key;

            ActionGroupsPanel.Children.Add(BuildGroup(
                groupName,
                group.Items,
                entry => new GroupItem(
                    entry.Action.Id ?? string.Empty,
                    entry.Display,
                    ReferenceEquals(entry.Action, _action),
                    () => SelectAction(entry),
                    menu => BuildActionMenu(menu, entry),
                    entry.Action.Description),
                isPackageScope: false,
                _actionVisuals));
        }
    }

    /// <summary>
    /// 动作条目的右键菜单。"联机帮助"用系统默认浏览器去搜这个命令的帮助——
    /// 工具包不可能把每个开关都写成字段，遇到没覆盖到的开关时这是最直接的出路。
    /// </summary>
    private void BuildActionMenu(MenuFlyout menu, ActionEntry entry)
    {
        var tool = _manifest?.Name ?? _manifest?.Id ?? string.Empty;
        var command = entry.Action.Command ?? string.Empty;

        menu.Items.Add(MenuItem("联机帮助（用默认浏览器搜索）", () => OpenSearch($"{tool} {command} 命令 帮助 用法")));
        menu.Items.Add(MenuItem("复制这个命令", () => CopyToClipboard($"{tool} {command}".Trim(), "命令名")));

        // 清单里如果写了网页地址，就顺手给一个"看出处"的入口
        var source = (entry.Action.Sources ?? []).FirstOrDefault(s =>
            !string.IsNullOrWhiteSpace(s.Url) && s.Url!.StartsWith("http", StringComparison.OrdinalIgnoreCase));

        if (source is not null)
        {
            menu.Items.Add(MenuItem($"看出处：{source.Title}", () => OpenUrl(source.Url, "出处文档")));
        }
    }

    /// <summary>按分组聚合。用户新建的空分组也要出现，否则没有地方可以把条目拖进去。</summary>
    private static List<(string Key, List<T> Items)> GroupBy<T>(
        List<T> items,
        Func<T, string> groupSelector,
        IReadOnlyList<string>? extraGroups = null)
    {
        var grouped = items
            .GroupBy(groupSelector, StringComparer.CurrentCulture)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.CurrentCulture);

        foreach (var extra in extraGroups ?? [])
        {
            grouped.TryAdd(extra, []);
        }

        return grouped
            .OrderBy(pair => pair.Key == "未分组" ? 2 : pair.Value.Count == 0 ? 1 : 0)
            .ThenBy(pair => pair.Key, StringComparer.CurrentCulture)
            .Select(pair => (pair.Key, pair.Value))
            .ToList();
    }

    private static bool Matches(string filter, params string?[] candidates) =>
        filter.Length == 0
        || candidates.Any(c => c is not null && c.Contains(filter, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>左栏里一个条目的呈现信息。</summary>
    private sealed record GroupItem(
        string Key,
        string Text,
        bool Selected,
        Action Select,
        Action<MenuFlyout> BuildMenu,
        string? Tooltip = null);

    /// <summary>
    /// 条目的可视元素（按钮 + 左侧那根强调色标记），用于**原地改高亮**。
    ///
    /// 为什么要留着它们：选中一个条目只需要换高亮，如果为此把整个列表重建一遍，
    /// 新建的 Expander 会从 0 高度播放展开动画——用户看到的就是"分组迅速折叠又展开"（已被反馈）。
    /// 所以选中时只更新这里登记的元素，绝不重建列表。
    /// </summary>
    private readonly Dictionary<string, (Button Button, Border Marker)> _packageVisuals = new(StringComparer.Ordinal);

    private readonly Dictionary<string, (Button Button, Border Marker)> _actionVisuals = new(StringComparer.Ordinal);

    /// <summary>原地刷新选中态（不重建控件、不触发任何展开动画）。</summary>
    private void UpdateSelectionHighlights(bool forPackages)
    {
        var registry = forPackages ? _packageVisuals : _actionVisuals;
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        foreach (var (key, visual) in registry)
        {
            var selected = forPackages
                ? string.Equals(key, _manifest?.Id, StringComparison.Ordinal)
                : ReferenceEquals(_action, _actions.FirstOrDefault(a => a.Action.Id == key)?.Action);

            visual.Button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            visual.Marker.Background = selected ? accent : clear;
        }
    }

    /// <summary>
    /// 生成一个可折叠的分组：标题 + 组内条目。
    ///
    /// 交互约定：
    ///   1. **展开状态跨重建保留**（选中条目会重建列表，不能因此把人家展开的分组收起来）；
    ///      只有启动时是全部收起的。
    ///   2. **分组靠右键菜单改，不做鼠标拖动**——拖放在真实使用中不可靠
    ///      （WinUI 里 Button 的 CanDrag 与点击手势会打架），菜单是确定能用的路径。
    /// </summary>
    private Expander BuildGroup<T>(
        string title,
        List<T> items,
        Func<T, GroupItem> describe,
        bool isPackageScope,
        Dictionary<string, (Button Button, Border Marker)> registry)
    {
        var list = new StackPanel { Spacing = 1 };

        foreach (var item in items)
        {
            var info = describe(item);

            var button = new Button
            {
                Content = info.Text,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 4, 8, 4),
                Background = null,
                BorderThickness = new Thickness(0),
                FontWeight = info.Selected ? FontWeights.SemiBold : FontWeights.Normal,
            };
            button.Click += (_, _) => info.Select();

            var menu = new MenuFlyout();
            info.BuildMenu(menu);
            button.ContextFlyout = menu;

            // 悬停提示：工具包显示一句话说明，动作显示它的 description（界面最省事的"这是什么"）
            if (!string.IsNullOrWhiteSpace(info.Tooltip))
            {
                ToolTipService.SetToolTip(button, info.Tooltip);
            }

            // 选中项左侧加一小段强调色，比整块高亮更稳妥（不依赖主题资源名）
            var marker = new Border
            {
                Width = 3,
                Background = info.Selected
                    ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                CornerRadius = new CornerRadius(2),
            };

            // 登记起来，之后选中态就靠原地改这两个元素，不再重建列表
            if (!string.IsNullOrEmpty(info.Key))
            {
                registry[info.Key] = (button, marker);
            }

            var row = new Grid { ColumnSpacing = 6 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(marker, 0);
            Grid.SetColumn(button, 1);
            row.Children.Add(marker);
            row.Children.Add(button);

            list.Children.Add(row);
        }

        var header = new TextBlock
        {
            Text = $"{title}（{items.Count}）",
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var scopeKey = (isPackageScope ? "pkg:" : "act:") + title;

        var expander = new Expander
        {
            Header = header,
            Content = list,

            // 展开状态**跨重建保留**：选中一个条目会重建列表，若这里一律写 false，
            // 用户刚展开的分组就会全部收起（实测被反馈过）。
            IsExpanded = _expandedGroups.Contains(scopeKey),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 2, 0, 0),
        };

        expander.Expanding += (_, _) => _expandedGroups.Add(scopeKey);
        expander.Collapsed += (_, _) => _expandedGroups.Remove(scopeKey);

        return expander;
    }

    private DispatcherQueueTimer? _filterTimer;

    /// <summary>
    /// 筛选框防抖：每敲一个字就重建整棵左栏（几十个分组、上百个按钮）在工具包多起来之后很浪费。
    /// 停手 200 ms 之后再重建一次即可。
    /// </summary>
    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        _filterTimer ??= CreateFilterTimer();

        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private DispatcherQueueTimer CreateFilterTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(200);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RebuildPackageList();
            RebuildActionList();
        };

        return timer;
    }

    private void SelectPackage(PackageEntry entry, bool restoreAction)
    {
        _manifest = entry.Manifest;
        _executablePath = null;

        _actions.Clear();
        foreach (var action in _manifest.Actions ?? [])
        {
            _actions.Add(new ActionEntry(
                $"{action.Title}（{(string.IsNullOrEmpty(action.Command) ? "-" : action.Command)}）",
                action));
        }

        _action = null;

        // 还没选动作时，这里应当解释"这个工具包是干什么的"，而不是一句干巴巴的提示。
        ActionTitle.Text = string.IsNullOrWhiteSpace(_manifest.Summary)
            ? _manifest.Name ?? string.Empty
            : $"{_manifest.Name} —— {_manifest.Summary}";

        ActionDescription.Text = BuildPackageIntro(_manifest);
        FormPanel.Children.Clear();
        CommandLineBox.Text = string.Empty;
        NextStepsPanel.Children.Clear();
        NextStepsPanel.Visibility = Visibility.Collapsed;
        RunButton.IsEnabled = false;

        // 工具包列表**不重建**（只换高亮，避免分组播放折叠/展开动画）；
        // 动作列表必须重建——换了工具包，动作本来就是另一批。
        UpdateSelectionHighlights(forPackages: true);
        RebuildActionList();

        StatusText.Text = $"{_manifest.Name} {_manifest.AppVersion} —— {_actions.Count} 个动作";
        _lastValues.Set(UiStateScope, "state", "lastPackage", _manifest.Id);

        // 自动化冒烟用的钩子：设了 ALLTOOL_SELECT_ACTION=<序号> 就自动选中该动作并生成表单。
        // 存在的理由：动态表单是最容易在运行时出错的地方，需要一个不靠人点鼠标的验证入口。
        var hook = Environment.GetEnvironmentVariable("ALLTOOL_SELECT_ACTION");
        if (int.TryParse(hook, out var index) && index >= 0 && index < _actions.Count)
        {
            SelectAction(_actions[index]);
            return;
        }

        if (restoreAction)
        {
            var lastAction = _lastValues.Get(UiStateScope, "state", $"lastAction:{_manifest.Id}");
            var previous = _actions.FirstOrDefault(a => a.Action.Id == lastAction);
            if (previous is not null)
            {
                SelectAction(previous);
            }
        }
    }

    /// <summary>
    /// 按清单声明的 <c>execution</c> 调整界面（规范 §2.6）：
    ///   run      —— 正常：宿主自己执行并捕获输出；
    ///   info     —— **不执行**：只把命令行摊开给你看，可以复制、可以丢进真终端；
    ///   terminal —— 需要交互或会弹窗：同样不捕获输出，直接在真终端里打开。
    ///
    /// 判断标准是"这一步该不该由工具替你做决定"，而不是"命令危不危险"。
    /// </summary>
    private void ApplyExecutionMode()
    {
        if (_action is null)
        {
            return;
        }

        var mode = _action.ExecutionOrDefault;
        var hostRuns = _action.HostRunsIt;

        RunButton.IsEnabled = hostRuns;
        RunButton.Content = hostRuns ? "执行" : "（此动作不直接执行）";
        TerminalButton.IsEnabled = true;

        if (hostRuns)
        {
            return;
        }

        var why = mode == "terminal"
            ? "这个命令需要交互或会弹出窗口，宿主没法可靠地驱动它。"
            : "这个命令会改动系统状态，宿主的判断是：**这一步该由你自己按下回车**。";

        ActionDescription.Text += Environment.NewLine + Environment.NewLine
            + "⚠ " + why + Environment.NewLine
            + "命令已经拼好并且随时可复制；要执行就点「在终端中打开」，它会在真正的命令行窗口里跑同一条命令。";
    }

    /// <summary>把当前这条命令放进真终端里执行（同一条命令，输出与交互由用户自己看）。</summary>
    private void OpenInTerminal()
    {
        if (_executablePath is null || _action is null)
        {
            StatusText.Text = "还没定位到可执行文件，无法打开终端";
            return;
        }

        IReadOnlyList<string> argv;

        try
        {
            SyncValues();
            argv = ArgvBuilder.Build(_action, _values);
        }
        catch (Exception ex)
        {
            StatusText.Text = "参数有误，无法打开终端：" + ex.Message;
            return;
        }

        // 复用执行层那条"CreateProcess 只接受一条命令行"的引号规则，避免自己拼错
        var commandLine = WindowsCommandLine.Build(_executablePath, argv);

        try
        {
            // cmd /k 保留窗口，用户能看到输出、也能继续敲命令
            Process.Start(new ProcessStartInfo("cmd.exe", $"/k {commandLine}")
            {
                UseShellExecute = true,
                WorkingDirectory = ResolveWorkingDirectory() ?? Environment.CurrentDirectory,
            });

            _logger.Info($"在终端中打开：{commandLine}");
            StatusText.Text = "已在终端中打开同一条命令";
        }
        catch (Exception ex)
        {
            _logger.Exception("打开终端时", ex);
            StatusText.Text = "打不开终端：" + ex.Message;
        }
    }

    private void OnOpenTerminalClicked(object sender, RoutedEventArgs e)
    {
        // 没定位过就先定位一次（与执行流程共用同一条查找逻辑）
        if (_executablePath is not null)
        {
            OpenInTerminal();
            return;
        }

        _ = LocateThenOpenTerminalAsync();
    }

    private async Task LocateThenOpenTerminalAsync()
    {
        if (_manifest?.Locate is null)
        {
            StatusText.Text = "这个工具包没有声明可执行文件";
            return;
        }

        StatusText.Text = "正在查找可执行文件…";
        var location = await ToolLocator.LocateAsync(_manifest.Locate, _runner);

        if (location is null)
        {
            StatusText.Text = $"找不到 {_manifest.Locate.Executable}。{_manifest.Locate.NotFoundHint}";
            return;
        }

        _executablePath = location.ExecutablePath;
        OpenInTerminal();
    }

    /// <summary>选中工具包但还没选动作时展示的介绍。</summary>
    private static string BuildPackageIntro(ToolManifest manifest)
    {
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(manifest.Description))
        {
            lines.Add(manifest.Description!);
        }

        var actions = manifest.Actions?.Count ?? 0;
        var groups = (manifest.Actions ?? [])
            .Select(a => a.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .Count();

        lines.Add($"共 {actions} 个动作" + (groups > 0 ? $"，分 {groups} 组" : string.Empty));
        lines.Add("← 在左侧「动作」里选一个；鼠标停在条目上能看到简要说明。");

        if (!string.IsNullOrWhiteSpace(manifest.Homepage))
        {
            lines.Add($"官网：{manifest.Homepage}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void SelectAction(ActionEntry entry, IReadOnlyDictionary<string, object?>? presets = null)
    {
        _action = entry.Action;
        _pendingPresets = presets;
        _lastValues.Set(UiStateScope, "state", $"lastAction:{_manifest?.Id}", entry.Action.Id);

        BuildForm();

        // **只改高亮，不重建列表**：重建会让分组重新播放展开动画，
        // 用户看到的就是"分组迅速折叠又展开"（已被反馈），完全不必要。
        UpdateSelectionHighlights(forPackages: false);
    }

    // ------------------------------------------------------------------ 工具包分组：新建 / 重命名 / 删除 / 拖动改分组
    //
    // 为什么只有工具包能自定义分组：**动作怎么分组是工具包作者的事**（清单里的 category），
    // 让用户再改一套只会产生矛盾；而"我这个工具包算哪一类"是用户自己的事。

    /// <summary>
    /// 「分组…」按钮：把**当前选中的工具包**移到别的分组，或新建一个分组并移入。
    ///
    /// 为什么要有这个按钮：右键菜单与鼠标拖动都有人反馈"用不了"
    /// （右键菜单在自动化里没法验证；拖动在 WinUI 里与点击手势打架）。
    /// 一个看得见的按钮是最可靠的路径——它同时也让"改分组"这件事变得**可被发现**。
    /// </summary>
    private async Task ChangeGroupInteractiveAsync()
    {
        var entry = _packages.FirstOrDefault(p => ReferenceEquals(p.Manifest, _manifest));

        if (entry is null)
        {
            StatusText.Text = "请先在左侧选中一个工具包，再点「分组…」";
            return;
        }

        var packageId = entry.Manifest.Id ?? string.Empty;
        var current = PackageGroupOf(entry);
        var choices = new List<string> { "未分组" };
        choices.AddRange(CurrentPackageGroups().Where(g => g != "未分组"));

        var picker = new ComboBox { ItemsSource = choices, HorizontalAlignment = HorizontalAlignment.Stretch };
        picker.SelectedItem = choices.Contains(current) ? current : "未分组";

        var newName = new TextBox { PlaceholderText = "（可选）新建一个分组名，填了就忽略上面的选择" };

        var dialog = new ContentDialog
        {
            Title = $"「{entry.Display}」的分组",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = $"当前：{current}", Opacity = 0.75 },
                    new TextBlock { Text = "移到：", Opacity = 0.75 },
                    picker,
                    newName,
                },
            },
            PrimaryButtonText = "应用",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(newName.Text))
        {
            _grouping.AddCustomGroup(forPackages: true, newName.Text);
            MovePackageToGroup(packageId, newName.Text.Trim());
            _expandedGroups.Add("pkg:" + newName.Text.Trim());
            RebuildPackageList();
            return;
        }

        var selected = picker.SelectedItem as string;

        MovePackageToGroup(packageId, selected == "未分组" ? null : selected);

        if (selected is not null && selected != "未分组")
        {
            _expandedGroups.Add("pkg:" + selected);
            RebuildPackageList();
        }
    }

    private void OnPackageGroupClicked(object sender, RoutedEventArgs e) => _ = ChangeGroupInteractiveAsync();

    /// <summary>把工具包放进目标分组（空 = 移出分组）。</summary>
    private void MovePackageToGroup(string packageId, string? groupName)
    {
        if (string.IsNullOrEmpty(packageId))
        {
            return;
        }

        _grouping.SetPackageGroup(packageId, groupName);
        _grouping.Save();
        RebuildPackageList();

        StatusText.Text = string.IsNullOrEmpty(groupName)
            ? $"「{packageId}」已移出分组"
            : $"「{packageId}」已移到「{groupName}」";
    }

    /// <summary>新建一个分组并立刻把某个工具包放进去（右键菜单里的"新建分组并移入…"）。</summary>
    private async Task CreateGroupAndMoveAsync(string packageId)
    {
        var input = new TextBox { PlaceholderText = "新分组名，例如「常用」「诊断」" };

        var dialog = new ContentDialog
        {
            Title = $"新建分组并移入「{packageId}」",
            Content = input,
            PrimaryButtonText = "创建并移入",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(input.Text))
        {
            return;
        }

        _grouping.AddCustomGroup(forPackages: true, input.Text);
        _grouping.SetPackageGroup(packageId, input.Text);
        _grouping.Save();
        RebuildPackageList();

        _expandedGroups.Add("pkg:" + input.Text.Trim());
        RebuildPackageList();

        StatusText.Text = $"「{packageId}」已移到新分组「{input.Text.Trim()}」";
    }

    /// <summary>在「工具包」标题上右键 → 新建一个空分组（然后把条目拖进去）。</summary>
    private async Task CreateGroupAsync()
    {
        var input = new TextBox { PlaceholderText = "新分组名，例如「常用」「诊断」" };

        var dialog = new ContentDialog
        {
            Title = "新建工具包分组",
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "建好之后，把工具包拖到这个分组标题上即可归入。", TextWrapping = TextWrapping.Wrap, Opacity = 0.75 },
                    input,
                },
            },
            PrimaryButtonText = "创建",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(input.Text))
        {
            return;
        }

        _grouping.AddCustomGroup(forPackages: true, input.Text);
        _grouping.Save();
        RebuildPackageList();

        StatusText.Text = $"已新建分组「{input.Text.Trim()}」，把工具包拖到它上面即可";
    }

    /// <summary>重命名一个分组。清单定义的分组也能改（改名后其成员会写上覆盖值）。</summary>
    private async Task RenameGroupAsync()
    {
        var groups = CurrentPackageGroups();

        if (groups.Count == 0)
        {
            StatusText.Text = "现在没有分组";
            return;
        }

        var picker = new ComboBox { ItemsSource = groups, HorizontalAlignment = HorizontalAlignment.Stretch };
        picker.SelectedIndex = 0;

        var input = new TextBox { PlaceholderText = "新的分组名" };

        var dialog = new ContentDialog
        {
            Title = "重命名工具包分组",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "要改哪个分组：", Opacity = 0.75 },
                    picker,
                    new TextBlock { Text = "改成：", Opacity = 0.75 },
                    input,
                },
            },
            PrimaryButtonText = "重命名",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || picker.SelectedItem is not string oldName
            || string.IsNullOrWhiteSpace(input.Text))
        {
            return;
        }

        var newName = input.Text.Trim();

        if (string.Equals(oldName, newName, StringComparison.CurrentCulture))
        {
            return;
        }

        // 原分组的成员全部写上覆盖值指向新名字（这样"改清单定义的分组名"也成立）
        foreach (var package in _packages.Where(p => string.Equals(PackageGroupOf(p), oldName, StringComparison.CurrentCulture)))
        {
            _grouping.SetPackageGroup(package.Manifest.Id ?? string.Empty, newName);
        }

        _grouping.AddCustomGroup(forPackages: true, newName);

        if (_grouping.GetCustomGroups(forPackages: true).Contains(oldName, StringComparer.CurrentCulture))
        {
            _grouping.RemoveCustomGroup(forPackages: true, oldName);
        }

        _expandedGroups.Remove("pkg:" + oldName);
        _grouping.Save();
        RebuildPackageList();

        StatusText.Text = $"分组「{oldName}」已重命名为「{newName}」";
    }

    /// <summary>删掉一个用户新建的分组（成员回到清单默认分组）。清单定义的分组不在这里删。</summary>
    private async Task DeleteGroupAsync()
    {
        var custom = _grouping.GetCustomGroups(forPackages: true).ToList();

        if (custom.Count == 0)
        {
            StatusText.Text = "没有可删除的自定义分组（清单定义的分组请用「全部恢复默认分组」）";
            return;
        }

        var picker = new ComboBox { ItemsSource = custom, HorizontalAlignment = HorizontalAlignment.Stretch };
        picker.SelectedIndex = 0;

        var dialog = new ContentDialog
        {
            Title = "删除工具包分组",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "要删哪个分组：", Opacity = 0.75 },
                    picker,
                    new TextBlock
                    {
                        Text = "分组里的工具包会回到「未分组」——工具包本身不会被卸载。",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.75,
                    },
                },
            },
            PrimaryButtonText = "删除分组",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || picker.SelectedItem is not string name)
        {
            return;
        }

        _grouping.RemoveCustomGroup(forPackages: true, name);
        _expandedGroups.Remove("pkg:" + name);
        _grouping.Save();
        RebuildPackageList();

        StatusText.Text = $"已删除分组「{name}」";
    }

    /// <summary>当前工具包列表里实际出现的分组名（用于重命名时的下拉选择）。</summary>
    private List<string> CurrentPackageGroups() =>
        _packages
            .Select(PackageGroupOf)
            .Concat(_grouping.GetCustomGroups(forPackages: true))
            .Distinct(StringComparer.CurrentCulture)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

    /// <summary>
    /// 给「工具包」/「动作」标题装上下文菜单。
    ///
    /// 两者的能力**刻意不同**：
    ///   - 「工具包」：新建/重命名/删除分组 + 展开收起 + 恢复默认，并且能接收拖来的工具包（改分组 / 恢复默认）；
    ///   - 「动作」：只有展开收起——动作分组由工具包清单定义，界面不掺和，避免"清单一套、界面一套"。
    /// </summary>
    private void WireSectionTitle(TextBlock title, bool forPackages, StackPanel panel)
    {
        var menu = new MenuFlyout();

        if (forPackages)
        {
            menu.Items.Add(MenuItem("新建分组…", () => _ = CreateGroupAsync()));
            menu.Items.Add(MenuItem("重命名分组…", () => _ = RenameGroupAsync()));
            menu.Items.Add(MenuItem("删除分组…", () => _ = DeleteGroupAsync()));
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        menu.Items.Add(MenuItem("全部展开", () => SetGroupsExpanded(panel, true)));
        menu.Items.Add(MenuItem("全部收起", () => SetGroupsExpanded(panel, false)));

        if (forPackages)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem("全部移出分组", () => ResetGroups()));
        }

        title.ContextFlyout = menu;
    }

    private static MenuFlyoutItem MenuItem(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();

        return item;
    }

    private void SetGroupsExpanded(Panel panel, bool expanded)
    {
        foreach (var child in panel.Children.OfType<Expander>())
        {
            child.IsExpanded = expanded;
        }
    }

    private void ResetGroups()
    {
        foreach (var package in _packages)
        {
            _grouping.SetPackageGroup(package.Manifest.Id ?? string.Empty, null);
        }

        _grouping.Save();
        RebuildPackageList();

        StatusText.Text = "已把全部工具包移出分组（都归到「未分组」）";
    }

    // ------------------------------------------------------------------ 工具包右键菜单

    /// <summary>
    /// 工具包条目右键：**通用的**由宿主固定提供（打开目录、官网、重载、卸载），
    /// **软件专有的**由清单的 <c>quickActions</c> 声明（例如 scoop 的"检查可用更新"）。
    /// </summary>
    private void BuildPackageMenu(MenuFlyout menu, PackageEntry entry)
    {
        var manifest = entry.Manifest;
        var packageId = manifest.Id ?? string.Empty;

        // ---- 改分组：菜单是**唯一入口**（不做鼠标拖动：拖放在真实使用中不可靠）----
        var current = PackageGroupOf(entry);
        var moveMenu = new MenuFlyoutSubItem { Text = $"移动到分组（现在：{current}）" };

        if (current != "未分组")
        {
            moveMenu.Items.Add(MenuItem("移出分组（未分组）", () => MovePackageToGroup(packageId, null)));
        }

        foreach (var group in CurrentPackageGroups()
                     .Where(g => g != "未分组" && !string.Equals(g, current, StringComparison.CurrentCulture)))
        {
            var target = group;
            moveMenu.Items.Add(MenuItem($"移到「{target}」", () => MovePackageToGroup(packageId, target)));
        }

        moveMenu.Items.Add(new MenuFlyoutSeparator());
        moveMenu.Items.Add(MenuItem("新建分组并移入…", () => _ = CreateGroupAndMoveAsync(packageId)));

        menu.Items.Add(moveMenu);
        menu.Items.Add(new MenuFlyoutSeparator());

        foreach (var quick in manifest.QuickActions ?? [])
        {
            if (string.IsNullOrWhiteSpace(quick.Title) || string.IsNullOrWhiteSpace(quick.Action))
            {
                continue;
            }

            var target = (manifest.Actions ?? []).FirstOrDefault(a => a.Id == quick.Action);

            if (target is null)
            {
                continue;   // 写错的动作 id 直接不显示，避免"点了才报错"
            }

            menu.Items.Add(MenuItem(quick.Title!, () =>
            {
                SelectPackage(entry, restoreAction: false);
                var actionEntry = _actions.FirstOrDefault(a => ReferenceEquals(a.Action, target));

                if (actionEntry is not null)
                {
                    SelectAction(actionEntry);
                }
            }));
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        menu.Items.Add(MenuItem("打开所在目录", () => _ = OpenPackageFolderAsync(entry)));
        menu.Items.Add(MenuItem("打开官网", () => OpenUrl(manifest.Homepage, "官网")));
        menu.Items.Add(MenuItem("版本信息", () => _ = ShowPackageInfoAsync(entry)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem("重新载入全部工具包", ReloadPackages));
        menu.Items.Add(MenuItem("卸载这个工具包…", () => _ = UninstallPackageAsync(entry)));
    }

    private static void OpenUrl(string? url, string what)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{what}打不开：{ex.Message}");
        }
    }

    /// <summary>
    /// 用系统默认浏览器搜一段文字。
    /// 用途：工具包不可能把每个开关都做成字段，"这个命令还有什么参数"最直接的出路就是搜一下。
    /// </summary>
    private static void OpenSearch(string query)
    {
        var url = "https://www.bing.com/search?q=" + Uri.EscapeDataString(query);

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine("打开浏览器失败：" + ex.Message);
        }
    }

    /// <summary>打开这个工具包对应的程序所在目录（需要先定位到可执行文件）。</summary>
    private async Task OpenPackageFolderAsync(PackageEntry entry)
    {
        var manifest = entry.Manifest;

        if (manifest.Locate is null)
        {
            StatusText.Text = "这个工具包没有声明可执行文件，无法定位目录";
            return;
        }

        StatusText.Text = "正在定位…";
        var location = await ToolLocator.LocateAsync(manifest.Locate, _runner);

        if (location is null)
        {
            StatusText.Text = $"找不到 {manifest.Locate.Executable}。{manifest.Locate.NotFoundHint}";
            return;
        }

        var directory = Path.GetDirectoryName(location.ExecutablePath);

        if (string.IsNullOrEmpty(directory))
        {
            StatusText.Text = "拿不到目录路径：" + location.ExecutablePath;
            return;
        }

        OpenInExplorer(directory);
        StatusText.Text = $"已打开：{directory}";
    }

    private async Task ShowPackageInfoAsync(PackageEntry entry)
    {
        var manifest = entry.Manifest;
        var lines = new List<string>
        {
            $"工具包 id：{manifest.Id}",
            $"名称：{manifest.Name}",
            $"清单版本：{manifest.ManifestVersion}",
            $"目标软件版本：{manifest.AppVersion}",
            $"最低要求：{manifest.Locate?.MinVersion ?? "（未声明）"}",
            $"目录：{entry.Directory}",
        };

        if (manifest.Locate is not null)
        {
            var location = await ToolLocator.LocateAsync(manifest.Locate, _runner);
            lines.Add(location is null
                ? $"实际定位：找不到 {manifest.Locate.Executable}"
                : $"实际定位：{location.ExecutablePath}");
        }

        var dialog = new ContentDialog
        {
            Title = manifest.Name,
            Content = new TextBlock { Text = string.Join("\n", lines), TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "关闭",
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
        };

        await dialog.ShowAsync();
    }

    private async Task UninstallPackageAsync(PackageEntry entry)
    {
        var dialog = new ContentDialog
        {
            Title = $"卸载「{entry.Manifest.Name}」？",
            Content = "工具包目录会被移到 plugins\\.trash\\ 下（**不是删除**），需要时可以自己拿回来。"
                      + $"\n\n目录：{entry.Directory}",
            PrimaryButtonText = "卸载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var result = PackageInstaller.Uninstall(entry.Directory, _pluginsRoot);
        StatusText.Text = result.Message;

        if (result.Success)
        {
            ReloadPackages();
        }
    }

    // ------------------------------------------------------------------ 安装 / 重载

    private void OnInstallPackageClicked(object sender, RoutedEventArgs e) => _ = InstallPackageInteractiveAsync();

    private async Task InstallPackageInteractiveAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var folder = await picker.PickSingleFolderAsync();

        if (folder is null)
        {
            return;
        }

        InstallFrom(folder.Path);
    }

    private void InstallFrom(string path)
    {
        var result = PackageInstaller.Install(path, _pluginsRoot);
        StatusText.Text = result.Message;

        AppendOutput(result.Success ? $"# {result.Message}" : $"# 安装失败：{result.Message}");

        if (result.Success)
        {
            ReloadPackages();
        }
    }

    private void OnReloadPackagesClicked(object sender, RoutedEventArgs e) => ReloadPackages();

    private void OnOpenPluginsFolderClicked(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_pluginsRoot))
        {
            OpenInExplorer(_pluginsRoot);
            StatusText.Text = "已打开：" + _pluginsRoot;
        }
        else
        {
            StatusText.Text = "工具包目录还不存在：" + _pluginsRoot;
        }
    }

    private void OnExitClicked(object sender, RoutedEventArgs e) => Close();

    /// <summary>打开日志目录。日志里是每个动作的完整输出——出问题时这是第一手材料。</summary>
    private void OnOpenLogFolderClicked(object sender, RoutedEventArgs e)
    {
        FlushOutput(force: true);

        var directory = _logger.Directory;

        try
        {
            Directory.CreateDirectory(directory);
            OpenInExplorer(directory);
            StatusText.Text = "日志目录：" + directory;
        }
        catch (Exception ex)
        {
            _logger.Exception("打开日志目录时", ex);
            StatusText.Text = "日志目录打不开：" + directory;
        }
    }

    /// <summary>
    /// 以管理员身份重新启动自己（会弹 UAC）。
    /// 做 Windows 自带命令集时这是刚需：chkdsk /f、sfc /scannow、diskpart 都要提权，
    /// 而普通启动的宿主拿不到管理员令牌。
    /// </summary>
    private void OnRestartElevatedClicked(object sender, RoutedEventArgs e)
    {
        var exe = Environment.ProcessPath;

        if (string.IsNullOrEmpty(exe))
        {
            StatusText.Text = "拿不到自己的可执行文件路径，无法重启";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            Close();
        }
        catch (Exception ex)
        {
            // 用户点了"否"也会走到这里（Win32Exception: 操作已被用户取消）
            StatusText.Text = "没有以管理员身份启动：" + ex.Message;
        }
    }

    /// <summary>当前进程是否已提权。</summary>
    private static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();

            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void OnOpenDocsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var root = RepoPaths.FindRepositoryRoot();
            var agents = Path.Combine(root, "AGENTS.md");

            if (File.Exists(agents))
            {
                Process.Start(new ProcessStartInfo(agents) { UseShellExecute = true });
                return;
            }
        }
        catch (Exception)
        {
            // 分发形态下没有仓库结构，落到下面提示
        }

        StatusText.Text = "找不到项目文档（分发形态下没有 AGENTS.md）";
    }

    private void OnAboutClicked(object sender, RoutedEventArgs e) => _ = ShowAboutAsync();

    private async Task ShowAboutAsync()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "未知";

        var packages = _packages.Count == 0
            ? "（没有载入任何工具包）"
            : string.Join("\n", _packages.Select(p =>
                $"  · {p.Manifest.Name}（{p.Manifest.Id}）{p.Manifest.AppVersion} —— {p.Manifest.Actions?.Count ?? 0} 个动作"));

        var text = $"""
            All Tool —— 把命令行软件变成可点击界面
            宿主版本：{version}
            运行环境：.NET {Environment.Version} / {Environment.OSVersion.VersionString}

            已载入 {_packages.Count} 个工具包：
            {packages}

            工具包目录：{_pluginsRoot}
            设置目录：{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "All Tool")}

            每个命令执行前都会显示将运行的完整命令行；工具包是纯声明的 YAML，不含代码。
            执行方式：直接调用程序（CreateProcess），不经过 cmd 或 PowerShell。

            ────────────────────────────────
            作者：Kangmori
            仓库：https://github.com/Kangmori/all-tool
            许可：MIT
            """;

        var dialog = new ContentDialog
        {
            Title = "关于 All Tool",
            Content = new ScrollViewer
            {
                // 内容会随工具包数量增长（15 个包就有 15 行），必须能滚动，否则超出对话框看不见
                Content = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                },
                MaxHeight = 420,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
            CloseButtonText = "关闭",
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
        };

        await dialog.ShowAsync();
    }

    // ------------------------------------------------------------------ 动态表单

    private void BuildForm()
    {
        FormPanel.Children.Clear();
        _values.Clear();
        _valueSync.Clear();
        _fieldControls.Clear();
        _pathBoxes.Clear();

        // 换了动作，上一轮的"下一步"建议就过期了
        NextStepsPanel.Children.Clear();
        NextStepsPanel.Visibility = Visibility.Collapsed;

        if (_action is null)
        {
            RunButton.IsEnabled = false;
            return;
        }

        ActionTitle.Text = _action.Title ?? string.Empty;
        ActionDescription.Text = _action.Description ?? string.Empty;

        // 需要管理员权限的动作：**提前说清楚**，别让用户跑完只看到一句程序自己吐的权限错误。
        // （Windows 自带命令里 chkdsk /f、sfc /scannow、diskpart 都属于这类。）
        if (_action.RequiresAdminEffective(_manifest?.Runtime?.RequiresAdmin ?? false))
        {
            ActionDescription.Text += IsElevated()
                ? "\n✔ 这个动作需要管理员权限，当前已是管理员。"
                : "\n⚠ 这个动作通常需要管理员权限。当前不是管理员，很可能会失败——可用「文件 → 以管理员身份重新启动」。";
        }

        if (_action.DangerOrDefault != DangerLevel.None)
        {
            // 优先用清单自己写的 confirmText：它比宿主硬编码的那句准确得多。
            // 例如 ipconfig /release 是 overwrite，但"会覆盖已有文件"完全说不通——
            // 清单里写的是"释放 DHCP 租约会立刻断开这个适配器的 IPv4 网络"。
            var detail = string.IsNullOrWhiteSpace(_action.ConfirmText)
                ? (_action.DangerOrDefault == DangerLevel.Destructive
                    ? "此动作会不可逆地修改数据，执行前会再次确认。"
                    : "此动作会改动已有数据，执行前会再次确认。")
                : _action.ConfirmText!;

            ActionDescription.Text += "\n\n" + detail;
        }

        var advancedPanel = new StackPanel { Spacing = 12 };
        var hasAdvanced = false;

        // `group` 的落地：清单里 70 多处标了分组（基础 / 安全 / 筛选 / 高级…），
        // 之前界面完全忽略它，所有字段平铺成一长列。这里在分组变化时插一个小组标题。
        // 注意普通字段与高级字段在两个不同的面板里，所以"上一个分组"要分别记。
        var lastGroupByPanel = new Dictionary<Panel, string?>();

        void AddWithGroupHeader(Panel target, FrameworkElement control, string? group)
        {
            var lastGroup = lastGroupByPanel.GetValueOrDefault(target);

            if (!string.IsNullOrEmpty(group) && !string.Equals(group, lastGroup, StringComparison.Ordinal))
            {
                target.Children.Add(new TextBlock
                {
                    Text = group,
                    FontWeight = FontWeights.SemiBold,
                    Opacity = 0.7,
                    Margin = new Thickness(0, 10, 0, 0),
                });
                lastGroupByPanel[target] = group;
            }

            target.Children.Add(control);
        }

        foreach (var field in _action.Fields ?? [])
        {
            if (string.IsNullOrEmpty(field.Id))
            {
                continue;
            }

            var control = CreateField(field, out var getter);
            _valueSync.Add((field.Id, getter));
            _values[field.Id] = getter();

            if (field.Advanced)
            {
                AddWithGroupHeader(advancedPanel, control, field.Group);
                hasAdvanced = true;
            }
            else
            {
                AddWithGroupHeader(FormPanel, control, field.Group);
            }
        }

        if (hasAdvanced)
        {
            FormPanel.Children.Add(new Expander
            {
                Header = "高级选项",
                Content = advancedPanel,
                IsExpanded = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            });
        }

        RunButton.IsEnabled = true;
        CancelButton.IsEnabled = false;
        ProgressIndicator.Value = 0;
        _pendingPresets = null;   // 预填值已经进到控件里了，别留着影响下次重建
        UpdateCommandLine();
        ApplyExecutionMode();
        StatusText.Text = "填好后点执行";
    }

    private FrameworkElement CreateField(ManifestField field, out Func<object?> getter)
    {
        var stack = new StackPanel { Spacing = 4 };

        var label = new TextBlock
        {
            Text = field.Required ? $"{field.Label} *" : field.Label,
            FontWeight = FontWeights.SemiBold,
        };

        var tip = string.Join(
            '\n',
            new[] { field.Help, string.IsNullOrEmpty(field.Doc) ? null : $"文档：{field.Doc}" }
                .Where(t => !string.IsNullOrWhiteSpace(t)));

        if (tip.Length > 0)
        {
            ToolTipService.SetToolTip(label, tip);
        }

        stack.Children.Add(label);

        // 规范里 save: true 的落地：回填上次用过的值。
        // 密码类型**永不落盘**，所以这里直接跳过（见 LastValuesStore 的说明）。
        var saved = field.Save && field.Type != "password" && field.Id is not null
            ? _lastValues.Get(_manifest?.Id ?? string.Empty, _action?.Id ?? string.Empty, field.Id)
            : null;

        // "下一步"按钮带过来的预填值优先于上次的值：它是这一轮明确要用的参数
        if (_pendingPresets is not null && field.Id is not null
            && _pendingPresets.TryGetValue(field.Id, out var presetValue))
        {
            saved = ToStoredText(presetValue);
        }

        // 能接受拖放的输入框（路径类与文本类都算：文本里填路径也很常见）
        TextBox? dropTarget = null;

        switch (field.Type)
        {
            case "bool":
            {
                var box = new CheckBox
                {
                    Content = string.IsNullOrEmpty(field.Help) ? "启用" : field.Help,
                    IsChecked = saved is not null
                        ? string.Equals(saved, "true", StringComparison.OrdinalIgnoreCase)
                        : field.Default is bool b
                            ? b
                            : string.Equals(field.Default?.ToString(), "true", StringComparison.OrdinalIgnoreCase),
                };
                box.Checked += (_, _) => UpdateCommandLine();
                box.Unchecked += (_, _) => UpdateCommandLine();
                stack.Children.Add(box);
                getter = () => box.IsChecked == true;
                break;
            }

            case "enum":
            case "literal":
            {
                var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };

                // 优先级：上次用过的值 > 清单标了 isDefault 的项 > default 指向的项 > 第一项
                FieldValue? preselect = saved is null
                    ? null
                    : (field.Values ?? []).FirstOrDefault(v => string.Equals(v.Value, saved, StringComparison.Ordinal));

                preselect ??= (field.Values ?? []).FirstOrDefault(v => v.IsDefault);

                preselect ??= field.Default is null
                    ? null
                    : (field.Values ?? []).FirstOrDefault(v =>
                        string.Equals(v.Value, field.Default.ToString(), StringComparison.Ordinal));

                foreach (var value in field.Values ?? [])
                {
                    var item = new ComboBoxItem { Content = value.Label ?? value.Value, Tag = value };
                    combo.Items.Add(item);

                    if (ReferenceEquals(value, preselect))
                    {
                        combo.SelectedItem = item;
                    }
                }

                combo.SelectedItem ??= combo.Items.FirstOrDefault();
                combo.SelectionChanged += (_, _) => UpdateCommandLine();
                stack.Children.Add(combo);
                getter = () => (combo.SelectedItem as ComboBoxItem)?.Tag is FieldValue chosen ? chosen.Value : null;
                break;
            }

            case "number":
            {
                var box = new NumberBox
                {
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    PlaceholderText = field.Placeholder ?? string.Empty,
                };

                box.Value = saved is not null
                    && double.TryParse(saved, NumberStyles.Float, CultureInfo.InvariantCulture, out var savedNumber)
                        ? savedNumber
                        : field.Default is not null
                            && double.TryParse(field.Default.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                                ? parsed
                                : double.NaN;

                box.ValueChanged += (_, _) => UpdateCommandLine();
                stack.Children.Add(box);
                getter = () => double.IsNaN(box.Value) ? null : box.Value;
                break;
            }

            case "password":
            {
                var box = new PasswordBox
                {
                    PlaceholderText = field.Placeholder ?? string.Empty,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                box.PasswordChanged += (_, _) => UpdateCommandLine();
                stack.Children.Add(box);
                getter = () => string.IsNullOrEmpty(box.Password) ? null : box.Password;
                break;
            }

            case "multiselect":
            case "files":
            case "paths":
            case "directories":
            {
                var box = new TextBox
                {
                    AcceptsReturn = true,
                    Height = 76,
                    TextWrapping = TextWrapping.NoWrap,
                    PlaceholderText = field.Placeholder ?? "每行一项",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };

                if (saved is not null)
                {
                    box.Text = saved;   // 回填上次用过的多值（每行一项）
                }

                dropTarget = box;
                box.TextChanged += (_, _) => UpdateCommandLine();
                stack.Children.Add(box);

                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var wantsFolders = field.Type is "directories" or "paths";
                var wantsFiles = field.Type is "files" or "paths";

                if (wantsFiles)
                {
                    var pickFiles = new Button { Content = "添加文件…" };
                    pickFiles.Click += async (_, _) => AppendLines(box, await PickFilesAsync(field));
                    buttons.Children.Add(pickFiles);
                }

                if (wantsFolders)
                {
                    // 注意：WinRT 的 FolderPicker 只有单选（没有 PickMultipleFoldersAsync），
                    // 所以"多个文件夹"靠重复点击逐个追加。
                    var pickFolder = new Button { Content = "添加文件夹…" };
                    pickFolder.Click += async (_, _) =>
                    {
                        var picked = await PickFolderAsync();

                        if (!string.IsNullOrEmpty(picked))
                        {
                            AppendLines(box, [picked]);
                        }
                    };
                    buttons.Children.Add(pickFolder);
                }

                stack.Children.Add(buttons);
                getter = () => box.Text
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)   // WinUI 的 TextBox 用 \r 换行
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0)
                    .ToList();
                break;
            }

            default:
            {
                // repeatable 的 text / textarea：一个值一行，产出多个参数。
                // 之前只有 multiselect / files / paths / directories 支持多值，于是"自由多值"字段
                // 只能借用 multiselect 类型（见待决事项 D9）。补上这一条之后，
                // 7z 的「分卷大小」这类字段才真的能填多个值——文档里 -v 本来就支持多值。
                var multiline = field.Type == "textarea" || field.Repeatable;

                var box = new TextBox
                {
                    PlaceholderText = field.Placeholder ?? (field.Repeatable ? "每行一项" : string.Empty),
                    AcceptsReturn = multiline,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Height = field.Repeatable && field.Type != "textarea" ? 76 : double.NaN,
                };

                // 回填上次的值优先于清单里的 default：用户上次填的就是他的意图
                box.Text = saved
                    ?? (field.Default is not null ? field.Default.ToString() ?? string.Empty : string.Empty);

                dropTarget = box;
                box.TextChanged += (_, _) => UpdateCommandLine();

                if (field.Type is "file" or "directory")
                {
                    var browse = new Button { Content = "浏览…" };
                    browse.Click += async (_, _) =>
                    {
                        var picked = field.Type == "directory"
                            ? await PickFolderAsync()
                            : await PickFileAsync(field);

                        if (!string.IsNullOrEmpty(picked))
                        {
                            box.Text = picked;
                        }
                    };

                    var row = new Grid { ColumnSpacing = 8 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    Grid.SetColumn(box, 0);
                    Grid.SetColumn(browse, 1);
                    row.Children.Add(box);
                    row.Children.Add(browse);
                    stack.Children.Add(row);
                }
                else
                {
                    stack.Children.Add(box);
                }

                getter = field.Repeatable
                    ? () => box.Text
                        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)   // WinUI 的 TextBox 用 \r 换行
                        .Select(line => line.Trim())
                        .Where(line => line.Length > 0)
                        .ToList()
                    : () => string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
                break;
            }
        }

        // 登记控件（必填校验要高亮它）与拖放目标（资源管理器拖来的文件直接填进去）
        if (field.Id is not null)
        {
            _fieldControls[field.Id] = dropTarget is not null ? dropTarget : stack;

            if (dropTarget is not null)
            {
                _pathBoxes[field.Id] = dropTarget;
                AttachDrop(dropTarget, field);
            }
        }

        return stack;
    }

    /// <summary>
    /// 让输入框接受从资源管理器拖来的文件/文件夹。
    ///
    /// 拖放本身没法自动化验证，所以"拖进来之后文本变成什么"被抽成了
    /// <see cref="PathDropLogic"/> 并单测覆盖；这里只负责事件接线与视觉反馈。
    /// </summary>
    private void AttachDrop(TextBox box, ManifestField field)
    {
        var multiValue = field.Repeatable || field.Type is "multiselect" or "files" or "paths" or "directories";
        var originalBorder = box.BorderBrush;

        box.AllowDrop = true;

        box.DragOver += (_, e) =>
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = multiValue ? "追加到这里" : "填到这里";
                e.DragUIOverride.IsCaptionVisible = true;
            }
        };

        box.DragEnter += (_, _) =>
            box.BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];

        box.DragLeave += (_, _) => box.BorderBrush = originalBorder;

        box.Drop += async (_, e) =>
        {
            box.BorderBrush = originalBorder;

            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            var paths = await ReadDroppedPathsAsync(e);
            if (paths.Count == 0)
            {
                return;
            }

            box.Text = PathDropLogic.Apply(box.Text, paths, multiValue);
            StatusText.Text = PathDropLogic.DescribeDrop(paths) + $"（填在「{field.Label}」）";
        };
    }

    private static async Task<List<string>> ReadDroppedPathsAsync(DragEventArgs e)
    {
        var items = await e.DataView.GetStorageItemsAsync();

        return items
            .Select(item => item.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
    }

    /// <summary>
    /// 必填校验：现在 required 不再只是标签上的一个星号。
    /// 缺必填时直接拦住并高亮那个字段，避免生成一条少参数的"看起来对"的命令。
    /// </summary>
    private bool TryValidateRequired(out string message)
    {
        message = string.Empty;

        if (_action is null)
        {
            return true;
        }

        foreach (var field in _action.Fields ?? [])
        {
            if (field.Required != true || field.Id is null)
            {
                continue;
            }

            _values.TryGetValue(field.Id, out var value);

            var empty = value switch
            {
                null => true,
                string text => string.IsNullOrWhiteSpace(text),
                System.Collections.IEnumerable items => !items.GetEnumerator().MoveNext(),
                _ => false,
            };

            if (!empty)
            {
                continue;
            }

            message = $"「{field.Label}」是必填项，请先填好再执行";

            if (_fieldControls.TryGetValue(field.Id, out var control))
            {
                // FrameworkElement 本身没有边框属性，得落到 Control 上
                if (control is Control target)
                {
                    target.BorderThickness = new Thickness(2);
                    target.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
                }

                _ = control.Focus(FocusState.Programmatic);
            }

            return false;
        }

        return true;
    }

    private void ClearValidationMarks()
    {
        foreach (var control in _fieldControls.Values)
        {
            if (control is TextBox box)
            {
                box.BorderThickness = new Thickness(1);
                box.ClearValue(Control.BorderBrushProperty);
            }
        }
    }

    /// <summary>执行完之后，按清单里声明的 nextSteps 呈现"下一步"按钮。</summary>
    private void ShowNextSteps(string output)
    {
        NextStepsPanel.Children.Clear();

        if (_action is null || _manifest is null)
        {
            NextStepsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var suggestions = NextStepMatcher.Match(_action, _manifest.Actions ?? [], output);

        NextStepsPanel.Children.Add(new TextBlock
        {
            Text = "下一步",
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.75,
        });

        // ---- 清单声明的建议（领域相关，例如 scoop status → 一键更新）----
        foreach (var suggestion in suggestions)
        {
            NextStepsPanel.Children.Add(BuildSuggestionRow(suggestion));
        }

        // ---- 通用选项 ----
        // 只放工具栏上没有的那一条：复制命令与「在终端中打开」已经在按钮条里常驻了，
        // 这里再放一遍就是重复（实测界面里会出现两个一模一样的按钮）。
        var generic = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        var helpButton = new Button { Content = "联机帮助" };
        var tool = _manifest.Name ?? _manifest.Id ?? string.Empty;
        var commandName = _action.Command ?? string.Empty;
        helpButton.Click += (_, _) => OpenSearch($"{tool} {commandName} 命令 帮助 用法");
        generic.Children.Add(helpButton);

        var folderButton = new Button { Content = "打开所在目录" };
        folderButton.Click += (_, _) => _ = OpenCurrentPackageFolderAsync();
        generic.Children.Add(folderButton);

        NextStepsPanel.Children.Add(generic);
        NextStepsPanel.Visibility = Visibility.Visible;
    }

    /// <summary>把一条"下一步"建议渲染成一行（按钮 + 理由）。</summary>
    private StackPanel BuildSuggestionRow(NextStepSuggestion suggestion)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var button = new Button { Content = suggestion.Title };
        button.Click += async (_, _) => await RunSuggestionAsync(suggestion);
        row.Children.Add(button);

        if (!string.IsNullOrWhiteSpace(suggestion.Reason))
        {
            row.Children.Add(new TextBlock
            {
                Text = suggestion.Reason,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return row;
    }

    /// <summary>打开**当前**工具包对应的程序所在目录（右键菜单里那个的便捷版）。</summary>
    private Task OpenCurrentPackageFolderAsync()
    {
        var entry = _packages.FirstOrDefault(p => ReferenceEquals(p.Manifest, _manifest));

        return entry is null ? Task.CompletedTask : OpenPackageFolderAsync(entry);
    }

    /// <summary>点"下一步"：切到目标动作、预填参数、直接执行。</summary>
    private async Task RunSuggestionAsync(NextStepSuggestion suggestion)
    {
        var entry = _actions.FirstOrDefault(a => ReferenceEquals(a.Action, suggestion.Target));

        if (entry is null)
        {
            StatusText.Text = "找不到这个动作（工具包可能变了）";
            return;
        }

        SelectAction(entry, suggestion.Values);

        // 从推荐"一键执行"时比手点更谨慎：只要目标动作会改动数据就先确认一次。
        // 手点执行时 overwrite 不额外确认（用户已经明确选了这个动作），
        // 但推荐只是"顺手一点"，不该让一次误触就去改系统——比如 scoop status 推荐的一键更新，
        // 那会真的更新用户已安装的软件。
        if (entry.Action.DangerOrDefault != DangerLevel.None)
        {
            var dialog = new ContentDialog
            {
                Title = entry.Action.Title,
                Content = "这一步会改动数据："
                    + (suggestion.Reason ?? "它是推荐的下一步")
                    + "\n\n" + (entry.Action.ConfirmText ?? "确定现在执行？"),
                PrimaryButtonText = "执行",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = (Content as FrameworkElement)?.XamlRoot,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                StatusText.Text = "已取消，动作已切好但没有执行";
                return;
            }
        }

        StatusText.Text = $"已切到「{entry.Action.Title}」，正在执行…";

        await Task.Yield();
        OnRunClicked(this, new RoutedEventArgs());
    }

    private void OnCopyCommandClicked(object sender, RoutedEventArgs e) =>
        CopyToClipboard(CommandLineBox.Text, "命令");

    private void OnCopyOutputClicked(object sender, RoutedEventArgs e) =>
        CopyToClipboard(OutputBox.Text, "输出");

    private void CopyToClipboard(string text, string what)
    {
        if (string.IsNullOrEmpty(text))
        {
            StatusText.Text = $"没有可复制的{what}";
            return;
        }

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);

        StatusText.Text = $"已复制{what}（{text.Length} 字符）";
    }

    /// <summary>
    /// 执行期间把会改变输入的控件禁掉，避免用户在跑的时候改参数造成困惑。
    /// 面板是 StackPanel（不是 Control，没有 IsEnabled），所以用 IsHitTestVisible + 变暗来表示。
    /// </summary>
    private void SetBusy(bool busy)
    {
        FormPanel.IsHitTestVisible = !busy;
        PackageGroupsPanel.IsHitTestVisible = !busy;
        ActionGroupsPanel.IsHitTestVisible = !busy;
        FilterBox.IsEnabled = !busy;
        FormPanel.Opacity = busy ? 0.6 : 1.0;
    }

    private static void AppendLines(TextBox box, IEnumerable<string> lines)
    {
        var existing = box.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)   // WinUI 的 TextBox 用 \r 换行
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        foreach (var line in lines)
        {
            if (!existing.Contains(line, StringComparer.OrdinalIgnoreCase))
            {
                existing.Add(line);
            }
        }

        box.Text = string.Join(Environment.NewLine, existing);
    }

    // ------------------------------------------------------------------ 选择器

    private async Task<string?> PickFileAsync(ManifestField field)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };

        foreach (var extension in field.Accept ?? [])
        {
            if (extension.StartsWith('.'))
            {
                picker.FileTypeFilter.Add(extension);
            }
        }

        if (picker.FileTypeFilter.Count == 0)
        {
            picker.FileTypeFilter.Add("*");
        }

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async Task<IReadOnlyList<string>> PickFilesAsync(ManifestField field)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };

        foreach (var extension in field.Accept ?? [])
        {
            if (extension.StartsWith('.'))
            {
                picker.FileTypeFilter.Add(extension);
            }
        }

        if (picker.FileTypeFilter.Count == 0)
        {
            picker.FileTypeFilter.Add("*");
        }

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var files = await picker.PickMultipleFilesAsync();
        return files.Select(f => f.Path).ToList();
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    // ------------------------------------------------------------------ 命令行预览

    private void SyncValues()
    {
        foreach (var (id, get) in _valueSync)
        {
            _values[id] = get();
        }
    }

    /// <summary>
    /// 把标了 <c>save: true</c> 的字段值记下来（规范承诺的能力）。
    /// 只在真正执行时记录——记录"用户真的用过"的值，而不是他随手点过的每个中间状态。
    /// </summary>
    private void RememberValues()
    {
        if (_manifest?.Id is null || _action?.Id is null)
        {
            return;
        }

        foreach (var field in _action.Fields ?? [])
        {
            // 密码永不落盘：这不是"没实现"，是刻意的决定。
            if (!field.Save || field.Type == "password" || field.Id is null)
            {
                continue;
            }

            _lastValues.Set(_manifest.Id, _action.Id, field.Id, ToStoredText(_values.GetValueOrDefault(field.Id)));
        }

        _lastValues.Save();
    }

    /// <summary>把字段值转成"界面上的文本形式"存盘（多值用换行连接）。</summary>
    private static string? ToStoredText(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        System.Collections.IEnumerable items => string.Join(
            Environment.NewLine,
            items.Cast<object?>().Select(item => item?.ToString() ?? string.Empty)),
        _ => value.ToString(),
    };

    private void UpdateCommandLine()
    {
        if (_action is null || _manifest is null)
        {
            CommandLineBox.Text = string.Empty;
            return;
        }

        SyncValues();

        try
        {
            var argv = ArgvBuilder.Build(_action, _values);
            var executable = _executablePath ?? _manifest.Locate?.Executable ?? "?";
            CommandLineBox.Text = ArgvBuilder.FormatForDisplay(executable, argv);
        }
        catch (Exception ex)
        {
            CommandLineBox.Text = "参数有误：" + ex.Message;
        }
    }

    // ------------------------------------------------------------------ 执行

    private async void OnRunClicked(object sender, RoutedEventArgs e)
    {
        if (_action is null || _manifest?.Locate is null)
        {
            return;
        }

        // 防重入：见 _running 的说明（重复进入会让"取消"取消错对象）
        if (_running)
        {
            StatusText.Text = "已经有一个任务在执行，请等它结束或先点取消";
            return;
        }

        _running = true;

        try
        {
            await RunCoreAsync();
        }
        finally
        {
            _running = false;
        }
    }

    private async Task RunCoreAsync()
    {
        // 守卫放在这里而不是只放调用处：这样编译器也知道后面 _action / _manifest.Locate 非空
        if (_action is null || _manifest?.Locate is null)
        {
            return;
        }

        if (_action.DangerOrDefault == DangerLevel.Destructive)
        {
            var dialog = new ContentDialog
            {
                Title = _action.Title,
                Content = _action.ConfirmText ?? "此操作不可撤销，确定继续？",
                PrimaryButtonText = "继续执行",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = (Content as FrameworkElement)?.XamlRoot,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        SyncValues();
        ClearValidationMarks();

        // 必填校验放在最前面：宁可不执行，也不要生成一条少参数的"看起来对"的命令
        if (!TryValidateRequired(out var validationMessage))
        {
            StatusText.Text = validationMessage;
            return;
        }

        RememberValues();   // 记下这次真正用到的值（标了 save: true 的字段），供下次回填

        if (_executablePath is null)
        {
            StatusText.Text = "正在查找可执行文件…";
            var location = await ToolLocator.LocateAsync(_manifest.Locate, _runner);

            if (location is null)
            {
                StatusText.Text = $"找不到 {_manifest.Locate.Executable}。{_manifest.Locate.NotFoundHint}";
                return;
            }

            _executablePath = location.ExecutablePath;

            if (location.Problem is not null)
            {
                AppendOutput("# 注意：" + location.Problem);
            }
        }

        IReadOnlyList<string> argv;
        try
        {
            argv = ArgvBuilder.Build(_action, _values);
        }
        catch (Exception ex)
        {
            StatusText.Text = "参数有误：" + ex.Message;
            return;
        }

        var workingDirectory = ResolveWorkingDirectory();
        var encoding = EncodingResolver.Resolve(_manifest.Runtime?.Encoding);
        ClearOutput();
        AppendOutput($"> {ArgvBuilder.FormatForDisplay(_executablePath, argv)}");

        _logger.Info($"开始执行：{_action.Id}（{_manifest.Id}）");
        _logger.Info($"  命令：{ArgvBuilder.FormatForDisplay(_executablePath, argv)}");
        _logger.Info($"  工作目录：{workingDirectory ?? "(继承)"}  编码：{encoding.WebName}  "
                     + $"伪控制台：{(_manifest.Runtime?.UsePseudoConsole ?? false)}");

        if (_action.RequiresAdminEffective(_manifest.Runtime?.RequiresAdmin ?? false) && !IsElevated())
        {
            AppendOutput("# 注意：这个动作通常需要管理员权限，而当前不是管理员——如果失败，请用「文件 → 以管理员身份重新启动」再试。");
        }

        if (workingDirectory is not null)
        {
            AppendOutput($"# 工作目录：{workingDirectory}");
        }

        _cancellation = new CancellationTokenSource();
        RunButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        SetBusy(true);
        ProgressIndicator.Value = 0;

        // 先转圈，等真的解析到百分比再切成确定进度。
        // 这么做是必要的：7z 在被重定向时**不输出百分比**，若一上来就画确定进度条，
        // 用户会以为程序卡住了。见 plugins/7zip/NOTES.md 的实测记录。
        ProgressIndicator.IsIndeterminate = true;
        var sawProgressValue = false;
        StatusText.Text = "执行中…";

        var parser = new ProgressParser(_action.Output?.Progress);

        // 结束标志：取消/退出之后，伪控制台可能还会把它缓冲区里的内容吐出来，
        // 那些行会跑到"结论行"后面，看起来像结论错了。收到结论后就别再追加了。
        var finished = false;

        var progress = new Progress<ProcessOutputLine>(line =>
        {
            if (finished)
            {
                return;
            }

            AppendOutput(line.Text);

            if (parser.TryParse(line.Text, out var value) && value is >= 0 and <= 100)
            {
                ProgressIndicator.IsIndeterminate = false;
                ProgressIndicator.Value = value;
                sawProgressValue = true;
            }
        });

        ProcessRunResult result;
        try
        {
            result = await _runner.RunAsync(
                new ProcessRunRequest
                {
                    Executable = _executablePath,
                    Arguments = argv,
                    WorkingDirectory = workingDirectory,
                    OutputEncoding = encoding,
                    Timeout = _manifest.Runtime?.TimeoutSeconds > 0
                        ? TimeSpan.FromSeconds(_manifest.Runtime.TimeoutSeconds)
                        : null,
                    Environment = _manifest.Runtime?.Env,
                    UsePseudoConsole = _manifest.Runtime?.UsePseudoConsole ?? false,
                },
                progress,
                _cancellation.Token);
        }
        catch (Exception ex)
        {
            AppendOutput("# 启动失败：" + ex.Message);
            StatusText.Text = "启动失败";
            FinishRun();
            return;
        }

        var verdict = result.Canceled || result.TimedOut
            ? ExitCodeInterpreter.FromInterruption(result.Canceled, result.TimedOut)
            : ExitCodeInterpreter.Interpret(result.ExitCode, _manifest.ExitCodes);

        finished = true;

        ProgressIndicator.IsIndeterminate = false;

        if (verdict.IsSuccess && sawProgressValue)
        {
            ProgressIndicator.Value = 100;
        }
        else if (!sawProgressValue && _action.Output?.Progress is not null)
        {
            ProgressIndicator.Value = 0;
            AppendOutput("# 没有解析到百分比进度：任务可能太快，或该程序在这种模式下不报进度；输出日志仍是实时到达的。");
        }

        AppendOutput(string.Empty);
        AppendOutput($"# {verdict.Meaning}（退出码 {result.ExitCode}，耗时 {result.Duration.TotalSeconds:F1} 秒）");

        _logger.Info($"执行结束：{_action.Id} 退出码={result.ExitCode} 耗时={result.Duration.TotalSeconds:F1}s "
                     + $"行数={result.TotalLineCount} 截断={result.Truncated} 取消={result.Canceled} 结论={verdict.Meaning}");

        // 跑完立刻把攒下的输出刷出来（否则用户会看到"结论先出现、正文还没到"）
        FlushOutput(force: true);

        if (result.Truncated)
        {
            AppendOutput($"# 输出被截断：共 {result.TotalLineCount} 行，界面只保留 {result.Lines.Count} 行");
        }

        StatusText.Text = $"{verdict.Meaning}（退出码 {result.ExitCode}）";

        // 按清单里声明的规则推荐下一步（例如 scoop status 跑完 → 一键更新）
        // 用执行结果里权威的输出行，**不要**读 OutputBox.Text：
        // 界面上的行是由 Progress<T> 异步投递过来的，跑到这里时末尾几行可能还没落地，
        // 拿它去匹配推荐规则会漏判（实测过：scoop status 明明有更新，却什么也没推荐）。
        ShowNextSteps(string.Join(Environment.NewLine, result.Lines.Select(line => line.Text)));

        if (string.Equals(_action.Output?.OpenOnFinish, "explorer", StringComparison.Ordinal))
        {
            var target = workingDirectory ?? _values.GetValueOrDefault("outputDir")?.ToString();

            if (!string.IsNullOrEmpty(target) && Directory.Exists(target))
            {
                OpenInExplorer(target);
            }
        }

        FinishRun();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        // 原先这里无条件显示"正在取消…"——结果"其实没取消成"也会显示同一句话，把问题掩盖了。
        // 现在如实报告：没有在跑的任务 / 取消请求已发出 / 取消出错。
        if (_cancellation is null)
        {
            StatusText.Text = "当前没有正在执行的任务（可能已经结束）";
            return;
        }

        try
        {
            _cancellation.Cancel();
            StatusText.Text = "正在取消…";
        }
        catch (Exception ex)
        {
            StatusText.Text = "取消失败：" + ex.Message;
        }
    }

    private void FinishRun()
    {
        RunButton.IsEnabled = true;
        CancelButton.IsEnabled = false;
        SetBusy(false);
        _cancellation?.Dispose();
        _cancellation = null;
    }

    /// <summary>
    /// 防重入：一次只能有一个任务在跑。
    ///
    /// 这不只是"保险"——没有它会出现一个很隐蔽的 bug：第二次进入会 new 一个新的 CTS 覆盖字段，
    /// 而**真正在跑的那个任务持有的是第一个 token**；此时点"取消"取消的是新 CTS，
    /// 老任务照样跑完（界面还显示"正在取消…"）。实测踩过这个坑，很难从现象上看出来。
    /// </summary>
    private bool _running;

    private string? ResolveWorkingDirectory()
    {
        var mode = _action?.WorkingDirectory ?? _manifest?.Runtime?.WorkingDirectory;

        return mode switch
        {
            "outputDir" => AsPath(_values.GetValueOrDefault("outputDir")),
            "userSelected" => AsPath(_values.GetValueOrDefault("workingDir")) ?? AsPath(_values.GetValueOrDefault("outputDir")),
            _ => null, // inherit：不设置，子进程继承宿主的工作目录
        };

        static string? AsPath(object? value)
        {
            var text = value?.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
    }

    private static void OpenInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // 打不开资源管理器不影响命令本身的成败。
        }
    }

    // ------------------------------------------------------------------ 输出区

    private void ClearOutput()
    {
        _outputLines.Clear();
        OutputBox.Text = string.Empty;
        _outputDirty = false;
        _pendingLogLines.Clear();
    }

    /// <summary>
    /// 收一行输出。
    ///
    /// **这里刻意不碰 TextBox**：原来每行都 <c>string.Join</c> + 赋值 + 读 <c>Text.Length</c>，
    /// 一次输出等于三次整串拷贝与一次完整重排；几万行的命令（`ipconfig /displaydns`）会让界面无响应。
    /// 现在只入队 + 标记脏，由 <see cref="FlushOutput"/> 按 150 ms 的节奏统一刷新；
    /// 同时把**原始行**攒起来批写进日志——界面只留最后 1000 行，日志里是全量。
    /// </summary>
    private void AppendOutput(string line)
    {
        // 走 ConPTY 时输出里混着颜色与光标控制序列（例如 ESC[17;1H）。
        // 直接显示就是乱码，所以展示前统一清掉。
        // （将来若要做彩色渲染，应当改成语义化渲染，而不是把这些字节原样丢给 TextBox。）
        var clean = AnsiText.Strip(line);

        _outputLines.Add(clean);

        if (_outputLines.Count > MaxOutputLines)
        {
            _outputLines.RemoveRange(0, _outputLines.Count - MaxOutputLines);
        }

        _outputDirty = true;

        // 日志攒批：太多行时按块切分，避免一次持有过大的列表
        _pendingLogLines.Add(clean);

        if (_pendingLogLines.Count >= 5000)
        {
            FlushLogLines();
        }
    }
}

public sealed record PackageEntry(string Display, string Directory, ToolManifest Manifest);

public sealed record ActionEntry(string Display, ManifestAction Action);
