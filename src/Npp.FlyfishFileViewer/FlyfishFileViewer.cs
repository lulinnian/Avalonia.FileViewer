using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NativeWebView.Core;
using WebViewControl = NativeWebView.Controls.NativeWebView;

namespace Npp.FlyfishFileViewer;

/// <summary>独立的本地文件预览控件。FilePath 可绑定；关闭控件须在 UI 线程等待 DisposeAsync。</summary>
public sealed class FlyfishFileViewer : UserControl, IAsyncDisposable
{
    public static readonly StyledProperty<string?> FilePathProperty = AvaloniaProperty.Register<FlyfishFileViewer, string?>(nameof(FilePath));
    public static readonly DirectProperty<FlyfishFileViewer, bool> IsLoadingProperty = AvaloniaProperty.RegisterDirect<FlyfishFileViewer, bool>(nameof(IsLoading), view => view.IsLoading);
    public static readonly DirectProperty<FlyfishFileViewer, string?> ErrorProperty = AvaloniaProperty.RegisterDirect<FlyfishFileViewer, string?>(nameof(Error), view => view.Error);
    private static readonly HashSet<FlyfishFileViewer> Active = [];
    private readonly Grid _layout = new() { RowDefinitions = new RowDefinitions("Auto,*") };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _profileDirectory = Path.Combine(Path.GetTempPath(), "NppFlyfish", $"{Environment.ProcessId}-{Guid.NewGuid():N}");
    private PreviewFileServer? _server;
    private WebViewControl? _browser;
    private Window? _window;
    private Task _pending = Task.CompletedTask;
    private Task? _closing;
    private bool _closed, _isLoading;
    private int _generation;
    private string? _error, _openedPath, _homeUri;

    public string? FilePath { get => GetValue(FilePathProperty); set => SetValue(FilePathProperty, value); }
    public bool IsLoading { get => _isLoading; private set => SetAndRaise(IsLoadingProperty, ref _isLoading, value); }
    public string? Error { get => _error; private set => SetAndRaise(ErrorProperty, ref _error, value); }

    public FlyfishFileViewer()
    {
        ProgressBar progress = new() { IsIndeterminate = true, Height = 3 };
        progress.Bind(IsVisibleProperty, new Binding(nameof(IsLoading)) { Source = this });
        TextBlock error = new() { Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };
        error.Bind(TextBlock.TextProperty, new Binding(nameof(Error)) { Source = this });
        Grid.SetRow(error, 1); _layout.Children.Add(progress); _layout.Children.Add(error); Content = _layout;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FilePathProperty && TopLevel.GetTopLevel(this) is not null) Reload();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_closed) return;
        if (_window is not null) _window.Closed -= OnWindowClosed;
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null) _window.Closed += OnWindowClosed;
        if (_openedPath != FilePath || _server is null) Reload();
    }

    public void Reload()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_closed) return;
        Active.Add(this);
        int generation = ++_generation;
        _pending = OpenAsync(_pending, FilePath, generation);
    }

    private async Task OpenAsync(Task previous, string? file, int generation)
    {
        try
        {
            await previous;
            if (_closed || generation != _generation) return;
            await ReleaseCurrentAsync();
            IsLoading = false; Error = null;
            if (_closed || generation != _generation || string.IsNullOrWhiteSpace(file)) return;
            IsLoading = true;
            var server = await PreviewFileServer.StartAsync(file, _lifetime.Token);
            if (_closed || generation != _generation) { await server.DisposeAsync(); return; }
            _server = server; _openedPath = file; _homeUri = server.ViewerUri.ToString();
            await CreateBrowserAsync(server, generation);
        }
        catch (Exception exception) { if (!_closed && generation == _generation) SetError(exception.Message); }
    }

    private async Task CreateBrowserAsync(PreviewFileServer server, int generation)
    {
        WebViewControl browser = CreateBrowser(server.ViewerUri);
        if (_closed || generation != _generation) { browser.Dispose(); return; }
        Grid.SetRow(browser, 1); _browser = browser; _layout.Children.Add(browser);
        await browser.InitializeAsync(_lifetime.Token);
        if (_closed || generation != _generation) return;
        if (browser.LifecycleState != NativeWebComponentState.Ready) SetError("此系统未安装可用的 WebView 运行时。");
    }

    private WebViewControl CreateBrowser(Uri viewerUri)
    {
        WebViewControl browser = new() { Name = "FlyfishBrowser" };
        // 与原控件一致：每份文档独立临时配置目录 + 隐私模式，语言跟随当前 UI 区域。
        NativeWebViewInstanceConfiguration configuration = browser.InstanceConfiguration;
        configuration.EnvironmentOptions.UserDataFolder = _profileDirectory;
        configuration.EnvironmentOptions.Language = CultureInfo.CurrentUICulture.Name;
        configuration.ControllerOptions.IsInPrivateModeEnabled = true;
        // 前端用 window.invokeCSharpAction 上报状态；本控件只提供标准 WebView 消息通道，
        // 因此在文档脚本执行前补一层桥接，前端产物无需改动。
        configuration.DocumentStartScripts.Add(new NativeWebViewDocumentStartScript(BridgeScript, NativeWebViewScriptFrameScope.MainFrame));
        browser.InstanceConfiguration = configuration;
        browser.NavigationStarted += (_, args) => args.Cancel = args.Uri?.ToString() is { } request && request != _homeUri && request != "about:blank";
        browser.NewWindowRequested += (_, args) => args.Handled = true;
        browser.WebMessageReceived += (_, args) => ReceiveState(args.Message);
        browser.Source = viewerUri;
        return browser;
    }

    private void ReceiveState(string? body)
    {
        if (_closed || _server is null || string.IsNullOrEmpty(body)) return;
        try
        {
            using JsonDocument message = JsonDocument.Parse(body);
            if (message.RootElement.GetProperty("token").GetString() != _server.Token) return;
            IsLoading = message.RootElement.GetProperty("state").GetString() == "loading";
            Error = message.RootElement.GetProperty("state").GetString() == "error" ? message.RootElement.GetProperty("message").GetString() : null;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
    }

    private void SetError(string message) { IsLoading = false; Error = message; }

    private const string BridgeScript = """
        window.invokeCSharpAction = window.invokeCSharpAction || function (value) {
          try { window.chrome?.webview?.postMessage(value); } catch (e) { /* 平台后端没有 chrome.webview 时保持静默 */ }
        };
        """;

    private async Task ReleaseCurrentAsync()
    {
        if (_browser is not null) _layout.Children.Remove(_browser);
        _browser?.Dispose(); _browser = null; _homeUri = null;
        PreviewFileServer? server = _server; _server = null;
        if (server is not null) await server.DisposeAsync();
    }

    public ValueTask DisposeAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        return new(_closing ??= CloseAsync());
    }

    private async Task CloseAsync()
    {
        _closed = true; ++_generation; _lifetime.Cancel();
        if (_window is not null) _window.Closed -= OnWindowClosed;
        await _pending;
        await ReleaseCurrentAsync();
        IsLoading = false;
        _lifetime.Dispose();
        await RemoveProfileAsync();
        Active.Remove(this);
    }

    private async Task RemoveProfileAsync()
    {
        for (int attempt = 0; attempt < 20 && Directory.Exists(_profileDirectory); attempt++)
        {
            try { Directory.Delete(_profileDirectory, true); return; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { await Task.Delay(100); }
        }
    }

    private async void OnWindowClosed(object? sender, EventArgs args)
    {
        try { await DisposeAsync(); }
        catch (Exception exception) { System.Diagnostics.Debug.WriteLine(exception); }
    }

    /// <summary>宿主退出时也等待已关闭 Tab 尚在收尾的控件，不依赖终结器或全局进程名清理。</summary>
    public static Task ShutdownAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        return Task.WhenAll(Active.ToArray().Select(view => view.DisposeAsync().AsTask()));
    }
}
