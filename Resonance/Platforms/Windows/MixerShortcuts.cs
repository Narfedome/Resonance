using System.Windows.Input;
using Microsoft.UI.Xaml;
using KeyboardAccelerator = Microsoft.UI.Xaml.Input.KeyboardAccelerator;
using Resonance.Features.AudioMixer;
using Windows.System;

namespace Resonance.Platforms.Windows;

/// <summary>
/// Raccourcis clavier du mixeur sous Windows : piloter la musique sans souris pendant qu'on parle aux
/// joueurs. Tous avec Ctrl, pour ne pas voler les flèches ou la barre d'espace aux sliders et aux
/// champs de texte. Actifs seulement quand le mixeur est la page affichée, sans boîte de dialogue
/// ouverte ni chargement de scène en cours (Shell désactivé pendant ce temps, cf. LoadScene).
/// </summary>
internal static class MixerShortcuts
{
    private const double MasterVolumeStep = 0.1;

    public static void Attach(UIElement target, AudioMixerViewModel vm)
    {
        // La page peut recevoir plusieurs handlers au fil de sa vie : jamais deux fois les mêmes raccourcis.
        if (target.KeyboardAccelerators.Count > 0)
            return;

        Add(target, VirtualKey.Right, () => Run(vm.NextSceneCommand));
        Add(target, VirtualKey.Left, () => Run(vm.PrevSceneCommand));
        Add(target, VirtualKey.Enter, () => Run(vm.PlayAllCommand));
        Add(target, VirtualKey.Space, () => Run(vm.FadeOutAllCommand));
        Add(target, VirtualKey.Back, () => Run(vm.StopAllCommand));
        Add(target, VirtualKey.Up, () => vm.MasterVolume = Math.Min(1, Math.Round(vm.MasterVolume + MasterVolumeStep, 2)));
        Add(target, VirtualKey.Down, () => vm.MasterVolume = Math.Max(0, Math.Round(vm.MasterVolume - MasterVolumeStep, 2)));
    }

    private static void Add(UIElement target, VirtualKey key, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control };
        accelerator.Invoked += (_, e) =>
        {
            if (!IsMixerActive())
                return;
            e.Handled = true;
            action();
        };
        target.KeyboardAccelerators.Add(accelerator);
    }

    private static void Run(ICommand command)
    {
        if (command.CanExecute(null))
            command.Execute(null);
    }

    private static bool IsMixerActive() =>
        Shell.Current is { IsEnabled: true } shell
        && shell.CurrentPage is AudioMixerPage
        && shell.Navigation.ModalStack.Count == 0;
}
