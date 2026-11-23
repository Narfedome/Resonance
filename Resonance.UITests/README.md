# Tests de bout en bout (Android, Appium)

Parcours complets sur l'appli Android, pilotée par Appium (pilote UiAutomator2). Chaque test part d'une
installation neuve : les données de l'appli sont effacées au démarrage de la session, et l'écran d'accueil comme
les visites guidées sont coupés (extra d'Intent `resonance_e2e`, voir `Resonance/Services/E2EMode.cs`).

Scénarios (`MixerScenarios.cs`) :

- **Lancer une scène** : campagne d'exemple chargée depuis Paramètres › Découvrir Resonance › Charger, première
  scène lancée depuis les Campagnes, ses canaux apparaissent dans le mixeur avec le nom de leur piste.
- **Supprimer une piste** : même départ, puis la piste du premier canal est supprimée depuis la Bibliothèque ; son
  canal a disparu du mixeur.

Ils ne tournent pas dans la CI et sont **ignorés** sans `RESONANCE_E2E=1` : un `dotnet test` ordinaire ne les
lance pas.

## Une fois par machine

```powershell
winget install OpenJS.NodeJS.LTS
npm install -g appium
appium driver install uiautomator2
```

Il faut aussi le SDK Android (`ANDROID_HOME`) et Java 17, déjà là pour builder l'appli.

## Lancer

1. Démarrer un émulateur. Celui de référence est l'AVD `r8test` (Pixel 7, API 35) ; l'AVD
   `pixel_7_-_api_31_0` est cassé. Par défaut les tests visent `emulator-5554`, sinon
   `$env:RESONANCE_E2E_DEVICE = "<id adb>"`.

   ```powershell
   & "$env:ANDROID_HOME\emulator\emulator.exe" -avd r8test
   ```

2. Installer l'appli **avec ses assemblies dans l'APK**. En Debug, MAUI les pousse sinon dans les données de
   l'appli (Fast Deployment), que chaque test efface : l'appli ne démarrerait plus.

   ```powershell
   dotnet build Resonance/Resonance.csproj -f net10.0-android -c Debug -p:EmbedAssembliesIntoApk=true -t:Install
   ```

3. Lancer les tests (le serveur Appium est démarré tout seul s'il ne tourne pas déjà) :

   ```powershell
   $env:RESONANCE_E2E = "1"; dotnet test Resonance.UITests
   ```

Compter 5 à 7 minutes (effacement des données et import de la campagne d'exemple à chaque test). En cas d'échec, une capture d'écran est enregistrée dans
`Resonance.UITests/bin/Debug/net10.0/TestResults/e2e/`.

## Écrire un test

- Un test est une méthode `[E2EFact]` qui ouvre une `AppSession` (installation neuve, appli lancée en mode E2E)
  dans un `using`, et prend une capture d'écran dans son `catch` avant de relancer l'exception.
- Repérer les éléments par leur `AutomationId` (MAUI l'expose comme identifiant de ressource Android, indépendant
  de la langue) avec `AppSession.Tap/WaitFor/All/Exists`. En ajouter dans le XAML au besoin : un même identifiant
  peut servir à tous les éléments d'une liste (`CampaignRow`, `SceneLaunchButton`, `MixerChannel`,
  `LibraryTrackTile`...), `All` les rend dans l'ordre d'affichage.
- UiAutomator ne voit que ce qui est à l'écran : `ScrollTo(automationId)` ou `ScrollToText(...)` font défiler la
  page ou la liste jusqu'à l'élément.
- Pour un texte (onglet, titre de piste), `WaitForText`, `HasText`, `ScrollToText` et `TapTab` acceptent la version
  française et anglaise : la langue de l'appli suit celle de l'appareil.
- Les popups de confirmation et d'information (`ConfirmDialog`) se valident avec `Confirm()`
  (`DialogConfirmButton`), ou s'annulent par `DialogCancelButton`.
- Un `AutomationId` posé sur un `ContentView` (ex. `MixerChannelTrackName` sur `RotatedLabelView`) désigne le
  conteneur : `AppSession.TextOf` lit alors le texte du libellé qu'il contient.
