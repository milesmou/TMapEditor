# TMap v2 格式

`.tmap` 是 UTF-8 JSON 文件。地图使用中心原点、X 轴向右、Y 轴向上的坐标系；长度单位与导出 PNG 像素一一对应。

## 根对象

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `formatVersion` | number | 当前固定为 `2`；不兼容 v1 多边形区域格式 |
| `name` | string | 地图名称 |
| `width` / `height` | number | 地图尺寸 |
| `gridSize` | number | 行走网格边长 |
| `chunkRows` / `chunkColumns` | number | 静态图切块数量 |
| `layers` | array | 图片层和对象层 |
| `resources` | array | 工程图片资源 |
| `sprites` | array | 图片元素 |
| `cells` | array | 稀疏保存的 Walk / Block 格子状态 |
| `objects` | array | 地图对象点 |

新建工程的 `layers` 默认为空。每个图层包含 `name`、`visible` 和 `type`；`type` 为 `Image` 或 `Object`。图层名称必须唯一，图片层名称会原样用于烘焙输出目录及 `Grid.json` 中的层级字段名。旧文件没有 `type` 时按图片层处理；旧对象没有 `layer` 时会自动迁移到一个对象层。

## 工程资源

导入的图片复制到 `.tmap` 同目录的 `Resources` 文件夹，资源表保存相对路径：

```json
{
  "id": "3a650b10-0240-4134-b80a-a57c8319f549",
  "name": "bridge",
  "imagePath": "Resources/bridge.png"
}
```

场景中的多个图片元素可以共享同一个资源。缩略图路径属于编辑器运行状态，不写入 `.tmap`。

## 图片元素

```json
{
  "id": "9eb63002-26ad-4e4f-b370-ac64d10c43d8",
  "name": "bridge",
  "layer": "BgChunkLayer",
  "imagePath": "art/bridge.png",
  "x": 120.34,
  "y": -68.189,
  "width": 802,
  "height": 549,
  "rotation": -5.562,
  "scaleX": 1,
  "scaleY": 1,
  "anchorX": 0.5,
  "anchorY": 0.5,
  "order": 40
}
```

`imagePath` 相对于 `.tmap` 所在目录。`rotation` 单位为角度；负缩放表示镜像。`order` 越大越晚绘制。

## 格子与对象

只有被画刷设置过的格子才会写入 `cells`。同一行列只允许一条记录，重新绘制会直接覆盖状态：

```json
{
  "row": 3,
  "column": 8,
  "state": "Walk"
}
```

对象通过 `layer` 保存所属对象层，同时保存名称与坐标。导出时编辑器计算其 `row`、`col`、`chunkRow` 和 `chunkCol`，并保留对象层名称。

图片元素和对象元素可包含布尔字段 `isLocked`；该字段只控制编辑器中的画布选中行为，不影响烘焙结果。旧文件未包含此字段时按未锁定处理。

`state` 使用区分大小写的字符串枚举：`Walk` 表示可行走，`Block` 表示阻挡。

## 导出规则

- Chunk 原点为地图左下角，命名为 `chunk_row_col.png`。
- 只有被图片覆盖的 Chunk 才写入 PNG，并记录到 `Grid.json` 对应图片层的 `chunks` 中。
- 导出前会校验所有图片引用；文件缺失或无法解码时终止导出，不写入新的导出结果。
- 再次导出会清理本工具识别到的旧 Chunk，以及已经从工程删除的图层产物；输出目录中的其他文件不会被删除。
- `Grid.json` 的 `layers` 数组按工程顺序记录所有层级，每项包含 `name` 与 `type`；`type` 为 `Image` 或 `Object`。
- 图片层不再生成独立 manifest 文件。每个图片层的原 manifest 数据直接写入 `Grid.json` 根对象，字段名为层级名称；Chunk PNG 仍输出到 `<层级名>/sprite/`。
- 对象条目保存在 `Grid.json` 的 `objects` 中，并通过 `layer` 记录所属对象层。
- 路点数据独立写入 `GridPath.json`。`walkableCells` 与 `blockedCells` 两类都有内容时只导出数量较少的一类；数量相同时导出 `walkableCells`。只有一类有内容时导出该类。
- 导出文件只记录 `tmapFile`，不包含 Cocos Scene 引用。

`Grid.json` 的关键结构如下：

```json
{
  "layers": [
    { "name": "Ground", "type": "Image" },
    { "name": "Npc", "type": "Object" }
  ],
  "objects": [],
  "Ground": {
    "chunkWidth": 750,
    "chunkHeight": 1334,
    "rows": 3,
    "columns": 6,
    "chunks": [
      { "row": 0, "col": 0, "x": -2250, "y": -2001, "file": "chunk_0_0" }
    ]
  }
}
```

`GridPath.json` 保存网格尺寸、行列数、地图尺寸以及精简后的 `walkableCells` 或 `blockedCells`。
