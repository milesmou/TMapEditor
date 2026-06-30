using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using TMapEditor.Controls;
using TMapEditor.Models;
using TMapEditor.Services;

namespace TMapEditor;

public partial class MainWindow : Window
{
    private readonly EditorSettings _settings;
    private TMapDocument _document = new();
    private bool _dirty;
    private bool _synchronizingSelection;
    private bool _updatingSelectionProperties;
    private Point _resourceDragStart;
    private PointerPressedEventArgs? _resourceDragPress;
    private TMapResource? _draggedResource;
    private bool _restoringUndo;
    private bool _closingConfirmed;
    private string? _undoSnapshot;
    private string _currentSnapshot = "";

    public MainWindow()
    {
        _settings = EditorSettingsService.Load();
        InitializeComponent();
        ResourceList.AddHandler(
            PointerPressedEvent,
            ResourceList_PointerPressed,
            handledEventsToo: true);
        ResourceList.AddHandler(
            PointerMovedEvent,
            ResourceList_PointerMoved,
            handledEventsToo: true);
        ResourcePreviewScaleSlider.Value = Math.Clamp(_settings.ResourcePreviewScale, 50, 200);
        EditorCanvas.SelectedItemChanged += EditorCanvas_SelectedItemChanged;
        EditorCanvas.DocumentChanging += EditorCanvas_DocumentChanging;
        EditorCanvas.DocumentChanged += EditorCanvas_DocumentChanged;
        EditorCanvas.HoveredCellChanged += EditorCanvas_HoveredCellChanged;
        OpenLastProjectOrCreateDocument();
        Loaded += (_, _) => EditorCanvas.FitToView();
    }

    private void OpenLastProjectOrCreateDocument()
    {
        var lastProjectPath = _settings.LastProjectPath;
        if (!string.IsNullOrWhiteSpace(lastProjectPath) && File.Exists(lastProjectPath))
        {
            try
            {
                SetDocument(TMapFileService.Load(lastProjectPath));
                StatusText.Text = "已自动打开上次工程";
                return;
            }
            catch
            {
                _settings.LastProjectPath = null;
                EditorSettingsService.Save(_settings);
            }
        }

        SetDocument(new TMapDocument());
    }

    private void SetDocument(TMapDocument document)
    {
        _document = document;
        EditorCanvas.Document = document;
        RefreshLayerControls(document.Layers.FirstOrDefault());
        RefreshResourceList();
        MapWidthText.Text = Format(document.Width);
        MapHeightText.Text = Format(document.Height);
        GridSizeText.Text = Format(document.GridSize);
        ChunkRowsText.Text = document.ChunkRows.ToString(CultureInfo.InvariantCulture);
        ChunkColumnsText.Text = document.ChunkColumns.ToString(CultureInfo.InvariantCulture);
        RefreshEntityList();
        UpdateSelectionProperties(null);
        ResetUndoState();
        SetDirty(false);
        FileText.Text = document.FilePath ?? "未保存";
    }

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardOrSave()) return;
        SetDocument(new TMapDocument());
        EditorCanvas.FitToView();
        StatusText.Text = "已新建地图";
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardOrSave()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开 TMap",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("TMap 地图") { Patterns = ["*.tmap"] },
                new FilePickerFileType("JSON 文件") { Patterns = ["*.json"] },
                FilePickerFileTypes.All
            ]
        });
        var filePath = files.FirstOrDefault()?.TryGetLocalPath();
        if (filePath is null) return;
        try
        {
            SetDocument(TMapFileService.Load(filePath));
            RememberCurrentProject();
            EditorCanvas.FitToView();
            StatusText.Text = "地图已打开";
        }
        catch (Exception exception)
        {
            await ShowError("打开失败", exception);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await SaveDocument(false);
    }

    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        await SaveDocument(true);
    }

    private async Task<bool> SaveDocument(bool saveAs)
    {
        var filePath = _document.FilePath;
        if (saveAs || filePath is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存 TMap",
                SuggestedFileName = GetDefaultMapFileName(),
                DefaultExtension = ".tmap",
                FileTypeChoices = [new FilePickerFileType("TMap 地图") { Patterns = ["*.tmap"] }]
            });
            filePath = file?.TryGetLocalPath();
            if (filePath is null) return false;
        }

        try
        {
            SaveDocumentToPath(filePath);
            StatusText.Text = "地图已保存";
            return true;
        }
        catch (Exception exception)
        {
            await ShowError("保存失败", exception);
            return false;
        }
    }

    private async void ImportResources_Click(object sender, RoutedEventArgs e)
    {
        if (_document.FilePath is null && !await SaveDocument(false)) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入图片资源到 TMap 工程",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("图片文件") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"] },
                FilePickerFileTypes.All
            ]
        });
        var filePaths = files.Select(file => file.TryGetLocalPath()).Where(path => path is not null).Cast<string>().ToList();
        if (filePaths.Count == 0) return;

        try
        {
            var resourceDirectory = Path.Combine(_document.BaseDirectory, "Resources");
            Directory.CreateDirectory(resourceDirectory);
            CaptureUndoSnapshot();
            foreach (var sourcePath in filePaths)
            {
                var destinationPath = GetUniqueResourcePath(resourceDirectory, Path.GetFileName(sourcePath));
                File.Copy(sourcePath, destinationPath);
                _document.Resources.Add(new TMapResource
                {
                    Name = Path.GetFileNameWithoutExtension(destinationPath),
                    ImagePath = TMapFileService.MakePortableImagePath(_document, destinationPath),
                    ThumbnailPath = destinationPath
                });
            }
            RefreshResourceList();
            SetDirty(true);
            await SaveDocument(false);
            StatusText.Text = $"已导入 {filePaths.Count} 个工程资源";
        }
        catch (Exception exception)
        {
            await ShowError("资源导入失败", exception);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!await ApplyDocumentSettings(showErrors: false)) return;
        if (!await SaveDocument(false)) return;
        var suggestedFolder = await GetSuggestedExportFolder();
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择地图烘焙输出目录",
            AllowMultiple = false,
            SuggestedStartLocation = suggestedFolder
        });
        var folderPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (folderPath is null) return;
        _settings.LastExportDirectory = Path.GetFullPath(folderPath);
        EditorSettingsService.Save(_settings);
        try
        {
            Cursor = new Cursor(StandardCursorType.Wait);
            StatusText.Text = "正在烘焙导出...";
            var exportDocument = CloneDocumentForExport();
            using var gpuContext = SkiaGpuContext.TryCreate();
            var result = await Task.Run(() =>
                TMapExporter.Export(exportDocument, folderPath, gpuContext, false));
            var renderer = result.HardwareAccelerated ? "GPU" : "CPU 回退";
            StatusText.Text = $"导出完成：{result.ChunkCount} chunks，{result.WalkableCount} 可行走格，{result.BlockedCount} 阻挡格，{renderer}";
            await ShowMessage("TMap Editor",
                $"地图导出完成。\n\nChunk：{result.ChunkCount}\n可行走格：{result.WalkableCount}\n阻挡格：{result.BlockedCount}\n对象：{result.ObjectCount}\n渲染：{renderer}",
                ["确定"]);
        }
        catch (Exception exception)
        {
            await ShowError("导出失败", exception);
        }
        finally
        {
            Cursor = null;
        }
    }

    private async Task<IStorageFolder?> GetSuggestedExportFolder()
    {
        var directory = _settings.LastExportDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return null;
        try
        {
            return await StorageProvider.TryGetFolderFromPathAsync(directory);
        }
        catch
        {
            return null;
        }
    }

    private TMapDocument CloneDocumentForExport()
    {
        var json = JsonSerializer.Serialize(_document, TMapJsonContext.Default.TMapDocument);
        var clone = JsonSerializer.Deserialize(json, TMapJsonContext.Default.TMapDocument) ?? new TMapDocument();
        clone.FilePath = _document.FilePath;
        return clone;
    }

    private async void ApplyDocument_Click(object sender, RoutedEventArgs e)
    {
        await ApplyDocumentSettings(showErrors: true);
    }

    private async Task<bool> ApplyDocumentSettings(bool showErrors)
    {
        if (!TryDouble(MapWidthText.Text, out var width) || width <= 0 ||
            !TryDouble(MapHeightText.Text, out var height) || height <= 0 ||
            !TryDouble(GridSizeText.Text, out var gridSize) || gridSize <= 0 ||
            !int.TryParse(ChunkRowsText.Text, out var chunkRows) || chunkRows <= 0 ||
            !int.TryParse(ChunkColumnsText.Text, out var chunkColumns) || chunkColumns <= 0)
        {
            if (showErrors) await ShowMessage("输入错误", "地图尺寸、网格尺寸和 Chunk 行列必须为正数。", ["确定"]);
            return false;
        }
        CaptureUndoSnapshot();
        _document.Width = width;
        _document.Height = height;
        _document.GridSize = gridSize;
        _document.ChunkRows = chunkRows;
        _document.ChunkColumns = chunkColumns;
        SetDirty(true);
        EditorCanvas.InvalidateVisual();
        StatusText.Text = "地图设置已应用";
        return true;
    }

    private void ApplySelectionProperties()
    {
        try
        {
            switch (EditorCanvas.SelectedItem)
            {
                case TMapSprite sprite:
                    CaptureUndoSnapshot();
                    sprite.Name = RequiredName(ItemNameText.Text);
                    var selectedLayer = SpriteLayerCombo.SelectedItem as TMapLayer;
                    sprite.Layer = selectedLayer?.Name ?? sprite.Layer;
                    if (selectedLayer is not null && !ReferenceEquals(LayerList.SelectedItem, selectedLayer))
                        LayerList.SelectedItem = selectedLayer;
                    sprite.X = ParseDouble(SpriteXText.Text, "X");
                    sprite.Y = ParseDouble(SpriteYText.Text, "Y");
                    sprite.Width = PositiveDouble(SpriteWidthText.Text, "宽度");
                    sprite.Height = PositiveDouble(SpriteHeightText.Text, "高度");
                    sprite.Rotation = ParseDouble(SpriteRotationText.Text, "旋转");
                    sprite.ScaleX = ParseDouble(SpriteScaleXText.Text, "Scale X");
                    sprite.ScaleY = ParseDouble(SpriteScaleYText.Text, "Scale Y");
                    sprite.AnchorX = ParseDouble(SpriteAnchorXText.Text, "Anchor X");
                    sprite.AnchorY = ParseDouble(SpriteAnchorYText.Text, "Anchor Y");
                    sprite.Order = int.Parse(SpriteOrderText.Text, CultureInfo.InvariantCulture);
                    break;
                case TMapObject mapObject:
                    CaptureUndoSnapshot();
                    mapObject.Name = RequiredName(ItemNameText.Text);
                    var selectedObjectLayer = ObjectLayerCombo.SelectedItem as TMapLayer;
                    mapObject.Layer = selectedObjectLayer?.Name ?? mapObject.Layer;
                    if (selectedObjectLayer is not null && !ReferenceEquals(LayerList.SelectedItem, selectedObjectLayer))
                        LayerList.SelectedItem = selectedObjectLayer;
                    mapObject.Args = ObjectArgsText.Text?.Trim() ?? "";
                    mapObject.X = ParseDouble(ObjectXText.Text, "X");
                    mapObject.Y = ParseDouble(ObjectYText.Text, "Y");
                    break;
                default:
                    return;
            }
            SetDirty(true);
            RefreshEntityList();
            EditorCanvas.InvalidateVisual();
            StatusText.Text = "属性已自动应用";
        }
        catch (Exception exception)
        {
            _ = ShowMessage("输入错误", exception.Message, ["确定"]);
        }
    }

    private void SelectionProperty_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_updatingSelectionProperties || e.Source is not TextBox) return;
        ApplySelectionProperties();
    }

    private void SelectionProperty_KeyDown(object sender, KeyEventArgs e)
    {
        if (_updatingSelectionProperties || e.Key != Key.Enter || e.Source is not TextBox) return;
        ApplySelectionProperties();
        e.Handled = true;
    }

    private void SelectionProperty_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelectionProperties || !IsInitialized) return;
        ApplySelectionProperties();
    }

    private async void ResetSpriteProperties_Click(object sender, RoutedEventArgs e)
    {
        if (EditorCanvas.SelectedItem is not TMapSprite sprite) return;
        try
        {
            var bitmap = LoadSpriteBitmap(sprite);
            CaptureUndoSnapshot();
            sprite.Width = bitmap.PixelSize.Width;
            sprite.Height = bitmap.PixelSize.Height;
            sprite.Rotation = 0;
            sprite.ScaleX = 1;
            sprite.ScaleY = 1;
            sprite.AnchorX = 0.5;
            sprite.AnchorY = 0.5;
            UpdateSelectionProperties(sprite);
            SetDirty(true);
            EditorCanvas.InvalidateVisual();
            StatusText.Text = "图片元素属性已重置";
        }
        catch (Exception exception)
        {
            await ShowError("重置属性失败", exception);
        }
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        var tag = sender switch
        {
            Button { Tag: string buttonTag } => buttonTag,
            MenuItem { Tag: string menuItemTag } => menuItemTag,
            _ => null
        };
        if (tag is null || !Enum.TryParse<EditorTool>(tag, out var tool)) return;
        EditorCanvas.Tool = tool;
        ToolHintText.Text = tool switch
        {
            EditorTool.WalkBrush => "行进区域画刷：按住左键连续刷，按住右键框选刷格子，Esc 中断",
            EditorTool.BlockBrush => "阻挡区域画刷：按住左键连续刷，按住右键框选刷格子，Esc 中断",
            EditorTool.EraseBrush => "清除格子画刷：按住左键连续清除，按住右键框选清除，Esc 中断",
            _ => "选择模式：左键移动，滚轮缩放，中键/空格拖动画布"
        };
    }

    private async void OptimizeBlockedRegions_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var snapshot = CreateDocumentSnapshot(_document);
            var result = BlockedRegionOptimizer.Optimize(_document);
            if (!result.HasWalkableSeed)
            {
                await ShowMessage("优化阻挡区域", "请先标记至少一个行进格，用于确定可到达区域。", ["确定"]);
                return;
            }
            if (result.AddedBlockedCells == 0 && result.AddedWalkableCells == 0)
            {
                StatusText.Text = "地图区域已全部标记";
                return;
            }

            _undoSnapshot = snapshot;
            RefreshCurrentSnapshot();
            UpdateUndoMenu();
            SetDirty(true);
            EditorCanvas.InvalidateVisual();
            StatusText.Text = $"阻挡区域优化完成：新增 {result.AddedBlockedCells} 个阻挡格，" +
                              $"{result.AddedWalkableCells} 个行进格";
        }
        catch (Exception exception)
        {
            await ShowError("优化阻挡区域失败", exception);
        }
    }

    private void ViewOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        EditorCanvas.ShowGrid = ShowGridCheck.IsChecked == true;
        EditorCanvas.ShowChunks = ShowChunksCheck.IsChecked == true;
        EditorCanvas.ShowCells = ShowWaypointsCheck.IsChecked == true;
        EditorCanvas.SnapToGrid = SnapCheck.IsChecked == true;
        EditorCanvas.InvalidateVisual();
    }

    private void LayerVisibility_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        CaptureUndoSnapshot();
        EditorCanvas.InvalidateVisual();
        SetDirty(true);
    }

    private void LayerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LayerList.SelectedItem is TMapLayer layer)
        {
            EditorCanvas.DropTargetLayer = layer.Name;
            if (layer.Type == TMapLayerType.Object)
            {
                EditorCanvas.Tool = EditorTool.Select;
                ToolHintText.Text = "对象层：单击空白处添加对象点，拖动已有对象点可移动";
            }
            else if (EditorCanvas.Tool == EditorTool.Select)
            {
                ToolHintText.Text = "选择模式：左键移动，滚轮缩放，中键/空格拖动画布";
            }
        }
        else
        {
            EditorCanvas.DropTargetLayer = "";
        }
        if (IsInitialized)
        {
            var layerName = (LayerList.SelectedItem as TMapLayer)?.Name;
            EditorCanvas.SetSelectedItems(EditorCanvas.SelectedItems.Where(item =>
                item switch
                {
                    TMapSprite sprite => sprite.Layer == layerName,
                    TMapObject mapObject => mapObject.Layer == layerName,
                    _ => false
                }));
            RefreshEntityList();
        }
    }

    private void EntityList_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(EntityList).Properties.IsRightButtonPressed) return;
        var item = (e.Source as Control)?.FindAncestorOfType<ListBoxItem>();
        if (item is null)
        {
            e.Handled = true;
            return;
        }
        if (!item.IsSelected)
        {
            EntityList.SelectedItems.Clear();
            item.IsSelected = true;
        }
    }

    private void EntityLock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { DataContext: ILockableDisplayItem item }) return;
        CaptureUndoSnapshot();
        item.IsLocked = !item.IsLocked;
        if (item.IsLocked)
        {
            EditorCanvas.SetSelectedItems(EditorCanvas.SelectedItems.Where(selected =>
                !ReferenceEquals(selected, item)));
        }
        SetDirty(true);
        RefreshEntityList();
        StatusText.Text = item.IsLocked ? $"已锁定：{item.DisplayName}" : $"已解锁：{item.DisplayName}";
        e.Handled = true;
    }

    private async void AddLayer_Click(object sender, RoutedEventArgs e)
    {
        var result = await PromptForLayer("新增层级", GetUniqueLayerName("Layer"), true);
        if (result is null) return;
        var (name, layerType) = result.Value;
        var typeName = layerType == TMapLayerType.Object ? "对象层" : "图片层";
        CaptureUndoSnapshot();
        var layer = new TMapLayer { Name = name, Type = layerType };
        _document.Layers.Add(layer);
        RefreshLayerControls(layer);
        SetDirty(true);
        EditorCanvas.InvalidateVisual();
        StatusText.Text = $"已新增{typeName}：{name}";
    }

    private async void RenameLayer_Click(object sender, RoutedEventArgs e)
    {
        if (LayerList.SelectedItem is not TMapLayer layer)
        {
            await ShowMessage("重命名图层", "请先选择要重命名的图层。", ["确定"]);
            return;
        }

        var oldName = layer.Name;
        var result = await PromptForLayer("重命名层级", oldName, false, layer);
        if (result is null || result.Value.Name == oldName) return;
        var name = result.Value.Name;
        CaptureUndoSnapshot();
        foreach (var sprite in _document.Sprites.Where(sprite => sprite.Layer == oldName)) sprite.Layer = name;
        foreach (var mapObject in _document.Objects.Where(mapObject => mapObject.Layer == oldName)) mapObject.Layer = name;
        layer.Name = name;
        RefreshLayerControls(layer);
        RefreshEntityList();
        UpdateSelectionProperties(EditorCanvas.SelectedItem);
        SetDirty(true);
        EditorCanvas.InvalidateVisual();
        StatusText.Text = $"图层已重命名：{oldName} → {name}";
    }

    private async void DeleteLayer_Click(object sender, RoutedEventArgs e)
    {
        if (LayerList.SelectedItem is not TMapLayer layer)
        {
            await ShowMessage("删除图层", "请先选择要删除的图层。", ["确定"]);
            return;
        }

        var sprites = _document.Sprites.Where(sprite => sprite.Layer == layer.Name).ToList();
        var objects = _document.Objects.Where(mapObject => mapObject.Layer == layer.Name).ToList();
        var elementCount = sprites.Count + objects.Count;
        var message = elementCount == 0
            ? $"确定删除图层“{layer.Name}”吗？"
            : $"图层“{layer.Name}”中有 {elementCount} 个元素。\n删除图层会同时删除这些元素，是否继续？";
        if (await ShowMessage("删除图层", message, ["是", "否"]) != "是") return;

        CaptureUndoSnapshot();
        var oldIndex = _document.Layers.IndexOf(layer);
        EditorCanvas.SetSelectedItems(EditorCanvas.SelectedItems.Except(sprites).Except(objects));
        foreach (var sprite in sprites) _document.Sprites.Remove(sprite);
        foreach (var mapObject in objects) _document.Objects.Remove(mapObject);
        _document.Layers.Remove(layer);
        var nextLayer = _document.Layers.Count == 0
            ? null
            : _document.Layers[Math.Min(oldIndex, _document.Layers.Count - 1)];
        RefreshLayerControls(nextLayer);
        RefreshEntityList();
        SetDirty(true);
        EditorCanvas.InvalidateVisual();
        StatusText.Text = $"已删除图层：{layer.Name}";
    }

    private async Task<(string Name, TMapLayerType Type)?> PromptForLayer(
        string title,
        string initialName,
        bool allowTypeSelection,
        TMapLayer? excludedLayer = null)
    {
        var candidate = initialName;
        while (true)
        {
            var dialog = new LayerNameDialog(title, candidate, allowTypeSelection, excludedLayer?.Type ?? TMapLayerType.Image);
            if (await dialog.ShowDialog<bool>(this) != true) return null;
            candidate = dialog.LayerName;
            try
            {
                var name = LayerNameValidator.Validate(candidate);
                if (_document.Layers.Any(layer => !ReferenceEquals(layer, excludedLayer) &&
                                                  string.Equals(layer.Name, name, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException($"图层“{name}”已经存在。");
                return (name, dialog.LayerType);
            }
            catch (Exception exception)
            {
                await ShowMessage(title, exception.Message, ["确定"]);
            }
        }
    }

    private string GetUniqueLayerName(string baseName)
    {
        if (!_document.Layers.Any(layer => string.Equals(layer.Name, baseName, StringComparison.OrdinalIgnoreCase)))
            return baseName;
        for (var index = 2; ; index++)
        {
            var candidate = $"{baseName}{index}";
            if (!_document.Layers.Any(layer => string.Equals(layer.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    private void RefreshLayerControls(TMapLayer? selection)
    {
        LayerList.ItemsSource = null;
        LayerList.ItemsSource = _document.Layers;
        _updatingSelectionProperties = true;
        try
        {
            SpriteLayerCombo.ItemsSource = null;
            SpriteLayerCombo.ItemsSource = _document.Layers.Where(layer => layer.Type == TMapLayerType.Image).ToList();
            SpriteLayerCombo.SelectedItem = EditorCanvas.SelectedItem is TMapSprite sprite
                ? _document.Layers.FirstOrDefault(layer => layer.Name == sprite.Layer)
                : null;
            ObjectLayerCombo.ItemsSource = null;
            ObjectLayerCombo.ItemsSource = _document.Layers.Where(layer => layer.Type == TMapLayerType.Object).ToList();
            ObjectLayerCombo.SelectedItem = EditorCanvas.SelectedItem is TMapObject mapObject
                ? _document.Layers.FirstOrDefault(layer => layer.Name == mapObject.Layer)
                : null;
        }
        finally
        {
            _updatingSelectionProperties = false;
        }
        LayerList.SelectedItem = selection;
        EditorCanvas.DropTargetLayer = selection?.Name ?? "";
    }

    private void ResourceList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _resourceDragStart = e.GetPosition(ResourceList);
        _resourceDragPress = null;
        _draggedResource = null;
        if (!e.GetCurrentPoint(ResourceList).Properties.IsLeftButtonPressed) return;
        if ((e.Source as Control)?.FindAncestorOfType<ListBoxItem>() is { DataContext: TMapResource resource })
        {
            _resourceDragPress = e;
            _draggedResource = resource;
            ResourceList.SelectedItem = resource;
        }
    }

    private async void ResourceList_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_resourceDragPress is null ||
            !e.GetCurrentPoint(ResourceList).Properties.IsLeftButtonPressed ||
            (_draggedResource ?? ResourceList.SelectedItem) is not TMapResource resource)
            return;
        var point = e.GetPosition(ResourceList);
        if (Math.Abs(point.X - _resourceDragStart.X) < 4 &&
            Math.Abs(point.Y - _resourceDragStart.Y) < 4)
            return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(TMapDragFormats.Resource, resource));
        var dragPress = _resourceDragPress;
        _resourceDragPress = null;
        _draggedResource = null;
        try
        {
            await DragDrop.DoDragDropAsync(dragPress, data, DragDropEffects.Copy);
        }
        catch (Exception exception)
        {
            await ShowError("拖放资源失败", exception);
        }
    }

    private void EntityList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelection) return;
        EditorCanvas.SetSelectedItems(EntityList.SelectedItems.Cast<object>());
    }

    private void EditorCanvas_SelectedItemChanged(object? sender, object? item)
    {
        var layerName = item switch
        {
            TMapSprite sprite => sprite.Layer,
            TMapObject mapObject => mapObject.Layer,
            _ => null
        };
        var itemLayer = _document.Layers.FirstOrDefault(layer => layer.Name == layerName);
        if (itemLayer is not null && !ReferenceEquals(LayerList.SelectedItem, itemLayer))
            LayerList.SelectedItem = itemLayer;
        _synchronizingSelection = true;
        EntityList.SelectedItems.Clear();
        foreach (var selectedItem in EditorCanvas.SelectedItems)
        {
            if (EntityList.Items.Contains(selectedItem)) EntityList.SelectedItems.Add(selectedItem);
        }
        if (item is not null) EntityList.ScrollIntoView(item);
        _synchronizingSelection = false;
        UpdateSelectionProperties(EditorCanvas.SelectedItems.Count == 1 ? item : null);
        if (EditorCanvas.SelectedItems.Count > 1)
            SelectionTypeText.Text = $"已选择 {EditorCanvas.SelectedItems.Count} 个元素";
    }

    private void EditorCanvas_DocumentChanged(object? sender, EventArgs e)
    {
        SetDirty(true);
        var entityCount = GetCurrentEntityItems().Count;
        if (EntityList.Items.Count != entityCount ||
            EditorCanvas.SelectedItems.Any(item => !EntityList.Items.Contains(item)))
        {
            RefreshEntityList();
        }
        var item = EditorCanvas.SelectedItems.Count == 1 ? EditorCanvas.SelectedItem : null;
        UpdateSelectionProperties(item);
        if (EditorCanvas.SelectedItems.Count > 1)
            SelectionTypeText.Text = $"已选择 {EditorCanvas.SelectedItems.Count} 个元素";
    }

    private void EditorCanvas_DocumentChanging(object? sender, EventArgs e)
    {
        CaptureUndoSnapshot();
    }

    private void EditorCanvas_HoveredCellChanged(object? sender, MapCellHoverEventArgs e)
    {
        StatusText.Text = e.IsInsideMap
            ? $"格子索引：[{e.Row},{e.Column}]"
            : "就绪";
    }

    private void UpdateSelectionProperties(object? item)
    {
        _updatingSelectionProperties = true;
        try
        {
            CommonPropertyPanel.IsVisible = item is not null;
            SpritePropertyPanel.IsVisible = item is TMapSprite;
            ObjectPropertyPanel.IsVisible = item is TMapObject;

            switch (item)
            {
                case TMapSprite sprite:
                    SelectionTypeText.Text = "图片元素";
                    ItemNameText.Text = sprite.Name;
                    SpriteLayerCombo.SelectedItem = _document.Layers.FirstOrDefault(layer => layer.Name == sprite.Layer);
                    SpriteImagePathText.Text = sprite.ImagePath;
                    SpriteXText.Text = Format(sprite.X);
                    SpriteYText.Text = Format(sprite.Y);
                    SpriteWidthText.Text = Format(sprite.Width);
                    SpriteHeightText.Text = Format(sprite.Height);
                    SpriteRotationText.Text = Format(sprite.Rotation);
                    SpriteScaleXText.Text = Format(sprite.ScaleX);
                    SpriteScaleYText.Text = Format(sprite.ScaleY);
                    SpriteAnchorXText.Text = Format(sprite.AnchorX);
                    SpriteAnchorYText.Text = Format(sprite.AnchorY);
                    SpriteOrderText.Text = sprite.Order.ToString(CultureInfo.InvariantCulture);
                    break;
                case TMapObject mapObject:
                    SelectionTypeText.Text = "地图对象";
                    ItemNameText.Text = mapObject.Name;
                    ObjectLayerCombo.SelectedItem = _document.Layers.FirstOrDefault(layer => layer.Name == mapObject.Layer);
                    ObjectArgsText.Text = mapObject.Args;
                    ObjectXText.Text = Format(mapObject.X);
                    ObjectYText.Text = Format(mapObject.Y);
                    break;
                default:
                    SelectionTypeText.Text = "未选择";
                    break;
            }
        }
        finally
        {
            _updatingSelectionProperties = false;
        }
    }

    private void RefreshEntityList()
    {
        var selection = EditorCanvas.SelectedItems.ToList();
        var entities = GetCurrentEntityItems();
        _synchronizingSelection = true;
        EntityList.ItemsSource = entities;
        foreach (var selectedItem in selection)
        {
            if (EntityList.Items.Contains(selectedItem)) EntityList.SelectedItems.Add(selectedItem);
        }
        _synchronizingSelection = false;
    }

    private List<object> GetCurrentEntityItems()
    {
        if (LayerList.SelectedItem is not TMapLayer layer) return [];
        return layer.Type == TMapLayerType.Object
            ? _document.Objects.Where(mapObject => mapObject.Layer == layer.Name).Cast<object>().ToList()
            : _document.Sprites.Where(sprite => sprite.Layer == layer.Name).Cast<object>().ToList();
    }

    private void RefreshResourceList()
    {
        TMapFileService.RefreshResourcePaths(_document);
        var selection = ResourceList.SelectedItem;
        ResourceList.ItemsSource = null;
        ResourceList.ItemsSource = _document.Resources;
        ResourceList.SelectedItem = selection;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        EditorCanvas.DeleteSelected();
        RefreshEntityList();
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => EditorCanvas.FitToView();

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control)
        {
            await SaveDocument(false);
            e.Handled = true;
        }
        else if (e.Key == Key.S && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            await SaveDocument(true);
            e.Handled = true;
        }
        else if (e.Key == Key.O && e.KeyModifiers == KeyModifiers.Control)
        {
            Open_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.N && e.KeyModifiers == KeyModifiers.Control)
        {
            New_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.E && e.KeyModifiers == KeyModifiers.Control)
        {
            Export_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control)
        {
            Undo_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.None)
        {
            EditorCanvas.FitToView();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && !IsTextInputFocused())
        {
            Delete_Click(sender, e);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.None && !IsTextInputFocused())
        {
            var offset = e.Key switch
            {
                Key.W => (X: 0d, Y: 1d),
                Key.A => (X: -1d, Y: 0d),
                Key.S => (X: 0d, Y: -1d),
                Key.D => (X: 1d, Y: 0d),
                _ => (X: 0d, Y: 0d)
            };
            if (offset != (0d, 0d) && EditorCanvas.NudgeSelectedSprites(offset.X, offset.Y))
                e.Handled = true;
        }
    }

    private async void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (!_closingConfirmed && _dirty)
        {
            e.Cancel = true;
            StatusText.Text = "正在保存地图...";
            if (await SaveDocument(false))
            {
                _closingConfirmed = true;
                Close();
            }
            return;
        }

        if (Resources["ImagePathToBitmapConverter"] is IDisposable bitmapConverter)
            bitmapConverter.Dispose();
        _settings.ResourcePreviewScale = ResourcePreviewScaleSlider.Value;
        EditorSettingsService.Save(_settings);
    }

    private void RememberCurrentProject()
    {
        if (_document.FilePath is null) return;
        _settings.LastProjectPath = _document.FilePath;
        _settings.ResourcePreviewScale = ResourcePreviewScaleSlider.Value;
        EditorSettingsService.Save(_settings);
    }

    private async Task<bool> ConfirmDiscardOrSave()
    {
        if (!_dirty) return true;
        var result = await ShowMessage("TMap Editor", "当前地图尚未保存，是否先保存？", ["是", "否", "取消"]);
        return result switch
        {
            "是" => await SaveDocument(false),
            "否" => true,
            _ => false
        };
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        if (dirty && !_restoringUndo) RefreshCurrentSnapshot();
        Title = $"TMap Editor - {_document.Name}{(dirty ? " *" : "")}";
    }

    private void SaveDocumentToPath(string filePath)
    {
        var resolvedPaths = _document.Sprites
            .Select(sprite => TMapFileService.ResolveImagePath(_document, sprite.ImagePath)).ToList();
        var resolvedResourcePaths = _document.Resources
            .Select(resource => TMapFileService.ResolveImagePath(_document, resource.ImagePath)).ToList();
        _document.FilePath = Path.GetFullPath(filePath);
        for (var index = 0; index < _document.Sprites.Count; index++)
        {
            _document.Sprites[index].ImagePath =
                TMapFileService.MakePortableImagePath(_document, resolvedPaths[index]);
        }
        for (var index = 0; index < _document.Resources.Count; index++)
        {
            var resource = _document.Resources[index];
            resource.ImagePath = TMapFileService.MakePortableImagePath(_document, resolvedResourcePaths[index]);
            resource.ThumbnailPath = resolvedResourcePaths[index];
        }
        TMapFileService.ApplyFileName(_document);
        TMapFileService.Save(_document, filePath);
        RememberCurrentProject();
        RefreshResourceList();
        FileText.Text = _document.FilePath;
        SetDirty(false);
        EditorCanvas.InvalidateVisual();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undoSnapshot is null) return;
        try
        {
            var filePath = _document.FilePath;
            var restored = JsonSerializer.Deserialize(_undoSnapshot, TMapJsonContext.Default.TMapDocument)
                           ?? throw new InvalidDataException("无法恢复撤销状态。");
            restored.FilePath = filePath;
            TMapFileService.Normalize(restored);
            _restoringUndo = true;
            SetDocument(restored);
            _restoringUndo = false;
            _undoSnapshot = null;
            RefreshCurrentSnapshot();
            UpdateUndoMenu();
            SetDirty(true);
            StatusText.Text = "已撤销上一步操作";
        }
        catch (Exception exception)
        {
            _restoringUndo = false;
            _ = ShowError("撤销失败", exception);
        }
    }

    private void CaptureUndoSnapshot()
    {
        if (_restoringUndo) return;
        var snapshot = CreateDocumentSnapshot(_document);
        if (snapshot == _currentSnapshot) _undoSnapshot = snapshot;
        else _undoSnapshot = _currentSnapshot;
        UpdateUndoMenu();
    }

    private void ResetUndoState()
    {
        _undoSnapshot = null;
        RefreshCurrentSnapshot();
        UpdateUndoMenu();
    }

    private void RefreshCurrentSnapshot()
    {
        _currentSnapshot = CreateDocumentSnapshot(_document);
    }

    private void UpdateUndoMenu()
    {
        if (UndoMenuItem is not null) UndoMenuItem.IsEnabled = _undoSnapshot is not null;
    }

    private static string CreateDocumentSnapshot(TMapDocument document)
    {
        return JsonSerializer.Serialize(document, TMapJsonContext.Default.TMapDocument);
    }

    private string GetDefaultMapFileName()
    {
        if (_document.FilePath is not null) return Path.GetFileName(_document.FilePath);
        return _document.Name + ".tmap";
    }

    private bool IsTextInputFocused()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return focused is TextBox or ComboBox;
    }

    private static string RequiredName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException("名称不能为空。") : value.Trim();
    }

    private static double PositiveDouble(string value, string fieldName)
    {
        var number = ParseDouble(value, fieldName);
        return number > 0 ? number : throw new InvalidDataException($"{fieldName}必须大于 0。");
    }

    private static double ParseDouble(string value, string fieldName)
    {
        return TryDouble(value, out var number)
            ? number
            : throw new InvalidDataException($"{fieldName}必须是有效数字。");
    }

    private static bool TryDouble(string value, out double number)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out number) ||
               double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string GetUniqueResourcePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate)) return candidate;
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; ; index++)
        {
            candidate = Path.Combine(directory, $"{name}_{index}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private Bitmap LoadSpriteBitmap(TMapSprite sprite)
    {
        var fullPath = TMapFileService.ResolveImagePath(_document, sprite.ImagePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("找不到图片文件。", fullPath);
        return new Bitmap(fullPath);
    }

    private Task<string?> ShowError(string title, Exception exception)
    {
        return ShowMessage(title, exception.Message, ["确定"]);
    }

    private async Task<string?> ShowMessage(string title, string message, IReadOnlyList<string> buttons)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Avalonia.Media.Brush.Parse("#25282D"),
            Content = CreateMessageContent(message, buttons)
        };
        return await dialog.ShowDialog<string?>(this);
    }

    private static Control CreateMessageContent(string message, IReadOnlyList<string> buttons)
    {
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 14 };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = Avalonia.Media.Brush.Parse("#F2F2F2")
        });
        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8
        };
        foreach (var label in buttons)
        {
            var button = new Button { Content = label, MinWidth = 72 };
            button.Click += (_, _) => button.FindAncestorOfType<Window>()?.Close(label);
            buttonPanel.Children.Add(button);
        }
        panel.Children.Add(buttonPanel);
        return panel;
    }
}
