using Resonance.Features.Library;
using Resonance.Models.Library;

namespace Resonance.Services
{
    public class LibraryPickerService : ILibraryPickerService
    {
        private readonly IServiceProvider _provider;

        public LibraryPickerService(IServiceProvider provider)
        {
            _provider = provider;
        }

        public async Task<LibraryItem?> PickTrackAsync()
        {
            var tcs = new TaskCompletionSource<LibraryItem?>();

            var navigationService =
                _provider.GetRequiredService<ILibraryPickerNavigationService>();

            navigationService.RegisterTaskSource(tcs);

            var page = _provider.GetRequiredService<LibraryTrackSelectorPage>();
            var modal = new NavigationPage(page);

            // Filet de sécurité : si la modale est fermée sans passer par ClosePickerAsync
            // (bouton/geste retour Android notamment), le TaskCompletionSource ne serait jamais
            // résolu et l'appelant resterait bloqué pour toujours (sa commande RelayCommand
            // restant "en cours", donc son bouton mort). TrySetResult est sans effet si un
            // résultat a déjà été posé par ClosePickerAsync.
            var window = Shell.Current.Window;
            void OnModalPopped(object? sender, ModalPoppedEventArgs e)
            {
                if (!ReferenceEquals(e.Modal, modal))
                    return;
                window.ModalPopped -= OnModalPopped;
                tcs.TrySetResult(null);
            }
            window.ModalPopped += OnModalPopped;

            await Shell.Current.Navigation.PushModalAsync(modal);

            return await tcs.Task;
        }

    }
    public interface ILibraryPickerService { Task<LibraryItem?> PickTrackAsync(); }
}
