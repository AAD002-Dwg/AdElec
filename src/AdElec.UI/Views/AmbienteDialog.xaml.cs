using System.Windows;
using AdElec.UI.ViewModels;

namespace AdElec.UI.Views;

public partial class AmbienteDialog : Window
{
    public AmbienteDialog(AmbienteDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.SetCerrar(() => Close());
    }
}
