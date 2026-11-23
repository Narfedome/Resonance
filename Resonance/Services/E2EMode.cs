namespace Resonance.Services
{
    /// <summary>Mode des tests de bout en bout (Resonance.UITests) : activé au lancement par l'extra d'Intent
    /// « resonance_e2e » (Android, voir MainActivity). Coupe ce qui viendrait se mettre en travers d'un scénario
    /// automatisé : l'écran d'accueil (App) et les visites guidées (AppTourService). Jamais activé par un lancement
    /// normal.</summary>
    public static class E2EMode
    {
        public const string IntentExtra = "resonance_e2e";

        public static bool IsEnabled { get; set; }
    }
}
