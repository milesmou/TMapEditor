using System.Globalization;
using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using SkiaSharp;
using TMapEditor.Controls;
using TMapEditor.Models;
using TMapEditor.Services;
using static TMapEditor.Views.EditorPalette;
using static TMapEditor.Services.EditorValueConverter;
using static TMapEditor.Services.ResourcePathUtility;

namespace TMapEditor;

public sealed class MainWindow : Window
{
    private readonly EditorSettings _settings;
    private readonly DispatcherTimer _objectColorCommitTimer = new(TimeSpan.FromMilliseconds(250));
    private readonly ContextMenu _resourceContextMenu = new();
    private readonly ContextMenu _entityContextMenu = new();

    private TMapDocument _document = new();
    private bool _dirty;
    private bool _synchronizingSelection;
    private bool _updatingSelectionProperties;
    private bool _updatingViewOptions;
    private bool _updatingLayerVisibility;
    private bool _restoringUndo;
    private bool _closingConfirmed;
    private string? _undoSnapshot;
    private string _currentSnapshot = "";
    private bool _objectColorChangePending;
    private string _renderBackend = "检测中";
    private double _resourcePreviewScale = 100;
    private TMapResource? _selectedResource;
    private ItemsView<object>? _entityView;

    private MenuItem _undoMenuItem = null!;
    private ListBox _layerList = null!;
    private ListBox _entityList = null!;
    private ComboBox _indexOriginCombo = null!;
    private ComboBox _spriteLayerCombo = null!;
    private ComboBox _objectLayerCombo = null!;
    private CheckBox _showGridCheck = null!;
    private CheckBox _showChunksCheck = null!;
    private CheckBox _showWaypointsCheck = null!;
    private CheckBox _showCellZCheck = null!;
    private CheckBox _snapCheck = null!;
    private TextBox _cellZText = null!;
    private TextBox _mapWidthText = null!;
    private TextBox _mapHeightText = null!;
    private TextBox _gridSizeText = null!;
    private TextBox _chunkRowsText = null!;
    private TextBox _chunkColumnsText = null!;
    private TextBlock _statusText = null!;
    private TextBlock _fileText = null!;
    private TextBlock _toolHintText = null!;
    private TextBlock _selectionTypeText = null!;
    private TextBlock _emptyPropertyHint = null!;
    private StackPanel _commonPropertyPanel = null!;
    private StackPanel _spritePropertyPanel = null!;
    private StackPanel _objectPropertyPanel = null!;
    private StackPanel _spriteZPanel = null!;
    private TextBox _itemNameText = null!;
    private TextBox _spriteImagePathText = null!;
    private TextBox _spriteXText = null!;
    private TextBox _spriteYText = null!;
    private TextBox _spriteZText = null!;
    private TextBox _spriteWidthText = null!;
    private TextBox _spriteHeightText = null!;
    private TextBox _spriteRotationText = null!;
    private TextBox _spriteOrderText = null!;
    private TextBox _spriteScaleXText = null!;
    private TextBox _spriteScaleYText = null!;
    private TextBox _spriteAnchorXText = null!;
    private TextBox _spriteAnchorYText = null!;
    private TextBox _objectNoteText = null!;
    private TextBox _objectArgsText = null!;
    private TextBox _objectXText = null!;
    private TextBox _objectYText = null!;
    private TextBox _objectZText = null!;
    private ColorPicker _objectDisplayColorPicker = null!;
    private Slider _resourcePreviewScaleSlider = null!;
    private TextBlock _resourcePreviewScaleText = null!;
    private WrapPanel _resourceTiles = null!;
    private MapCanvas _editorCanvas = null!;
    private readonly Dictionary<EditorTool, Button> _toolButtons = [];
    private readonly Dictionary<TMapResource, Border> _resourceTileBorders = [];

    public MainWindow()
    {
        _settings = EditorSettingsService.Load();
        _resourcePreviewScale = Math.Clamp(_settings.ResourcePreviewScale, 50, 200);
        BuildWindow();
        _objectColorCommitTimer.Tick += FlushObjectColorChange;
        _editorCanvas.SelectedItemChanged += EditorCanvas_SelectedItemChanged;
        _editorCanvas.DocumentChanging += EditorCanvas_DocumentChanging;
        _editorCanvas.DocumentChanged += EditorCanvas_DocumentChanged;
        _editorCanvas.HoveredCellChanged += EditorCanvas_HoveredCellChanged;
        _resourceContextMenu.Item("删除资源", () => DeleteResource_Click());
        _entityContextMenu.Item("删除", () => Delete_Click());
        OpenLastProjectOrCreateDocument();
        Loaded += () =>
        {
            _renderBackend = GetRenderBackendName();
            UpdateWindowTitle();
            _editorCanvas.FitToView();
        };
    }

    public MapCanvas EditorCanvas => _editorCanvas;

    private void BuildWindow()
    {
        Title = "TMap Editor";
        Icon = IconSource.FromResource<MainWindow>("TMapEditor.icon.ico");
        WindowSize = WindowSize.Resizable(1560, 920, 1180, 700);
        Background = BackgroundBrush;
        StartupLocation = WindowStartupLocation.CenterScreen;
        Closing += Window_Closing;
        PreviewKeyDown += Window_KeyDown;
        PreviewKeyUp += e =>
        {
            if (e.Key == Key.Space && !IsTextInputFocused())
            {
                _editorCanvas.IsSpaceDown = false;
                e.Handled = true;
            }
        };

        var fileMenu = new Menu()
            .Item("新建", () => New_Click())
            .Item("打开...", () => Open_Click())
            .Separator()
            .Item("保存", () => Save_Click())
            .Item("另存为...", () => SaveAs_Click())
            .Separator()
            .Item("导出...", () => Export_Click())
            .Separator()
            .Item("退出", () => Exit_Click());

        _undoMenuItem = new MenuItem("撤销");
        _undoMenuItem.Click = Undo_Click;
        _undoMenuItem.IsEnabled = false;
        var editMenu = new Menu();
        editMenu.Items.Add(_undoMenuItem);
        editMenu.Separator()
            .Item("行进区域画刷", () => SelectTool(EditorTool.WalkBrush))
            .Item("阻挡区域画刷", () => SelectTool(EditorTool.BlockBrush))
            .Item("清除格子画刷", () => SelectTool(EditorTool.EraseBrush))
            .Separator()
            .Item("优化阻挡区域", () => OptimizeBlockedRegions_Click());

        var viewMenu = new Menu()
            .Item("适应窗口", () => Fit_Click());

        var menuBar = new MenuBar().Items(
            new MenuItem("文件(_F)").Menu(fileMenu),
            new MenuItem("编辑(_E)").Menu(editMenu),
            new MenuItem("视图(_V)").Menu(viewMenu));

        var toolbar = new Border
        {
            Background = ToolbarBrush,
            Padding = new Thickness(4, 3)
        };
        toolbar.Child = BuildToolbar();

        var statusBar = new Border
        {
            Background = StatusBrush,
            Padding = new Thickness(8, 4)
        };
        _statusText = new TextBlock { Text = "就绪", VerticalAlignment = VerticalAlignment.Center };
        _fileText = new TextBlock
        {
            Text = "未保存",
            Foreground = DimTextBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusBar.Child = new Grid().Columns("Auto,*,360").Children(
            _statusText,
            _toolHintText.Column(1),
            _fileText.Column(2));

        var mainArea = new DockPanel().LastChildFill().Children(
            menuBar.DockTop(),
            toolbar.DockTop(),
            statusBar.DockBottom(),
            BuildMainSplit());

        Content = mainArea;
        UpdateToolButtonAppearance(EditorTool.Select);
    }

    private StackPanel BuildToolbar()
    {
        _showGridCheck = new CheckBox { IsChecked = true, Margin = new Thickness(6, 0) }.Content("网格");
        _showChunksCheck = new CheckBox { Margin = new Thickness(6, 0) }.Content("Chunk");
        _showWaypointsCheck = new CheckBox { IsChecked = true, Margin = new Thickness(6, 0) }.Content("路点");
        _showCellZCheck = new CheckBox { IsChecked = true, Margin = new Thickness(6, 0) }.Content("格子 Z");
        _snapCheck = new CheckBox { Margin = new Thickness(6, 0) }.Content("吸附网格");
        foreach (var checkBox in new[] { _showGridCheck, _showChunksCheck, _showWaypointsCheck, _showCellZCheck, _snapCheck })
            checkBox.CheckedChanged += _ => ViewOption_Changed();

        _cellZText = new TextBox { Text = "1", Width = 55, Margin = new Thickness(4, 0, 4, 0) };
        _cellZText.TextChanged += CellZText_TextChanged;

        _toolHintText = new TextBlock
        {
            Text = "选择模式：左键移动，滚轮缩放，中键/空格拖动画布",
            Margin = new Thickness(12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4
        }.Children(
            ToolbarGroup(ToolButton("选择 / 移动", EditorTool.Select)),
            ToolbarGroup(
                _showGridCheck,
                _showChunksCheck,
                _showWaypointsCheck,
                _showCellZCheck,
                _snapCheck),
            ToolbarGroup(
                ToolButton("刷 Z", EditorTool.CellZBrush),
                new TextBlock { Text = "Z", Margin = new Thickness(6, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center },
                _cellZText,
                ToolButton("清除 Z", EditorTool.EraseCellZBrush)),
            ToolbarGroup(new Button().Content("适应窗口").OnClick(Fit_Click)));
    }

    private static Border ToolbarGroup(params UIElement[] children) => new()
    {
        Background = new Color(38, 41, 46),
        BorderBrush = BorderBrushColor,
        BorderThickness = 1,
        Padding = new Thickness(3, 2),
        Child = new StackPanel { Orientation = Orientation.Horizontal }.Children(children)
    };

    private Button ToolButton(string text, EditorTool tool)
    {
        var button = new Button().Content(text);
        button.Click += () => SelectTool(tool);
        _toolButtons[tool] = button;
        return button;
    }

    private void SelectTool(EditorTool tool)
    {
        if (tool == EditorTool.CellZBrush)
        {
            if (!int.TryParse(_cellZText.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var z) || z == 0)
            {
                StatusTextSet("格子 Z 必须是非零整数；Z=0 请使用“清除 Z”");
                _cellZText.Focus();
                return;
            }
            _editorCanvas.CellZBrushValue = z;
        }
        _editorCanvas.Tool = tool;
        UpdateToolButtonAppearance(tool);
        _toolHintText.Text = tool switch
        {
            EditorTool.WalkBrush => "行进区域画刷：按住左键连续刷，按住右键框选刷格子，Esc 中断",
            EditorTool.BlockBrush => "阻挡区域画刷：按住左键连续刷，按住右键框选刷格子，Esc 中断",
            EditorTool.EraseBrush => "清除格子画刷：按住左键连续清除，按住右键框选清除，Esc 中断",
            EditorTool.CellZBrush => $"格子 Z 画刷（Z={_editorCanvas.CellZBrushValue}）：按住左键连续刷，按住右键框选刷格子",
            EditorTool.EraseCellZBrush => "清除格子 Z：按住左键连续清除，按住右键框选清除，Esc 中断",
            _ => "选择模式：左键移动，滚轮缩放，中键/空格拖动画布"
        };
    }

    private void UpdateToolButtonAppearance(EditorTool selectedTool)
    {
        foreach (var (tool, button) in _toolButtons)
            button.Background = tool == selectedTool ? new Color(15, 111, 145) : ButtonBrush;
    }

    private void StatusTextSet(string text) => _statusText.Text = text;

    private SplitPanel BuildMainSplit()
    {
        var leftPanel = BuildLeftPanel();
        var canvasBorder = new Border
        {
            BorderBrush = BorderBrushColor,
            BorderThickness = 1
        };
        _editorCanvas = new MapCanvas();
        canvasBorder.Child = _editorCanvas;

        var innerSplit = new SplitPanel
        {
            Orientation = Orientation.Horizontal,
            First = canvasBorder,
            Second = BuildRightPanel(),
            FirstLength = GridLength.Stars(1),
            SecondLength = GridLength.Pixels(320),
            SplitterThickness = 6
        };

        return new SplitPanel
        {
            Orientation = Orientation.Horizontal,
            First = leftPanel,
            Second = innerSplit,
            FirstLength = GridLength.Pixels(270),
            SecondLength = GridLength.Stars(1),
            SplitterThickness = 6
        };
    }

    private Border BuildLeftPanel()
    {
        var listSplit = new SplitPanel
        {
            Orientation = Orientation.Vertical,
            First = BuildLayersPanel(),
            Second = BuildEntitiesPanel(),
            FirstLength = GridLength.Stars(3),
            SecondLength = GridLength.Stars(2),
            SplitterThickness = 5
        };
        var panelSplit = new SplitPanel
        {
            Orientation = Orientation.Vertical,
            First = BuildSettingsPanel(),
            Second = listSplit,
            FirstLength = GridLength.Pixels(224),
            SecondLength = GridLength.Stars(1),
            SplitterThickness = 5
        };
        return new Border
        {
            Background = PanelBrush,
            Child = panelSplit
        };
    }

    private ScrollViewer BuildSettingsPanel()
    {
        _mapWidthText = new TextBox { Text = "4500" };
        _mapHeightText = new TextBox { Text = "4002" };
        _gridSizeText = new TextBox { Text = "32" };
        _chunkRowsText = new TextBox { Text = "3" };
        _chunkColumnsText = new TextBox { Text = "6" };
        _indexOriginCombo = new ComboBox().Items("左上", "左下");
        _indexOriginCombo.SelectedIndex = 0;
        _indexOriginCombo.SelectionChanged += _ => _editorCanvas.RefreshHoveredCell();

        var settings = new ScrollViewer
        {
            VerticalScroll = ScrollMode.Auto,
            Background = PanelBrush,
            HorizontalScroll = ScrollMode.Disabled
        };
        settings.Content = new StackPanel { Margin = new Thickness(8), Spacing = 6 }.Children(
            new TextBlock { Text = "地图设置", FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 6) },
            new UniformGrid { Columns = 2 }.Children(
                LabeledField("宽度", _mapWidthText),
                LabeledField("高度", _mapHeightText),
                LabeledField("索引原点", _indexOriginCombo),
                LabeledField("网格尺寸", _gridSizeText),
                LabeledField("Chunk 行", _chunkRowsText),
                LabeledField("Chunk 列", _chunkColumnsText)),
            new Button { Margin = new Thickness(3, 6, 3, 3) }.Content("应用地图设置").OnClick(() => ApplyDocument_Click()));
        return settings;
    }

    private static StackPanel LabeledField(string label, UIElement editor)
    {
        return new StackPanel { Spacing = 2 }.Children(
            new TextBlock { Text = label, Margin = new Thickness(3, 0) },
            editor);
    }

    private Grid BuildLayersPanel()
    {
        _layerList = new ListBox { Background = BackgroundBrush, Margin = new Thickness(8, 0, 8, 4) };
        _layerList.ItemTemplate = new DelegateTemplate<TMapLayer>(
            build: ctx =>
            {
                var checkBox = new CheckBox().Register(ctx, "Visible");
                var iconText = new TextBlock().Register(ctx, "TypeIcon");
                var nameText = new TextBlock().Register(ctx, "Name");
                checkBox.CheckedChanged += _ =>
                {
                    if (checkBox.Tag is not TMapLayer layer || _updatingLayerVisibility) return;
                    var visible = checkBox.IsChecked == true;
                    if (layer.Visible == visible) return;
                    layer.Visible = visible;
                    LayerVisibility_Changed(layer);
                };
                return new Grid().Columns("Auto,Auto,*").Children(checkBox, iconText, nameText);
            },
            bind: (view, layer, _, ctx) =>
            {
                var checkBox = ctx.Get<CheckBox>("Visible");
                checkBox.Tag = layer;
                _updatingLayerVisibility = true;
                checkBox.IsChecked = layer.Visible;
                _updatingLayerVisibility = false;
                ctx.Get<TextBlock>("TypeIcon").Text = layer.TypeIcon;
                ctx.Get<TextBlock>("Name").Text = layer.Name;
            },
            unbind: null);
        _layerList.SelectionChanged += o => LayerList_SelectionChanged(o as TMapLayer);

        var grid = new Grid().Rows("Auto,Auto,*,Auto,Auto");
        grid.Add(new Border { Height = 1, Background = BorderBrushColor, Margin = new Thickness(0, 0, 0, 6) }.Row(0));
        grid.Add(new TextBlock { Text = "地图层级", FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 2, 4, 6) }.Row(1));
        grid.Add(_layerList.Row(2));
        grid.Add(new UniformGrid { Columns = 3 }.Children(
            new Button().Content("新增").OnClick(() => AddLayer_Click()),
            new Button().Content("重命名").OnClick(() => RenameLayer_Click()),
            new Button().Content("删除").OnClick(() => DeleteLayer_Click())).Row(3));
        grid.Add(new UniformGrid { Columns = 2 }.Children(
            new Button().Content("上移（靠前）").OnClick(() => MoveLayerUp_Click()),
            new Button().Content("下移（靠后）").OnClick(() => MoveLayerDown_Click())).Row(4));
        return grid;
    }

    private Grid BuildEntitiesPanel()
    {
        _entityList = new ListBox
        {
            Background = BackgroundBrush,
            SelectionMode = ItemsSelectionMode.Extended,
            Margin = new Thickness(8, 0, 8, 8)
        };
        _entityList.ItemTemplate = new DelegateTemplate<object>(
            build: ctx =>
            {
                var nameText = new TextBlock
                {
                    FontSize = 13,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                }.Register(ctx, "DisplayName");
                var lockButton = new ToggleButton
                {
                    Width = 22,
                    Height = 20,
                    Padding = new Thickness(0),
                    Margin = new Thickness(4, 0, 0, 0),
                    FontSize = 11
                }.Register(ctx, "Lock");
                lockButton.CheckedChanged += isChecked =>
                {
                    if (lockButton.Tag is ILockableDisplayItem item && !_synchronizingSelection)
                        EntityLock_Click(item, isChecked);
                };
                return new Grid().Columns("*,Auto").Children(nameText, lockButton);
            },
            bind: (view, item, _, ctx) =>
            {
                var lockButton = ctx.Get<ToggleButton>("Lock");
                lockButton.Tag = item;
                if (item is ILockableDisplayItem lockable)
                {
                    _synchronizingSelection = true;
                    lockButton.IsChecked = lockable.IsLocked;
                    _synchronizingSelection = false;
                    lockButton.Content(lockable.LockIcon);
                }
                ctx.Get<TextBlock>("DisplayName").Text = item is IDisplayItem display ? display.DisplayName : item.ToString() ?? "";
            },
            unbind: null);
        _entityList.SelectedIndicesChanged += EntityList_SelectedIndicesChanged;
        _entityList.MouseDown += EntityList_MouseDown;
        _entityList.ContextMenu = _entityContextMenu;

        var grid = new Grid().Rows("Auto,Auto,*");
        grid.Add(new Border { Height = 1, Background = BorderBrushColor, Margin = new Thickness(0, 0, 0, 6) }.Row(0));
        grid.Add(new TextBlock { Text = "元素", FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 2, 4, 6) }.Row(1));
        grid.Add(_entityList.Row(2));
        return grid;
    }

    private SplitPanel BuildRightPanel()
    {
        return new SplitPanel
        {
            Orientation = Orientation.Vertical,
            First = BuildPropertiesPanel(),
            Second = BuildResourcesPanel(),
            FirstLength = GridLength.Stars(1),
            SecondLength = GridLength.Stars(1),
            SplitterThickness = 6
        };
    }

    private ScrollViewer BuildPropertiesPanel()
    {
        _selectionTypeText = new TextBlock { Text = "未选择", Foreground = DimTextBrush, Margin = new Thickness(2, 0, 2, 10) };
        _emptyPropertyHint = new TextBlock
        {
            Text = "在画布或元素列表中选择一个元素，\n即可在这里查看和编辑属性。",
            Foreground = DimTextBrush,
            Margin = new Thickness(2, 12),
            TextAlignment = TextAlignment.Center
        };

        _itemNameText = new TextBox();
        _commonPropertyPanel = new StackPanel { IsVisible = false, Spacing = 4 }.Children(
            new TextBlock { Text = "名称" },
            _itemNameText);

        _spriteLayerCombo = new ComboBox();
        _spriteLayerCombo.SelectionChanged += _ => SelectionProperty_SelectionChanged();
        _spriteImagePathText = new TextBox { IsReadOnly = true };
        _spriteXText = new TextBox();
        _spriteYText = new TextBox();
        _spriteZText = new TextBox();
        _spriteWidthText = new TextBox();
        _spriteHeightText = new TextBox();
        _spriteRotationText = new TextBox();
        _spriteOrderText = new TextBox();
        _spriteScaleXText = new TextBox();
        _spriteScaleYText = new TextBox();
        _spriteAnchorXText = new TextBox();
        _spriteAnchorYText = new TextBox();
        _spriteZPanel = new StackPanel { IsVisible = false }.Children(
            new TextBlock { Text = "Z" },
            _spriteZText);

        _spritePropertyPanel = new StackPanel { IsVisible = false, Spacing = 6 }.Children(
            new TextBlock { Text = "图层" },
            _spriteLayerCombo,
            new TextBlock { Text = "图片路径" },
            _spriteImagePathText,
            new UniformGrid { Columns = 2 }.Children(
                LabeledField("X", _spriteXText),
                LabeledField("Y", _spriteYText),
                _spriteZPanel,
                LabeledField("宽度", _spriteWidthText),
                LabeledField("高度", _spriteHeightText),
                LabeledField("旋转°", _spriteRotationText),
                LabeledField("顺序", _spriteOrderText),
                LabeledField("Scale X", _spriteScaleXText),
                LabeledField("Scale Y", _spriteScaleYText),
                LabeledField("Anchor X", _spriteAnchorXText),
                LabeledField("Anchor Y", _spriteAnchorYText)),
            new Button { Margin = new Thickness(3, 6, 3, 3) }.Content("重置属性").OnClick(() => ResetSpriteProperties_Click()));

        _objectLayerCombo = new ComboBox();
        _objectLayerCombo.SelectionChanged += _ => SelectionProperty_SelectionChanged();
        _objectNoteText = new TextBox();
        _objectArgsText = new TextBox();
        _objectDisplayColorPicker = new ColorPicker
        {
            SelectedColor = new Color(0, 191, 255),
            ShowAlpha = false,
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _objectDisplayColorPicker.SelectedColorChanged += ObjectDisplayColorPicker_ColorChanged;
        _objectXText = new TextBox();
        _objectYText = new TextBox();
        _objectZText = new TextBox();

        _objectPropertyPanel = new StackPanel { IsVisible = false, Spacing = 6 }.Children(
            new TextBlock { Text = "图层" },
            _objectLayerCombo,
            new TextBlock { Text = "备注" },
            _objectNoteText,
            new TextBlock { Text = "Args" },
            _objectArgsText,
            new TextBlock { Text = "显示颜色" },
            _objectDisplayColorPicker,
            new UniformGrid { Columns = 2 }.Children(
                LabeledField("X", _objectXText),
                LabeledField("Y", _objectYText),
                LabeledField("Z", _objectZText)));

        foreach (var textBox in new[]
                 {
                     _itemNameText, _spriteXText, _spriteYText, _spriteZText, _spriteWidthText,
                     _spriteHeightText, _spriteRotationText, _spriteOrderText, _spriteScaleXText,
                     _spriteScaleYText, _spriteAnchorXText, _spriteAnchorYText, _objectNoteText,
                     _objectArgsText, _objectXText, _objectYText, _objectZText
                 })
        {
            textBox.LostFocus += () =>
            {
                if (!_updatingSelectionProperties) ApplySelectionProperties(deferEntityRefresh: true);
            };
            textBox.KeyDown += e =>
            {
                if (!_updatingSelectionProperties && e.Key == Key.Enter)
                {
                    ApplySelectionProperties();
                    e.Handled = true;
                }
            };
        }

        var properties = new ScrollViewer { VerticalScroll = ScrollMode.Auto, Background = PanelBrush };
        properties.Content = new StackPanel { Margin = new Thickness(10), Spacing = 6 }.Children(
            new TextBlock { Text = "属性", FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(2, 6) },
            _selectionTypeText,
            _emptyPropertyHint,
            _commonPropertyPanel,
            _spritePropertyPanel,
            _objectPropertyPanel);
        return properties;
    }

    private Border BuildResourcesPanel()
    {
        _resourcePreviewScaleSlider = new Slider
        {
            Minimum = 50,
            Maximum = 200,
            Value = _resourcePreviewScale,
            VerticalAlignment = VerticalAlignment.Center
        };
        _resourcePreviewScaleSlider.ValueChanged += ResourcePreviewScaleSlider_ValueChanged;
        _resourcePreviewScaleText = new TextBlock
        {
            Text = $"{_resourcePreviewScale:0}%",
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        _resourceTiles = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4
        };

        var header = new Grid().Columns("*,Auto").Children(
            new TextBlock { Text = "资源", FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
            new Button { Margin = new Thickness(4, 0) }.Content("导入图片...").OnClick(() => ImportResources_Click()).Column(1));

        var scaleRow = new Grid().Columns("Auto,*,44").Children(
            new TextBlock { Text = "预览缩放", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) },
            _resourcePreviewScaleSlider.Column(1),
            _resourcePreviewScaleText.Column(2));

        var hint = new TextBlock
        {
            Text = "将缩略图拖到场景中创建图片元素",
            Foreground = DimTextBrush,
            Margin = new Thickness(2, 6, 2, 0)
        };

        var tileScroll = new ScrollViewer { VerticalScroll = ScrollMode.Auto, HorizontalScroll = ScrollMode.Disabled };
        tileScroll.Content = _resourceTiles;

        var panel = new DockPanel { Margin = new Thickness(8) };
        panel.Add(header.DockTop());
        panel.Add(scaleRow.DockTop());
        panel.Add(hint.DockBottom());
        panel.Add(tileScroll);
        return new Border
        {
            BorderBrush = BorderBrushColor,
            NonUniformBorderThickness = new Thickness(1, 1, 0, 0),
            Background = new Color(32, 35, 40),
            Child = panel
        };
    }

    private void ResourcePreviewScaleSlider_ValueChanged(double value)
    {
        _resourcePreviewScale = value;
        _resourcePreviewScaleText.Text = $"{value:0}%";
        RefreshResourceList();
    }

    private void OpenLastProjectOrCreateDocument()
    {
        var lastProjectPath = _settings.LastProjectPath;
        if (!string.IsNullOrWhiteSpace(lastProjectPath) && File.Exists(lastProjectPath))
        {
            try
            {
                SetDocument(TMapFileService.Load(lastProjectPath));
                StatusTextSet("已自动打开上次工程");
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
        _editorCanvas.Document = document;
        RefreshLayerControls(document.Layers.FirstOrDefault());
        RefreshResourceList();
        _mapWidthText.Text = Format(document.Width);
        _mapHeightText.Text = Format(document.Height);
        _gridSizeText.Text = Format(document.GridSize);
        _chunkRowsText.Text = document.ChunkRows.ToString(CultureInfo.InvariantCulture);
        _chunkColumnsText.Text = document.ChunkColumns.ToString(CultureInfo.InvariantCulture);
        _indexOriginCombo.SelectedIndex = document.IndexOrigin == TMapIndexOrigin.LeftBottom ? 1 : 0;
        RestoreViewOptions(document.ViewSettings);
        RefreshEntityList();
        UpdateSelectionProperties(null);
        ResetUndoState();
        SetDirty(false);
        _fileText.Text = document.FilePath ?? "未保存";
    }

    private async void New_Click()
    {
        if (!await ConfirmDiscardOrSave()) return;
        SetDocument(new TMapDocument());
        _editorCanvas.FitToView();
        StatusTextSet("已新建地图");
    }

    private async void Open_Click()
    {
        if (!await ConfirmDiscardOrSave()) return;
        var filePath = await FileDialog.OpenFileAsync(new OpenFileDialogOptions
        {
            Owner = this,
            Title = "打开 TMap",
            Filters =
            [
                new FileFilter("TMap 地图", "*.tmap"),
                new FileFilter("JSON 文件", "*.json"),
                new FileFilter("所有文件", "*.*")
            ]
        });
        if (filePath is null) return;
        try
        {
            SetDocument(TMapFileService.Load(filePath));
            RememberCurrentProject();
            _editorCanvas.FitToView();
            StatusTextSet("地图已打开");
        }
        catch (Exception exception)
        {
            await ShowError("打开失败", exception);
        }
    }

    private async void Save_Click() => await SaveDocument(false);

    private async void SaveAs_Click() => await SaveDocument(true);

    private async Task<bool> SaveDocument(bool saveAs)
    {
        FlushObjectColorChange();
        var filePath = _document.FilePath;
        if (saveAs || filePath is null)
        {
            var result = await FileDialog.SaveFileAsync(new SaveFileDialogOptions
            {
                Owner = this,
                Title = "保存 TMap",
                FileName = GetDefaultMapFileName(),
                DefaultExtension = "tmap",
                OverwritePrompt = true,
                Filters = [new FileFilter("TMap 地图", "*.tmap")]
            });
            filePath = result;
            if (filePath is null) return false;
        }

        try
        {
            SaveDocumentToPath(filePath);
            StatusTextSet("地图已保存");
            return true;
        }
        catch (Exception exception)
        {
            await ShowError("保存失败", exception);
            return false;
        }
    }

    private async void ImportResources_Click()
    {
        if (_document.FilePath is null && !await SaveDocument(false)) return;
        var filePaths = await FileDialog.OpenFilesAsync(new OpenFileDialogOptions
        {
            Owner = this,
            Title = "导入图片资源到 TMap 工程",
            Multiselect = true,
            Filters =
            [
                new FileFilter("图片文件", "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"),
                new FileFilter("所有文件", "*.*")
            ]
        });
        if (filePaths is null || filePaths.Length == 0) return;

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
            StatusTextSet($"已导入 {filePaths.Length} 个工程资源");
        }
        catch (Exception exception)
        {
            await ShowError("资源导入失败", exception);
        }
    }

    private async void Export_Click()
    {
        if (!await ApplyDocumentSettings(showErrors: false)) return;
        if (!await SaveDocument(false)) return;
        var folderPath = await FileDialog.SelectFolderAsync(new FolderDialogOptions
        {
            Owner = this,
            Title = "选择地图烘焙输出目录",
            InitialDirectory = string.IsNullOrWhiteSpace(_settings.LastExportDirectory)
                ? null
                : _settings.LastExportDirectory
        });
        if (folderPath is null) return;
        _settings.LastExportDirectory = Path.GetFullPath(folderPath);
        EditorSettingsService.Save(_settings);
        try
        {
            Cursor = CursorType.Wait;
            StatusTextSet("正在烘焙导出...");
            var exportDocument = CloneDocumentForExport();
            using var gpuContext = SkiaGpuContext.TryCreate();
            var result = await Task.Run(() =>
                TMapExporter.Export(exportDocument, folderPath, gpuContext, false));
            var renderer = result.HardwareAccelerated ? "GPU" : "CPU 回退";
            StatusTextSet($"导出完成：{result.ChunkCount} chunks，{result.WalkableCount} 可行走格，" +
                          $"{result.BlockedCount} 阻挡格，{result.ObjectCount} 对象，" +
                          $"{result.DynamicImageCount} 动态图片，{renderer}");
            await ShowMessage("TMap Editor",
                $"地图导出完成。\n\nChunk：{result.ChunkCount}\n可行走格：{result.WalkableCount}\n" +
                $"阻挡格：{result.BlockedCount}\n对象：{result.ObjectCount}\n" +
                $"动态图片：{result.DynamicImageCount}\n渲染：{renderer}",
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

    private TMapDocument CloneDocumentForExport()
    {
        var json = JsonSerializer.Serialize(_document, TMapJsonContext.Default.TMapDocument);
        var clone = JsonSerializer.Deserialize(json, TMapJsonContext.Default.TMapDocument) ?? new TMapDocument();
        clone.FilePath = _document.FilePath;
        return clone;
    }

    private async void ApplyDocument_Click() => await ApplyDocumentSettings(showErrors: true);

    private async Task<bool> ApplyDocumentSettings(bool showErrors)
    {
        if (!TryDouble(_mapWidthText.Text, out var width) || width <= 0 ||
            !TryDouble(_mapHeightText.Text, out var height) || height <= 0 ||
            !TryDouble(_gridSizeText.Text, out var gridSize) || gridSize <= 0 ||
            !int.TryParse(_chunkRowsText.Text, out var chunkRows) || chunkRows <= 0 ||
            !int.TryParse(_chunkColumnsText.Text, out var chunkColumns) || chunkColumns <= 0)
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
        _document.IndexOrigin = _indexOriginCombo.SelectedIndex == 1
            ? TMapIndexOrigin.LeftBottom
            : TMapIndexOrigin.LeftTop;
        SetDirty(true);
        _editorCanvas.InvalidateVisual();
        StatusTextSet("地图设置已应用");
        return true;
    }

    private void ApplySelectionProperties(bool deferEntityRefresh = false)
    {
        try
        {
            switch (_editorCanvas.SelectedItem)
            {
                case TMapSprite sprite:
                    CaptureUndoSnapshot();
                    sprite.Name = RequiredName(_itemNameText.Text);
                    var selectedLayer = _spriteLayerCombo.SelectedItem as TMapLayer;
                    sprite.Layer = selectedLayer?.Name ?? sprite.Layer;
                    if (selectedLayer is not null && !ReferenceEquals(_layerList.SelectedItem, selectedLayer))
                        _layerList.SelectedItem = selectedLayer;
                    sprite.X = ParseDouble(_spriteXText.Text, "X");
                    sprite.Y = ParseDouble(_spriteYText.Text, "Y");
                    sprite.Width = PositiveDouble(_spriteWidthText.Text, "宽度");
                    sprite.Height = PositiveDouble(_spriteHeightText.Text, "高度");
                    sprite.Rotation = ParseDouble(_spriteRotationText.Text, "旋转");
                    sprite.ScaleX = ParseDouble(_spriteScaleXText.Text, "Scale X");
                    sprite.ScaleY = ParseDouble(_spriteScaleYText.Text, "Scale Y");
                    sprite.AnchorX = ParseDouble(_spriteAnchorXText.Text, "Anchor X");
                    sprite.AnchorY = ParseDouble(_spriteAnchorYText.Text, "Anchor Y");
                    sprite.Order = int.Parse(_spriteOrderText.Text, CultureInfo.InvariantCulture);
                    sprite.Z = int.Parse(_spriteZText.Text, CultureInfo.InvariantCulture);
                    _spriteZPanel.IsVisible = selectedLayer?.Type == TMapLayerType.Object;
                    break;
                case TMapObject mapObject:
                    CaptureUndoSnapshot();
                    mapObject.Name = RequiredName(_itemNameText.Text);
                    var selectedObjectLayer = _objectLayerCombo.SelectedItem as TMapLayer;
                    mapObject.Layer = selectedObjectLayer?.Name ?? mapObject.Layer;
                    if (selectedObjectLayer is not null && !ReferenceEquals(_layerList.SelectedItem, selectedObjectLayer))
                        _layerList.SelectedItem = selectedObjectLayer;
                    mapObject.Note = _objectNoteText.Text?.Trim() ?? "";
                    mapObject.Args = _objectArgsText.Text?.Trim() ?? "";
                    mapObject.DisplayColor = FormatDisplayColor(_objectDisplayColorPicker.SelectedColor);
                    mapObject.X = ParseDouble(_objectXText.Text, "X");
                    mapObject.Y = ParseDouble(_objectYText.Text, "Y");
                    mapObject.Z = int.Parse(_objectZText.Text, CultureInfo.InvariantCulture);
                    break;
                default:
                    return;
            }
            SetDirty(true);
            if (deferEntityRefresh)
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is not null)
                    dispatcher.BeginInvoke(DispatcherPriority.Background, RefreshEntityList);
                else
                    RefreshEntityList();
            }
            else
            {
                RefreshEntityList();
            }
            _editorCanvas.InvalidateVisual();
            StatusTextSet("属性已自动应用");
        }
        catch (Exception exception)
        {
            _ = ShowMessage("输入错误", exception.Message, ["确定"]);
        }
    }

    private void SelectionProperty_SelectionChanged()
    {
        if (_updatingSelectionProperties) return;
        ApplySelectionProperties();
    }

    private void ObjectDisplayColorPicker_ColorChanged(Color color)
    {
        if (_updatingSelectionProperties || _editorCanvas.SelectedItem is not TMapObject mapObject)
            return;
        var formatted = FormatDisplayColor(color);
        if (mapObject.DisplayColor == formatted) return;
        if (!_objectColorChangePending)
        {
            CaptureUndoSnapshot();
            _objectColorChangePending = true;
        }
        mapObject.DisplayColor = formatted;
        _objectColorCommitTimer.Stop();
        _objectColorCommitTimer.Start();
        StatusTextSet("对象点颜色已更新");
    }

    private void FlushObjectColorChange()
    {
        _objectColorCommitTimer.Stop();
        if (!_objectColorChangePending) return;
        _objectColorChangePending = false;
        SetDirty(true);
        _editorCanvas.InvalidateVisual();
    }

    private async void ResetSpriteProperties_Click()
    {
        if (_editorCanvas.SelectedItem is not TMapSprite sprite) return;
        try
        {
            var bitmap = LoadSpriteBitmap(sprite);
            CaptureUndoSnapshot();
            sprite.Width = bitmap.Width;
            sprite.Height = bitmap.Height;
            sprite.Rotation = 0;
            sprite.ScaleX = 1;
            sprite.ScaleY = 1;
            sprite.AnchorX = 0.5;
            sprite.AnchorY = 0.5;
            UpdateSelectionProperties(sprite);
            SetDirty(true);
            _editorCanvas.InvalidateVisual();
            StatusTextSet("图片元素属性已重置");
        }
        catch (Exception exception)
        {
            await ShowError("重置属性失败", exception);
        }
    }

    private void CellZText_TextChanged(string text)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var z) || z == 0)
            return;
        _editorCanvas.CellZBrushValue = z;
        if (_editorCanvas.Tool == EditorTool.CellZBrush)
            _toolHintText.Text = $"格子 Z 画刷（Z={z}）：按住左键连续刷，按住右键框选刷格子";
    }

    private async void OptimizeBlockedRegions_Click()
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
                StatusTextSet("地图区域已全部标记");
                return;
            }

            _undoSnapshot = snapshot;
            RefreshCurrentSnapshot();
            UpdateUndoMenu();
            SetDirty(true);
            _editorCanvas.InvalidateVisual();
            StatusTextSet($"阻挡区域优化完成：新增 {result.AddedBlockedCells} 个阻挡格，" +
                          $"{result.AddedWalkableCells} 个行进格");
        }
        catch (Exception exception)
        {
            await ShowError("优化阻挡区域失败", exception);
        }
    }

    private void ViewOption_Changed()
    {
        if (_updatingViewOptions) return;
        CaptureUndoSnapshot();
        _document.ViewSettings.ShowGrid = _showGridCheck.IsChecked == true;
        _document.ViewSettings.ShowChunks = _showChunksCheck.IsChecked == true;
        _document.ViewSettings.ShowWaypoints = _showWaypointsCheck.IsChecked == true;
        _document.ViewSettings.ShowCellZs = _showCellZCheck.IsChecked == true;
        _document.ViewSettings.SnapToGrid = _snapCheck.IsChecked == true;
        _editorCanvas.ShowGrid = _showGridCheck.IsChecked == true;
        _editorCanvas.ShowChunks = _showChunksCheck.IsChecked == true;
        _editorCanvas.ShowCells = _showWaypointsCheck.IsChecked == true;
        _editorCanvas.ShowCellZs = _showCellZCheck.IsChecked == true;
        _editorCanvas.SnapToGrid = _snapCheck.IsChecked == true;
        _editorCanvas.InvalidateVisual();
        SetDirty(true);
    }

    private void RestoreViewOptions(TMapViewSettings settings)
    {
        _updatingViewOptions = true;
        try
        {
            _showGridCheck.IsChecked = settings.ShowGrid;
            _showChunksCheck.IsChecked = settings.ShowChunks;
            _showWaypointsCheck.IsChecked = settings.ShowWaypoints;
            _showCellZCheck.IsChecked = settings.ShowCellZs;
            _snapCheck.IsChecked = settings.SnapToGrid;
            _editorCanvas.ShowGrid = settings.ShowGrid;
            _editorCanvas.ShowChunks = settings.ShowChunks;
            _editorCanvas.ShowCells = settings.ShowWaypoints;
            _editorCanvas.ShowCellZs = settings.ShowCellZs;
            _editorCanvas.SnapToGrid = settings.SnapToGrid;
            _editorCanvas.InvalidateVisual();
        }
        finally
        {
            _updatingViewOptions = false;
        }
    }

    private void LayerVisibility_Changed(TMapLayer layer)
    {
        CaptureUndoSnapshot();
        _editorCanvas.InvalidateVisual();
        SetDirty(true);
    }

    private void LayerList_SelectionChanged(TMapLayer? layer)
    {
        if (layer is not null)
        {
            _editorCanvas.DropTargetLayer = layer.Name;
            if (layer.Type == TMapLayerType.Object)
            {
                _editorCanvas.Tool = EditorTool.Select;
                _toolHintText.Text = "对象层：单击空白处添加对象点，也可从资源区拖入动态图片";
            }
            else if (_editorCanvas.Tool == EditorTool.Select)
            {
                _toolHintText.Text = "选择模式：左键移动，滚轮缩放，中键/空格拖动画布";
            }
        }
        else
        {
            _editorCanvas.DropTargetLayer = "";
        }
        var layerName = layer?.Name;
        _editorCanvas.SetSelectedItems(_editorCanvas.SelectedItems.Where(item =>
            item switch
            {
                TMapSprite sprite => sprite.Layer == layerName,
                TMapObject mapObject => mapObject.Layer == layerName,
                _ => false
            }));
        RefreshEntityList();
    }

    private void EntityList_MouseDown(MouseEventArgs e)
    {
        if (!e.RightButton) return;
        if (_entityList.TryGetItemIndexAt(e, out var index))
        {
            if (!_entityList.IsSelected(index))
            {
                _synchronizingSelection = true;
                _entityList.SelectRange(index, index);
                _synchronizingSelection = false;
                if (_entityView is not null &&
                    index >= 0 &&
                    index < _entityView.Count &&
                    _entityView.GetItem(index) is { } selected)
                {
                    _editorCanvas.SetSelectedItems([selected]);
                }
            }
        }
        else
        {
            e.Handled = true;
            return;
        }
        _entityContextMenu.ShowAt(_entityList, e.GetPosition(_entityList));
        e.Handled = true;
    }

    private void EntityList_SelectedIndicesChanged()
    {
        if (_synchronizingSelection) return;
        _editorCanvas.SetSelectedItems(_entityList.SelectedItems.OfType<object>());
    }

    private void EntityLock_Click(ILockableDisplayItem item, bool isLocked)
    {
        if (item.IsLocked == isLocked) return;
        CaptureUndoSnapshot();
        item.IsLocked = isLocked;
        if (item.IsLocked)
        {
            _editorCanvas.SetSelectedItems(_editorCanvas.SelectedItems.Where(selected =>
                !ReferenceEquals(selected, item)));
        }
        SetDirty(true);
        RefreshEntityList();
        StatusTextSet(item.IsLocked ? $"已锁定：{item.DisplayName}" : $"已解锁：{item.DisplayName}");
    }

    private async void AddLayer_Click()
    {
        var result = await PromptForLayer("新增层级", GetUniqueLayerName("Layer"), true);
        if (result is null) return;
        var (name, layerType) = result.Value;
        var typeName = layerType == TMapLayerType.Object ? "对象层" : "图片层";
        CaptureUndoSnapshot();
        var layer = new TMapLayer { Name = name, Type = layerType };
        _document.Layers.Insert(0, layer);
        RefreshLayerControls(layer);
        SetDirty(true);
        _editorCanvas.InvalidateVisual();
        StatusTextSet($"已新增{typeName}：{name}");
    }

    private async void RenameLayer_Click()
    {
        if (_layerList.SelectedItem is not TMapLayer layer)
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
        UpdateSelectionProperties(_editorCanvas.SelectedItem);
        SetDirty(true);
        _editorCanvas.InvalidateVisual();
        StatusTextSet($"图层已重命名：{oldName} → {name}");
    }

    private async void DeleteLayer_Click()
    {
        if (_layerList.SelectedItem is not TMapLayer layer)
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
        _editorCanvas.SetSelectedItems(_editorCanvas.SelectedItems.Except(sprites).Except(objects));
        foreach (var sprite in sprites) _document.Sprites.Remove(sprite);
        foreach (var mapObject in objects) _document.Objects.Remove(mapObject);
        _document.Layers.Remove(layer);
        var nextLayer = _document.Layers.Count == 0
            ? null
            : _document.Layers[Math.Min(oldIndex, _document.Layers.Count - 1)];
        RefreshLayerControls(nextLayer);
        RefreshEntityList();
        SetDirty(true);
        _editorCanvas.InvalidateVisual();
        StatusTextSet($"已删除图层：{layer.Name}");
    }

    private void MoveLayerUp_Click() => MoveSelectedLayer(-1);

    private void MoveLayerDown_Click() => MoveSelectedLayer(1);

    private void MoveSelectedLayer(int offset)
    {
        if (_layerList.SelectedItem is not TMapLayer layer) return;
        var oldIndex = _document.Layers.IndexOf(layer);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= _document.Layers.Count) return;

        CaptureUndoSnapshot();
        _document.Layers.RemoveAt(oldIndex);
        _document.Layers.Insert(newIndex, layer);
        RefreshLayerControls(layer);
        SetDirty(true);
        _editorCanvas.InvalidateVisual();
        StatusTextSet(offset < 0
            ? $"图层已上移：{layer.Name}"
            : $"图层已下移：{layer.Name}");
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
            var dialog = new LayerNameDialog(title, candidate, allowTypeSelection,
                excludedLayer?.Type ?? TMapLayerType.Image);
            await dialog.ShowDialogAsync(this);
            if (dialog.Result != true) return null;
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
        _layerList.ItemsSource = ItemsView.Create(_document.Layers, layer => layer.Name, layer => layer.Name);
        _updatingSelectionProperties = true;
        try
        {
            _spriteLayerCombo.ItemsSource = ItemsView.Create(_document.Layers, layer => layer.Name, layer => layer.Name);
            _spriteLayerCombo.SelectedItem = _editorCanvas.SelectedItem is TMapSprite sprite
                ? _document.Layers.FirstOrDefault(layer => layer.Name == sprite.Layer)
                : null;
            var objectLayers = _document.Layers.Where(layer => layer.Type == TMapLayerType.Object).ToList();
            _objectLayerCombo.ItemsSource = ItemsView.Create(objectLayers, layer => layer.Name, layer => layer.Name);
            _objectLayerCombo.SelectedItem = _editorCanvas.SelectedItem is TMapObject mapObject
                ? _document.Layers.FirstOrDefault(layer => layer.Name == mapObject.Layer)
                : null;
        }
        finally
        {
            _updatingSelectionProperties = false;
        }
        _layerList.SelectedItem = selection;
        _editorCanvas.DropTargetLayer = selection?.Name ?? "";
    }

    private void RefreshResourceList()
    {
        TMapFileService.RefreshResourcePaths(_document);
        _resourceTiles.Clear();
        _resourceTileBorders.Clear();
        if (_selectedResource is not null && !_document.Resources.Contains(_selectedResource))
            _selectedResource = null;
        var tileWidth = 104.0 * _resourcePreviewScale / 100.0;
        var tileHeight = 78.0 * _resourcePreviewScale / 100.0 + 22;
        _resourceTiles.ItemWidth = tileWidth + 12;
        _resourceTiles.ItemHeight = tileHeight;
        foreach (var resource in _document.Resources)
        {
            _resourceTiles.Add(BuildResourceTile(resource, tileWidth, tileHeight));
        }
    }

    private Border BuildResourceTile(TMapResource resource, double imageWidth, double imageHeight)
    {
        var nameText = new TextBlock
        {
            Text = resource.Name,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 4, 2, 0)
        };
        var imageHost = new Border
        {
            Background = new Color(21, 23, 26),
            BorderBrush = new Color(74, 79, 87),
            BorderThickness = 1,
            Width = imageWidth,
            Height = imageHeight
        };
        if (File.Exists(resource.ThumbnailPath))
        {
            imageHost.Child = new Image
            {
                Source = ImageSource.FromFile(resource.ThumbnailPath),
                StretchMode = Stretch.Uniform,
                Margin = new Thickness(3)
            };
        }
        var tile = new Border
        {
            Padding = new Thickness(5),
            Width = imageWidth + 10,
            BorderBrush = BorderBrushColor,
            BorderThickness = 1,
            ToolTip = new ToolTip { Content = new TextBlock { Text = resource.ImagePath } }
        };
        tile.Child = new StackPanel { Spacing = 0 }.Children(imageHost, nameText);
        tile.Tag = resource;
        _resourceTileBorders[resource] = tile;
        UpdateResourceTileAppearance(tile, resource);
        tile.MouseEnter += () => UpdateResourceTileAppearance(tile, resource, isHovered: true);
        tile.MouseLeave += () => UpdateResourceTileAppearance(tile, resource);
        tile.CanDrag = true;
        tile.DragStarting += e =>
        {
            if (tile.Tag is TMapResource dragged)
            {
                SelectResource(dragged);
                UpdateResourceTileAppearance(tile, dragged, isDragging: true);
                var data = new DataObject();
                data.SetData(TMapDragFormats.Resource, dragged);
                e.Data = data;
                e.AllowedEffects = DragDropEffects.Copy;
                e.Preview = new DragPreviewContent
                {
                    Element = new Border
                    {
                        Background = ResourceSelectedBrush,
                        BorderBrush = AccentBrush,
                        BorderThickness = 2,
                        Padding = new Thickness(12, 8),
                        Child = new TextBlock
                        {
                            Text = $"＋  {dragged.Name}",
                            Foreground = TextBrush,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        }
                    },
                    MaxWidth = 180,
                    Opacity = 0.9
                };
            }
        };
        tile.DragCompleted += _ => UpdateResourceTileAppearance(tile, resource);
        tile.MouseDown += e =>
        {
            if (e.LeftButton || e.RightButton)
                SelectResource(resource);
            if (e.RightButton)
            {
                _resourceContextMenu.ShowAt(tile, e.GetPosition(tile));
                e.Handled = true;
            }
        };
        return tile;
    }

    private void SelectResource(TMapResource resource)
    {
        _selectedResource = resource;
        foreach (var (item, tile) in _resourceTileBorders)
            UpdateResourceTileAppearance(tile, item);
        StatusTextSet($"已选择资源：{resource.Name}；拖到画布可创建图片元素");
    }

    private void UpdateResourceTileAppearance(
        Border tile,
        TMapResource resource,
        bool isHovered = false,
        bool isDragging = false)
    {
        var isSelected = ReferenceEquals(_selectedResource, resource);
        tile.Background = isDragging
            ? new Color(12, 60, 80)
            : isSelected ? ResourceSelectedBrush
            : isHovered ? ResourceHoverBrush : new Color(32, 35, 40);
        tile.BorderBrush = isSelected || isDragging ? AccentBrush : BorderBrushColor;
        tile.BorderThickness = isSelected || isDragging ? 2 : 1;
    }

    private async void DeleteResource_Click()
    {
        if (_selectedResource is null) return;
        var resource = _selectedResource;
        _selectedResource = null;
        var resourcePath = TMapFileService.ResolveImagePath(_document, resource.ImagePath);
        var usageCount = _document.Sprites.Count(sprite =>
            string.Equals(TMapFileService.ResolveImagePath(_document, sprite.ImagePath), resourcePath,
                StringComparison.OrdinalIgnoreCase));
        if (usageCount > 0)
        {
            await ShowMessage("删除资源", $"资源“{resource.Name}”正被 {usageCount} 个图片元素使用，请先删除这些元素。", ["确定"]);
            return;
        }

        var sharedByResource = _document.Resources.Any(item => !ReferenceEquals(item, resource) &&
            string.Equals(TMapFileService.ResolveImagePath(_document, item.ImagePath), resourcePath,
                StringComparison.OrdinalIgnoreCase));
        var resourcesDirectory = Path.Combine(_document.BaseDirectory, "Resources");
        var deleteFile = !sharedByResource && IsPathWithinDirectory(resourcePath, resourcesDirectory);
        var message = deleteFile
            ? $"确定删除资源“{resource.Name}”吗？\n工程 Resources 目录中的图片文件也会被删除，此操作无法撤销。"
            : $"确定从资源列表移除“{resource.Name}”吗？";
        if (await ShowMessage("删除资源", message, ["是", "否"]) != "是") return;

        var index = _document.Resources.IndexOf(resource);
        _document.Resources.Remove(resource);
        RefreshResourceList();
        SetDirty(true);
        if (!await SaveDocument(false))
        {
            _document.Resources.Insert(Math.Clamp(index, 0, _document.Resources.Count), resource);
            RefreshResourceList();
            SetDirty(true);
            return;
        }

        if (deleteFile && File.Exists(resourcePath))
        {
            try
            {
                File.Delete(resourcePath);
            }
            catch (Exception exception)
            {
                await ShowError("资源已移除，但图片文件删除失败", exception);
            }
        }
        ResetUndoState();
        StatusTextSet($"已删除资源：{resource.Name}");
    }

    private void Delete_Click()
    {
        _editorCanvas.DeleteSelected();
        RefreshEntityList();
    }

    private void Fit_Click() => _editorCanvas.FitToView();

    private void Exit_Click() => Close();

    private void Undo_Click()
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
            StatusTextSet("已撤销上一步操作");
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
        _undoSnapshot = snapshot == _currentSnapshot ? snapshot : _currentSnapshot;
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
        if (_undoMenuItem is not null) _undoMenuItem.IsEnabled = _undoSnapshot is not null;
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
        var focused = FocusManager.FocusedElement;
        return focused is TextBox or ComboBox;
    }

    private void Window_KeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.S && e.Modifiers.HasFlag(ModifierKeys.Control) && !e.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _ = SaveDocument(false);
            e.Handled = true;
        }
        else if (e.Key == Key.S && e.Modifiers.HasFlag(ModifierKeys.Control) && e.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _ = SaveDocument(true);
            e.Handled = true;
        }
        else if (e.Key == Key.O && e.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Open_Click();
            e.Handled = true;
        }
        else if (e.Key == Key.N && e.Modifiers.HasFlag(ModifierKeys.Control))
        {
            New_Click();
            e.Handled = true;
        }
        else if (e.Key == Key.E && e.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Export_Click();
            e.Handled = true;
        }
        else if (e.Key == Key.Z && e.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Undo_Click();
            e.Handled = true;
        }
        else if (e.Key == Key.F && e.Modifiers == ModifierKeys.None)
        {
            _editorCanvas.FitToView();
            e.Handled = true;
        }
        else if (e.Key == Key.Space && !IsTextInputFocused())
        {
            _editorCanvas.IsSpaceDown = true;
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && !IsTextInputFocused())
        {
            Delete_Click();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && !IsTextInputFocused())
        {
            _editorCanvas.CancelBrush();
            e.Handled = true;
        }
        else if (e.Modifiers == ModifierKeys.None && !IsTextInputFocused())
        {
            var offset = e.Key switch
            {
                Key.W => (X: 0d, Y: 1d),
                Key.A => (X: -1d, Y: 0d),
                Key.S => (X: 0d, Y: -1d),
                Key.D => (X: 1d, Y: 0d),
                _ => (X: 0d, Y: 0d)
            };
            if (offset != (0d, 0d) && _editorCanvas.NudgeSelectedSprites(offset.X, offset.Y))
                e.Handled = true;
        }
    }

    private async void Window_Closing(ClosingEventArgs e)
    {
        FlushObjectColorChange();
        if (!_closingConfirmed && _dirty)
        {
            e.Cancel = true;
            using var deferral = e.GetDeferral();
            StatusTextSet("正在保存地图...");
            if (await SaveDocument(false))
            {
                _closingConfirmed = true;
                Close();
            }
            return;
        }

        _settings.ResourcePreviewScale = _resourcePreviewScaleSlider.Value;
        EditorSettingsService.Save(_settings);
    }

    private void RememberCurrentProject()
    {
        if (_document.FilePath is null) return;
        _settings.LastProjectPath = _document.FilePath;
        _settings.ResourcePreviewScale = _resourcePreviewScaleSlider.Value;
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
        UpdateWindowTitle();
    }

    private void UpdateWindowTitle()
    {
        Title = $"TMap Editor - {_document.Name}{(_dirty ? " *" : "")} [{_renderBackend}]";
    }

    private string GetRenderBackendName()
    {
        var backend = Application.SelectedGraphicsBackend;
        var skiaPath = _editorCanvas.IsGpuPath ? "GPU" : "CPU";
        return $"{backend} ({skiaPath})";
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
        _fileText.Text = _document.FilePath;
        SetDirty(false);
    }

    private void EditorCanvas_SelectedItemChanged(object? sender, object? item)
    {
        FlushObjectColorChange();
        var layerName = item switch
        {
            TMapSprite sprite => sprite.Layer,
            TMapObject mapObject => mapObject.Layer,
            _ => null
        };
        var itemLayer = _document.Layers.FirstOrDefault(layer => layer.Name == layerName);
        if (itemLayer is not null && !ReferenceEquals(_layerList.SelectedItem, itemLayer))
            _layerList.SelectedItem = itemLayer;
        SyncEntityListSelection();
        UpdateSelectionProperties(_editorCanvas.SelectedItems.Count == 1 ? item : null);
        if (_editorCanvas.SelectedItems.Count > 1)
            _selectionTypeText.Text = $"已选择 {_editorCanvas.SelectedItems.Count} 个元素";
    }

    private void EditorCanvas_DocumentChanged(object? sender, EventArgs e)
    {
        SetDirty(true);
        var entityCount = GetCurrentEntityItems().Count;
        if (_entityView?.Count != entityCount ||
            _editorCanvas.SelectedItems.Any(item => _entityView?.Items.Contains(item) != true))
        {
            RefreshEntityList();
        }
        var item = _editorCanvas.SelectedItems.Count == 1 ? _editorCanvas.SelectedItem : null;
        UpdateSelectionProperties(item);
        if (_editorCanvas.SelectedItems.Count > 1)
            _selectionTypeText.Text = $"已选择 {_editorCanvas.SelectedItems.Count} 个元素";
    }

    private void EditorCanvas_DocumentChanging(object? sender, EventArgs e)
    {
        CaptureUndoSnapshot();
    }

    private void EditorCanvas_HoveredCellChanged(object? sender, MapCellHoverEventArgs e)
    {
        if (!e.IsInsideMap)
        {
            StatusTextSet("就绪");
            return;
        }
        var cell = _document.Cells.FirstOrDefault(item => item.Row == e.Row && item.Column == e.Column);
        var z = _document.CellZs.FirstOrDefault(item => item.Row == e.Row && item.Column == e.Column)?.Z ?? 0;
        var state = cell?.State switch
        {
            TMapCellState.Walk => "行进",
            TMapCellState.Block => "阻挡",
            _ => "未设置",
        };
        var displayRow = e.Row;
        if (displayRow.HasValue && _indexOriginCombo.SelectedIndex == 0)
        {
            var rows = (int)Math.Ceiling(_document.Height / _document.GridSize);
            displayRow = rows - 1 - displayRow.Value;
        }
        StatusTextSet($"格子索引：[{displayRow},{e.Column}]，通行：{state}，Z：{z}");
    }

    private void SyncEntityListSelection()
    {
        _synchronizingSelection = true;
        try
        {
            if (_entityView is IMultiSelectableItemsView multi)
            {
                multi.ClearSelection();
                for (var index = 0; index < _entityView.Count; index++)
                {
                    if (_editorCanvas.SelectedItems.Contains(_entityView.GetItem(index)))
                        multi.SetSelected(index, true);
                }
            }
        }
        finally
        {
            _synchronizingSelection = false;
        }
    }

    private void UpdateSelectionProperties(object? item)
    {
        _updatingSelectionProperties = true;
        try
        {
            _commonPropertyPanel.IsVisible = item is not null;
            _spritePropertyPanel.IsVisible = item is TMapSprite;
            _objectPropertyPanel.IsVisible = item is TMapObject;
            _emptyPropertyHint.IsVisible = item is null;

            switch (item)
            {
                case TMapSprite sprite:
                    var spriteLayer = _document.Layers.FirstOrDefault(layer => layer.Name == sprite.Layer);
                    _selectionTypeText.Text = spriteLayer?.Type == TMapLayerType.Object ? "对象层图片" : "图片元素";
                    _itemNameText.Text = sprite.Name;
                    _spriteLayerCombo.SelectedItem = spriteLayer;
                    _spriteImagePathText.Text = sprite.ImagePath;
                    _spriteXText.Text = Format(sprite.X);
                    _spriteYText.Text = Format(sprite.Y);
                    _spriteWidthText.Text = Format(sprite.Width);
                    _spriteHeightText.Text = Format(sprite.Height);
                    _spriteRotationText.Text = Format(sprite.Rotation);
                    _spriteScaleXText.Text = Format(sprite.ScaleX);
                    _spriteScaleYText.Text = Format(sprite.ScaleY);
                    _spriteAnchorXText.Text = Format(sprite.AnchorX);
                    _spriteAnchorYText.Text = Format(sprite.AnchorY);
                    _spriteOrderText.Text = sprite.Order.ToString(CultureInfo.InvariantCulture);
                    _spriteZText.Text = sprite.Z.ToString(CultureInfo.InvariantCulture);
                    _spriteZPanel.IsVisible = spriteLayer?.Type == TMapLayerType.Object;
                    break;
                case TMapObject mapObject:
                    _selectionTypeText.Text = "地图对象";
                    _itemNameText.Text = mapObject.Name;
                    _objectLayerCombo.SelectedItem = _document.Layers.FirstOrDefault(layer => layer.Name == mapObject.Layer);
                    _objectNoteText.Text = mapObject.Note;
                    _objectArgsText.Text = mapObject.Args;
                    _objectDisplayColorPicker.SelectedColor = ParseDisplayColor(mapObject.DisplayColor);
                    _objectXText.Text = Format(mapObject.X);
                    _objectYText.Text = Format(mapObject.Y);
                    _objectZText.Text = mapObject.Z.ToString(CultureInfo.InvariantCulture);
                    break;
                default:
                    _selectionTypeText.Text = "未选择";
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
        var selection = _editorCanvas.SelectedItems.ToList();
        var entities = GetCurrentEntityItems();
        _entityView = new ItemsView<object>(entities,
            item => item is IDisplayItem display ? display.DisplayName : item.ToString() ?? "",
            item => item);
        _entityList.ItemsSource = _entityView;
        SyncEntityListSelection();
    }

    private List<object> GetCurrentEntityItems()
    {
        if (_layerList.SelectedItem is not TMapLayer layer) return [];
        return layer.Type == TMapLayerType.Object
            ? _document.Objects.Where(mapObject => mapObject.Layer == layer.Name).Cast<object>()
                .Concat(_document.Sprites.Where(sprite => sprite.Layer == layer.Name))
                .ToList()
            : _document.Sprites.Where(sprite => sprite.Layer == layer.Name).Cast<object>().ToList();
    }

    private SKBitmap LoadSpriteBitmap(TMapSprite sprite)
    {
        var fullPath = TMapFileService.ResolveImagePath(_document, sprite.ImagePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("找不到图片文件。", fullPath);
        return SKBitmap.Decode(fullPath)
               ?? throw new InvalidDataException($"无法解码图片文件：{sprite.ImagePath}");
    }

    private Task<string?> ShowError(string title, Exception exception)
    {
        return ShowMessage(title, exception.Message, ["确定"]);
    }

    private async Task<string?> ShowMessage(string title, string message, IReadOnlyList<string> buttons)
    {
        var mappedButtons = buttons.Select(button => button switch
        {
            "是" => new MessageButton("是", MessageButtonRole.Accept),
            "否" => new MessageButton("否", MessageButtonRole.Destructive),
            "取消" => new MessageButton("取消", MessageButtonRole.Reject),
            _ => new MessageButton(button, MessageButtonRole.Accept)
        }).ToList();
        var result = await MessageBox.PromptAsync(new MessageBoxOptions
        {
            Message = message,
            Title = title,
            Icon = buttons.Count > 1 ? PromptIconKind.Question : PromptIconKind.Info,
            Owner = this,
            Buttons = mappedButtons
        });
        return result switch
        {
            true => mappedButtons.FirstOrDefault(button => button.Role == MessageButtonRole.Accept)?.Text
                    ?? buttons[0],
            false => mappedButtons.FirstOrDefault(button => button.Role == MessageButtonRole.Destructive)?.Text
                     ?? mappedButtons.FirstOrDefault(button => button.Role == MessageButtonRole.Reject)?.Text
                     ?? "否",
            _ => mappedButtons.FirstOrDefault(button => button.Role == MessageButtonRole.Reject)?.Text
                 ?? "取消"
        };
    }
}
