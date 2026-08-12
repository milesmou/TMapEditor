using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using TMapEditor.Models;

namespace TMapEditor;

public sealed class LayerNameDialog : Window
{
    private readonly TextBox _layerNameText = new();
    private readonly ComboBox _layerTypeCombo = new();
    private readonly StackPanel _layerTypePanel = new();

    public LayerNameDialog(
        string title,
        string initialName,
        bool allowTypeSelection = false,
        TMapLayerType initialLayerType = TMapLayerType.Image)
    {
        Title = title;
        WindowSize = WindowSize.Fixed(360, allowTypeSelection ? 205 : 150);
        StartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new Color(37, 40, 45);

        _layerNameText.Text = initialName;
        _layerTypeCombo.Items("图片层", "对象层");
        _layerTypeCombo.SelectedIndex = initialLayerType == TMapLayerType.Object ? 1 : 0;
        _layerTypePanel.Spacing(6)
            .Children(
                new TextBlock { Text = "层级类型", Margin = new Thickness(3, 0, 3, 4) },
                _layerTypeCombo);
        _layerTypePanel.IsVisible = allowTypeSelection;

        var okButton = new Button { MinWidth = 72 }.Content("确定");
        okButton.Click += Ok_Click;
        var cancelButton = new Button { MinWidth = 72 }.Content("取消");
        cancelButton.Click += Cancel_Click;

        var grid = new Grid().Rows("Auto,Auto,Auto,*").Margin(14);
        grid.Add(new TextBlock { Text = "层级名称", Margin = new Thickness(3, 0, 3, 4) }.Row(0));
        grid.Add(_layerNameText.Row(1));
        grid.Add(_layerTypePanel.Row(2));
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Spacing = 8
        }.Row(3).Children(okButton, cancelButton);
        grid.Add(buttonPanel);
        Content = grid;

        Loaded += () =>
        {
            _layerNameText.Focus();
        };
        PreviewKeyDown += e =>
        {
            if (e.Key == Key.Enter)
            {
                Ok_Click();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Cancel_Click();
                e.Handled = true;
            }
        };
    }

    public string LayerName => _layerNameText.Text;
    public TMapLayerType LayerType => _layerTypeCombo.SelectedIndex == 1
        ? TMapLayerType.Object
        : TMapLayerType.Image;
    public bool? Result { get; private set; }

    private void Ok_Click()
    {
        Result = true;
        Close();
    }

    private void Cancel_Click()
    {
        Result = false;
        Close();
    }
}
