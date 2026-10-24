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
- `Views/OnboardingOverlayView.xaml.cs` : retranche l'origine de l'overlay avant de dessiner le trou
  (`ToOverlayCoordinatesAsync`) - la page modale commence sous la barre de titre sous Windows, sous la
  barre d'état sous Android, ce qui décalait le trou vers le bas.
- `Internals/OnboardingTargetRegistry.cs` : plusieurs éléments par clé au lieu du seul dernier attaché ;
  `TryGet` préfère le plus récent visible sur la page affichée par le Shell (lignes de liste répétées,
  vue Bibliothèque réutilisée par les sélecteurs de pistes).
- `Views/OnboardingTooltipCard.xaml.cs` : libellés Suivant / Terminer / Passer réglables
  (`NextLabel`, `DoneLabel`, `SkipLabel`) au lieu de l'anglais codé en dur, pour la localisation.
- `OnboardingCoordinator.cs`, `Internals/OnboardingOverlayHost.cs`, `Views/OnboardingOverlayView.xaml.cs` :
  propriété `GlowColor` (lueur autour du trou et flèche), transmise jusqu'au dessin comme `ScrimColor`.
- `Views/OnboardingTooltipCard.xaml` : textes et boutons aux couleurs du thème de Resonance
  (`AppText`, `AppTextMuted`, police OpenSans) et boutons des popups de l'appli (Passer :
  `DialogSecondaryButtonStyle`, Suivant : style `Button` implicite), ombre sombre au lieu du halo
  blanc. Cette carte dépend donc désormais de ces ressources de l'appli.
- `OnboardingStep.cs`, `OnboardingCoordinator.cs`, `Views/OnboardingOverlayView.xaml.cs` : étapes de
  présentation sans cible (`TargetKey` vide : bulle centrée, sans trou), action `BeforeShow` lancée avant
  une étape (ouvre un segment, comme `RequiredRoute`) et évènement `TourEnded` pour défaire ces actions.

Garder ce fichier à jour à chaque nouvelle modification, pour pouvoir reporter facilement une
version plus récente du projet d'origine.
