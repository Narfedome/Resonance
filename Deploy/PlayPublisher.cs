#:package Google.Apis.AndroidPublisher.v3@1.77.0.4316
// Publie un AAB sur Google Play via la Google Play Developer API, lance par Build-Release.ps1
// (dotnet run Deploy\PlayPublisher.cs -- ...). Fichier unique (« file-based app » .NET 10) plutot
// qu'un projet : rien a ajouter a la solution, le package NuGet est restaure au premier lancement.
//
// Deroulement d'un « edit » (transaction de la Play Console) : ouverture, upload de l'AAB, upload du
// mapping.txt R8 (desobscurcissement des plantages), affectation de la version a la piste avec ses
// notes de version, puis validation. Tant que l'edit n'est pas valide, rien n'est visible cote Play ;
// avec --dry-run il est supprime au lieu d'etre valide, pour tester sans rien publier.
//
// Arguments :
//   --key <json>        cle du compte de service (droit de publier les releases dans la Play Console)
//   --package <id>      nom de package Android (com.narfedome.xxx)
//   --aab <fichier>     app bundle signe avec la cle d'upload
//   --track <piste>     production (defaut), beta, alpha, internal...
//   --name <nom>        nom de la release affiche dans la Play Console (ex. 1.3.350)
//   --mapping <fichier> mapping.txt R8, optionnel
//   --changelog <dossier Website> + --changelog-version <majeure.mineure> : notes de version
//                       (« Nouveautes » de la fiche Play) reprises du changelog du site, section
//                       <h2>majeure.mineure</h2> de changelog.html (fr-FR) et en/changelog.html (en-US)
//   --listing-images <dossier> : remplace dans le meme edit les images de la fiche (fr-FR et en-US),
//                       depuis la sortie de StoreListing\Build-StoreListing.ps1 : captures telephone
//                       phone-*.png (memes images pour les deux langues) et image de presentation
//                       feature-graphic-fr.png / feature-graphic-en.png. Les images existantes du meme
//                       type sont supprimees d'abord (deleteall), sinon elles s'ajouteraient aux nouvelles.
//   --aab est facultatif avec --listing-images : sans AAB, l'edit ne touche que les images (avec
//                       --dry-run, pour tester l'envoi sans rien publier).
//   --dry-run           tout faire sauf valider l'edit
//   --notes-only        afficher les notes extraites du changelog, sans rien envoyer

using Google.Apis.AndroidPublisher.v3;
using Google.Apis.AndroidPublisher.v3.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using System.Net;
using System.Text.RegularExpressions;
using ImageType = Google.Apis.AndroidPublisher.v3.EditsResource.ImagesResource.UploadMediaUpload.ImageTypeEnum;

var options = ParseArgs(args);
string Required(string name) => options.TryGetValue(name, out var value) && value != null
    ? value
    : throw new ArgumentException($"Argument --{name} manquant.");

try
{
    var track = options.GetValueOrDefault("track") ?? "production";
    var releaseName = options.GetValueOrDefault("name");
    var mappingPath = options.GetValueOrDefault("mapping");
    var websiteDir = options.GetValueOrDefault("changelog");
    var dryRun = options.ContainsKey("dry-run");

    var releaseNotes = new List<LocalizedText>();
    if (websiteDir != null)
    {
        var changelogVersion = Required("changelog-version");
        foreach (var (language, page) in new[] { ("fr-FR", "changelog.html"), ("en-US", Path.Combine("en", "changelog.html")) })
        {
            var text = ReleaseNotesFromChangelog(Path.Combine(websiteDir, page), changelogVersion);
            if (text != null)
                releaseNotes.Add(new LocalizedText { Language = language, Text = text });
            else
                Console.WriteLine($"Pas de section {changelogVersion} dans {page} : pas de notes {language}.");
        }
        foreach (var note in releaseNotes)
            Console.WriteLine($"--- Notes {note.Language} ({note.Text.Length} caracteres) ---{Environment.NewLine}{note.Text}");
    }
    if (options.ContainsKey("notes-only"))
        return 0;

    var package = Required("package");
    var listingImagesDir = options.GetValueOrDefault("listing-images");
    var aabPath = listingImagesDir != null ? options.GetValueOrDefault("aab") : Required("aab");
    var listingImages = listingImagesDir != null ? ListingImages(listingImagesDir) : [];

    var credential = CredentialFactory.FromFile<ServiceAccountCredential>(Required("key"))
        .ToGoogleCredential()
        .CreateScoped(AndroidPublisherService.Scope.Androidpublisher);
    var service = new AndroidPublisherService(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "PlayPublisher",
    });
    // Un AAB pese plusieurs dizaines de Mo : les 100 s par defaut ne suffisent pas toujours.
    service.HttpClient.Timeout = TimeSpan.FromMinutes(15);

    var edit = await service.Edits.Insert(null, package).ExecuteAsync();
    var committed = false;
    try
    {
        if (listingImages.Count > 0)
            await UploadListingImagesAsync(service, package, edit.Id, listingImages);

        if (aabPath == null)
        {
            if (dryRun)
            {
                Console.WriteLine("--dry-run : edit abandonne, rien n'est publie.");
            }
            else
            {
                await service.Edits.Commit(package, edit.Id).ExecuteAsync();
                committed = true;
                Console.WriteLine("Images de la fiche Google Play publiees.");
            }
            return 0;
        }

        Console.WriteLine($"Upload de {Path.GetFileName(aabPath)}...");
        Bundle bundle;
        using (var aab = File.OpenRead(aabPath))
        {
            var upload = service.Edits.Bundles.Upload(package, edit.Id, aab, "application/octet-stream");
            Ensure(await upload.UploadAsync(), "AAB");
            bundle = upload.ResponseBody;
        }
        var versionCode = bundle.VersionCode ?? throw new InvalidOperationException("Version code absent de la reponse d'upload.");
        Console.WriteLine($"AAB envoye (versionCode {versionCode}).");

        if (mappingPath != null && File.Exists(mappingPath))
        {
            using var mapping = File.OpenRead(mappingPath);
            var upload = service.Edits.Deobfuscationfiles.Upload(package, edit.Id, versionCode,
                EditsResource.DeobfuscationfilesResource.UploadMediaUpload.DeobfuscationFileTypeEnum.Proguard,
                mapping, "application/octet-stream");
            Ensure(await upload.UploadAsync(), "mapping.txt");
            Console.WriteLine("mapping.txt envoye.");
        }
        else if (mappingPath != null)
        {
            Console.WriteLine($"mapping.txt introuvable ({mappingPath}) : plantages non desobscurcis pour cette version.");
        }

        await service.Edits.Tracks.Update(new Track
        {
            TrackValue = track,
            Releases =
            [
                new TrackRelease
                {
                    Name = releaseName,
                    VersionCodes = [versionCode],
                    Status = "completed",
                    ReleaseNotes = releaseNotes.Count > 0 ? releaseNotes : null,
                },
            ],
        }, package, edit.Id, track).ExecuteAsync();
        Console.WriteLine($"Version affectee a la piste '{track}'" + (releaseNotes.Count > 0 ? $" avec notes ({string.Join(", ", releaseNotes.Select(n => n.Language))})." : " (sans notes de version)."));

        if (dryRun)
        {
            Console.WriteLine("--dry-run : edit abandonne, rien n'est publie.");
        }
        else
        {
            await service.Edits.Commit(package, edit.Id).ExecuteAsync();
            committed = true;
            Console.WriteLine($"Publie sur Google Play ({track}, versionCode {versionCode}) : la mise en ligne suit l'examen de Google.");
        }
    }
    finally
    {
        if (!committed)
        {
            try { await service.Edits.Delete(package, edit.Id).ExecuteAsync(); } catch { /* l'edit expire de lui-meme */ }
        }
    }
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"ECHEC : {e.Message}");
    return 1;
}

// Images a envoyer par langue de fiche : (langue, type, fichiers dans l'ordre d'affichage).
// Verifie tout avant d'ouvrir l'edit : un dossier incomplet ne doit pas vider la fiche a moitie.
static List<(string Language, ImageType Type, string[] Files)> ListingImages(string dir)
{
    var phones = Directory.Exists(dir)
        ? Directory.GetFiles(dir, "phone-*.png").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray()
        : [];
    // Google Play : 2 a 8 captures telephone.
    if (phones.Length is < 2 or > 8)
        throw new ArgumentException($"{phones.Length} capture(s) phone-*.png dans {dir} : Google Play en demande de 2 a 8.");
    var result = new List<(string, ImageType, string[])>();
    foreach (var (language, suffix) in new[] { ("fr-FR", "fr"), ("en-US", "en") })
    {
        var feature = Path.Combine(dir, $"feature-graphic-{suffix}.png");
        if (!File.Exists(feature))
            throw new ArgumentException($"{feature} introuvable.");
        result.Add((language, ImageType.PhoneScreenshots, phones));
        result.Add((language, ImageType.FeatureGraphic, [feature]));
    }
    return result;
}

static async Task UploadListingImagesAsync(AndroidPublisherService service, string package, string editId,
    List<(string Language, ImageType Type, string[] Files)> images)
{
    foreach (var (language, type, files) in images)
    {
        await service.Edits.Images.Deleteall(package, editId, language,
            Enum.Parse<EditsResource.ImagesResource.DeleteallRequest.ImageTypeEnum>(type.ToString())).ExecuteAsync();
        foreach (var file in files)
        {
            using var stream = File.OpenRead(file);
            var upload = service.Edits.Images.Upload(package, editId, language,
                type, stream, "image/png");
            Ensure(await upload.UploadAsync(), $"{Path.GetFileName(file)} ({language})");
        }
        Console.WriteLine($"Fiche {language} : {type} remplace(s) par {string.Join(", ", files.Select(Path.GetFileName))}.");
    }
}

static void Ensure(IUploadProgress progress, string what)
{
    if (progress.Status != UploadStatus.Completed)
        throw new InvalidOperationException($"Upload {what} echoue : {progress.Exception?.Message ?? progress.Status.ToString()}");
}

// Reprend les <li> de la section <h2>version</h2> du changelog, en texte brut, une puce par ligne.
// Google Play limite les notes a 500 caracteres : on garde les premieres entrees en entier tant
// qu'elles tiennent, puis une ligne « … » signale que la liste complete est sur le site.
static string? ReleaseNotesFromChangelog(string changelogPath, string version)
{
    const int maxLength = 500;
    const string more = "…";
    if (!File.Exists(changelogPath))
        return null;
    var html = File.ReadAllText(changelogPath);
    var section = Regex.Match(html,
        $@"<h2>\s*{Regex.Escape(version)}\s*</h2>(?<body>.*?)(?=<h2>|</main>|$)",
        RegexOptions.Singleline);
    if (!section.Success)
        return null;

    var items = Regex.Matches(section.Groups["body"].Value, @"<li>(?<item>.*?)</li>", RegexOptions.Singleline)
        .Select(m => WebUtility.HtmlDecode(Regex.Replace(m.Groups["item"].Value, "<[^>]+>", "")))
        .Select(item => "• " + Regex.Replace(item, @"\s+", " ").Trim())
        .ToList();
    if (items.Count == 0)
        return null;

    var lines = new List<string>();
    foreach (var item in items)
    {
        var candidate = string.Join("\n", lines.Append(item));
        var reserve = item == items[^1] ? 0 : more.Length + 1;
        if (candidate.Length + reserve > maxLength)
            break;
        lines.Add(item);
    }
    if (lines.Count == 0)
        return items[0][..(maxLength - more.Length)].TrimEnd() + more;
    if (lines.Count < items.Count)
        lines.Add(more);
    return string.Join("\n", lines);
}

static Dictionary<string, string?> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string?>();
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--"))
            throw new ArgumentException($"Argument inattendu : {args[i]}");
        var name = args[i][2..];
        var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--");
        result[name] = hasValue ? args[++i] : null;
    }
    return result;
}
