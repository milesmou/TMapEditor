# TMap Editor

TMap Editor 是基于 .NET 10、[MewUI](https://github.com/Aprillz/MewUI) 和 SkiaSharp 的独立地图编辑器。工程文件使用可读的 UTF-8 JSON 格式 `.tmap`，导出结果兼容现有游戏地图加载结构。

当前 Windows 界面使用 Direct2D，地图画布通过 SkiaSharp GPU 路径绘制；GPU 不可用或输出纹理超过设备限制时，导出会自动回退到 CPU。

`.tmap` 字段、坐标系和导出结构详见 [TMAP_FORMAT.md](TMAP_FORMAT.md)。

## 环境要求

- Windows 10/11：当前主要开发和发布平台
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)：从源码运行或构建时需要
- 支持 Direct2D 的显卡驱动；没有可用 GPU 时导出仍可使用 CPU

程序入口保留了 macOS 和 X11 平台注册逻辑，但仓库内的 `publish.cmd` 只生成 Windows x64 版本。

## 快速开始

使用仓库中的自包含版本：

```powershell
.\Release\TMapEditor.exe
```

从源码启动：

```powershell
.\run.cmd
```

或：

```powershell
dotnet run --project .\TMapEditor.csproj
```

启动时会尝试打开上一次成功打开或保存的工程；路径无效时创建空白工程。GUI 使用单例模式，已有窗口运行时再次启动会恢复并激活原窗口；无窗口命令行导出不受此限制。

## 主要功能

### 工程和资源

- 新建、打开、保存和另存为 `.tmap`
- 未保存修改保护，以及最近工程和最近导出目录记忆
- 导入 PNG、JPG、WebP、BMP，并复制到工程的 `Resources` 目录
- 保存资源缩略图和相对路径，支持 50%–200% 预览缩放
- 删除未被引用的资源；仍被场景元素使用的资源会阻止删除
- 从资源视图拖放图片到图片层或对象层

### 图层和元素

- 图片层和对象层可新增、重命名、删除、排序和控制显示
- 图片元素支持选择、多选、移动、缩放、镜像、旋转、锚点和绘制顺序
- 对象层图片支持独立 `Z`，作为动态图片单独导出
- 对象点支持名称、位置、`Z`、参数、备注和编辑器显示颜色
- 图片元素和对象点可锁定；锁定后不能从画布选中
- 画布选中跨图层元素时会自动切换到对应图层

### 网格和画布

- 滚轮缩放，中键或空格加左键平移
- 网格、Chunk 边界、路点、格子 Z 和吸附网格显示开关
- Walk、Block 和清除格子画刷
- 非零格子 Z 和清除 Z 画刷
- 左键连续绘制、右键矩形框选，以及撤销
- 根据已有行进格计算可达区域并优化阻挡格
- 视图设置保存在 `.tmap` 中，重新打开时自动恢复

### 导出

- 图片层按 Chunk 烘焙为 `chunk_row_col.png`
- 图层、Chunk、对象和动态图片信息写入 `Grid.json`
- 通行格、阻挡格和非零格子 Z 写入 `GridPath.json`
- 导出前验证全部图片引用
- 自动清理本工具上次生成、当前已经失效的 Chunk 和图层产物
- GPU 优先，失败时自动回退 CPU

## 基本工作流

1. 新建或打开 `.tmap`。
2. 设置地图宽高、网格尺寸、Chunk 行列数和索引原点。
3. 新增图片层或对象层。
4. 点击右下角“导入图片...”，将素材加入工程资源库。
5. 选择目标图层，把资源缩略图拖入画布。
6. 编辑元素属性、对象点、通行格和格子 Z。
7. 保存工程，通过“文件 → 导出...”选择游戏地图输出目录。

场景图片必须从工程资源视图拖入，不能绕过 `Resources` 目录直接引用外部图片。图片层元素参与 Chunk 烘焙；对象层图片复制到 `<对象层名称>/images/`，并作为动态图片写入 `Grid.json`。

## 快捷键和画布操作

| 操作 | 快捷键或鼠标 |
| --- | --- |
| 新建 | `Ctrl+N` |
| 打开 | `Ctrl+O` |
| 保存 | `Ctrl+S` |
| 另存为 | `Ctrl+Shift+S` |
| 导出 | `Ctrl+E` |
| 撤销 | `Ctrl+Z` |
| 适应窗口 | `F` |
| 删除选中元素 | `Delete` |
| 微调选中图片位置 | `W` / `A` / `S` / `D`，每次 1 个地图单位 |
| 多选 | 元素列表中使用 `Ctrl` / `Shift`，或画布中 `Ctrl+左键` |
| 缩放画布 | 鼠标滚轮 |
| 平移画布 | 鼠标中键拖动，或 `Space+左键`拖动 |
| 连续画刷 | 按住鼠标左键拖动 |
| 矩形画刷 | 按住鼠标右键拖动，松开后提交 |
| 中断当前画刷 | `Esc` |

在工具栏输入非零整数并点击“刷 Z”后，可以绘制格子 Z；“清除 Z”只删除格子 Z，不改变 Walk / Block 状态。

## 导出结果

典型输出目录如下：

```text
output/
├─ Grid.json
├─ GridPath.json
├─ BgChunkLayer/
│  ├─ chunk_0_0.png
│  └─ chunk_0_1.png
└─ ObjectLayer/
   └─ images/
      └─ tree.png
```

- 图片层名称会原样用于输出目录和 `Grid.json.ImageLayers` 字段。
- 对象层数据写入 `Grid.json.ObjectLayers`。
- 对象备注和显示颜色仅供编辑器使用，不会导出。
- 再次导出只清理本工具能够识别的旧产物，不会删除输出目录中的其他文件。

## 命令行导出

无需打开编辑器窗口即可导出 `.tmap`：

```powershell
.\Release\TMapEditor.exe --export "D:\Maps\map1.tmap" --output "D:\Game\assets\bundles\maps\map1\map1"
```

也可以执行构建目录中的 DLL：

```powershell
dotnet .\bin\Debug\net10.0\TMapEditor.dll --export ".\example.tmap" --output ".\Export"
```

退出码：

| 退出码 | 含义 |
| ---: | --- |
| `0` | 导出成功 |
| `1` | 工程读取、资源验证或导出失败 |
| `2` | 命令行参数错误 |

完成信息会显示 Chunk、格子、对象和动态图片数量，以及实际使用的 `GPU` 或 `CPU` 渲染路径。

## 构建和发布

调试构建：

```powershell
dotnet build .\TMapEditor.csproj -c Debug
```

生成 Windows x64 自包含单文件版本：

```powershell
.\publish.cmd
```

发布结果写入 `Release/TMapEditor.exe`。发布脚本启用单文件压缩、部分裁剪和 Direct2D 后端，不生成 PDB。

## 工程结构

| 路径 | 职责 |
| --- | --- |
| `Program.cs` | 平台后端注册、应用启动和命令行导出入口 |
| `Views/MainWindow.cs` | MewUI 主窗口、菜单、面板和编辑流程 |
| `Views/LayerNameDialog.cs` | 图层新增和重命名对话框 |
| `Views/EditorPalette.cs` | 编辑器窗口共享配色 |
| `Controls/MapCanvas.cs` | Skia 画布绘制、命中测试、拖放和指针编辑 |
| `Controls/MapCanvasTypes.cs` | 画布工具和交互事件类型 |
| `Models/TMapDocument.cs` | `.tmap` 文档、图层、资源、元素、格子和对象模型 |
| `Services/TMapFileService.cs` | 工程加载、保存和资源路径处理 |
| `Services/TMapExporter.cs` | Chunk、JSON 和动态图片导出 |
| `Services/CommandLineExportService.cs` | 无窗口命令行导出流程 |
| `Services/SingleInstanceGuard.cs` | GUI 单例进程互斥锁和重复启动激活通知 |
| `Services/EditorValueConverter.cs` | 编辑字段解析、格式化与校验 |
| `Services/ResourcePathUtility.cs` | 资源文件命名与目录边界判断 |
| `Services/BlockedRegionOptimizer.cs` | 可达区域与阻挡格优化 |
| `Services/EditorSettingsService.cs` | 最近工程、导出目录和资源预览设置 |
| `TMAP_FORMAT.md` | `.tmap` v2 和导出格式规范 |

## 工程数据注意事项

- `.tmap` 和图片素材应放在同一个地图工程目录中，便于整体移动。
- 保存时图片路径相对于 `.tmap` 所在目录写入。
- 导入资源会复制原图，后续移动外部原文件不会影响工程。
- 图层名称必须唯一，并会参与输出目录和 JSON 字段命名；重命名前应确认游戏侧引用。
- 命令行导出不会打开窗口，也不会修改源 `.tmap`。
