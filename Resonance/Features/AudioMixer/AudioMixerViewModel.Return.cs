using CommunityToolkit.Mvvm.Input;
using Resonance.Models;

namespace Resonance.Features.AudioMixer
{
    // Retour de la scène libre vers la scène de campagne qu'on a quittée pour y aller : le bouton
    // flottant de la scène libre devient alors une flèche de retour, au lieu de laisser chercher
    // soi-même chapitre et scène dans le sélecteur.
    public partial class AudioMixerViewModel
    {
        private Session? _returnSession;
        private Scene? _returnScene;

        /// <summary>En scène libre, avec une scène de campagne où revenir.</summary>
        public bool CanReturnFromFreeform => IsFreeformActive && _returnScene != null && _returnSession != null;

        /// <summary>Bouton flottant visible : aller en scène libre, ou en revenir.</summary>
        public bool ShowFreeformButton => !IsFreeformActive || CanReturnFromFreeform;

        /// <summary>Infobulle et description du bouton : aller en scène libre, ou revenir à la scène (nommée).</summary>
        public string ReturnFromFreeformLabel =>
            CanReturnFromFreeform ? string.Format(Loc["MixerReturnToScene"], _returnScene!.Title) : Loc["MixerFreeform"];

        partial void OnIsFreeformActiveChanged(bool value) => NotifyReturnChanged();

        private void RememberReturnScene(Session? session, Scene? scene)
        {
            _returnSession = session;
            _returnScene = scene;
            NotifyReturnChanged();
        }

        private void NotifyReturnChanged()
        {
            OnPropertyChanged(nameof(CanReturnFromFreeform));
            OnPropertyChanged(nameof(ShowFreeformButton));
            OnPropertyChanged(nameof(ReturnFromFreeformLabel));
        }

        // Un seul bouton, deux rôles selon où l'on est.
        [RelayCommand]
        private async Task ToggleFreeform()
        {
            if (CanReturnFromFreeform)
                await ReturnFromFreeform();
            else if (!IsFreeformActive)
                await SelectFreeformScene();
        }

        private async Task ReturnFromFreeform()
        {
            if (_returnSession is not Session session || _returnScene is not Scene scene)
                return;

            var campaign = (await _sceneDataService.GetCampaignsAsync()).FirstOrDefault(c => c.Id == session.CampaignId);
            if (campaign == null)
            {
                // La campagne a disparu entre-temps : plus rien où revenir.
                RememberReturnScene(null, null);
                return;
            }

            await LoadFromPlayAsync(campaign, session, scene);
        }
    }
}
