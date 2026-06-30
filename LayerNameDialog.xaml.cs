using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TMapEditor;

public partial class LayerNameDialog : Window
{
    public LayerNameDialog() : this("图层名称", "")
    {
    }

    public LayerNameDialog(string title, string initialName)
    {
        InitializeComponent();
        Title = title;
        LayerNameText.Text = initialName;
        Loaded += (_, _) =>
        {
            LayerNameText.Focus();
            LayerNameText.SelectAll();
        };
    }

    public string LayerName => LayerNameText.Text;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
