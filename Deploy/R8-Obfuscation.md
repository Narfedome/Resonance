# Obscurcissement R8 (Android)

Mis en place le 2026-10-10 sur la branche `r8`, à l'identique dans **Mordheim Ledger** et **Resonance**
(dépôt DmTools). Toute modification de ce dispositif doit être reportée dans les deux dépôts.

## Contexte

La Play Console signalait « L'optimisation du code DEX est inférieure à notre seuil » : obscurcissement à
1 % (seuil 25 %, à corriger d'ici février 2027, sinon impact possible sur la visibilité de l'appli).

Le SDK .NET Android impose `-dontobfuscate` dans le `proguard_xamarin.cfg` qu'il génère : avec
`AndroidLinkTool=r8` seul, R8 réduit le code Java sans jamais le renommer.

## Mise en place

- `AndroidLinkTool=r8` en Release Android (csproj).
- Cible `EnableR8Obfuscation` (csproj) : retire le `proguard_xamarin.cfg` du SDK et le remplace par
  `Platforms/Android/proguard_xamarin_obfuscate.cfg`, une copie sans `-dontobfuscate`. **À resynchroniser
  si une mise à jour du SDK change ses règles** (comparer avec `obj/Release/net10.0-android/proguard/proguard_xamarin.cfg`).
- Ce `.cfg` est déclaré en `<ProguardConfiguration>` (item public), et pas seulement ajouté à
  `_ProguardConfiguration` dans la cible : sinon le modifier ne relance pas R8 en build incrémental.
- `-printmapping` vers `bin/.../mapping.txt`. Le SDK embarque ce fichier dans l'AAB, ce qui permet à la
  Play Console de désobscurcir les traces de plantage.
- `-p:SkipR8Obfuscation=true` désactive l'obscurcissement (R8 continue de réduire le code).

## Les deux plantages corrigés

Le SDK n'ayant jamais été prévu pour obscurcir, ses règles de conservation sont incomplètes. Deux trous,
chacun faisant planter l'appli au démarrage en Release :

1. **`ClassNotFoundException: net.dot.android.ApplicationRegistration`**. Les classes Java générées par
   le build et retrouvées par leur nom via JNI ne sont pas conservées. Corrigé par
   `-keep class net.dot.android.**` et `-keep class mono.**` dans la section « Ajouts » en fin de
   `proguard_xamarin_obfuscate.cfg`.
2. **`NoSuchFieldError: Lifecycle$State.DESTROYED`**. `proguard_project_references.cfg` (généré par le SDK
   depuis les bindings conservés après ILLink) conserve les classes et leurs méthodes, mais jamais les
   champs. La cible génère `obj/.../proguard/proguard_binding_fields.cfg`, qui ajoute
   `-keep class X { <fields>; }` pour chaque classe de ce fichier.

## Résultat

Mesuré via `mapping.txt` (pas avec la formule exacte de Google) : environ 63 % des classes et 53 % des
méthodes renommées, dans les deux applis.

Vérifié sur émulateur API 35 : démarrage, onglets principaux, wizard de création (Mordheim Ledger) et
onglets principaux (Resonance). **Le parcours n'est pas exhaustif.**

## Si un nouveau plantage Release apparaît

Une `ClassNotFoundException`, `NoSuchFieldError` ou `NoSuchMethodError` sur une classe Java (androidx,
material...) signifie qu'un nom lu par JNI a été renommé par R8. Ajouter la règle `-keep` correspondante
dans la section « Ajouts » de `proguard_xamarin_obfuscate.cfg`, dans les deux dépôts.

L'exception d'origine est souvent masquée par une `TypeInitializationException` ou une
`JavaProxyThrowable`. Pour la voir, entourer temporairement `CreateMauiApp()` dans
`Platforms/Android/MainApplication.cs` d'un `try/catch` qui appelle
`Android.Util.Log.Error("R8DIAG", ex.ToString())`, puis lire `adb logcat -s R8DIAG:E`.

## Tester en local

```bash
dotnet build Resonance/Resonance.csproj -f net10.0-android -c Release -p:AndroidPackageFormat=apk -p:RuntimeIdentifier=android-x64
```

Installer ensuite l'APK `*-Signed.apk` de `bin/Release/net10.0-android/android-x64/` avec `adb install -r`
sur l'AVD `r8test` (API 35). L'AVD `pixel_7_-_api_31_0` pointe vers une image système absente.
