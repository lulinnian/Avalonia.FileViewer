using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Viewer = Npp.FlyfishFileViewer.FlyfishFileViewer;

namespace Avalonia.FileViewer.Demo;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<PreviewFile> _files = [];
    private bool _closing;
    private bool _canClose;

    public MainWindow() : this([]) { }

    public MainWindow(string[] paths)
    {
        InitializeComponent();
        Files.ItemsSource = _files;
        Preview.PropertyChanged += OnPreviewPropertyChanged;
        Closing += OnClosing;
        AddFiles(Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Samples"))
            .Where(path => !Path.GetFileName(path).EndsWith("LICENSE.txt", StringComparison.OrdinalIgnoreCase)));
        AddFiles(paths.Where(File.Exists));
        Files.SelectedIndex = _files.Count > 0 ? 0 : -1;
    }

    private async void OnOpenFiles(object? sender, RoutedEventArgs args)
    {
        try
        {
            var selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要预览的文件（可多选）",
                AllowMultiple = true,
                FileTypeFilter = [FilePickerFileTypes.All]
            });
            if (_closing) return;
            var paths = selected.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
            AddFiles(paths);
            if (paths.Length > 0) Files.SelectedItem = _files.First(file => file.FullPath == Path.GetFullPath(paths[0]));
        }
        catch (Exception exception) { Status.Text = $"打开失败：{exception.Message}"; }
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            string fullPath = Path.GetFullPath(path);
            if (_files.Any(file => string.Equals(file.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))) continue;
            _files.Add(new PreviewFile(fullPath));
        }
    }

    private void OnFileSelected(object? sender, SelectionChangedEventArgs args)
    {
        Preview.FilePath = (Files.SelectedItem as PreviewFile)?.FullPath;
        EmptyHint.IsVisible = Preview.FilePath is null;
        UpdateStatus();
    }

    private void OnReload(object? sender, RoutedEventArgs args) => Preview.Reload();

    private void OnClear(object? sender, RoutedEventArgs args)
    {
        Files.SelectedItem = null;
        _files.Clear();
    }

    private void OnPreviewPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Viewer.IsLoadingProperty || args.Property == Viewer.ErrorProperty) UpdateStatus();
    }

    private void UpdateStatus() => Status.Text = Preview.Error is { } error ? $"预览失败：{error}"
        : Preview.FilePath is null ? "就绪"
        : $"{(Preview.IsLoading ? "加载中" : "当前文件")}：{Preview.FilePath}";

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_canClose) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        IsEnabled = false;
        try
        {
            await Preview.DisposeAsync();
            await Viewer.ShutdownAsync();
            Preview.PropertyChanged -= OnPreviewPropertyChanged;
            _canClose = true;
            Close();
        }
        catch (Exception exception)
        {
            Status.Text = $"关闭预览失败，请重试：{exception.Message}";
            IsEnabled = true;
            _closing = false;
        }
    }

    private sealed record PreviewFile(string FullPath)
    {
        public override string ToString() => Path.GetFileName(FullPath);
    }
}
