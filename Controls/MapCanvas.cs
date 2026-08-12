using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Skia.Controls;
using SkiaSharp;
using TMapEditor.Models;
using TMapEditor.Services;

namespace TMapEditor.Controls;

public sealed class MapCanvas : SkiaCanvasView
{
    private static readonly SKTypeface UiTypeface = CreateUiTypeface();
    private readonly Dictionary<string, SKBitmap> _bitmapCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<object> _selectedItems = [];
    private readonly ContextMenu _elementContextMenu;
    private TMapDocument _document = new();
    private object? _selectedItem;
    private EditorTool _tool;
    private double _zoom = 0.16;
    private Vector _pan;
    private bool _isPanning;
    private bool _isDragging;
    private bool _isResizing;
    private Point _lastScreenPoint;
    private Point _dragStartScreenPoint;
    private TMapPoint? _dragStartMapPoint;
    private TMapSprite? _resizeSprite;
    private ResizeHandle _resizeHandle;
    private double _resizeStartX;
    private double _resizeStartY;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private double _dragStartX;
    private double _dragStartY;
    private int? _hoveredRow;
    private int? _hoveredColumn;
    private (int Row, int Column)? _brushStartCell;
    private (int Row, int Column)? _brushEndCell;
    private EditorTool _activeBrushTool;
    private bool _isContinuousBrushing;
    private bool _isRectangleBrushing;
    private bool _brushChanged;
    private bool _activePointerEditChanged;
    private TMapResource? _resourceDropPreview;
    private TMapPoint? _resourceDropPreviewPoint;

    public MapCanvas()
    {
        Focusable = true;
        ContinuousAnimation = false;
        Cursor = CursorType.Arrow;
        AllowDrop = true;
        var deleteItem = new ContextMenu();
        deleteItem.AddItem("删除", DeleteSelected);
        _elementContextMenu = deleteItem;
        MouseDown += OnCanvasMouseDown;
        MouseMove += OnCanvasMouseMove;
        MouseUp += OnCanvasMouseUp;
        MouseWheel += OnCanvasMouseWheel;
        MouseLeave += OnCanvasMouseLeave;
        DragOver += OnCanvasDragOver;
        DragLeave += OnCanvasDragLeave;
        Drop += OnCanvasDrop;
        PaintSurface += OnPaintSurface;
    }

    public event EventHandler<object?>? SelectedItemChanged;
    public event EventHandler? DocumentChanging;
    public event EventHandler? DocumentChanged;
    public event EventHandler<MapCellHoverEventArgs>? HoveredCellChanged;

    public TMapDocument Document
    {
        get => _document;
        set
        {
            _document = value ?? new TMapDocument();
            ClearBitmapCache();
            SelectedItem = null;
            InvalidateVisual();
        }
    }

    public object? SelectedItem
    {
        get => _selectedItem;
        set
        {
            SetSelectedItems(value is null ? [] : [value], value);
        }
    }

    public IReadOnlyList<object> SelectedItems => _selectedItems;

    public void SetSelectedItems(IEnumerable<object> items)
    {
        var selection = items.Distinct().ToList();
        SetSelectedItems(selection, selection.LastOrDefault());
    }

    public EditorTool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            CancelCellBrush();
            Cursor = value == EditorTool.Select
                ? CursorType.Arrow
                : CursorType.Cross;
            InvalidateVisual();
        }
    }

    public bool ShowGrid { get; set; } = true;
    public bool ShowChunks { get; set; }
    public bool ShowCells { get; set; } = true;
    public bool ShowCellZs { get; set; } = true;
    public bool SnapToGrid { get; set; }
    public int CellZBrushValue { get; set; } = 1;
    public string DropTargetLayer { get; set; } = "";

    /// <summary>由主窗口在空格键按下/松开时更新，用于空格 + 左键平移。</summary>
    public bool IsSpaceDown { get; set; }

    public void FitToView()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || Document.Width <= 0 || Document.Height <= 0) return;
        _zoom = Math.Clamp(Math.Min(
            (Bounds.Width - 40) / Document.Width,
            (Bounds.Height - 40) / Document.Height), 0.02, 8);
        _pan = new Vector();
        InvalidateVisual();
    }

    public void DeleteSelected()
    {
        if (_selectedItems.Count == 0) return;
        NotifyDocumentChanging();
        foreach (var item in _selectedItems.ToList())
        {
            switch (item)
            {
                case TMapSprite sprite:
                    Document.Sprites.Remove(sprite);
                    break;
                case TMapObject mapObject:
                    Document.Objects.Remove(mapObject);
                    break;
            }
        }
        SelectedItem = null;
        NotifyDocumentChanged();
    }

    public bool NudgeSelectedSprites(double dx, double dy)
    {
        var sprites = _selectedItems.OfType<TMapSprite>().ToList();
        if (sprites.Count == 0) return false;

        NotifyDocumentChanging();
        foreach (var sprite in sprites)
        {
            sprite.X += dx;
            sprite.Y += dy;
        }

        NotifyDocumentChanged();
        return true;
    }

    public void CancelBrush()
    {
        var brushChanged = _brushChanged;
        CancelCellBrush();
        if (brushChanged) NotifyDocumentChanged();
        else InvalidateVisual();
    }

    public TMapSprite? AddResourceAt(TMapResource resource, TMapPoint point)
    {
        var bitmap = LoadBitmap(resource.ImagePath);
        if (!IsInsideMap(point) || bitmap is null) return null;

        point = Snap(point);
        if (!Document.Layers.Any(item => item.Name == DropTargetLayer)) return null;
        NotifyDocumentChanging();
        var sprite = new TMapSprite
        {
            Name = resource.Name,
            Layer = DropTargetLayer,
            ImagePath = resource.ImagePath,
            X = point.X,
            Y = point.Y,
            Width = bitmap.Width,
            Height = bitmap.Height,
            Order = Document.Sprites.Count == 0 ? 0 : Document.Sprites.Max(item => item.Order) + 1
        };
        Document.Sprites.Add(sprite);
        SelectedItem = sprite;
        NotifyDocumentChanged();
        return sprite;
    }

    public void RefreshHoveredCell() =>
        HoveredCellChanged?.Invoke(this, new MapCellHoverEventArgs(_hoveredRow, _hoveredColumn));

    private void OnPaintSurface(SKCanvas canvas, SKImageInfo info)
    {
        var scale = Bounds.Width > 0 ? (float)(info.Width / Bounds.Width) : 1f;
        canvas.Clear(new SKColor(30, 32, 36));
        canvas.Save();
        canvas.Scale(scale);
        DrawMapBackground(canvas);
        DrawImageLayers(canvas);
        if (ShowCells) DrawCells(canvas);
        if (ShowCellZs) DrawCellZs(canvas);
        DrawObjectLayers(canvas);
        DrawCellBrushPreview(canvas);
        if (ShowGrid) DrawGrid(canvas);
        if (ShowChunks) DrawChunks(canvas);
        DrawSelection(canvas);
        DrawResourceDropPreview(canvas);
        canvas.Restore();
    }

    private void DrawResourceDropPreview(SKCanvas canvas)
    {
        if (_resourceDropPreview is null || _resourceDropPreviewPoint is null) return;
        var bitmap = LoadBitmap(_resourceDropPreview.ImagePath);
        if (bitmap is null) return;

        var point = MapToScreen(_resourceDropPreviewPoint);
        var halfWidth = bitmap.Width * _zoom / 2;
        var halfHeight = bitmap.Height * _zoom / 2;
        var rect = new SKRect(
            (float)(point.X - halfWidth),
            (float)(point.Y - halfHeight),
            (float)(point.X + halfWidth),
            (float)(point.Y + halfHeight));
        using var previewPaint = new SKPaint { Color = new SKColor(255, 255, 255, 150) };
        using var outlinePaint = new SKPaint
        {
            Color = new SKColor(0, 180, 255, 230),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };
        canvas.DrawBitmap(bitmap, rect, previewPaint);
        canvas.DrawRect(rect, outlinePaint);
        canvas.DrawCircle((float)point.X, (float)point.Y, 4, outlinePaint);
    }

    private void DrawMapBackground(SKCanvas canvas)
    {
        var topLeft = MapToScreen(new TMapPoint(-Document.Width / 2, Document.Height / 2));
        var rect = new SKRect(
            (float)topLeft.X,
            (float)topLeft.Y,
            (float)(topLeft.X + Document.Width * _zoom),
            (float)(topLeft.Y + Document.Height * _zoom));
        using var fill = new SKPaint { Color = new SKColor(59, 61, 66), Style = SKPaintStyle.Fill };
        using var border = new SKPaint { Color = new SKColor(130, 135, 145), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        canvas.DrawRect(rect, fill);
        canvas.DrawRect(rect, border);
    }

    private void DrawImageLayers(SKCanvas canvas)
    {
        foreach (var layer in Document.Layers
                     .Where(layer => layer.Visible && layer.Type == TMapLayerType.Image).Reverse())
        {
            foreach (var sprite in Document.Sprites.Where(sprite => sprite.Layer == layer.Name)
                         .OrderBy(sprite => sprite.Order))
                DrawSprite(canvas, sprite);
        }
    }

    private void DrawObjectLayers(SKCanvas canvas)
    {
        foreach (var layer in Document.Layers
                     .Where(layer => layer.Visible && layer.Type == TMapLayerType.Object).Reverse())
        {
            foreach (var sprite in Document.Sprites.Where(sprite => sprite.Layer == layer.Name)
                         .OrderBy(sprite => sprite.Z)
                         .ThenBy(sprite => sprite.Order))
                DrawSprite(canvas, sprite);
            foreach (var mapObject in Document.Objects.Where(mapObject => mapObject.Layer == layer.Name)
                         .OrderBy(mapObject => mapObject.Z))
                DrawObject(canvas, mapObject);
        }
    }

    private void DrawSprite(SKCanvas canvas, TMapSprite sprite)
    {
        var bitmap = LoadBitmap(sprite.ImagePath);
        if (bitmap is null) return;
        var center = MapToScreen(new TMapPoint(sprite.X, sprite.Y));
        canvas.Save();
        canvas.Translate((float)center.X, (float)center.Y);
        canvas.RotateDegrees((float)-sprite.Rotation);
        canvas.Scale((float)(sprite.ScaleX * _zoom), (float)(sprite.ScaleY * _zoom));
        var rect = new SKRect(
            (float)(-sprite.AnchorX * sprite.Width),
            (float)(-(1 - sprite.AnchorY) * sprite.Height),
            (float)((1 - sprite.AnchorX) * sprite.Width),
            (float)(sprite.AnchorY * sprite.Height));
        canvas.DrawBitmap(bitmap, rect);
        canvas.Restore();
    }

    private void DrawCells(SKCanvas canvas)
    {
        using var fill = new SKPaint { Style = SKPaintStyle.Fill };
        foreach (var cell in Document.Cells)
        {
            var rect = GetCellScreenRect(cell.Row, cell.Column);
            fill.Color = cell.State == TMapCellState.Walk
                ? new SKColor(0, 210, 75, 105)
                : new SKColor(235, 55, 55, 105);
            canvas.DrawRect(rect, fill);
        }
    }

    private void DrawCellZs(SKCanvas canvas)
    {
        using var fill = new SKPaint { Style = SKPaintStyle.Fill };
        using var border = new SKPaint { Color = SKColors.DeepSkyBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        foreach (var cell in Document.CellZs)
        {
            var rect = GetCellScreenRect(cell.Row, cell.Column);
            fill.Color = cell.Z > 0
                ? new SKColor(30, 150, 255, 75)
                : new SKColor(180, 80, 230, 75);
            canvas.DrawRect(rect, fill);
            canvas.DrawRect(rect, border);
            if (_zoom * Document.GridSize < 18) continue;
            var size = (float)Math.Clamp(_zoom * Document.GridSize * 0.4, 10, 18);
            using var font = new SKFont(UiTypeface, size);
            using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            var text = cell.Z.ToString();
            var width = font.MeasureText(text);
            var centerX = (rect.Left + rect.Right) / 2f;
            var centerY = (rect.Top + rect.Bottom) / 2f;
            canvas.DrawText(text,
                centerX - width / 2f,
                centerY + size * 0.35f,
                font, textPaint);
        }
    }

    private void DrawObject(SKCanvas canvas, TMapObject mapObject)
    {
        var point = MapToScreen(new TMapPoint(mapObject.X, mapObject.Y));
        using var fill = new SKPaint { Color = ParseDisplayColor(mapObject.DisplayColor), Style = SKPaintStyle.Fill };
        using var outline = new SKPaint
        {
            Color = _selectedItems.Contains(mapObject) ? SKColors.Yellow : SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = _selectedItems.Contains(mapObject) ? 2 : 1
        };
        var oval = new SKRect((float)(point.X - 6), (float)(point.Y - 6), (float)(point.X + 6), (float)(point.Y + 6));
        canvas.DrawOval(oval, fill);
        canvas.DrawOval(oval, outline);
        using var font = new SKFont(UiTypeface, 12);
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText(mapObject.Label, (float)(point.X + 8), (float)(point.Y - 17 + 12), font, textPaint);
    }

    private static SKTypeface CreateUiTypeface()
    {
        string[] candidates =
        [
            "Microsoft YaHei UI",
            "Microsoft YaHei",
            "Segoe UI",
            "Noto Sans CJK SC",
            "PingFang SC",
            "Source Han Sans SC"
        ];
        foreach (var family in candidates)
        {
            var typeface = SKTypeface.FromFamilyName(family);
            if (typeface is not null &&
                !string.Equals(typeface.FamilyName, "sans-serif", StringComparison.OrdinalIgnoreCase))
            {
                return typeface;
            }
            typeface?.Dispose();
        }

        return SKTypeface.CreateDefault();
    }

    private static SKColor ParseDisplayColor(string? value)
    {
        try
        {
            var color = Color.FromHex(value ?? "#00BFFF");
            return new SKColor(color.R, color.G, color.B, color.A);
        }
        catch (FormatException)
        {
            return new SKColor(0, 191, 255);
        }
    }

    private void DrawCellBrushPreview(SKCanvas canvas)
    {
        if (!_isRectangleBrushing || !_brushStartCell.HasValue || !_brushEndCell.HasValue) return;
        var points = GetBrushRectanglePoints(_brushStartCell.Value, _brushEndCell.Value);
        var brush = _activeBrushTool switch
        {
            EditorTool.WalkBrush => SKColors.LimeGreen,
            EditorTool.BlockBrush => SKColors.OrangeRed,
            EditorTool.CellZBrush => SKColors.DeepSkyBlue,
            EditorTool.EraseCellZBrush => SKColors.MediumPurple,
            _ => SKColors.WhiteSmoke,
        };
        using var pen = new SKPaint { Color = brush, Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true };
        using var path = CreateScreenPolygonPath(points.Select(MapToScreen).ToList(), true);
        canvas.DrawPath(path, pen);
    }

    private void DrawGrid(SKCanvas canvas)
    {
        if (Document.GridSize <= 0 || _zoom * Document.GridSize < 4) return;
        using var pen = new SKPaint { Color = new SKColor(255, 255, 255, 75), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        var minX = -Document.Width / 2;
        var minY = -Document.Height / 2;
        var columns = (int)Math.Ceiling(Document.Width / Document.GridSize);
        var rows = (int)Math.Ceiling(Document.Height / Document.GridSize);
        for (var col = 0; col <= columns; col++)
        {
            var x = Math.Min(Document.Width / 2, minX + col * Document.GridSize);
            DrawLine(canvas, pen, MapToScreen(new TMapPoint(x, minY)), MapToScreen(new TMapPoint(x, Document.Height / 2)));
        }
        for (var row = 0; row <= rows; row++)
        {
            var y = Math.Min(Document.Height / 2, minY + row * Document.GridSize);
            DrawLine(canvas, pen, MapToScreen(new TMapPoint(minX, y)), MapToScreen(new TMapPoint(Document.Width / 2, y)));
        }
    }

    private void DrawChunks(SKCanvas canvas)
    {
        if (Document.ChunkColumns <= 0 || Document.ChunkRows <= 0) return;
        using var pen = new SKPaint { Color = SKColors.Gold, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        var minX = -Document.Width / 2;
        var minY = -Document.Height / 2;
        for (var col = 0; col <= Document.ChunkColumns; col++)
        {
            var x = minX + col * Document.Width / Document.ChunkColumns;
            DrawLine(canvas, pen, MapToScreen(new TMapPoint(x, minY)), MapToScreen(new TMapPoint(x, Document.Height / 2)));
        }
        for (var row = 0; row <= Document.ChunkRows; row++)
        {
            var y = minY + row * Document.Height / Document.ChunkRows;
            DrawLine(canvas, pen, MapToScreen(new TMapPoint(minX, y)), MapToScreen(new TMapPoint(Document.Width / 2, y)));
        }
    }

    private void DrawSelection(SKCanvas canvas)
    {
        using var pen = new SKPaint { Color = SKColors.Cyan, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        foreach (var sprite in _selectedItems.OfType<TMapSprite>())
        {
            var corners = GetSpriteCorners(sprite).Select(MapToScreen).ToList();
            using var geometry = CreateScreenPolygonPath(corners, true);
            canvas.DrawPath(geometry, pen);
            DrawResizeHandles(canvas, corners);
        }
    }

    private void DrawResizeHandles(SKCanvas canvas, IReadOnlyList<Point> corners)
    {
        if (corners.Count != 4) return;
        var handles = new[]
        {
            corners[0],
            corners[1],
            corners[2],
            corners[3],
            Midpoint(corners[0], corners[1]),
            Midpoint(corners[1], corners[2]),
            Midpoint(corners[2], corners[3]),
            Midpoint(corners[3], corners[0])
        };
        using var fill = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        using var outline = new SKPaint { Color = SKColors.Cyan, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        foreach (var point in handles)
        {
            var rect = new SKRect((float)(point.X - 4), (float)(point.Y - 4), (float)(point.X + 4), (float)(point.Y + 4));
            canvas.DrawRect(rect, fill);
            canvas.DrawRect(rect, outline);
        }
    }

    private static void DrawLine(SKCanvas canvas, SKPaint paint, Point start, Point end)
    {
        canvas.DrawLine((float)start.X, (float)start.Y, (float)end.X, (float)end.Y, paint);
    }

    private void OnCanvasMouseWheel(MouseWheelEventArgs e)
    {
        var before = ScreenToMap(e.GetPosition(this));
        _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), 0.02, 8);
        var afterScreen = MapToScreen(before);
        _pan += e.GetPosition(this) - afterScreen;
        InvalidateVisual();
        e.Handled = true;
    }

    private void OnCanvasMouseDown(MouseEventArgs e)
    {
        Focus();
        _lastScreenPoint = e.GetPosition(this);
        var window = FindVisualRoot() as Window;
        if (e.MiddleButton || (e.LeftButton && IsSpaceDown))
        {
            _isPanning = true;
            window?.CaptureMouse(this);
            Cursor = CursorType.Hand;
            e.Handled = true;
            return;
        }

        if (e.RightButton)
        {
            if (IsCellBrushTool())
            {
                BeginRectangleCellBrush(ScreenToMap(_lastScreenPoint));
                if (_isRectangleBrushing) window?.CaptureMouse(this);
                e.Handled = true;
                return;
            }

            var contextMapPoint = ScreenToMap(_lastScreenPoint);
            var hit = HitTestItem(contextMapPoint, _lastScreenPoint);
            if (hit is not null)
            {
                if (!_selectedItems.Contains(hit)) SelectedItem = hit;
                _elementContextMenu.ShowAt(this, _lastScreenPoint);
                e.Handled = true;
            }
            return;
        }

        if (!e.LeftButton) return;
        var mapPoint = ScreenToMap(_lastScreenPoint);
        switch (Tool)
        {
            case EditorTool.WalkBrush:
            case EditorTool.BlockBrush:
            case EditorTool.EraseBrush:
            case EditorTool.CellZBrush:
            case EditorTool.EraseCellZBrush:
                BeginContinuousCellBrush(mapPoint);
                if (_isContinuousBrushing) window?.CaptureMouse(this);
                break;
            default:
                BeginSelectionOrDrag(mapPoint, _lastScreenPoint, e.Modifiers.HasFlag(ModifierKeys.Control));
                if (_isDragging || _isResizing) window?.CaptureMouse(this);
                break;
        }
        e.Handled = true;
    }

    private void OnCanvasMouseMove(MouseEventArgs e)
    {
        var screenPoint = e.GetPosition(this);
        if (!_isPanning && !_isDragging && !_isResizing)
            UpdateHoveredCell(screenPoint);
        if (_isPanning)
        {
            _pan += screenPoint - _lastScreenPoint;
            _lastScreenPoint = screenPoint;
            InvalidateVisual();
            return;
        }

        if (_isContinuousBrushing && e.LeftButton)
        {
            var cell = GetMapCell(ScreenToMap(screenPoint));
            if (cell.HasValue && cell != _brushEndCell)
            {
                ApplyCellBrushLine(_brushEndCell!.Value, cell.Value);
                _brushEndCell = cell;
                InvalidateVisual();
            }
            return;
        }

        if (_isRectangleBrushing && e.RightButton)
        {
            var cell = GetMapCell(ScreenToMap(screenPoint));
            if (cell.HasValue && cell != _brushEndCell)
            {
                _brushEndCell = cell;
                InvalidateVisual();
            }
            return;
        }

        if (_isResizing && _resizeSprite is not null && e.LeftButton)
        {
            if (ResizeSelectedSprite(ScreenToMap(screenPoint)))
            {
                _activePointerEditChanged = true;
                InvalidateVisual();
            }
            return;
        }

        if (!_isDragging || !e.LeftButton || _dragStartMapPoint is null) return;
        var dx = (screenPoint.X - _dragStartScreenPoint.X) / _zoom;
        var dy = -(screenPoint.Y - _dragStartScreenPoint.Y) / _zoom;
        var changed = false;
        switch (SelectedItem)
        {
            case TMapSprite sprite:
                changed = MoveSprite(sprite, _dragStartX + dx, _dragStartY + dy);
                break;
            case TMapObject mapObject:
                changed = MoveObject(mapObject, _dragStartX + dx, _dragStartY + dy);
                break;
        }
        if (!changed) return;
        _activePointerEditChanged = true;
        InvalidateVisual();
    }

    private void OnCanvasMouseLeave()
    {
        SetHoveredCell(null, null);
    }

    private void OnCanvasDragOver(DragEventArgs e)
    {
        if (e.Data.TryGetData<TMapResource>(TMapDragFormats.Resource, out var resource) &&
            Document.Layers.Any(layer => layer.Name == DropTargetLayer) &&
            IsInsideMap(ScreenToMap(PointFromScreen(e.ScreenPosition))))
        {
            _resourceDropPreview = resource;
            _resourceDropPreviewPoint = Snap(ScreenToMap(PointFromScreen(e.ScreenPosition)));
            InvalidateVisual();
            e.Effect = DragDropEffects.Copy;
            e.Accepted = true;
        }
        else
        {
            ClearResourceDropPreview();
            e.Effect = DragDropEffects.None;
            e.Accepted = false;
        }
        e.Handled = true;
    }

    private void OnCanvasDragLeave(DragEventArgs e)
    {
        ClearResourceDropPreview();
        e.Handled = true;
    }

    private void OnCanvasDrop(DragEventArgs e)
    {
        ClearResourceDropPreview();
        if (!e.Data.TryGetData<TMapResource>(TMapDragFormats.Resource, out var resource))
        {
            e.Effect = DragDropEffects.None;
            e.Accepted = false;
            e.Handled = true;
            return;
        }

        var point = ScreenToMap(PointFromScreen(e.ScreenPosition));
        e.Effect = AddResourceAt(resource, point) is null
            ? DragDropEffects.None
            : DragDropEffects.Copy;
        e.Accepted = e.Effect == DragDropEffects.Copy;
        e.Handled = true;
    }

    private void ClearResourceDropPreview()
    {
        if (_resourceDropPreview is null && _resourceDropPreviewPoint is null) return;
        _resourceDropPreview = null;
        _resourceDropPreviewPoint = null;
        InvalidateVisual();
    }

    private void OnCanvasMouseUp(MouseEventArgs e)
    {
        var window = FindVisualRoot() as Window;
        if (e.Button == MouseButton.Left && _isContinuousBrushing)
        {
            CancelCellBrush();
            window?.ReleaseMouseCapture();
            NotifyDocumentChanged();
            e.Handled = true;
            return;
        }
        if (e.Button == MouseButton.Right && _isRectangleBrushing)
        {
            CommitRectangleCellBrush();
            window?.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }
        if (_isPanning || _isDragging || _isResizing)
        {
            var shouldNotifyDocumentChanged = (_isDragging || _isResizing) && _activePointerEditChanged;
            _isPanning = false;
            _isDragging = false;
            _isResizing = false;
            _activePointerEditChanged = false;
            _resizeSprite = null;
            _resizeHandle = ResizeHandle.None;
            window?.ReleaseMouseCapture();
            Cursor = Tool == EditorTool.Select
                ? CursorType.Arrow
                : CursorType.Cross;
            if (shouldNotifyDocumentChanged) NotifyDocumentChanged();
            else InvalidateVisual();
        }
    }

    private void BeginContinuousCellBrush(TMapPoint point)
    {
        var cell = GetMapCell(point);
        if (!cell.HasValue) return;
        NotifyDocumentChanging();
        _brushStartCell = cell;
        _brushEndCell = cell;
        _activeBrushTool = Tool;
        _isContinuousBrushing = true;
        _brushChanged |= ApplyCellBrush(cell.Value);
        InvalidateVisual();
    }

    private void BeginRectangleCellBrush(TMapPoint point)
    {
        var cell = GetMapCell(point);
        if (!cell.HasValue) return;
        _brushStartCell = cell;
        _brushEndCell = cell;
        _activeBrushTool = Tool;
        _isRectangleBrushing = true;
        InvalidateVisual();
    }

    private void CommitRectangleCellBrush()
    {
        if (!_brushStartCell.HasValue || !_brushEndCell.HasValue) return;
        NotifyDocumentChanging();
        var start = _brushStartCell.Value;
        var end = _brushEndCell.Value;
        var minColumn = Math.Min(start.Column, end.Column);
        var maxColumn = Math.Max(start.Column, end.Column);
        var minRow = Math.Min(start.Row, end.Row);
        var maxRow = Math.Max(start.Row, end.Row);
        for (var row = minRow; row <= maxRow; row++)
        {
            for (var column = minColumn; column <= maxColumn; column++)
            {
                _brushChanged |= ApplyCellBrush((row, column));
            }
        }
        CancelCellBrush();
        NotifyDocumentChanged();
    }

    private void ApplyCellBrushLine((int Row, int Column) start, (int Row, int Column) end)
    {
        var column = start.Column;
        var row = start.Row;
        var columnDelta = Math.Abs(end.Column - column);
        var rowDelta = Math.Abs(end.Row - row);
        var columnStep = column < end.Column ? 1 : -1;
        var rowStep = row < end.Row ? 1 : -1;
        var error = columnDelta - rowDelta;

        while (true)
        {
            _brushChanged |= ApplyCellBrush((row, column));
            if (column == end.Column && row == end.Row) break;
            var doubledError = error * 2;
            if (doubledError > -rowDelta)
            {
                error -= rowDelta;
                column += columnStep;
            }
            if (doubledError < columnDelta)
            {
                error += columnDelta;
                row += rowStep;
            }
        }
    }

    private bool ApplyCellBrush((int Row, int Column) position)
    {
        if (_activeBrushTool is EditorTool.CellZBrush or EditorTool.EraseCellZBrush)
            return ApplyCellZBrush(position);

        var cell = Document.Cells.FirstOrDefault(candidate =>
            candidate.Row == position.Row && candidate.Column == position.Column);
        var state = _activeBrushTool switch
        {
            EditorTool.WalkBrush => TMapCellState.Walk,
            EditorTool.BlockBrush => TMapCellState.Block,
            _ => (TMapCellState?)null,
        };
        if (state.HasValue)
        {
            if (cell is not null)
            {
                if (cell.State == state.Value) return false;
                cell.State = state.Value;
            }
            else Document.Cells.Add(new TMapCell
                { Row = position.Row, Column = position.Column, State = state.Value });
            return true;
        }
        else if (cell is not null)
        {
            Document.Cells.Remove(cell);
            return true;
        }
        return false;
    }

    private bool ApplyCellZBrush((int Row, int Column) position)
    {
        var cell = Document.CellZs.FirstOrDefault(candidate =>
            candidate.Row == position.Row && candidate.Column == position.Column);
        if (_activeBrushTool == EditorTool.CellZBrush && CellZBrushValue != 0)
        {
            if (cell is not null)
            {
                if (cell.Z == CellZBrushValue) return false;
                cell.Z = CellZBrushValue;
            }
            else Document.CellZs.Add(new TMapCellZ
                { Row = position.Row, Column = position.Column, Z = CellZBrushValue });
            return true;
        }
        if (cell is null) return false;
        Document.CellZs.Remove(cell);
        return true;
    }

    private void CancelCellBrush()
    {
        _brushStartCell = null;
        _brushEndCell = null;
        _activeBrushTool = EditorTool.Select;
        _isContinuousBrushing = false;
        _isRectangleBrushing = false;
        _brushChanged = false;
    }

    private bool IsCellBrushTool() => Tool is
        EditorTool.WalkBrush or EditorTool.BlockBrush or EditorTool.EraseBrush or
        EditorTool.CellZBrush or EditorTool.EraseCellZBrush;

    private List<TMapPoint> GetBrushRectanglePoints(
        (int Row, int Column) start,
        (int Row, int Column) end)
    {
        var minColumn = Math.Min(start.Column, end.Column);
        var maxColumn = Math.Max(start.Column, end.Column);
        var minRow = Math.Min(start.Row, end.Row);
        var maxRow = Math.Max(start.Row, end.Row);
        var originX = -Document.Width / 2;
        var originY = -Document.Height / 2;
        var left = originX + minColumn * Document.GridSize;
        var right = Math.Min(Document.Width / 2, originX + (maxColumn + 1) * Document.GridSize);
        var bottom = originY + minRow * Document.GridSize;
        var top = Math.Min(Document.Height / 2, originY + (maxRow + 1) * Document.GridSize);
        return
        [
            new TMapPoint(left, bottom),
            new TMapPoint(right, bottom),
            new TMapPoint(right, top),
            new TMapPoint(left, top)
        ];
    }

    private SKRect GetCellScreenRect(int row, int column)
    {
        var originX = -Document.Width / 2;
        var originY = -Document.Height / 2;
        var left = originX + column * Document.GridSize;
        var right = Math.Min(Document.Width / 2, left + Document.GridSize);
        var bottom = originY + row * Document.GridSize;
        var top = Math.Min(Document.Height / 2, bottom + Document.GridSize);
        var topLeft = MapToScreen(new TMapPoint(left, top));
        return new SKRect(
            (float)topLeft.X,
            (float)topLeft.Y,
            (float)(topLeft.X + (right - left) * _zoom),
            (float)(topLeft.Y + (top - bottom) * _zoom));
    }

    private void AddObject(TMapPoint point)
    {
        if (!IsInsideMap(point)) return;
        if (!Document.Layers.Any(layer => layer.Name == DropTargetLayer && layer.Type == TMapLayerType.Object))
            return;
        point = Snap(point);
        NotifyDocumentChanging();
        var mapObject = new TMapObject
        {
            Name = $"Object_{Document.Objects.Count + 1}",
            Layer = DropTargetLayer,
            X = point.X,
            Y = point.Y
        };
        Document.Objects.Add(mapObject);
        SelectedItem = mapObject;
        Tool = EditorTool.Select;
        NotifyDocumentChanged();
    }

    private void BeginSelectionOrDrag(TMapPoint mapPoint, Point screenPoint, bool extendSelection)
    {
        if (!extendSelection && SelectedItem is TMapSprite { IsLocked: false } selectedSprite)
        {
            var resizeHandle = HitTestResizeHandle(selectedSprite, screenPoint);
            if (resizeHandle != ResizeHandle.None)
            {
                BeginResize(selectedSprite, resizeHandle);
                return;
            }
        }

        var hit = HitTestItem(mapPoint, screenPoint);
        if (hit is null &&
            Document.Layers.Any(layer => layer.Name == DropTargetLayer && layer.Type == TMapLayerType.Object))
        {
            AddObject(mapPoint);
            return;
        }
        if (extendSelection)
        {
            if (hit is not null) ToggleSelectedItem(hit);
            return;
        }
        SelectedItem = hit;
        switch (hit)
        {
            case TMapSprite sprite:
                BeginDrag(mapPoint, sprite.X, sprite.Y);
                break;
            case TMapObject mapObject:
                BeginDrag(mapPoint, mapObject.X, mapObject.Y);
                break;
        }
    }

    private void BeginDrag(TMapPoint mapPoint, double x, double y)
    {
        NotifyDocumentChanging();
        _isDragging = true;
        _dragStartMapPoint = mapPoint;
        _dragStartScreenPoint = _lastScreenPoint;
        _dragStartX = x;
        _dragStartY = y;
        _activePointerEditChanged = false;
    }

    private void BeginResize(TMapSprite sprite, ResizeHandle handle)
    {
        NotifyDocumentChanging();
        _isResizing = true;
        _resizeSprite = sprite;
        _resizeHandle = handle;
        _resizeStartX = sprite.X;
        _resizeStartY = sprite.Y;
        _resizeStartWidth = sprite.Width;
        _resizeStartHeight = sprite.Height;
        _activePointerEditChanged = false;
    }

    private object? HitTestItem(TMapPoint mapPoint, Point screenPoint)
    {
        foreach (var layer in Document.Layers.Where(layer => layer.Visible && layer.Type == TMapLayerType.Object))
        {
            foreach (var mapObject in Document.Objects
                         .Where(item => !item.IsLocked && item.Layer == layer.Name)
                         .OrderByDescending(item => item.Z))
            {
                var point = MapToScreen(new TMapPoint(mapObject.X, mapObject.Y));
                if (Distance(point, screenPoint) <= 10) return mapObject;
            }
            foreach (var sprite in Document.Sprites
                         .Where(sprite => !sprite.IsLocked && sprite.Layer == layer.Name)
                         .OrderByDescending(sprite => sprite.Z)
                         .ThenByDescending(sprite => sprite.Order))
            {
                if (HitTestSprite(mapPoint, sprite)) return sprite;
            }
        }
        foreach (var layer in Document.Layers.Where(layer => layer.Visible && layer.Type == TMapLayerType.Image))
        {
            foreach (var sprite in Document.Sprites
                         .Where(sprite => !sprite.IsLocked && sprite.Layer == layer.Name)
                         .OrderByDescending(sprite => sprite.Order))
            {
                if (HitTestSprite(mapPoint, sprite)) return sprite;
            }
        }
        return null;
    }

    private bool HitTestSprite(TMapPoint point, TMapSprite sprite)
    {
        if (Math.Abs(sprite.ScaleX) < 0.000001 || Math.Abs(sprite.ScaleY) < 0.000001) return false;
        var dx = point.X - sprite.X;
        var dy = point.Y - sprite.Y;
        var radians = sprite.Rotation * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var x = (cos * dx + sin * dy) / sprite.ScaleX;
        var y = (-sin * dx + cos * dy) / sprite.ScaleY;
        return x >= -sprite.AnchorX * sprite.Width && x <= (1 - sprite.AnchorX) * sprite.Width &&
               y >= -sprite.AnchorY * sprite.Height && y <= (1 - sprite.AnchorY) * sprite.Height;
    }

    private static SKPath CreateScreenPolygonPath(IReadOnlyList<Point> points, bool close)
    {
        var path = new SKPath();
        if (points.Count == 0) return path;
        path.MoveTo((float)points[0].X, (float)points[0].Y);
        foreach (var point in points.Skip(1))
        {
            path.LineTo((float)point.X, (float)point.Y);
        }
        if (close) path.Close();
        return path;
    }

    private IEnumerable<TMapPoint> GetSpriteCorners(TMapSprite sprite)
    {
        var local = new[]
        {
            new TMapPoint(-sprite.AnchorX * sprite.Width, -sprite.AnchorY * sprite.Height),
            new TMapPoint((1 - sprite.AnchorX) * sprite.Width, -sprite.AnchorY * sprite.Height),
            new TMapPoint((1 - sprite.AnchorX) * sprite.Width, (1 - sprite.AnchorY) * sprite.Height),
            new TMapPoint(-sprite.AnchorX * sprite.Width, (1 - sprite.AnchorY) * sprite.Height)
        };
        var radians = sprite.Rotation * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        foreach (var point in local)
        {
            var x = point.X * sprite.ScaleX;
            var y = point.Y * sprite.ScaleY;
            yield return new TMapPoint(sprite.X + cos * x - sin * y, sprite.Y + sin * x + cos * y);
        }
    }

    private ResizeHandle HitTestResizeHandle(TMapSprite sprite, Point screenPoint)
    {
        var corners = GetSpriteCorners(sprite).Select(MapToScreen).ToList();
        if (corners.Count != 4) return ResizeHandle.None;

        var cornerHandles = new[]
        {
            ResizeHandle.Left | ResizeHandle.Bottom,
            ResizeHandle.Right | ResizeHandle.Bottom,
            ResizeHandle.Right | ResizeHandle.Top,
            ResizeHandle.Left | ResizeHandle.Top
        };
        for (var index = 0; index < corners.Count; index++)
        {
            if (Distance(corners[index], screenPoint) <= 8) return cornerHandles[index];
        }

        var edges = new[]
        {
            (Start: corners[0], End: corners[1], Handle: ResizeHandle.Bottom),
            (Start: corners[1], End: corners[2], Handle: ResizeHandle.Right),
            (Start: corners[2], End: corners[3], Handle: ResizeHandle.Top),
            (Start: corners[3], End: corners[0], Handle: ResizeHandle.Left)
        };
        foreach (var edge in edges)
        {
            if (DistanceToSegment(screenPoint, edge.Start, edge.End) <= 6) return edge.Handle;
        }
        return ResizeHandle.None;
    }

    private bool ResizeSelectedSprite(TMapPoint mapPoint)
    {
        if (_resizeSprite is null || _resizeHandle == ResizeHandle.None) return false;
        var local = WorldToSpriteLocal(mapPoint, _resizeStartX, _resizeStartY,
            _resizeSprite.Rotation, _resizeSprite.ScaleX, _resizeSprite.ScaleY);
        var oldLeft = -_resizeSprite.AnchorX * _resizeStartWidth;
        var oldRight = (1 - _resizeSprite.AnchorX) * _resizeStartWidth;
        var oldBottom = -_resizeSprite.AnchorY * _resizeStartHeight;
        var oldTop = (1 - _resizeSprite.AnchorY) * _resizeStartHeight;

        var targetWidth = _resizeStartWidth;
        var targetHeight = _resizeStartHeight;
        var fixedOldX = 0d;
        var fixedOldY = 0d;

        if (_resizeHandle.HasFlag(ResizeHandle.Left))
        {
            targetWidth = Math.Max(1, oldRight - local.X);
            fixedOldX = oldRight;
        }
        else if (_resizeHandle.HasFlag(ResizeHandle.Right))
        {
            targetWidth = Math.Max(1, local.X - oldLeft);
            fixedOldX = oldLeft;
        }

        if (_resizeHandle.HasFlag(ResizeHandle.Bottom))
        {
            targetHeight = Math.Max(1, oldTop - local.Y);
            fixedOldY = oldTop;
        }
        else if (_resizeHandle.HasFlag(ResizeHandle.Top))
        {
            targetHeight = Math.Max(1, local.Y - oldBottom);
            fixedOldY = oldBottom;
        }

        var scale = GetAspectResizeScale(targetWidth, targetHeight);
        var newWidth = Math.Max(1, _resizeStartWidth * scale);
        var newHeight = Math.Max(1, _resizeStartHeight * scale);
        var fixedNewX = GetFixedLocalX(newWidth);
        var fixedNewY = GetFixedLocalY(newHeight);

        var fixedWorld = SpriteLocalToWorld(new TMapPoint(fixedOldX, fixedOldY),
            _resizeStartX, _resizeStartY, _resizeSprite.Rotation, _resizeSprite.ScaleX, _resizeSprite.ScaleY);
        var newFixedOffset = SpriteLocalToWorldOffset(new TMapPoint(fixedNewX, fixedNewY),
            _resizeSprite.Rotation, _resizeSprite.ScaleX, _resizeSprite.ScaleY);

        var newX = fixedWorld.X - newFixedOffset.X;
        var newY = fixedWorld.Y - newFixedOffset.Y;
        if (NearlyEqual(_resizeSprite.Width, newWidth) &&
            NearlyEqual(_resizeSprite.Height, newHeight) &&
            NearlyEqual(_resizeSprite.X, newX) &&
            NearlyEqual(_resizeSprite.Y, newY))
        {
            return false;
        }

        _resizeSprite.Width = newWidth;
        _resizeSprite.Height = newHeight;
        _resizeSprite.X = newX;
        _resizeSprite.Y = newY;
        return true;
    }

    private double GetAspectResizeScale(double targetWidth, double targetHeight)
    {
        var scaleX = targetWidth / _resizeStartWidth;
        var scaleY = targetHeight / _resizeStartHeight;
        if (_resizeHandle.HasFlag(ResizeHandle.Left) || _resizeHandle.HasFlag(ResizeHandle.Right))
        {
            if (_resizeHandle.HasFlag(ResizeHandle.Top) || _resizeHandle.HasFlag(ResizeHandle.Bottom))
                return Math.Max(scaleX, scaleY);
            return scaleX;
        }
        return scaleY;
    }

    private double GetFixedLocalX(double width)
    {
        if (_resizeHandle.HasFlag(ResizeHandle.Left)) return (1 - _resizeSprite!.AnchorX) * width;
        if (_resizeHandle.HasFlag(ResizeHandle.Right)) return -_resizeSprite!.AnchorX * width;
        return 0;
    }

    private double GetFixedLocalY(double height)
    {
        if (_resizeHandle.HasFlag(ResizeHandle.Bottom)) return (1 - _resizeSprite!.AnchorY) * height;
        if (_resizeHandle.HasFlag(ResizeHandle.Top)) return -_resizeSprite!.AnchorY * height;
        return 0;
    }

    private static Point Midpoint(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static double DistanceToSegment(Point point, Point start, Point end)
    {
        var segment = end - start;
        var lengthSquared = segment.X * segment.X + segment.Y * segment.Y;
        if (lengthSquared <= 0.000001) return Distance(point, start);
        var t = ((point.X - start.X) * segment.X + (point.Y - start.Y) * segment.Y) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        var projection = new Point(start.X + t * segment.X, start.Y + t * segment.Y);
        return Distance(point, projection);
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static TMapPoint WorldToSpriteLocal(
        TMapPoint point,
        double originX,
        double originY,
        double rotation,
        double scaleX,
        double scaleY)
    {
        var dx = point.X - originX;
        var dy = point.Y - originY;
        var radians = rotation * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var x = cos * dx + sin * dy;
        var y = -sin * dx + cos * dy;
        return new TMapPoint(
            Math.Abs(scaleX) < 0.000001 ? 0 : x / scaleX,
            Math.Abs(scaleY) < 0.000001 ? 0 : y / scaleY);
    }

    private static TMapPoint SpriteLocalToWorld(
        TMapPoint local,
        double originX,
        double originY,
        double rotation,
        double scaleX,
        double scaleY)
    {
        var offset = SpriteLocalToWorldOffset(local, rotation, scaleX, scaleY);
        return new TMapPoint(originX + offset.X, originY + offset.Y);
    }

    private static TMapPoint SpriteLocalToWorldOffset(
        TMapPoint local,
        double rotation,
        double scaleX,
        double scaleY)
    {
        var x = local.X * scaleX;
        var y = local.Y * scaleY;
        var radians = rotation * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new TMapPoint(cos * x - sin * y, sin * x + cos * y);
    }

    private SKBitmap? LoadBitmap(string imagePath)
    {
        try
        {
            var fullPath = TMapFileService.ResolveImagePath(Document, imagePath);
            if (_bitmapCache.TryGetValue(fullPath, out var cached)) return cached;
            if (!File.Exists(fullPath)) return null;
            var bitmap = SKBitmap.Decode(fullPath);
            _bitmapCache[fullPath] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void ClearBitmapCache()
    {
        foreach (var bitmap in _bitmapCache.Values)
        {
            bitmap.Dispose();
        }
        _bitmapCache.Clear();
    }

    private Point MapToScreen(TMapPoint point)
    {
        return new Point(
            Bounds.Width / 2 + _pan.X + point.X * _zoom,
            Bounds.Height / 2 + _pan.Y - point.Y * _zoom);
    }

    private TMapPoint ScreenToMap(Point point)
    {
        return new TMapPoint(
            (point.X - Bounds.Width / 2 - _pan.X) / _zoom,
            -(point.Y - Bounds.Height / 2 - _pan.Y) / _zoom);
    }

    private void UpdateHoveredCell(Point screenPoint)
    {
        var cell = GetMapCell(ScreenToMap(screenPoint));
        SetHoveredCell(cell?.Row, cell?.Column);
    }

    private (int Row, int Column)? GetMapCell(TMapPoint point)
    {
        if (Document.GridSize <= 0 || Document.Width <= 0 || Document.Height <= 0) return null;
        var originX = -Document.Width / 2;
        var originY = -Document.Height / 2;
        if (point.X < originX || point.X >= originX + Document.Width ||
            point.Y < originY || point.Y >= originY + Document.Height)
            return null;

        var column = (int)Math.Floor((point.X - originX) / Document.GridSize);
        var row = (int)Math.Floor((point.Y - originY) / Document.GridSize);
        return (row, column);
    }

    private void SetHoveredCell(int? row, int? column)
    {
        if (_hoveredRow == row && _hoveredColumn == column) return;
        _hoveredRow = row;
        _hoveredColumn = column;
        HoveredCellChanged?.Invoke(this, new MapCellHoverEventArgs(row, column));
    }

    private TMapPoint Snap(TMapPoint point)
    {
        if (!SnapToGrid || Document.GridSize <= 0) return point;
        return new TMapPoint(
            Math.Round(point.X / Document.GridSize) * Document.GridSize,
            Math.Round(point.Y / Document.GridSize) * Document.GridSize);
    }

    private bool MoveSprite(TMapSprite sprite, double x, double y)
    {
        var point = Snap(new TMapPoint(x, y));
        if (NearlyEqual(sprite.X, point.X) && NearlyEqual(sprite.Y, point.Y)) return false;
        sprite.X = point.X;
        sprite.Y = point.Y;
        return true;
    }

    private bool MoveObject(TMapObject mapObject, double x, double y)
    {
        var point = Snap(new TMapPoint(x, y));
        if (NearlyEqual(mapObject.X, point.X) && NearlyEqual(mapObject.Y, point.Y)) return false;
        mapObject.X = point.X;
        mapObject.Y = point.Y;
        return true;
    }

    private bool IsInsideMap(TMapPoint point)
    {
        return point.X >= -Document.Width / 2 && point.X < Document.Width / 2 &&
               point.Y >= -Document.Height / 2 && point.Y < Document.Height / 2;
    }

    private void ToggleSelectedItem(object item)
    {
        var selection = _selectedItems.ToList();
        if (!selection.Remove(item)) selection.Add(item);
        SetSelectedItems(selection, selection.LastOrDefault());
    }

    private void SetSelectedItems(IEnumerable<object> items, object? primaryItem)
    {
        var selection = items.Distinct().ToList();
        if (_selectedItems.SequenceEqual(selection) && ReferenceEquals(_selectedItem, primaryItem)) return;
        _selectedItems.Clear();
        _selectedItems.AddRange(selection);
        _selectedItem = primaryItem;
        SelectedItemChanged?.Invoke(this, primaryItem);
        InvalidateVisual();
    }

    private void NotifyDocumentChanged()
    {
        InvalidateVisual();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyDocumentChanging()
    {
        DocumentChanging?.Invoke(this, EventArgs.Empty);
    }

    private static bool NearlyEqual(double left, double right)
    {
        return Math.Abs(left - right) < 0.0001;
    }
}
