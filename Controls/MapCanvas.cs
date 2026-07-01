using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TMapEditor.Models;
using TMapEditor.Services;

namespace TMapEditor.Controls;

public enum EditorTool
{
    Select,
    WalkBrush,
    BlockBrush,
    EraseBrush,
    CellZBrush,
    EraseCellZBrush
}

[Flags]
internal enum ResizeHandle
{
    None = 0,
    Left = 1,
    Right = 2,
    Bottom = 4,
    Top = 8
}

public sealed class MapCellHoverEventArgs(int? row, int? column) : EventArgs
{
    public int? Row { get; } = row;
    public int? Column { get; } = column;
    public bool IsInsideMap => Row.HasValue && Column.HasValue;
}

public sealed class MapCanvas : Control
{
    private readonly Dictionary<string, Bitmap> _bitmapCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<object> _selectedItems = [];
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
    private readonly ContextMenu _elementContextMenu;
    private KeyModifiers _lastKeyModifiers;
    private bool _isSpaceDown;
    private bool _activePointerEditChanged;

    public MapCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
        var deleteItem = new MenuItem { Header = "删除" };
        deleteItem.Click += (_, _) => DeleteSelected();
        _elementContextMenu = new ContextMenu { ItemsSource = new[] { deleteItem } };
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnCanvasDragOver, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnCanvasDrop, handledEventsToo: true);
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
                ? new Cursor(StandardCursorType.Arrow)
                : new Cursor(StandardCursorType.Cross);
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
            Width = bitmap.PixelSize.Width,
            Height = bitmap.PixelSize.Height,
            Order = Document.Sprites.Count == 0 ? 0 : Document.Sprites.Max(item => item.Order) + 1
        };
        Document.Sprites.Add(sprite);
        SelectedItem = sprite;
        NotifyDocumentChanged();
        return sprite;
    }

    public override void Render(DrawingContext dc)
    {
        base.Render(dc);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 32, 36)), null, new Rect(Bounds.Size));
        DrawMapBackground(dc);
        DrawImageLayers(dc);
        if (ShowCells) DrawCells(dc);
        if (ShowCellZs) DrawCellZs(dc);
        DrawObjectLayers(dc);
        DrawCellBrushPreview(dc);
        if (ShowGrid) DrawGrid(dc);
        if (ShowChunks) DrawChunks(dc);
        DrawSelection(dc);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClearBitmapCache();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var before = ScreenToMap(e.GetPosition(this));
        _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), 0.02, 8);
        var afterScreen = MapToScreen(before);
        _pan += e.GetPosition(this) - afterScreen;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        _lastScreenPoint = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        var updateKind = properties.PointerUpdateKind;
        _lastKeyModifiers = e.KeyModifiers;
        if (updateKind == PointerUpdateKind.MiddleButtonPressed ||
            (updateKind == PointerUpdateKind.LeftButtonPressed && _isSpaceDown))
        {
            _isPanning = true;
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.Hand);
            e.Handled = true;
            return;
        }

        if (updateKind == PointerUpdateKind.RightButtonPressed)
        {
            if (IsCellBrushTool())
            {
                e.Pointer.Capture(this);
                BeginRectangleCellBrush(ScreenToMap(_lastScreenPoint));
                e.Handled = true;
                return;
            }

            var contextMapPoint = ScreenToMap(_lastScreenPoint);
            var hit = HitTestItem(contextMapPoint, _lastScreenPoint);
            if (hit is not null)
            {
                if (!_selectedItems.Contains(hit)) SelectedItem = hit;
                _elementContextMenu.Open(this);
                e.Handled = true;
            }
            return;
        }

        if (updateKind != PointerUpdateKind.LeftButtonPressed) return;
        e.Pointer.Capture(this);
        var mapPoint = ScreenToMap(_lastScreenPoint);
        switch (Tool)
        {
            case EditorTool.WalkBrush:
            case EditorTool.BlockBrush:
            case EditorTool.EraseBrush:
            case EditorTool.CellZBrush:
            case EditorTool.EraseCellZBrush:
                BeginContinuousCellBrush(mapPoint);
                break;
            default:
                BeginSelectionOrDrag(mapPoint, _lastScreenPoint);
                break;
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var screenPoint = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        if (!_isPanning && !_isDragging && !_isResizing)
            UpdateHoveredCell(screenPoint);
        if (_isPanning)
        {
            _pan += screenPoint - _lastScreenPoint;
            _lastScreenPoint = screenPoint;
            InvalidateVisual();
            return;
        }

        if (_isContinuousBrushing && properties.IsLeftButtonPressed)
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

        if (_isRectangleBrushing && properties.IsRightButtonPressed)
        {
            var cell = GetMapCell(ScreenToMap(screenPoint));
            if (cell.HasValue && cell != _brushEndCell)
            {
                _brushEndCell = cell;
                InvalidateVisual();
            }
            return;
        }

        if (_isResizing && _resizeSprite is not null && properties.IsLeftButtonPressed)
        {
            if (ResizeSelectedSprite(ScreenToMap(screenPoint)))
            {
                _activePointerEditChanged = true;
                InvalidateVisual();
            }
            return;
        }

        if (!_isDragging || !properties.IsLeftButtonPressed || _dragStartMapPoint is null) return;
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

    protected override void OnPointerExited(PointerEventArgs e)
    {
        SetHoveredCell(null, null);
        base.OnPointerExited(e);
    }

    private void OnCanvasDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(TMapDragFormats.Resource) &&
            Document.Layers.Any(layer => layer.Name == DropTargetLayer) &&
            IsInsideMap(ScreenToMap(e.GetPosition(this))))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnCanvasDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(TMapDragFormats.Resource) is not TMapResource resource)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var point = ScreenToMap(e.GetPosition(this));
        e.DragEffects = AddResourceAt(resource, point) is null
            ? DragDropEffects.None
            : DragDropEffects.Copy;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var updateKind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (updateKind == PointerUpdateKind.LeftButtonReleased && _isContinuousBrushing)
        {
            CancelCellBrush();
            e.Pointer.Capture(null);
            NotifyDocumentChanged();
            e.Handled = true;
            return;
        }
        if (updateKind == PointerUpdateKind.RightButtonReleased && _isRectangleBrushing)
        {
            CommitRectangleCellBrush();
            e.Pointer.Capture(null);
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
            e.Pointer.Capture(null);
            Cursor = Tool == EditorTool.Select
                ? new Cursor(StandardCursorType.Arrow)
                : new Cursor(StandardCursorType.Cross);
            if (shouldNotifyDocumentChanged) NotifyDocumentChanged();
            else InvalidateVisual();
        }
        base.OnPointerReleased(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _isSpaceDown = true;
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            var brushChanged = _brushChanged;
            CancelCellBrush();
            if (brushChanged) NotifyDocumentChanged();
            else InvalidateVisual();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _isSpaceDown = false;
            e.Handled = true;
        }
        base.OnKeyUp(e);
    }

    private void DrawMapBackground(DrawingContext dc)
    {
        var topLeft = MapToScreen(new TMapPoint(-Document.Width / 2, Document.Height / 2));
        var rect = new Rect(topLeft.X, topLeft.Y, Document.Width * _zoom, Document.Height * _zoom);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(59, 61, 66)),
            new Pen(new SolidColorBrush(Color.FromRgb(130, 135, 145)), 1), rect);
    }

    private void DrawImageLayers(DrawingContext dc)
    {
        foreach (var layer in Document.Layers
                     .Where(layer => layer.Visible && layer.Type == TMapLayerType.Image).Reverse())
        {
            foreach (var sprite in Document.Sprites.Where(sprite => sprite.Layer == layer.Name)
                         .OrderBy(sprite => sprite.Order))
                DrawSprite(dc, sprite);
        }
    }

    private void DrawObjectLayers(DrawingContext dc)
    {
        foreach (var layer in Document.Layers
                     .Where(layer => layer.Visible && layer.Type == TMapLayerType.Object).Reverse())
        {
            foreach (var sprite in Document.Sprites.Where(sprite => sprite.Layer == layer.Name)
                         .OrderBy(sprite => sprite.Z)
                         .ThenBy(sprite => sprite.Order))
                DrawSprite(dc, sprite);
            foreach (var mapObject in Document.Objects.Where(mapObject => mapObject.Layer == layer.Name)
                         .OrderBy(mapObject => mapObject.Z))
                DrawObject(dc, mapObject);
        }
    }

    private void DrawSprite(DrawingContext dc, TMapSprite sprite)
    {
        var bitmap = LoadBitmap(sprite.ImagePath);
        if (bitmap is null) return;
        var center = MapToScreen(new TMapPoint(sprite.X, sprite.Y));
        using (dc.PushTransform(Matrix.CreateTranslation(center.X, center.Y)))
        using (dc.PushTransform(Matrix.CreateRotation(-sprite.Rotation * Math.PI / 180)))
        using (dc.PushTransform(Matrix.CreateScale(sprite.ScaleX * _zoom, sprite.ScaleY * _zoom)))
        {
            var rect = new Rect(
                -sprite.AnchorX * sprite.Width,
                -(1 - sprite.AnchorY) * sprite.Height,
                sprite.Width,
                sprite.Height);
            dc.DrawImage(bitmap, rect);
        }
    }

    private void DrawCells(DrawingContext dc)
    {
        foreach (var cell in Document.Cells)
        {
            var rect = GetCellScreenRect(cell.Row, cell.Column);
            var fill = new SolidColorBrush(cell.State == TMapCellState.Walk
                ? Color.FromArgb(105, 0, 210, 75)
                : Color.FromArgb(105, 235, 55, 55));
            dc.DrawRectangle(fill, null, rect);
        }
    }

    private void DrawCellZs(DrawingContext dc)
    {
        foreach (var cell in Document.CellZs)
        {
            var rect = GetCellScreenRect(cell.Row, cell.Column);
            var color = cell.Z > 0
                ? Color.FromArgb(75, 30, 150, 255)
                : Color.FromArgb(75, 180, 80, 230);
            dc.DrawRectangle(new SolidColorBrush(color), new Pen(Brushes.DeepSkyBlue, 1), rect);
            if (_zoom * Document.GridSize < 18) continue;
            var text = CreateText(cell.Z.ToString(), Math.Clamp(_zoom * Document.GridSize * 0.4, 10, 18));
            dc.DrawText(text, new Point(
                rect.Center.X - text.Width / 2,
                rect.Center.Y - text.Height / 2));
        }
    }

    private void DrawObject(DrawingContext dc, TMapObject mapObject)
    {
        var point = MapToScreen(new TMapPoint(mapObject.X, mapObject.Y));
        var brush = new SolidColorBrush(ParseDisplayColor(mapObject.DisplayColor));
        var outline = _selectedItems.Contains(mapObject) ? Brushes.Yellow : Brushes.White;
        dc.DrawEllipse(brush, new Pen(outline, _selectedItems.Contains(mapObject) ? 2 : 1), point, 6, 6);
        dc.DrawText(CreateText(mapObject.Label, 12), new Point(point.X + 8, point.Y - 17));
    }

    private static Color ParseDisplayColor(string? value)
    {
        try
        {
            return Color.Parse(value ?? "#00BFFF");
        }
        catch (FormatException)
        {
            return Color.FromRgb(0, 191, 255);
        }
    }

    private void DrawCellBrushPreview(DrawingContext dc)
    {
        if (!_isRectangleBrushing || !_brushStartCell.HasValue || !_brushEndCell.HasValue) return;
        var points = GetBrushRectanglePoints(_brushStartCell.Value, _brushEndCell.Value);
        var brush = _activeBrushTool switch
        {
            EditorTool.WalkBrush => Brushes.LimeGreen,
            EditorTool.BlockBrush => Brushes.OrangeRed,
            EditorTool.CellZBrush => Brushes.DeepSkyBlue,
            EditorTool.EraseCellZBrush => Brushes.MediumPurple,
            _ => Brushes.WhiteSmoke,
        };
        var pen = new Pen(brush, 3);
        dc.DrawGeometry(null, pen, CreatePolygonGeometry(points, true));
    }

    private void DrawGrid(DrawingContext dc)
    {
        if (Document.GridSize <= 0 || _zoom * Document.GridSize < 4) return;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)), 1);
        var minX = -Document.Width / 2;
        var minY = -Document.Height / 2;
        var columns = (int)Math.Ceiling(Document.Width / Document.GridSize);
        var rows = (int)Math.Ceiling(Document.Height / Document.GridSize);
        for (var col = 0; col <= columns; col++)
        {
            var x = Math.Min(Document.Width / 2, minX + col * Document.GridSize);
            dc.DrawLine(pen, MapToScreen(new TMapPoint(x, minY)), MapToScreen(new TMapPoint(x, Document.Height / 2)));
        }
        for (var row = 0; row <= rows; row++)
        {
            var y = Math.Min(Document.Height / 2, minY + row * Document.GridSize);
            dc.DrawLine(pen, MapToScreen(new TMapPoint(minX, y)), MapToScreen(new TMapPoint(Document.Width / 2, y)));
        }
    }

    private void DrawChunks(DrawingContext dc)
    {
        if (Document.ChunkColumns <= 0 || Document.ChunkRows <= 0) return;
        var pen = new Pen(Brushes.Gold, 2);
        var minX = -Document.Width / 2;
        var minY = -Document.Height / 2;
        for (var col = 0; col <= Document.ChunkColumns; col++)
        {
            var x = minX + col * Document.Width / Document.ChunkColumns;
            dc.DrawLine(pen, MapToScreen(new TMapPoint(x, minY)), MapToScreen(new TMapPoint(x, Document.Height / 2)));
        }
        for (var row = 0; row <= Document.ChunkRows; row++)
        {
            var y = minY + row * Document.Height / Document.ChunkRows;
            dc.DrawLine(pen, MapToScreen(new TMapPoint(minX, y)), MapToScreen(new TMapPoint(Document.Width / 2, y)));
        }
    }

    private void DrawSelection(DrawingContext dc)
    {
        foreach (var sprite in _selectedItems.OfType<TMapSprite>())
        {
            var corners = GetSpriteCorners(sprite).Select(MapToScreen).ToList();
            var geometry = CreateScreenPolygonGeometry(corners, true);
            dc.DrawGeometry(null, new Pen(Brushes.Cyan, 2), geometry);
            DrawResizeHandles(dc, corners);
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

    private Rect GetCellScreenRect(int row, int column)
    {
        var originX = -Document.Width / 2;
        var originY = -Document.Height / 2;
        var left = originX + column * Document.GridSize;
        var right = Math.Min(Document.Width / 2, left + Document.GridSize);
        var bottom = originY + row * Document.GridSize;
        var top = Math.Min(Document.Height / 2, bottom + Document.GridSize);
        var topLeft = MapToScreen(new TMapPoint(left, top));
        return new Rect(topLeft.X, topLeft.Y, (right - left) * _zoom, (top - bottom) * _zoom);
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

    private void BeginSelectionOrDrag(TMapPoint mapPoint, Point screenPoint)
    {
        var extendSelection = _lastKeyModifiers.HasFlag(KeyModifiers.Control);
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

    private StreamGeometry CreatePolygonGeometry(IReadOnlyList<TMapPoint> points, bool close)
    {
        return CreateScreenPolygonGeometry(points.Select(MapToScreen).ToList(), close);
    }

    private static StreamGeometry CreateScreenPolygonGeometry(IReadOnlyList<Point> points, bool close)
    {
        var geometry = new StreamGeometry();
        if (points.Count == 0) return geometry;
        using var context = geometry.Open();
        context.BeginFigure(points[0], close);
        foreach (var point in points.Skip(1))
        {
            context.LineTo(point);
        }
        if (close) context.EndFigure(true);
        return geometry;
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

    private void DrawResizeHandles(DrawingContext dc, IReadOnlyList<Point> corners)
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
        foreach (var point in handles)
        {
            dc.DrawRectangle(Brushes.White, new Pen(Brushes.Cyan, 1),
                new Rect(point.X - 4, point.Y - 4, 8, 8));
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

    private Bitmap? LoadBitmap(string imagePath)
    {
        try
        {
            var fullPath = TMapFileService.ResolveImagePath(Document, imagePath);
            if (_bitmapCache.TryGetValue(fullPath, out var cached)) return cached;
            if (!File.Exists(fullPath)) return null;
            var bitmap = new Bitmap(fullPath);
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

    private static FormattedText CreateText(string text, double size)
    {
        return new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Brushes.White);
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
