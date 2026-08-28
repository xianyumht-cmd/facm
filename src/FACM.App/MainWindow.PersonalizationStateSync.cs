using System.ComponentModel;
using System.Threading;
using FACM.App.ViewModels;
using Microsoft.UI.Xaml;

namespace FACM.App;

public sealed partial class MainWindow
{
    private int _personalizationRefreshQueued;

    private void OnPersonalizationViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_closed || !ReferenceEquals(sender, _personalizationCenter)) return;
        if (sender is PersonalizationViewModel viewModel &&
            args.PropertyName is nameof(PersonalizationViewModel.IsBusy)
                or nameof(PersonalizationViewModel.Status)
                or nameof(PersonalizationViewModel.SelectedPet)
                or nameof(PersonalizationViewModel.IsPetEnabled)
                or nameof(PersonalizationViewModel.CanControlDesktopPet))
        {
            (Application.Current as App)?.ReportPersonalizationState(viewModel, args.PropertyName ?? string.Empty);
        }

        QueuePersonalizationSurfaceRefresh();
    }

    private void QueuePersonalizationSurfaceRefresh()
    {
        if (_closed || Interlocked.Exchange(ref _personalizationRefreshQueued, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _personalizationRefreshQueued, 0);
                if (!_closed) SyncPersonalizationSurface();
            }))
        {
            Interlocked.Exchange(ref _personalizationRefreshQueued, 0);
        }
    }

    internal void RefreshPersonalizationSurfaceFromRuntime() => QueuePersonalizationSurfaceRefresh();

    private void TracePersonalizationPetAction(string reason, bool success, string petId, string detail = "") =>
        (Application.Current as App)?.ReportPersonalizationAction(reason, success, petId, detail);
}
