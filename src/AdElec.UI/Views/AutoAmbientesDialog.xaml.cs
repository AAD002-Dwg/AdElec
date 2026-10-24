using System.Windows;
using AdElec.UI.ViewModels;

namespace AdElec.UI.Views;

public partial class AutoAmbientesDialog : Window
{
    public AutoAmbientesDialog(AutoAmbientesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.SetCerrar(() => Close());
    }
}
