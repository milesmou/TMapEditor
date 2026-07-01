# TMap v2 格式

`.tmap` 是 UTF-8 JSON 文件。地图使用中心原点、X 轴向右、Y 轴向上的坐标系；长度单位与导出 PNG 像素一一对应。

## 根对象

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `formatVersion` | number | 当前固定为 `2`；不兼容 v1 多边形区域格式 |
| `name` | string | 地图名称；打开或保存时会同步为 `.tmap` 文件名（不含扩展名） |
| `width` / `height` | number | 地图尺寸 |
| `gridSize` | number | 行走网格边长 |
| `chunkRows` / `chunkColumns` | number | 静态图切块数量 |
| `viewSettings` | object | 工程视图设置 |
| `layers` | array | 图片层和对象层 |
| `resources` | array | 工程图片资源 |
| `sprites` | array | 图片元素 |
| `cells` | array | 稀疏保存的 Walk / Block 格子状态 |
| `cellZs` | array | 稀疏保存的非零格子 Z |
| `objects` | array | 地图对象点 |

新建工程的 `layers` 默认为空。每个图层包含 `name`、`visible` 和 `type`；`type` 为 `Image` 或 `Object`。图层名称必须唯一，图片层名称会原样用于烘焙输出目录及 `Grid.json` 中的层级字段名。旧文件没有 `type` 时按图片层处理；旧对象没有 `layer` 时会自动迁移到一个对象层。

`viewSettings` 保存编辑器中的 `showGrid`、`showChunks`、`showWaypoints`、`showCellZs` 和 `snapToGrid`，重新打开工程时恢复这些选项；它们不影响烘焙数据。

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
  "imagePath": "Resources/bridge.png",
  "x": 120.34,
  "y": -68.189,
  "width": 802,
  "height": 549,
  "rotation": -5.562,
  "scaleX": 1,
  "scaleY": 1,
  "anchorX": 0.5,
  "anchorY": 0.5,
  "order": 40,
  "z": 0
}
```

`imagePath` 相对于 `.tmap` 所在目录。`rotation` 单位为角度；负缩放表示镜像。`order` 越大越晚绘制。图片元素可以归属图片层或对象层：图片层元素参与 Chunk 烘焙，对象层元素作为动态图片单独导出。对象层图片使用 `z` 控制绘制先后，数值越大越靠前；图片层忽略该字段。

## 格子与对象

只有被画刷设置过的格子才会写入 `cells`。同一行列只允许一条记录，重新绘制会直接覆盖状态：

```json
{
  "row": 3,
  "column": 8,
  "state": "Walk"
}
```

格子 Z 与通行状态相互独立，只有非零值才会写入 `cellZs`。同一行列只允许一条记录；未记录的格子 Z 为 `0`：

```json
{
  "row": 3,
  "column": 8,
  "z": 10
}
```

对象通过 `layer` 保存所属对象层，同时保存名称、坐标、`z`、字符串参数 `args`、备注 `note` 与编辑器显示颜色 `displayColor`（`#RRGGBB` 或 `#AARRGGBB`）。导出时编辑器计算其 `row`、`col`、`chunkRow` 和 `chunkCol`，并写入对应对象层的 `Objects`；非空 `args` 去除首尾空白后导出为 `Args`，空参数不写入。备注和显示颜色仅供编辑器使用，不影响导出数据。对象点和对象层图片都按 `z` 从小到大导出。对象层图片写入同层的 `Images`，每项包含元素名称、导出图片文件名、地图坐标和 `Z`。

图片元素和对象元素可包含布尔字段 `isLocked`；该字段只控制编辑器中的画布选中行为，不影响烘焙结果。旧文件未包含此字段时按未锁定处理。

`state` 使用区分大小写的字符串枚举：`Walk` 表示可行走，`Block` 表示阻挡。

## 导出规则

- Chunk 原点为地图左下角，命名为 `chunk_row_col.png`。
- 只有被图片覆盖的 Chunk 才写入 PNG，并记录到 `Grid.json` 的 `ImageLayers` 对应图片层数组中。
- 导出前会校验所有图片引用；文件缺失或无法解码时终止导出，不写入新的导出结果。
- 再次导出会清理本工具识别到的旧 Chunk，以及已经从工程删除的图层产物；输出目录中的其他文件不会被删除。
- 所有导出 JSON 属性统一使用 Pascal Case 命名。
- `Grid.json` 的 `Layers` 数组按工程顺序记录所有层级，每项包含 `Name` 与 `Type`；`Type` 为 `Image` 或 `Object`。
- 地图宽高分别保存为 `MapWidth` 与 `MapHeight`。
- 图片层不再生成独立 manifest 文件。所有图片层集中在 `Grid.json` 的 `ImageLayers` 对象中，层级名称为字段名、字段值为 Chunk 数组；Chunk PNG 直接输出到 `<层级名>/`。
- 所有对象层集中在 `Grid.json` 的 `ObjectLayers` 对象中。每个层级包含 `Objects` 和 `Images` 两个数组，即使没有对应元素也会保留空数组。
- 对象层中的图片不会参与 Chunk 烘焙；其原图复制到 `<对象层名称>/images/` 并保留扩展名，`Images[].File` 保存不带扩展名的文件名。同一源图片在同一对象层只复制一次，重名文件会自动追加编号。
- 路点数据独立写入 `GridPath.json`。`WalkableCells` 与 `BlockedCells` 两类都有内容时只导出数量较少的一类；数量相同时导出 `WalkableCells`。只有一类有内容时导出该类。非零格子 Z 独立写入 `ZCells`，每项格式为 `[Row, Col, Z]`，不受通行状态精简规则影响。
- `Grid.json` 和 `GridPath.json` 都记录生成时间 `GeneratedAt`、源文件名 `TmapFile` 与导出类型 `ExportType`，不包含 Cocos Scene 引用。

`Grid.json` 的关键结构如下：

```json
{
  "GeneratedAt": "2026-07-01T02:30:00.0000000Z",
  "TmapFile": "map1.tmap",
  "ExportType": "grid",
  "GridSize": 32,
  "Rows": 126,
  "Columns": 141,
  "ChunkRows": 3,
  "ChunkColumns": 6,
  "OriginMode": "sourceLayerLeftBottom",
  "MapWidth": 4500,
  "MapHeight": 4002,
  "Layers": [
    { "Name": "Ground", "Type": "Image" },
    { "Name": "Npc", "Type": "Object" }
  ],
  "ImageLayers": {
    "Ground": [
      { "Row": 0, "Col": 0, "X": -2250, "Y": -2001, "File": "chunk_0_0" }
    ]
  },
  "ObjectLayers": {
    "Npc": {
      "Objects": [
        { "Name": "Npc_1", "Row": 4, "Col": 8, "ChunkRow": 0, "ChunkCol": 0, "Z": 5, "Args": "vendor" }
      ],
      "Images": [
        { "Name": "Tree_1", "File": "tree", "X": 1200, "Y": 860, "Z": 10 }
      ]
    }
  }
}
```

`GridPath.json` 保存网格尺寸、行列数、地图尺寸、精简后的 `WalkableCells` 或 `BlockedCells`，以及非零 `ZCells`：

```json
{
  "GeneratedAt": "2026-07-01T02:30:00.0000000Z",
  "TmapFile": "map1.tmap",
  "ExportType": "gridPath",
  "GridSize": 32,
  "Rows": 126,
  "Columns": 141,
  "OriginMode": "sourceLayerLeftBottom",
  "MapWidth": 4500,
  "MapHeight": 4002,
  "WalkableCells": [[3, 8]],
  "ZCells": [[3, 8, 10], [3, 9, 10]]
}
```
