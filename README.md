# Avalonia.FileViewer

从 NPP（nppstudio.develop）里单独拎出来的 **Flyfish 本地文件预览控件** + 一个可运行的多格式预览 Demo。

```
Avalonia.FileViewer.sln
src/Npp.FlyfishFileViewer/        控件、单文档回环服务和已打包的 Flyfish Web 资源
samples/Avalonia.FileViewer.Demo/ 演示宿主（本机运行，无 NPP 依赖）
```

## 运行 Demo

```bash
dotnet build Avalonia.FileViewer.sln -c Debug -m:1
dotnet run --project samples/Avalonia.FileViewer.Demo
```

Demo 左侧列表内嵌 4 个示例文件（`samples/Avalonia.FileViewer.Demo/Samples/`）：`报告.docx`、`演示.pptx`、`表格.xlsx`、`54880_chinese.ppt`。也可用「打开文件…」多选任意本地文件（PDF、图片、文本、音视频等，实际能力取决于已打包的渲染器）。

界面：打开文件 / 重新加载 / 清空列表；右侧为 `FlyfishFileViewer` 控件；底部状态栏显示当前路径与 `IsLoading` / `Error` 投影。关闭窗口时会先等待控件 `DisposeAsync()` 与 `FlyfishFileViewer.ShutdownAsync()`，不会留下预览端口的监听。

## 使用控件

```csharp
var preview = new Npp.FlyfishFileViewer.FlyfishFileViewer { FilePath = absolutePath };
container.Content = preview;
await preview.DisposeAsync();               // 真正关闭时在 UI 线程等待
await Npp.FlyfishFileViewer.FlyfishFileViewer.ShutdownAsync();  // 退出前等待全部实例
```

`FilePath` 支持绑定与重新赋值；`IsLoading`、`Error` 为只读通知属性；`Reload()` 重新加载。

## WebView 走哪套实现（不收费）

控件没有使用 Avalonia 的商业 WebView 组件（`Avalonia.Controls.WebView` / Avalonia Accelerate），而是用 MIT 许可的社区实现：

| 项目 | 版本 | 许可 |
|---|---|---|
| `NativeWebView` + `NativeWebView.Core/Interop` | 12.0.4.9 | MIT |
| `NativeWebView.Platform.Windows/macOS/Linux` | 12.0.4.9 | MIT |
| Avalonia | 12.1.4 | MIT |

选它的原因（实测/查证）：

- Avalonia 官方 WebView 的 NuGet 包虽然是 MIT 标注，但它属于 Avalonia Accelerate 的 **Components**；官方许可附表明确“Community 只含 Tools，不含 Components，不得随社区许可分发 Components”，要分发就得买 Plus/Pro。而 `NativeWebView` 是独立 MIT 项目，不依赖 `AvaloniaUI.Licensing`，运行时也没有相关遥测。
- 代价：社区实现，不是 Avalonia 官方支持渠道；Windows 已在本机实测，macOS/Linux 后端未验证（需要对应系统实机跑一次）。

**关于 License Key**：本解决方案不再需要 `AvaloniaUILicenseKey`，也不需要 `AvaloniaLicense.props`（Demo 里那行可选导入已删除）。核对证据：`dotnet list package --include-transitive` 的依赖图里没有 `Avalonia.Controls.WebView` 也没有 `AvaloniaUI.Licensing`；输出目录里没有任何 licensing/accelerate 程序集；不带任何许可键也能正常构建、运行、渲染。

构建日志里那条 `Avalonia Accelerate Community requires telemetry…` 与“会不会弹授权/要不要买”无关：它来自 Avalonia 12 自带的构建期统计任务 `Avalonia.BuildServices`（`AvaloniaStats`，BeforeTargets=CoreCompile），**任何** Avalonia 12 工程都会打印 —— 用一个只引用 `Avalonia.Desktop` 的空控制台工程复现过，一字不差。它是 Avalonia 自家构建工具的提示，不是本控件的依赖。

如果一定要 Avalonia 11：同族还有一个 MIT 的社区分支 `WebView.Avalonia.Cross` 11.3.1（Avalonia ≥11.3.1），但它和本控件现在用的 `NativeWebView` 是两套 API，需要把 `FlyfishFileViewer.cs` 的浏览器部分再移植一遍，且该分支同样待实机验证。当前 `NativeWebView` 要求 Avalonia ≥12.0.4，所以本解决方案停在 12.1.4。

## 运行前提

- .NET 10（`net10.0`），Avalonia 12.1.4。
- **Windows 需要 WebView2 Runtime**（Win10/11 一般已内置；缺失时控件显示“此系统未安装可用的 WebView 运行时。”）。
- 宿主窗口必须带 `app.manifest` 声明受支持的 Windows 版本，否则原生控件宿主无法创建子窗口。
- 控件用进程内 ASP.NET Core Kestrel，框架依赖部署需要 ASP.NET Core Runtime；独立部署由 SDK 打包所需运行时。
- 不需要 Avalonia 许可键；运行时依赖里没有任何许可/遥测组件。构建期 Avalonia 12 自己的 `AvaloniaStats` 任务仍会打印一条提示（见上），与授权无关。

## 前端资源

`src/Npp.FlyfishFileViewer/Web/`（Flyfish 3.3.0 standard preset + `@file-viewer/renderer-ppt` 3.3.0）与 `ThirdPartyNotices/` 已随源码保存，正常 .NET 构建不需要 Node/npm。需要改前端时：

```bash
cd src/Npp.FlyfishFileViewer/Frontend
npm ci --ignore-scripts --no-audit --no-fund
npm run build
```

服务只监听 `127.0.0.1` 的系统分配端口，一个实例只暴露一个已捕获文件，每份文档独立随机令牌并核对 Host，请求不能指定磁盘路径。

## 许可

Flyfish 核心及渲染包装层为 Apache-2.0，但不覆盖全部第三方运行时：旧版 `.ppt` 引擎是 **Flyfish Public Watermarked Runtime License**（允许商业集成与离线分发，每页必须保留原始可见水印）。完整依赖声明见 `src/Npp.FlyfishFileViewer/ThirdPartyNotices/THIRD-PARTY-NOTICES.txt`；Demo 的示例文件许可见 `samples/Avalonia.FileViewer.Demo/Samples/*LICENSE.txt`。

上游资料：[File Viewer](https://github.com/flyfish-dev/file-viewer)、[格式及 PPT 引擎许可边界](https://github.com/flyfish-dev/file-viewer/blob/main/docs/guide/formats.md)、[NativeWebView](https://github.com/wieslawsoltes/NativeWebView)。
