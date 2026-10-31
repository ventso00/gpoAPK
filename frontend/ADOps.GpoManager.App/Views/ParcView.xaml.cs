using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ADOps.GpoManager.App.Views;

public partial class ParcView : UserControl
{
    public ParcView()
    {
        InitializeComponent();
    }

    /// <summary>Ouverture de la fiche ordinateur : simple plomberie UI (aucune logique métier).</summary>
    private void OnComputerDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.ParcViewModel viewModel
            && viewModel.SelectedComputer is not null)
        {
            viewModel.OpenComputerDetailCommand.Execute(null);
        }
    }
}