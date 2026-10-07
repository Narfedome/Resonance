using Android.App;
using Android.Content;
using Microsoft.Maui.ApplicationModel;
using AndroidUri = Android.Net.Uri;

namespace Resonance.Platforms.Android
{
    /// <summary>
    /// Sélection d'un document via ACTION_OPEN_DOCUMENT sans passer par FilePicker.Default.PickAsync,
    /// pour récupérer l'Uri content:// choisi sans que .NET MAUI ne le recopie lui-même dans le cache
    /// (cf. FileService.PickImportPackageAndroidAsync, qui fait cette copie lui-même de façon asynchrone).
    /// Réimplémentation minimale de ce que fait IntermediateActivity (interne à .NET MAUI, inaccessible
    /// depuis le code applicatif) : relaie le résultat via MainActivity.OnActivityResult.
    /// </summary>
    internal static class AndroidDmPackPicker
    {
        internal const int RequestCode = 42424;

        private static TaskCompletionSource<AndroidUri?>? _pending;

        public static Task<AndroidUri?> PickAsync(string mimeType)
        {
            var tcs = new TaskCompletionSource<AndroidUri?>();
            _pending = tcs;

            var intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType(mimeType);

            var activity = Platform.CurrentActivity
                ?? throw new InvalidOperationException("Aucune activité Android active pour lancer le sélecteur de fichier.");
            activity.StartActivityForResult(intent, RequestCode);

            return tcs.Task;
        }

        internal static void HandleActivityResult(Result resultCode, Intent? data)
        {
            var tcs = _pending;
            _pending = null;
            if (tcs == null)
                return;

            if (resultCode != Result.Ok || data?.Data == null)
            {
                tcs.TrySetResult(null);
                return;
            }

            tcs.TrySetResult(data.Data);
        }
    }
}
