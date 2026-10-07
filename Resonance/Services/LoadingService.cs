using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Resonance.Services;

public class LoadingService : INotifyPropertyChanged
{
    private int _depth;
    private bool _isLoading;

    public bool IsLoading
    {
        get => _isLoading;
        private set { if (_isLoading == value) return; _isLoading = value; OnPropertyChanged(); }
    }

    public async Task RunAsync(Func<Task> action)
    {
        Show();
        try { await action(); }
        finally { Hide(); }
    }

    public void Show()
    {
        if (++_depth == 1) IsLoading = true;
    }

    public void Hide()
    {
        if (--_depth <= 0) { _depth = 0; IsLoading = false; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
