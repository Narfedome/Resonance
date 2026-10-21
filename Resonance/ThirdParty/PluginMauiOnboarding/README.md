# Plugin.Maui.Onboarding (code intégré)

Visite guidée « projecteur » (fond sombre percé autour d'un contrôle + bulle d'explication) utilisée par
`Services/AppTourService.cs`.

- Projet d'origine : https://github.com/aminparsa18/Plugin.Maui.Onboarding
- Auteur : Amin Parsa
- Licence : MIT (voir `LICENSE` dans ce dossier, à conserver avec le code)
- Version reprise : 0.3.1, commit `5ad699018db63a8be4dfe1bfd18867f1e6379d63`

Intégré en source plutôt que via le paquet NuGet : celui-ci ne cible qu'Android et iOS, alors que
Resonance est aussi publiée sous Windows.

## Modifications par rapport à l'original

- `Internals/OnboardingBoundsExtensions.cs` : lecture de la position à l'écran sous Windows
  (`TransformToVisual`), absente de l'original.
- `Views/OnboardingOverlayView.xaml.cs` : sous Windows, retranche l'origine de l'overlay (la page
  modale commence sous la barre de titre) avant de dessiner le trou (`ToOverlayCoordinatesAsync`).
- `Views/OnboardingTooltipCard.xaml.cs` : libellés Suivant / Terminer / Passer réglables
  (`NextLabel`, `DoneLabel`, `SkipLabel`) au lieu de l'anglais codé en dur, pour la localisation.

Garder ce fichier à jour à chaque nouvelle modification, pour pouvoir reporter facilement une
version plus récente du projet d'origine.
