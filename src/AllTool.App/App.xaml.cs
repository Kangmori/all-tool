using Microsoft.UI.Xaml;

namespace AllTool.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();

        // 限制最小尺寸：否则窗口能被拖到只剩标题栏三个按钮（最大化/最小化/关闭），
        // 内容全被裁掉——产品负责人实测反馈过。
        if (_window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 880;
            presenter.PreferredMinimumHeight = 560;
        }
        _window.Activate();
    }
}
