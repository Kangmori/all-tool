using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Swpj.Core.Discovery;
using Swpj.Core.Execution;
using Swpj.Core.Manifest;
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
    private readonly List<PackageEntry> _packages = [];
    private readonly List<ActionEntry> _actions = [];
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly List<(string Id, Func<object?> Get)> _valueSync = [];
    private readonly List<string> _outputLines = [];

    private ToolManifest? _manifest;
    private ManifestAction? _action;
    private string? _executablePath;
    private CancellationTokenSource? _cancellation;

    public MainWindow()
    {
        InitializeComponent();
        LoadPackages();
    }

    // ------------------------------------------------------------------ 载入

    private void LoadPackages()
    {
        try
        {
            var pluginsRoot = RepoPaths.PluginsDirectory;

            foreach (var (directory, manifest) in ManifestLoader.LoadAll(pluginsRoot))
            {
                _packages.Add(new PackageEntry(
                    $"{manifest.Name}（{manifest.Id}）",
                    directory,
                    manifest));
            }

            PackageList.ItemsSource = _packages;
            StatusText.Text = _packages.Count == 0
                ? $"在 {pluginsRoot} 里没找到任何工具包"
                : $"已载入 {_packages.Count} 个工具包，请选择";

            // 只有一个/第一个工具包时直接选中，省一次点击。
            if (_packages.Count > 0)
            {
                PackageList.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "载入工具包失败：" + ex.Message;
        }
    }

    private void OnPackageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PackageList.SelectedItem is not PackageEntry entry)
        {
            return;
        }

        _manifest = entry.Manifest;
        _executablePath = null;

        _actions.Clear();
        foreach (var action in _manifest.Actions ?? [])
        {
            _actions.Add(new ActionEntry(
                $"{action.Title}（{(string.IsNullOrEmpty(action.Command) ? "-" : action.Command)}）",
                action));
        }

        ActionList.ItemsSource = null;
        ActionList.ItemsSource = _actions;
        ActionTitle.Text = "请选择一个动作";
        ActionDescription.Text = string.Empty;
        FormPanel.Children.Clear();
        CommandLineBox.Text = string.Empty;
        RunButton.IsEnabled = false;

        StatusText.Text = $"{_manifest.Name} {_manifest.AppVersion} —— {_actions.Count} 个动作";

        // 自动化冒烟用的钩子：设了 SWPJ_SELECT_ACTION=<序号> 就自动选中该动作并生成表单。
        // 存在的理由：动态表单是最容易在运行时出错的地方，需要一个不靠人点鼠标的验证入口。
        if (int.TryParse(Environment.GetEnvironmentVariable("SWPJ_SELECT_ACTION"), out var index)
            && index >= 0 && index < _actions.Count)
        {
            ActionList.SelectedIndex = index;
        }
    }

    private void OnActionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActionList.SelectedItem is not ActionEntry entry)
        {
            return;
        }

        _action = entry.Action;
        BuildForm();
    }

    // ------------------------------------------------------------------ 动态表单

    private void BuildForm()
    {
        FormPanel.Children.Clear();
        _values.Clear();
        _valueSync.Clear();

        if (_action is null)
        {
            RunButton.IsEnabled = false;
            return;
        }

        ActionTitle.Text = _action.Title ?? string.Empty;
        ActionDescription.Text = _action.Description ?? string.Empty;

        if (_action.Danger != DangerLevel.None)
        {
            ActionDescription.Text += _action.Danger == DangerLevel.Destructive
                ? "\n⚠ 此动作会不可逆地修改数据，执行前会再次确认。"
                : "\n⚠ 此动作会覆盖已有文件。";
        }

        var advancedPanel = new StackPanel { Spacing = 12 };
        var hasAdvanced = false;

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
                advancedPanel.Children.Add(control);
                hasAdvanced = true;
            }
            else
            {
                FormPanel.Children.Add(control);
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

        switch (field.Type)
        {
            case "bool":
            {
                var box = new CheckBox
                {
                    Content = string.IsNullOrEmpty(field.Help) ? "启用" : field.Help,
                    IsChecked = field.Default is bool b
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
                FieldValue? preselect = (field.Values ?? []).FirstOrDefault(v => v.IsDefault);

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

                box.Value = field.Default is not null
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
                    .Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0)
                    .ToList();
                break;
            }

            default:
            {
                var box = new TextBox
                {
                    PlaceholderText = field.Placeholder ?? string.Empty,
                    AcceptsReturn = field.Type == "textarea",
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };

                if (field.Default is not null)
                {
                    box.Text = field.Default.ToString() ?? string.Empty;
                }

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

                getter = () => string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
                break;
            }
        }

        return stack;
    }

    private static void AppendLines(TextBox box, IEnumerable<string> lines)
    {
        var existing = box.Text
            .Split('\n')
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

        if (_action.Danger == DangerLevel.Destructive)
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
        ProgressIndicator.Value = 0;

        // 先转圈，等真的解析到百分比再切成确定进度。
        // 这么做是必要的：7z 在被重定向时**不输出百分比**，若一上来就画确定进度条，
        // 用户会以为程序卡住了。见 plugins/7zip/NOTES.md 的实测记录。
        ProgressIndicator.IsIndeterminate = true;
        var sawProgressValue = false;
        StatusText.Text = "执行中…";

        var parser = new ProgressParser(_action.Output?.Progress);
        var progress = new Progress<ProcessOutputLine>(line =>
        {
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
        StatusText.Text = "正在取消…";
        _cancellation?.Cancel();
    }

    private void FinishRun()
    {
        RunButton.IsEnabled = true;
        CancelButton.IsEnabled = false;
        _cancellation?.Dispose();
        _cancellation = null;
    }

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
