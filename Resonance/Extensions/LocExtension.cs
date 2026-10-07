using CommunityToolkit.Mvvm.Messaging;
using Resonance.Services;
using System.ComponentModel;

namespace Resonance.Extensions
{
    [ContentProperty(nameof(Key))]
    public class LocExtension : IMarkupExtension<BindingBase>
    {
        public string Key { get; set; } = string.Empty;

        public BindingBase ProvideValue(IServiceProvider serviceProvider)
            => new Binding
            {
                Mode = BindingMode.OneWay,
                Path = nameof(LocalizedString.Value),
                Source = new LocalizedString(Key)
            };

        object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
            => ProvideValue(serviceProvider);
    }

    public class LocalizedString : INotifyPropertyChanged
    {
        private readonly string _key;
        public event PropertyChangedEventHandler? PropertyChanged;

        public LocalizedString(string key)
        {
            _key = key;

            // Une LocalizedString est créée à chaque binding {loc:Loc ...}, donc potentiellement des
            // dizaines par page. Un abonnement classique sur ce singleton fuirait indéfiniment ;
            // WeakReferenceMessenger ne retient qu'une référence faible, donc les instances devenues
            // orphelines (page fermée) sont GC'ables normalement.
            WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this,
                (r, m) => ((LocalizedString)r).OnLanguageChanged());
        }

        private void OnLanguageChanged()
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));

        public string Value => LocalizationService.Instance[_key];
    }
}
