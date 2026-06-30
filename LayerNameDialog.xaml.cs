using Avalonia.Controls;
using Avalonia.Interactivity;
using TMapEditor.Models;

namespace TMapEditor;

public partial class LayerNameDialog : Window
{
    public LayerNameDialog() : this("图层名称", "")
    {
    }

    public LayerNameDialog(
        string title,
        string initialName,
        bool allowTypeSelection = false,
        TMapLayerType initialLayerType = TMapLayerType.Image)
    {
        InitializeComponent();
        Title = title;
        LayerNameText.Text = initialName;
        LayerTypePanel.IsVisible = allowTypeSelection;
        if (allowTypeSelection) Height = 205;
        LayerTypeCombo.SelectedIndex = initialLayerType == TMapLayerType.Object ? 1 : 0;
        Loaded += (_, _) =>
        {
            LayerNameText.Focus();
            LayerNameText.SelectAll();
        };
    }

    public string LayerName => LayerNameText.Text;
    public TMapLayerType LayerType => LayerTypeCombo.SelectedIndex == 1
        ? TMapLayerType.Object
        : TMapLayerType.Image;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
