using CommunityToolkit.Mvvm.ComponentModel;

namespace Resonance.Models.Library
{
    public partial class Track : LibraryItem
    {
        [ObservableProperty]
        private TimeSpan duration;

        partial void OnDurationChanged(TimeSpan value)
        {
            OnPropertyChanged(nameof(DurationFormatted));
        }
        public string DurationFormatted =>
            Duration.TotalHours >= 1
        ? Duration.ToString(@"hh\:mm\:ss")
        : Duration.ToString(@"mm\:ss");

        [ObservableProperty]
        private double volume = 1.0;

        [ObservableProperty]
        private string hash = string.Empty;

        [ObservableProperty]
        private string category = string.Empty;
    }
}
