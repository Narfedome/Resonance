using Resonance.Models.Library;
using System;
using System.Collections.Generic;
using System.Text;

namespace Resonance.Services
{
    public class LibraryPickerNavigationService : ILibraryPickerNavigationService
    {
        private TaskCompletionSource<LibraryItem?>? _tcs;

        public void RegisterTaskSource(TaskCompletionSource<LibraryItem?> tcs)
        {
            _tcs = tcs;
        }

        public async Task ClosePickerAsync(LibraryItem? result)
        {
            _tcs?.TrySetResult(result);

            if (Shell.Current.Navigation.ModalStack.Count > 0)
                await Shell.Current.Navigation.PopModalAsync();
        }
    }
    public interface ILibraryPickerNavigationService
    {
        void RegisterTaskSource(TaskCompletionSource<LibraryItem?> tcs);
        Task ClosePickerAsync(LibraryItem? result);
    }
}
