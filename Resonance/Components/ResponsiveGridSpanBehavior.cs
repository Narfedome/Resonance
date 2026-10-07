namespace Resonance.Components;

/// <summary>
/// GridItemsLayout.Span est un nombre de colonnes fixe : sur un grand écran (Desktop), les tuiles
/// s'étirent pour remplir chaque colonne au lieu de garder une taille fixe. Ce comportement recalcule
/// Span à partir de la largeur réellement disponible du CollectionView, pour que le nombre de
/// colonnes s'adapte et que TileWidth reste la taille effective d'une tuile (elle doit rester
/// HorizontalOptions="Center" côté DataTemplate, sinon elle s'étire quand même dans sa cellule).
/// Desktop uniquement : sur Android/iOS, la largeur d'écran ne varie pas comme une fenêtre desktop
/// redimensionnable (au mieux une rotation) - le Span fixe déclaré en XAML reste donc inchangé là-bas.
/// </summary>
public class ResponsiveGridSpanBehavior : Behavior<CollectionView>
{
    public static readonly BindableProperty TileWidthProperty =
        BindableProperty.Create(nameof(TileWidth), typeof(double), typeof(ResponsiveGridSpanBehavior), 120.0);

    public double TileWidth
    {
        get => (double)GetValue(TileWidthProperty);
        set => SetValue(TileWidthProperty, value);
    }

    private CollectionView? collectionView;

    protected override void OnAttachedTo(CollectionView bindable)
    {
        base.OnAttachedTo(bindable);
        collectionView = bindable;
        bindable.SizeChanged += OnSizeChanged;
        UpdateSpan();
    }

    protected override void OnDetachingFrom(CollectionView bindable)
    {
        bindable.SizeChanged -= OnSizeChanged;
        collectionView = null;
        base.OnDetachingFrom(bindable);
    }

    private void OnSizeChanged(object? sender, EventArgs e) => UpdateSpan();

    private void UpdateSpan()
    {
        if (DeviceInfo.Current.Idiom != DeviceIdiom.Desktop)
            return;

        if (collectionView?.ItemsLayout is not GridItemsLayout gridLayout || collectionView.Width <= 0)
            return;

        int span = Math.Max(1, (int)(collectionView.Width / TileWidth));
        if (gridLayout.Span != span)
            gridLayout.Span = span;
    }
}
