using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;

namespace Resonance.Services
{
    public enum AppPalette
    {
        NuitEtOr = 0,
        ForetProfonde = 1,
        TaverneAmbre = 2,
        AcierBrume = 3
    }

    public enum AppThemePreference
    {
        System = 0,
        Light = 1,
        Dark = 2
    }

    public class ThemeService : INotifyPropertyChanged
    {
        public static readonly ThemeService Instance = new();

        private const string PaletteKey   = "app_palette";
        private const string ThemePrefKey = "app_theme";

        private AppPalette          _palette;
        private AppThemePreference  _themePref;

        public event Action? ThemeChanged;
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ImageSource.FromResource (EmbeddedResource, cf. csproj) plutot que MauiImage+Source="x.svg"
        // (FileImageSource) : le watermark restait invisible sur Windows via MauiImage/FileImageSource
        // (element mesure/positionne correctement mais jamais reellement dessine) - confirme reglé en
        // passant par ce chemin de chargement different (lecture directe du flux de la resource
        // embarquee). Les SVG sources d'origine (Resources/Images/nuit.svg etc.) ont ete retires,
        // remplaces par ces PNG pre-rendus (Resources/WatermarkImages/*.png).
        public ImageSource WatermarkImageSource => ImageSource.FromResource(_palette switch
        {
            AppPalette.ForetProfonde => "foret.png",
            AppPalette.TaverneAmbre  => "taverne.png",
            AppPalette.AcierBrume    => "acier.png",
            _                        => "nuit.png",
        }, typeof(ThemeService).Assembly);

        private ThemeService()
        {
            _palette   = (AppPalette)Preferences.Default.Get(PaletteKey,   (int)AppPalette.NuitEtOr);
            _themePref = (AppThemePreference)Preferences.Default.Get(ThemePrefKey, (int)AppThemePreference.System);
        }

        public AppPalette Palette
        {
            get => _palette;
            set
            {
                if (_palette == value) return;
                _palette = value;
                Preferences.Default.Set(PaletteKey, (int)value);
                Apply();
                ThemeChanged?.Invoke();
                OnPropertyChanged(nameof(WatermarkImageSource));
            }
        }

        public AppThemePreference ThemePreference
        {
            get => _themePref;
            set
            {
                if (_themePref == value) return;
                _themePref = value;
                Preferences.Default.Set(ThemePrefKey, (int)value);
                ApplyThemePreference();
                Apply();
                ThemeChanged?.Invoke();
            }
        }

        public void Initialize()
        {
            if (Application.Current is not null)
                Application.Current.RequestedThemeChanged += (_, _) =>
                {
                    if (_themePref == AppThemePreference.System) Apply();
                };

            ApplyThemePreference();
            Apply();
        }

        private void ApplyThemePreference()
        {
            if (Application.Current is null) return;
            Application.Current.UserAppTheme = _themePref switch
            {
                AppThemePreference.Light => AppTheme.Light,
                AppThemePreference.Dark  => AppTheme.Dark,
                _                        => AppTheme.Unspecified
            };
        }

        public bool IsDark() =>
            _themePref == AppThemePreference.Dark ||
            (_themePref == AppThemePreference.System && Application.Current?.RequestedTheme == AppTheme.Dark);

        public void Apply()
        {
            if (Application.Current?.Resources is null) return;
            var tokens = GetPaletteTokens(_palette, IsDark());
            // Variante semi-transparente d'AppSurface (derivee ici plutot que repetee dans les 8
            // jeux de tokens ci-dessous) : pour les cartes de section (cf. SectionCardBorderStyle
            // dans Styles.xaml) qui doivent rester lisibles par-dessus le filigrane sans pour autant
            // etre un aplat plein aussi lourd visuellement que les popups (DialogCardBorder).
            tokens["AppSurfaceTranslucent"] = tokens["AppSurface"].WithAlpha(0.90f);
            // Rouge destructif fixe (pas par palette, cf. Colors.xaml) : ajoute ici pour suivre le
            // meme pipeline que les autres tokens plutot que de rester uniquement en fallback statique.
            tokens["AppDanger"] = Color.FromArgb("#C0392B");
            var res = Application.Current.Resources;
            foreach (var kv in tokens)
                res[kv.Key] = kv.Value;
        }

        public Color CurrentAccent => GetPaletteTokens(_palette, IsDark()).GetValueOrDefault("AppAccent", Colors.Gray);
        public Color CurrentAccentSecondary => GetPaletteTokens(_palette, IsDark()).GetValueOrDefault("AppAccentSecondary", Colors.Gray);

        private static Dictionary<string, Color> GetPaletteTokens(AppPalette palette, bool dark) => palette switch
        {
            AppPalette.NuitEtOr => dark
                ? new() {
                    ["AppBackground"]      = Color.FromArgb("#1C1A2E"),
                    ["AppSurface"]         = Color.FromArgb("#2A2750"),
                    ["AppAccent"]          = Color.FromArgb("#C9A84C"),
                    ["AppAccentSecondary"] = Color.FromArgb("#3f3b77"),
                    ["AppText"]            = Color.FromArgb("#F0EDE0"),
                    ["AppTextMuted"]       = Color.FromArgb("#A09880"),
                    ["AppBorder"]          = Color.FromArgb("#3D3A65"),
                }
                : new() {
                    ["AppBackground"]      = Color.FromArgb("#F0EDF8"),
                    ["AppSurface"]         = Color.FromArgb("#F7F5FD"),
                    ["AppAccent"]          = Color.FromArgb("#9B7A1A"),
                    ["AppAccentSecondary"] = Color.FromArgb("#3f3b77"),
                    ["AppText"]            = Color.FromArgb("#1C1A2E"),
                    ["AppTextMuted"]       = Color.FromArgb("#5A4A6A"),
                    ["AppBorder"]          = Color.FromArgb("#D8D0EE"),
                },

            // AppAccentSecondary en pin très sombre plutôt qu'une nuance claire de vert (cf.
            // discussion avec l'utilisateur : boutons et repères "en lecture"/fade se confondaient
            // trop, contraste insuffisant entre deux verts proches). Même valeur en clair et en
            // sombre (comme Taverne Ambrée ci-dessous) : assez sombre pour rester un accent net
            // dans les deux cas.
            AppPalette.ForetProfonde => dark
                ? new() {
                    ["AppBackground"]      = Color.FromArgb("#1A2318"),
                    ["AppSurface"]         = Color.FromArgb("#243020"),
                    ["AppAccent"]          = Color.FromArgb("#7AAF5C"),
                    ["AppAccentSecondary"] = Color.FromArgb("#1F3D14"),
                    ["AppText"]            = Color.FromArgb("#E8F0E4"),
                    ["AppTextMuted"]       = Color.FromArgb("#8A9E80"),
                    ["AppBorder"]          = Color.FromArgb("#354A2A"),
                }
                : new() {
                    ["AppBackground"]      = Color.FromArgb("#EDF4E8"),
                    ["AppSurface"]         = Color.FromArgb("#F5FAF2"),
                    ["AppAccent"]          = Color.FromArgb("#4A7A2C"),
                    ["AppAccentSecondary"] = Color.FromArgb("#1F3D14"),
                    ["AppText"]            = Color.FromArgb("#1A2318"),
                    ["AppTextMuted"]       = Color.FromArgb("#3A5A28"),
                    ["AppBorder"]          = Color.FromArgb("#C8DFC0"),
                },

            AppPalette.TaverneAmbre => dark
                ? new() {
                    ["AppBackground"]      = Color.FromArgb("#1E1218"),
                    ["AppSurface"]         = Color.FromArgb("#2A1A20"),
                    ["AppAccent"]          = Color.FromArgb("#C47B3A"),
                    ["AppAccentSecondary"] = Color.FromArgb("#5e434d"),
                    ["AppText"]            = Color.FromArgb("#F5ECE0"),
                    ["AppTextMuted"]       = Color.FromArgb("#A08070"),
                    ["AppBorder"]          = Color.FromArgb("#3D2530"),
                }
                : new() {
                    ["AppBackground"]      = Color.FromArgb("#FAF0E0"),
                    ["AppSurface"]         = Color.FromArgb("#FEF8EC"),
                    ["AppAccent"]          = Color.FromArgb("#A0522D"),
                    ["AppAccentSecondary"] = Color.FromArgb("#5e434d"),
                    ["AppText"]            = Color.FromArgb("#2A1810"),
                    ["AppTextMuted"]       = Color.FromArgb("#6B4A30"),
                    ["AppBorder"]          = Color.FromArgb("#E8D5B0"),
                },

            // AppAccentSecondary en ardoise indigo plutôt qu'une nuance de bleu, même raison que
            // ForetProfonde ci-dessus. Même valeur en clair et en sombre.
            AppPalette.AcierBrume => dark
                ? new() {
                    ["AppBackground"]      = Color.FromArgb("#141C24"),
                    ["AppSurface"]         = Color.FromArgb("#1E2C3A"),
                    ["AppAccent"]          = Color.FromArgb("#5B8FA8"),
                    ["AppAccentSecondary"] = Color.FromArgb("#354c63"),
                    ["AppText"]            = Color.FromArgb("#E0ECF4"),
                    ["AppTextMuted"]       = Color.FromArgb("#7A9AAE"),
                    ["AppBorder"]          = Color.FromArgb("#2E3F50"),
                }
                : new() {
                    ["AppBackground"]      = Color.FromArgb("#EBF2F7"),
                    ["AppSurface"]         = Color.FromArgb("#F5FAFD"),
                    ["AppAccent"]          = Color.FromArgb("#2E6B8A"),
                    ["AppAccentSecondary"] = Color.FromArgb("#354c63"),
                    ["AppText"]            = Color.FromArgb("#141C24"),
                    ["AppTextMuted"]       = Color.FromArgb("#3A5A70"),
                    ["AppBorder"]          = Color.FromArgb("#C5D8E8"),
                },

            _ => new()
        };

        public static (Color dark, Color light, Color accent) GetPaletteSwatchColors(AppPalette p) => p switch
        {
            AppPalette.NuitEtOr      => (Color.FromArgb("#1C1A2E"), Color.FromArgb("#F0EDF8"), Color.FromArgb("#C9A84C")),
            AppPalette.ForetProfonde => (Color.FromArgb("#1A2318"), Color.FromArgb("#EDF4E8"), Color.FromArgb("#7AAF5C")),
            AppPalette.TaverneAmbre  => (Color.FromArgb("#1E1218"), Color.FromArgb("#FAF0E0"), Color.FromArgb("#C47B3A")),
            AppPalette.AcierBrume    => (Color.FromArgb("#141C24"), Color.FromArgb("#EBF2F7"), Color.FromArgb("#5B8FA8")),
            _                        => (Colors.Black, Colors.White, Colors.Gray)
        };
    }
}
