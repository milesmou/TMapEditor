# AGENTS.md

本文档适用于整个仓库，供自动化编码代理和维护者执行修改、验证与提交时使用。

## 项目目标

TMap Editor 是 `.tmap` v2 地图工程编辑器。修改必须优先保证：

1. 已有 `.tmap` 工程可以继续加载和保存。
2. 编辑器导出结构与 `TMAP_FORMAT.md` 保持一致。
3. MewUI 版本的功能和视觉行为不低于迁移前版本。
4. GPU 路径不可用时仍能可靠回退到 CPU 导出。

## 技术栈与边界

- 目标框架：`.NET 10`，启用 Nullable 和 Implicit Usings。
- UI：`Aprillz.MewUI`，界面在 C# 中构建，不再使用 Avalonia AXAML。
- Windows 后端：Direct2D；画布：`Aprillz.MewUI.Skia` + SkiaSharp。
- 导出 GPU 上下文由 `Services/SkiaGpuContext.cs` 管理。
- JSON 使用 `System.Text.Json` 源生成上下文 `Services/TMapJsonContext.cs`。
- Windows 发布由 `publish.cmd` 生成 `Release/TMapEditor.exe`。

不要重新引入 Avalonia 包、AXAML 文件或 Avalonia 类型。平台相关初始化集中保留在 `Program.cs`；不要把 Win32 专用逻辑散落到模型和导出服务中。

## 关键文件

- `Program.cs`：GUI/CLI 分流、平台后端注册。
- `Views/MainWindow.cs`：主窗口布局、菜单、状态和编辑流程。
- `Views/LayerNameDialog.cs`：图层名称及类型对话框。
- `Views/EditorPalette.cs`：编辑器窗口共享颜色。
- `Controls/MapCanvas.cs`：绘制、命中测试、缩放、平移、画刷、元素拖动和资源拖放。
- `Controls/MapCanvasTypes.cs`：画布工具、缩放手柄和悬停事件类型。
- `Models/TMapDocument.cs`：持久化数据模型。
- `Services/TMapFileService.cs`：加载、保存和资源路径。
- `Services/TMapExporter.cs`：导出实现。
- `Services/CommandLineExportService.cs`：命令行参数和无窗口导出流程。
- `Services/SingleInstanceGuard.cs`：GUI 单例进程互斥锁和重复启动激活通知；命令行导出不受限制。
- `Services/EditorValueConverter.cs`：编辑器字段的解析、格式化和校验。
- `Services/ResourcePathUtility.cs`：资源文件命名和目录边界判断。
- `TMAP_FORMAT.md`：格式与兼容性规范；修改数据结构时必须同步更新。

## 实现约束

### 文档兼容

- 保持 `.tmap` 为 UTF-8 JSON，当前格式版本为 `2`。
- 新增持久化字段时提供合理默认值，确保旧工程仍可读取。
- 不得无意改变中心原点坐标系、Y 轴方向、格子索引转换或 Chunk 命名。
- 图片路径继续相对于 `.tmap` 所在目录保存。
- 图层名称、对象层动态图片和路点精简规则以 `TMAP_FORMAT.md` 为准。

### UI 与交互

- 主窗口使用 MewUI 控件和代码式布局。
- 地图内容绘制应留在 `MapCanvas` 的 Skia 绘制路径中，避免为大量地图元素创建独立 UI 控件。
- 修改布局或控件样式时，对照现有界面的尺寸、间距、颜色、焦点、禁用状态和键鼠行为。
- `icon.ico` 必须保留 16–256 像素的多尺寸图层，并通过嵌入资源
  `TMapEditor.icon.ico` 设置，保证标题栏、任务栏和单文件发布均可用。
- 文本输入获得焦点时，不应让画布的删除、移动、空格平移等快捷键误触发。
- 所有会修改文档的操作必须正确维护脏状态、撤销快照、选中状态和画布刷新。
- 异步对话框、保存和导出流程要处理取消与异常，不能在失败后继续下一步。

### 导出与性能

- GPU 和 CPU 导出结果除 `GeneratedAt` 外应保持一致。
- 修改导出逻辑时至少核对输出文件列表、PNG 内容、`Grid.json` 和 `GridPath.json`。
- 保持缺失图片校验和失效产物清理逻辑，不能删除非本工具生成的文件。
- 避免在绘制热路径中反复解码图片、创建无界缓存或执行磁盘 IO。
- IDisposable 的 Skia/OpenTK 资源必须明确释放。

## 构建与验证

基础验证命令：

```powershell
dotnet build .\TMapEditor.csproj -c Debug
```

涉及发布、依赖、图标或后端配置时还要执行：

```powershell
.\publish.cmd
```

涉及导出时，使用命令行模式验证：

```powershell
.\Release\TMapEditor.exe --export ".\example.tmap" --output ".\obj\export-check"
```

涉及 UI 时，应实际启动程序验证受影响窗口或交互。至少检查：

- 程序能启动并加载工程。
- 标题、窗口图标和渲染后端文字正确。
- 修改的控件在常用窗口尺寸下没有遮挡或溢出。
- 对应鼠标和键盘操作生效。
- 不会意外修改真实测试工程；需要写操作时优先使用副本。

仓库当前没有独立测试项目，因此不能只以编译成功代替行为验证。

## 文档同步

- 用户可见功能、快捷键、启动或发布方式变化时更新 `README.md`。
- `.tmap` 字段、默认值、兼容行为或导出结构变化时更新 `TMAP_FORMAT.md`。
- 开发流程、技术边界或验证要求变化时更新本文件。
- 文档描述必须以当前代码为准，不要记录尚未实现的功能。

## Git 规则

- 使用 `git diff` 时必须加 `-w`，忽略空白和换行符差异。
- 提交前执行 `git status --short`、`git diff -w --check` 和必要的构建验证。
- 保留用户已有的无关修改，不要使用 `git reset --hard`、`git checkout --` 等方式覆盖工作区。
- “提交 git”表示：在本地仓库创建提交，并自动推送到当前所在的远程分支。
- 提交信息应简洁描述实际变化，不要混入无关文件。
- `Release/TMapEditor.exe` 当前纳入版本控制；影响运行结果或发布内容的修改，应运行 `publish.cmd` 并确认是否需要同步更新该文件。
