using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Swpj.Core.Discovery;
using Swpj.Core.Execution;
using Swpj.Core.Manifest;
using Swpj.Core.Settings;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Swpj.App;

/// <summary>
/// 宿主主窗口。
///
/// 界面完全是"清单驱动"的：左侧列出工具包与动作，中间的表单由 <see cref="BuildForm"/>
/// 按字段定义动态生成，执行前把将要运行的命令原样显示出来（硬规则 R6）。
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>输出框最多保留多少行（避免超长输出把界面拖慢）。</summary>
    private const int MaxOutputLines = 1000;

    private readonly ProcessRunner _runner = new();
    private readonly LastValuesStore _lastValues = LastValuesStore.OpenDefault();
    private readonly GroupingStore _grouping = GroupingStore.OpenDefault();
    private readonly List<PackageEntry> _packages = [];
    private readonly List<ActionEntry> _actions = [];
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly List<(string Id, Func<object?> Get)> _valueSync = [];
    private readonly List<string> _outputLines = [];

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

        LoadPackages();
    }

    // ------------------------------------------------------------------ 左栏：工具包与动作（按分组呈现）

    /// <summary>记住上次选中的工具包/动作，下次打开直接回到那里。</summary>
    private const string UiStateScope = "__ui__";

    /// <summary>实际使用的工具包目录（安装/卸载都作用在这里）。</summary>
    private string _pluginsRoot = string.Empty;

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

            // 恢复上次的选择；没有记录就选第一个，省一次点击。
            // 自动化冒烟可以用 SWPJ_SELECT_PACKAGE 钉住工具包——只靠 SWPJ_SELECT_ACTION 不够，
            // 因为"恢复上次选择"会让那个序号套用到别的包上（脚本会静默失灵，踩过）。
            var forcedPackage = Environment.GetEnvironmentVariable("SWPJ_SELECT_PACKAGE");
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
        }
    }

    private string PackageGroupOf(PackageEntry entry) =>
        _grouping.GetPackageGroup(entry.Manifest.Id ?? string.Empty)
        ?? (string.IsNullOrWhiteSpace(entry.Manifest.Category) ? "未分组" : entry.Manifest.Category!);

    private string ActionGroupOf(ManifestAction action) =>
        _grouping.GetActionGroup(_manifest?.Id ?? string.Empty, action.Id ?? string.Empty)
        ?? (string.IsNullOrWhiteSpace(action.Category) ? "未分组" : action.Category!);

    /// <summary>按分组重建左侧工具包列表。</summary>
    private void RebuildPackageList()
    {
        var filter = FilterBox?.Text?.Trim() ?? string.Empty;

        var visible = _packages
            .Where(p => Matches(filter, p.Display, p.Manifest.Description, p.Manifest.Id))
            .ToList();

        PackageGroupsPanel.Children.Clear();

        foreach (var group in GroupBy(visible, PackageGroupOf, _grouping.GetCustomGroups(forPackages: true)))
        {
            var groupName = group.Key;

            PackageGroupsPanel.Children.Add(BuildGroup(
                groupName,
                group.Items,
                entry => new GroupItem(
                    entry.Display,
                    ReferenceEquals(entry.Manifest, _manifest),
                    () => SelectPackage(entry, restoreAction: false),
                    DragKey.Package(entry.Manifest.Id ?? string.Empty, entry.Display),
                    menu => BuildPackageMenu(menu, entry)),
                DragKey.PackageFormat,
                key => MovePackageToGroup(key, groupName)));
        }
    }

    private void RebuildActionList()
    {
        var filter = FilterBox?.Text?.Trim() ?? string.Empty;

        var visible = _actions
            .Where(a => Matches(filter, a.Display, a.Action.Title, a.Action.Command, a.Action.Description))
            .ToList();

        ActionGroupsPanel.Children.Clear();

        if (_manifest is null)
        {
            return;
        }

        var packageId = _manifest.Id ?? string.Empty;

        foreach (var group in GroupBy(visible, a => ActionGroupOf(a.Action), _grouping.GetCustomGroups(forPackages: false)))
        {
            var groupName = group.Key;

            ActionGroupsPanel.Children.Add(BuildGroup(
                groupName,
                group.Items,
                entry => new GroupItem(
                    entry.Display,
                    ReferenceEquals(entry.Action, _action),
                    () => SelectAction(entry),
                    DragKey.Action(packageId, entry.Action.Id ?? string.Empty, entry.Display),
                    _ => { }),
                DragKey.ActionFormat,
                key => MoveActionToGroup(key, groupName)));
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
        string Text,
        bool Selected,
        Action Select,
        string DragKey,
        Action<MenuFlyout> BuildMenu);

    /// <summary>拖动条目时放在剪贴板数据里的键，用来区分"内部条目"与"从资源管理器拖来的文件"。</summary>
    private static class DragKey
    {
        public const string PackageFormat = "swpj.package";
        public const string ActionFormat = "swpj.action";

        public static string Package(string packageId, string display) => $"{packageId}\u0001{display}";

        public static string Action(string packageId, string actionId, string display) =>
            $"{packageId}\u0001{actionId}\u0001{display}";
    }

    /// <summary>
    /// 生成一个可折叠的分组：标题 + 组内条目。
    ///
    /// 交互上有三条约定：
    ///   1. **默认折叠**——工具包多起来之后全展开会很长；
    ///   2. 条目可以**拖到别的分组标题上**来改分组（比右键菜单直观）；
    ///   3. 拖到「工具包」/「动作」标题上 = 恢复清单里的默认分组。
    /// </summary>
    private static Expander BuildGroup<T>(
        string title,
        List<T> items,
        Func<T, GroupItem> describe,
        string dragFormat,
        Action<string> onItemDropped)
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
                CanDrag = true,
            };
            button.Click += (_, _) => info.Select();

            button.DragStarting += (_, e) =>
            {
                e.Data.SetData(dragFormat, info.DragKey);
                e.Data.RequestedOperation = DataPackageOperation.Move;
                e.DragUI.SetContentFromDataPackage();
            };

            var menu = new MenuFlyout();
            info.BuildMenu(menu);
            button.ContextFlyout = menu;

            // 选中项左侧加一小段强调色，比整块高亮更稳妥（不依赖主题资源名）
            var marker = new Border
            {
                Width = 3,
                Background = info.Selected
                    ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                CornerRadius = new CornerRadius(2),
            };

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

        var expander = new Expander
        {
            Header = header,
            Content = list,
            IsExpanded = false,                 // 默认折叠
            AllowDrop = true,                   // 接收拖来的条目 → 加入本组
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 2, 0, 0),
        };

        expander.DragOver += (_, e) =>
        {
            if (e.DataView.Contains(dragFormat))
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.DragUIOverride.Caption = $"移到「{title}」";
                e.DragUIOverride.IsCaptionVisible = true;
            }
        };

        expander.Drop += (_, e) =>
        {
            if (e.DataView.Contains(dragFormat))
            {
                e.Handled = true;
                onItemDropped(e.DataView.GetDataAsync(dragFormat).AsTask().GetAwaiter().GetResult() as string ?? string.Empty);
            }
        };

        return expander;
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        RebuildPackageList();
        RebuildActionList();
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
        ActionTitle.Text = "请选择一个动作";
        ActionDescription.Text = string.Empty;
        FormPanel.Children.Clear();
        CommandLineBox.Text = string.Empty;
        NextStepsPanel.Children.Clear();
        NextStepsPanel.Visibility = Visibility.Collapsed;
        RunButton.IsEnabled = false;

        RebuildPackageList();
        RebuildActionList();

        StatusText.Text = $"{_manifest.Name} {_manifest.AppVersion} —— {_actions.Count} 个动作";
        _lastValues.Set(UiStateScope, "state", "lastPackage", _manifest.Id);

        // 自动化冒烟用的钩子：设了 SWPJ_SELECT_ACTION=<序号> 就自动选中该动作并生成表单。
        // 存在的理由：动态表单是最容易在运行时出错的地方，需要一个不靠人点鼠标的验证入口。
        var hook = Environment.GetEnvironmentVariable("SWPJ_SELECT_ACTION");
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

    private void SelectAction(ActionEntry entry, IReadOnlyDictionary<string, object?>? presets = null)
    {
        _action = entry.Action;
        _pendingPresets = presets;
        _lastValues.Set(UiStateScope, "state", $"lastAction:{_manifest?.Id}", entry.Action.Id);

        BuildForm();
        RebuildActionList();
    }

    // ------------------------------------------------------------------ 分组：拖动改分组 / 新建分组

    /// <summary>把拖过来的工具包放进目标分组（空分组名 = 恢复清单里的默认分组）。</summary>
    private void MovePackageToGroup(string dragKey, string? groupName)
    {
        var packageId = dragKey.Split('\u0001')[0];

        if (string.IsNullOrEmpty(packageId))
        {
            return;
        }

        _grouping.SetPackageGroup(packageId, groupName);
        _grouping.Save();
        RebuildPackageList();

        StatusText.Text = string.IsNullOrEmpty(groupName)
            ? $"「{packageId}」已恢复清单里的默认分组"
            : $"「{packageId}」已移到「{groupName}」";
    }

    private void MoveActionToGroup(string dragKey, string? groupName)
    {
        var parts = dragKey.Split('\u0001');

        if (parts.Length < 2 || _manifest?.Id is null)
        {
            return;
        }

        _grouping.SetActionGroup(_manifest.Id, parts[1], groupName);
        _grouping.Save();
        RebuildActionList();

        StatusText.Text = string.IsNullOrEmpty(groupName)
            ? $"「{parts[1]}」已恢复清单里的默认分组"
            : $"「{parts[1]}」已移到「{groupName}」";
    }

    /// <summary>在「工具包」/「动作」标题上右键 → 新建一个空分组（然后把条目拖进去）。</summary>
    private async Task CreateGroupAsync(bool forPackages)
    {
        var input = new TextBox { PlaceholderText = "新分组名，例如「常用」「诊断」" };

        var dialog = new ContentDialog
        {
            Title = forPackages ? "新建工具包分组" : "新建动作分组",
            Content = input,
            PrimaryButtonText = "创建",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(input.Text))
        {
            return;
        }

        _grouping.AddCustomGroup(forPackages, input.Text);
        _grouping.Save();

        if (forPackages)
        {
            RebuildPackageList();
        }
        else
        {
            RebuildActionList();
        }

        StatusText.Text = $"已新建分组「{input.Text.Trim()}」，把条目拖进去即可";
    }

    /// <summary>删掉一个用户新建的分组（里面的条目回到清单默认分组）。</summary>
    private async Task RemoveGroupAsync(bool forPackages, string name)
    {
        var dialog = new ContentDialog
        {
            Title = $"删除分组「{name}」？",
            Content = "分组里的条目会回到清单里声明的默认分组，条目本身不会被删掉。",
            PrimaryButtonText = "删除分组",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _grouping.RemoveCustomGroup(forPackages, name);
        _grouping.Save();

        if (forPackages)
        {
            RebuildPackageList();
        }
        else
        {
            RebuildActionList();
        }

        StatusText.Text = $"已删除分组「{name}」";
    }

    /// <summary>给「工具包」/「动作」标题装上下文菜单，并让它能接收"拖出来恢复默认分组"。</summary>
    private void WireSectionTitle(TextBlock title, bool forPackages, StackPanel panel)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem("新建分组…", () => _ = CreateGroupAsync(forPackages)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem("全部展开", () => SetGroupsExpanded(panel, true)));
        menu.Items.Add(MenuItem("全部收起", () => SetGroupsExpanded(panel, false)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem("全部恢复默认分组", () => ResetGroups(forPackages)));
        title.ContextFlyout = menu;

        // 把条目拖到标题上 = 恢复清单里的默认分组
        title.AllowDrop = true;

        title.DragOver += (_, e) =>
        {
            var format = forPackages ? DragKey.PackageFormat : DragKey.ActionFormat;

            if (e.DataView.Contains(format))
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.DragUIOverride.Caption = "恢复默认分组";
                e.DragUIOverride.IsCaptionVisible = true;
            }
        };

        title.Drop += (_, e) =>
        {
            var format = forPackages ? DragKey.PackageFormat : DragKey.ActionFormat;

            if (!e.DataView.Contains(format))
            {
                return;
            }

            e.Handled = true;
            var key = e.DataView.GetDataAsync(format).AsTask().GetAwaiter().GetResult() as string ?? string.Empty;

            if (forPackages)
            {
                MovePackageToGroup(key, null);
            }
            else
            {
                MoveActionToGroup(key, null);
            }
        };
    }

    private static MenuFlyoutItem MenuItem(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();

        return item;
    }

    private static void SetGroupsExpanded(Panel panel, bool expanded)
    {
        foreach (var child in panel.Children.OfType<Expander>())
        {
            child.IsExpanded = expanded;
        }
    }

    private void ResetGroups(bool forPackages)
    {
        if (forPackages)
        {
            foreach (var package in _packages)
            {
                _grouping.SetPackageGroup(package.Manifest.Id ?? string.Empty, null);
            }

            _grouping.Save();
            RebuildPackageList();
        }
        else if (_manifest?.Id is string packageId)
        {
            foreach (var action in _actions)
            {
                _grouping.SetActionGroup(packageId, action.Action.Id ?? string.Empty, null);
            }

            _grouping.Save();
            RebuildActionList();
        }

        StatusText.Text = "已恢复清单里声明的默认分组";
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
            swpj —— 把命令行软件变成可点击界面
            宿主版本：{version}
            运行环境：.NET {Environment.Version} / {Environment.OSVersion.VersionString}

            已载入 {_packages.Count} 个工具包：
            {packages}

            工具包目录：{_pluginsRoot}
            设置目录：{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "swpj")}

            每个命令执行前都会显示将运行的完整命令行；工具包是纯声明的 YAML，不含代码。
            执行方式：直接调用程序（CreateProcess），不经过 cmd 或 PowerShell。
            """;

        var dialog = new ContentDialog
        {
            Title = "关于 swpj",
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
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

        if (_action.DangerOrDefault != DangerLevel.None)
        {
            ActionDescription.Text += _action.DangerOrDefault == DangerLevel.Destructive
                ? "\n⚠ 此动作会不可逆地修改数据，执行前会再次确认。"
                : "\n⚠ 此动作会覆盖已有文件。";
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

        if (suggestions.Count == 0)
        {
            NextStepsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        NextStepsPanel.Children.Add(new TextBlock
        {
            Text = "下一步",
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.75,
        });

        foreach (var suggestion in suggestions)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var button = new Button { Content = suggestion.Title };
            var captured = suggestion;
            button.Click += async (_, _) => await RunSuggestionAsync(captured);
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

            NextStepsPanel.Children.Add(row);
        }

        NextStepsPanel.Visibility = Visibility.Visible;
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
    }

    private void AppendOutput(string line)
    {
        // 走 ConPTY 时输出里混着颜色与光标控制序列（例如 ESC[17;1H）。
        // 直接显示就是乱码，所以展示前统一清掉。
        // （将来若要做彩色渲染，应当改成语义化渲染，而不是把这些字节原样丢给 TextBox。）
        _outputLines.Add(AnsiText.Strip(line));

        if (_outputLines.Count > MaxOutputLines)
        {
            _outputLines.RemoveRange(0, _outputLines.Count - MaxOutputLines);
        }

        OutputBox.Text = string.Join(Environment.NewLine, _outputLines);
        OutputBox.SelectionStart = OutputBox.Text.Length;
    }
}

public sealed record PackageEntry(string Display, string Directory, ToolManifest Manifest);

public sealed record ActionEntry(string Display, ManifestAction Action);
