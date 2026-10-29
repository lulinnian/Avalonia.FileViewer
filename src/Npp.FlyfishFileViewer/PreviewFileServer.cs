using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Npp.FlyfishFileViewer;

/// <summary>进程内、仅回环地址的单文档服务。没有可按请求传入的磁盘路径，也不启动外部服务进程。</summary>
public sealed class PreviewFileServer : IAsyncDisposable
{
    private readonly WebApplication _application;
    private PhysicalFileProvider? _assets;
    private Task? _closing;
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public Uri BaseUri { get; private set; } = null!;
    public Uri ViewerUri { get; private set; } = null!;

    private PreviewFileServer(WebApplication application) => _application = application;

    public static async Task<PreviewFileServer> StartAsync(string filePath, CancellationToken cancellationToken = default)
    {
        string file = Path.GetFullPath(filePath);
        if (!File.Exists(file)) throw new FileNotFoundException("预览文件不存在。", file);
        string assets = Path.Combine(AppContext.BaseDirectory, "FlyfishWeb");
        if (!File.Exists(Path.Combine(assets, "index.html"))) throw new DirectoryNotFoundException("缺少 FlyfishWeb 预览资源，请重新安装应用。");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = assets, ApplicationName = typeof(PreviewFileServer).Assembly.FullName });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var result = new PreviewFileServer(builder.Build());
        result.Configure(assets, file);
        try
        {
            await result._application.StartAsync(cancellationToken).ConfigureAwait(false);
            result.BaseUri = new Uri(result._application.Urls.Single());
            result.ViewerUri = new Uri(result.BaseUri, $"/index.html?token={result.Token}&name={Uri.EscapeDataString(Path.GetFileName(file))}&locale={Uri.EscapeDataString(CultureInfo.CurrentUICulture.Name)}");
            return result;
        }
        catch { await result.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private void Configure(string assets, string file)
    {
        _application.Use(async (context, next) =>
        {
            if (BaseUri is null || context.Request.Host.Value != BaseUri.Authority) { context.Response.StatusCode = 403; return; }
            SetHeaders(context.Response);
            await next(context).ConfigureAwait(false);
        });
        _application.MapGet("/file/{token}", (string token) => token == Token
            ? Results.File(file, "application/octet-stream", enableRangeProcessing: true)
            : Results.NotFound());
        FileExtensionContentTypeProvider types = new();
        types.Mappings[".mjs"] = "text/javascript"; types.Mappings[".wasm"] = "application/wasm";
        types.Mappings[".bcmap"] = "application/octet-stream"; types.Mappings[".pfb"] = "application/octet-stream";
        _assets = new PhysicalFileProvider(assets);
        _application.UseStaticFiles(new StaticFileOptions { FileProvider = _assets, ContentTypeProvider = types });
    }

    private static void SetHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-eval' 'wasm-unsafe-eval' blob:; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data: blob:; media-src 'self' data: blob:; worker-src 'self' blob:; connect-src 'self' blob:; frame-src 'self' blob:; object-src 'none'; base-uri 'self'; form-action 'none'; frame-ancestors 'none'";
    }

    public ValueTask DisposeAsync() => new(_closing ??= CloseAsync());

    private async Task CloseAsync()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
        try { await _application.StopAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        finally
        {
            await _application.DisposeAsync().ConfigureAwait(false);
            _assets?.Dispose();
        }
    }
}
