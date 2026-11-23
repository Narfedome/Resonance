namespace Resonance.UITests;

/// <summary>Test de bout en bout : ignoré tant que RESONANCE_E2E ne vaut pas 1 (il faut un émulateur, l'appli
/// installée et Appium, voir README.md) - un simple « dotnet test » de la solution ne le lance donc jamais.</summary>
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RESONANCE_E2E") != "1")
            Skip = "Test de bout en bout : définir RESONANCE_E2E=1 (voir Resonance.UITests/README.md).";
    }
}
