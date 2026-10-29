# Npp.FlyfishFileViewer

Avalonia 12 的独立 Flyfish 本地文件预览控件，位于本解决方案的 `src/` 文件夹（由 NPP 主仓 `Controls/Npp.FlyfishFileViewer` 单独拎出，代码与前端资源与主仓一致）。参考了 `D:\Research\XMLProject\fileviewer` 的 WebView 封装思路；本项目自行实现单文件服务和生命周期管理，不依赖该目录构建。

## 使用

```csharp
var preview = new Npp.FlyfishFileViewer.FlyfishFileViewer { FilePath = absolutePath };
container.Content = preview;
// 真正关闭时在 UI 线程等待；普通 Tab 切换可继续缓存并重挂载同一控件。
await preview.DisposeAsync();
// 应用退出前等待所有实例，包括已经开始关闭但仍在收尾的控件。
await Npp.FlyfishFileViewer.FlyfishFileViewer.ShutdownAsync();
```

`FilePath` 支持绑定与重新赋值；`IsLoading`、`Error` 为只读通知属性，`Reload()` 可重新加载。宿主只需要平台 WebView 运行时（Windows 为 WebView2 Runtime），无需 Avalonia 许可键。控件使用进程内 ASP.NET Core Kestrel，框架依赖部署需要 .NET 10 的 ASP.NET Core Runtime；独立部署由 SDK 打包所需运行时。

UI 由控件自带（顶部 `ProgressBar`、正文区 `NativeWebView`、错误文本），宿主只需给容器和 `FilePath`；最小宿主见 `samples/Avalonia.FileViewer.Demo`。

浏览器层用的是 MIT 许可的社区实现 `NativeWebView` 12.0.4.9（+ `NativeWebView.Platform.Windows/macOS/Linux`），不是 Avalonia Accelerate 的商业 WebView 组件：官方许可附表把 Community 限定为 Tools，Components 要付费才可分发；本项目不依赖 `AvaloniaUI.Licensing`，也不需要 `AvaloniaUILicenseKey`。代价是社区实现，Windows 已实测，macOS/Linux 后端待实机验证。（构建日志里的 `Avalonia Accelerate Community requires telemetry…` 来自 Avalonia 12 自带的 `AvaloniaStats` 构建任务，任何 Avalonia 12 工程都会打印，与授权无关。）

## 支持的格式

前端固定为 Flyfish 3.3.0 的 standard preset，另加 `@file-viewer/renderer-ppt` 3.3.0（引擎 `@file-viewer/ppt` 0.3.6）。这是已安装渲染器的集合，不承诺上游完整目录里的所有格式；未引入 CAD 等扩展。控件本身支持 DOCX、PPTX、旧版 PPT、XLSX、PDF、图片、文本等；无法识别或损坏的文件显示前端错误，由宿主决定是否提供“使用系统默认程序打开”。在 NPP 主仓中，CSV/Excel/Arrow/PDF/图片/代码/Markdown/工作流仍优先走专用预览，未命中的本地文件才进入本控件。

## 本地资源与构建

```powershell
cd src/Npp.FlyfishFileViewer/Frontend
npm ci --ignore-scripts --no-audit --no-fund
npm run build
```

`package-lock.json`、`Web/` 和 `ThirdPartyNotices/` 随源码保存，正常 .NET 构建不需要 Node 或 npm。Vite 官方插件复制 Worker、WASM、字体等运行资源；许可证脚本收集生产依赖的完整声明。局部 `.gitattributes` 保留分发资源的准确字节，避免换行转换干扰完整性校验。`FlyfishWeb/` 与 `ThirdPartyNotices/Flyfish/` 自动进入宿主输出和发布目录，单文件发布时仍保留为外部文件，资源基于 `AppContext.BaseDirectory` 查找。

旧 PPT 运行时通过 Rollup external/path 映射统一加载 `vendor/ppt/index.mjs`，字体、WASM 和 Worker 只保留官方插件复制的一套原始文件，避免 Vite 在 `assets/` 再生成相同资源。字体及 WASM 的 SHA-256 完整性校验和水印保持原样；发布应使用干净目录，避免旧输出中已不再引用的哈希资源残留。

服务仅监听 `127.0.0.1` 的系统分配端口，一个实例只暴露一个已捕获文件。每份文档使用独立随机令牌，核对 Host；请求不能指定磁盘路径。CSP 限定本地来源和解析所需的 blob/data，不调用外部文档转换服务或 CDN。原始文件只读。

## 生命周期

不启动 Node、Office 或转换服务进程。服务与宿主同进程，退出或崩溃时操作系统关闭监听；正常关闭还等待 Kestrel 停止和请求释放。

控件把 `NativeWebView` 直接挂进自己的 Grid，首次进入视觉树后 `InitializeAsync`；`LifecycleState` 不是 `Ready` 时显示“此系统未安装可用的 WebView 运行时”。初始化期间关闭由代数号和 `_pending` 任务串行化：迟到的 server/browser 会被丢弃并释放，不会留下监听或原生窗口。

前端用 `window.invokeCSharpAction` 上报状态，社区实现只提供标准 WebView 消息通道，因此控件在 `DocumentStartScripts` 里补了一层桥接（`window.chrome.webview.postMessage`），Flyfish 产物本身不用改。

宿主窗口必须带 `app.manifest`（声明受支持的 Windows 版本），否则 `NativeWebView` 无法创建子窗口：`Unable to create child window for native control host`。Demo 的 `app.manifest` 是可直接照抄的最小版本。

Windows 每个实例使用唯一临时 UserDataFolder 和隐私模式，正常关闭后限次重试清理该目录；macOS 使用非持久化数据存储，GTK 使用临时数据管理器。崩溃后可能留下临时缓存目录，不能据此认定进程仍存活；没有按全局进程名强杀或删除其他实例的目录。

## 许可

Flyfish 核心和各渲染包装层的 Apache-2.0 许可不覆盖全部第三方运行时。旧版 `.ppt` 引擎采用 **Flyfish Public Watermarked Runtime License**，允许商业产品集成和离线分发，但每页必须保留原始可见水印；本项目不修改该引擎或水印。DOCX/PPTX/Excel 不使用这个旧 PPT 引擎。完整依赖声明见 `ThirdPartyNotices/THIRD-PARTY-NOTICES.txt`。

旧 PPT 的兼容性仍受上游引擎限制：公开的 `layout_types_2011.ppt` 样本被 0.3.6 拒绝并报告 `native error 3`，宿主显示错误并保留系统打开入口；不通过修改专有引擎绕过解析或授权检查。

上游资料：[File Viewer](https://github.com/flyfish-dev/file-viewer)、[格式及 PPT 引擎许可边界](https://github.com/flyfish-dev/file-viewer/blob/main/docs/guide/formats.md)、[Avalonia NativeWebView](https://docs.avaloniaui.net/controls/web/nativewebview)。

## 验收现状

本仓库保留 NPP 主仓的可复现验证结果与方式，验收脚手架（`Test/AIWorkbench.ImeSmoke --flyfish-artifacts`、`Test/FlyfishTestSupport/VerifyProcesses.ps1`）仍在主仓，未随控件拷出：

- 主仓 `Test/AIWorkbench.Tests/FlyfishPreviewTests.cs`：真实 HTTP 单文件隔离、令牌、Host、范围请求、资源策略、关闭释放和专用预览优先级。
- 主仓 `AIWorkbench.ImeSmoke --flyfish-artifacts`：DOCX/PPTX/PPT/Excel 的原生 WebView 渲染、Tab 切换与加载中关闭，PPT 像素证据 `flyfish-ppt-preview.png`。
- 本仓 Demo 已在 Windows 实测（换用 MIT `NativeWebView` 之后）：4 个示例（DOCX/PPTX/XLSX/旧版 PPT）真实渲染出内容、`IsLoading` 归零、`Error` 为空、`LifecycleState=Ready`；“重新加载”后正常；关闭并 `ShutdownAsync` 后预览端口全部释放，6 个引用 `NppFlyfish` 配置目录的 msedgewebview2 子进程归零，`%TEMP%\NppFlyfish` 无残留目录。macOS/Linux 仍需在对应系统执行实际渲染与退出回归。
