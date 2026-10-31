using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ADOps.GpoManager.App.ViewModels;

namespace ADOps.GpoManager.App.Views;

public partial class WorkspaceView : UserControl
{
    private WorkspaceViewModel? _viewModel;

    public WorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ConsoleLines.CollectionChanged -= OnConsoleLinesChanged;
            _viewModel.ClientEvents.CollectionChanged -= OnClientEventsChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as WorkspaceViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.ConsoleLines.CollectionChanged += OnConsoleLinesChanged;
        _viewModel.ClientEvents.CollectionChanged += OnClientEventsChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceViewModel.ActivePage))
        {
            WorkspaceScroll.Dispatcher.BeginInvoke(new Action(() => WorkspaceScroll.ScrollToHome()));
        }
    }

    private void OnConsoleLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => ScrollToEnd(DeployConsoleScroll);

    private void OnClientEventsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => ScrollToEnd(ClientEventsScroll);

    private static void ScrollToEnd(ScrollViewer viewer)
    {
        if (viewer is null)
        {
            return;
        }

        viewer.Dispatcher.BeginInvoke(new Action(() => viewer.ScrollToEnd()));
    }
}
