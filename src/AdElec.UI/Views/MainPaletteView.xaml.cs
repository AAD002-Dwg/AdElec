using System.Windows.Controls;
using AdElec.UI.ViewModels;

namespace AdElec.UI.Views;

public partial class MainPaletteView : UserControl
{
    public MainPaletteView(PanelViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
