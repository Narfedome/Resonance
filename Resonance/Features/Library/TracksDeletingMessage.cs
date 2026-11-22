namespace Resonance.Features.Library
{
    /// <summary>
    /// Envoyé juste AVANT la suppression de pistes de la Bibliothèque, pour que le mixeur retire les
    /// canaux qui les jouent : sans ça, une scène ouverte gardait (et continuait de jouer) le canal d'une
    /// piste supprimée. Avant plutôt qu'après : un player, même en pause, verrouille son fichier sous
    /// Windows, qui ne pourrait sinon pas être effacé du disque.
    /// </summary>
    public sealed record TracksDeletingMessage(IReadOnlyCollection<int> TrackIds);
}
