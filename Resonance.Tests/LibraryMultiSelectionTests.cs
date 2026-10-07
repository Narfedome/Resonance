using Resonance.Features.Library;
using Resonance.Models.Library;

namespace Resonance.Tests;

public class LibraryMultiSelectionTests
{
    [Fact]
    public void Track_SyncsInitialIsSelected_FromPriorSelection()
    {
        var selection = new LibraryMultiSelection<Track>();
        var track = new Track { Id = 1 };

        // Sélectionné via "Tout sélectionner" avant même d'être chargé (pagination).
        selection.SelectIds(new[] { 1 }, Enumerable.Empty<Track>());
        selection.Track(track);

        Assert.True(track.IsSelected);
    }

    [Fact]
    public void CheckingItem_UpdatesSelectedCountAndSelectedIds()
    {
        var selection = new LibraryMultiSelection<Track>();
        var track = new Track { Id = 5 };
        selection.Track(track);

        track.IsSelected = true;

        Assert.Equal(1, selection.SelectedCount);
        Assert.True(selection.HasSelection);
        Assert.Contains(5, selection.SelectedIds);
    }

    [Fact]
    public void UncheckingItem_RemovesFromSelection()
    {
        var selection = new LibraryMultiSelection<Track>();
        var track = new Track { Id = 5 };
        selection.Track(track);
        track.IsSelected = true;

        track.IsSelected = false;

        Assert.Equal(0, selection.SelectedCount);
        Assert.False(selection.HasSelection);
        Assert.DoesNotContain(5, selection.SelectedIds);
    }

    [Fact]
    public void Untrack_StopsReactingToFurtherChanges()
    {
        var selection = new LibraryMultiSelection<Track>();
        var track = new Track { Id = 5 };
        selection.Track(track);

        selection.Untrack(track);
        track.IsSelected = true;

        Assert.Equal(0, selection.SelectedCount);
    }

    [Fact]
    public void SelectIds_MarksOnlyLoadedMatchingItemsAsSelected()
    {
        var selection = new LibraryMultiSelection<Track>();
        var loaded1 = new Track { Id = 1 };
        var loaded2 = new Track { Id = 2 };
        selection.Track(loaded1);
        selection.Track(loaded2);

        // Id 3 n'est pas chargé (page suivante) : doit rester en mémoire sans planter.
        selection.SelectIds(new[] { 1, 3 }, new[] { loaded1, loaded2 });

        Assert.True(loaded1.IsSelected);
        Assert.False(loaded2.IsSelected);
        Assert.Equal(2, selection.SelectedCount);
        Assert.Contains(3, selection.SelectedIds);
    }

    [Fact]
    public void ContainsAll_TrueOnlyWhenEveryIdIsSelected()
    {
        var selection = new LibraryMultiSelection<Track>();
        selection.SelectIds(new[] { 1, 2 }, Enumerable.Empty<Track>());

        Assert.True(selection.ContainsAll(new[] { 1, 2 }));
        Assert.False(selection.ContainsAll(new[] { 1, 2, 3 }));
    }

    [Fact]
    public void DeselectAll_ClearsSelectionAndUnchecksLoadedItems()
    {
        var selection = new LibraryMultiSelection<Track>();
        var loaded = new Track { Id = 1 };
        selection.Track(loaded);
        selection.SelectIds(new[] { 1, 2 }, new[] { loaded });

        selection.DeselectAll(new[] { loaded });

        Assert.False(loaded.IsSelected);
        Assert.Equal(0, selection.SelectedCount);
        Assert.False(selection.HasSelection);
    }

    [Fact]
    public void Clear_ResetsCountButDoesNotUncheckLoadedItems()
    {
        // Comportement volontaire (voir doc de Clear) : Clear() sert au changement de contexte
        // (rechargement complet de la liste), pas à un "tout désélectionner" visuel - c'est DeselectAll
        // qui s'en charge. Ce test protège cette distinction.
        var selection = new LibraryMultiSelection<Track>();
        var loaded = new Track { Id = 1 };
        selection.Track(loaded);
        loaded.IsSelected = true;

        selection.Clear();

        Assert.Equal(0, selection.SelectedCount);
        Assert.True(loaded.IsSelected);
    }
}
