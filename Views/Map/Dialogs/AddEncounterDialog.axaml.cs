using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dujahit.ViewModels;
using System.Collections.Generic;
using Dujahit.Views;

namespace Dujahit.Views.Map.Dialogs
{
    public partial class AddEncounterDialog : DialogWindow
    {
        public EncounterPresetRowViewModel? Selected { get; private set; }
        public bool Accepted { get; private set; }

        private Border? _selectedCard;

        public AddEncounterDialog()
        {
            InitializeComponent();
        }

        public void SetEncounters(IReadOnlyList<EncounterPresetRowViewModel> encounters)
        {
            EncounterList.ItemsSource = encounters;
        }

        private void OnCardClicked(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border card) return;
            if (card.Tag is not EncounterPresetRowViewModel row) return;

            if (_selectedCard != null) _selectedCard.Classes.Remove("selected");
            card.Classes.Add("selected");
            _selectedCard = card;

            Selected = row;
        }

        private void OnAdd(object? sender, RoutedEventArgs e)
        {
            if (Selected == null) return;
            Accepted = true;
            Close();
        }

        private void OnCancel(object? sender, RoutedEventArgs e)
        {
            Accepted = false;
            Close();
        }
    }
}
