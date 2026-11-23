using System.Diagnostics;
using System.Net.Http;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Support.UI;

namespace Resonance.UITests;

/// <summary>Une session Appium sur l'appli Android, données effacées au démarrage (comme une installation neuve),
/// sans écran d'accueil ni visites guidées (extra d'Intent « resonance_e2e », voir Services.E2EMode dans l'appli).
/// Démarre le serveur Appium s'il ne tourne pas déjà. Les éléments sont repérés par leur AutomationId, que MAUI
/// expose comme identifiant de ressource Android - indépendant de la langue de l'appli.</summary>
public sealed class AppSession : IDisposable
{
    public const string Package = "com.narfedome.resonance";
    // Nom Java généré par MAUI pour Resonance.MainActivity (hash du namespace, stable tant qu'il ne change pas).
    private const string Activity = "crc644132c9466dd4775b.MainActivity";
    private static readonly Uri ServerUri = new("http://127.0.0.1:4723/");

    private static Process? _server;
    private static readonly object ServerLock = new();

    public AndroidDriver Driver { get; }

    public AppSession()
    {
        EnsureServer();
        var options = new AppiumOptions
        {
            PlatformName = "Android",
            AutomationName = "UiAutomator2",
            DeviceName = Environment.GetEnvironmentVariable("RESONANCE_E2E_DEVICE") ?? "emulator-5554",
        };
        options.AddAdditionalAppiumOption("udid", Environment.GetEnvironmentVariable("RESONANCE_E2E_DEVICE") ?? "emulator-5554");
        options.AddAdditionalAppiumOption("appPackage", Package);
        options.AddAdditionalAppiumOption("appActivity", Activity);
        options.AddAdditionalAppiumOption("noReset", false); // données effacées : appli comme neuve
        options.AddAdditionalAppiumOption("autoGrantPermissions", true);
        options.AddAdditionalAppiumOption("optionalIntentArguments", "--ez resonance_e2e true");
        options.AddAdditionalAppiumOption("newCommandTimeout", 300);
        options.AddAdditionalAppiumOption("appWaitDuration", 60000);
        Driver = new AndroidDriver(ServerUri, options, TimeSpan.FromMinutes(3));
        Driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;
    }

    // ---- Repérage -------------------------------------------------------------------------------------------

    public static By Id(string automationId) => MobileBy.Id($"{Package}:id/{automationId}");

    private static By Text(string text) => MobileBy.AndroidUIAutomator($"new UiSelector().text(\"{text}\")");

    public IReadOnlyCollection<IWebElement> All(string automationId) => Driver.FindElements(Id(automationId));

    public bool Exists(string automationId) => All(automationId).Count > 0;

    public IWebElement WaitFor(string automationId, int seconds = 20) =>
        new WebDriverWait(Driver, TimeSpan.FromSeconds(seconds)).Until(d => d.FindElements(Id(automationId)).FirstOrDefault(e => e.Displayed))!;

    /// <summary>Attend que <paramref name="condition"/> soit vraie (ex. le mixeur a fini de charger ses canaux).</summary>
    public void WaitUntil(Func<bool> condition, string because, int seconds = 30)
    {
        var wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(seconds)) { Message = because };
        wait.Until(_ => condition());
    }

    /// <summary>Premier élément dont le texte vaut exactement l'un des textes donnés (français ou anglais).</summary>
    public IWebElement WaitForText(int seconds, params string[] texts) =>
        new WebDriverWait(Driver, TimeSpan.FromSeconds(seconds)).Until(d => texts.Select(t => d.FindElements(Text(t))
            .FirstOrDefault()).FirstOrDefault(e => e is not null))!;

    public bool HasText(params string[] texts) => texts.Any(t => Driver.FindElements(Text(t)).Count > 0);

    /// <summary>Texte affiché par un élément : le sien, ou celui du premier libellé qu'il contient (un
    /// AutomationId posé sur un ContentView MAUI désigne le conteneur, pas le TextView).</summary>
    public static string TextOf(IWebElement element)
    {
        if (!string.IsNullOrEmpty(element.Text)) return element.Text;
        return element.FindElements(By.ClassName("android.widget.TextView")).Select(e => e.Text)
            .FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "";
    }

    // ---- Actions --------------------------------------------------------------------------------------------

    public void Tap(string automationId) => WaitFor(automationId).Click();

    /// <summary>Fait défiler la liste ou la page jusqu'à l'élément (UiAutomator ne voit que ce qui est à l'écran).</summary>
    public IWebElement ScrollTo(string automationId) => ScrollIntoView($"new UiSelector().resourceId(\"{Package}:id/{automationId}\")");

    /// <summary>Fait défiler jusqu'au premier texte exact trouvé parmi ceux donnés (français ou anglais).</summary>
    public IWebElement ScrollToText(params string[] texts)
    {
        foreach (var text in texts)
        {
            try { return ScrollIntoView($"new UiSelector().text(\"{text}\")"); }
            catch (NoSuchElementException) { }
        }
        throw new NoSuchElementException($"Texte introuvable : {string.Join(" / ", texts)}");
    }

    private IWebElement ScrollIntoView(string selector) => Driver.FindElement(MobileBy.AndroidUIAutomator(
        $"new UiScrollable(new UiSelector().scrollable(true).instance(0)).scrollIntoView({selector})"));

    /// <summary>Onglet du Shell, par son titre (français ou anglais).</summary>
    public void TapTab(string french, string english)
    {
        // Au démarrage, la barre d'onglets se redessine (titres traduits par AppShell.UpdateTabTitles) : l'élément
        // trouvé peut être remplacé avant le tap, d'où les nouvelles tentatives.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                WaitForText(60, french, english).Click();
                break;
            }
            catch (StaleElementReferenceException) when (attempt < 5)
            {
                Pause(500);
            }
        }
        Pause(1200);
    }

    /// <summary>Bouton de validation de la popup de confirmation / d'information (ConfirmDialog).</summary>
    public void Confirm(int seconds = 20)
    {
        WaitFor("DialogConfirmButton", seconds).Click();
        Pause();
    }

    /// <summary>Laisse l'appli finir une navigation ou un chargement.</summary>
    public static void Pause(int milliseconds = 800) => Thread.Sleep(milliseconds);

    /// <summary>Capture d'écran dans TestResults/e2e, pour comprendre un échec.</summary>
    public string Screenshot(string name)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestResults", "e2e");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{DateTime.Now:HHmmss}-{name}.png");
        Driver.GetScreenshot().SaveAsFile(path);
        return path;
    }

    public void Dispose()
    {
        try { Driver.Quit(); } catch (WebDriverException) { }
        Driver.Dispose();
    }

    // ---- Serveur Appium -------------------------------------------------------------------------------------

    private static void EnsureServer()
    {
        lock (ServerLock)
        {
            if (IsServerUp()) return;
            _server ??= Process.Start(new ProcessStartInfo("cmd.exe", "/c appium --port 4723 --log-level error")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { _server?.Kill(entireProcessTree: true); } catch { } };
            for (var i = 0; i < 60 && !IsServerUp(); i++) Thread.Sleep(500);
            if (!IsServerUp()) throw new InvalidOperationException("Le serveur Appium n'a pas démarré (npm install -g appium, voir README.md).");
        }
    }

    private static bool IsServerUp()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            return http.GetAsync(new Uri(ServerUri, "status")).GetAwaiter().GetResult().IsSuccessStatusCode;
        }
        catch (Exception) { return false; }
    }
}
