using CommunityToolkit.Mvvm.ComponentModel;
using Resonance.Models.Library;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Resonance.Models
{
    public partial class Character : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<Spell> spells = new ObservableCollection<Spell>();
        public int Id { get; set; }
    }
}
