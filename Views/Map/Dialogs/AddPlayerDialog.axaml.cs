using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dujahit.ViewModels;
using System.Collections.Generic;
using System;
using Dujahit.Views;
using Dujahit.Models.UI;

namespace Dujahit.Views.Map.Dialogs
{
    public partial class AddPlayerDialog : DialogWindow
    {
        public PlayerOptionViewModel? Selected { get; private set; }
        public int InitiativeRoll => (int)(InitiativeInput.Value ?? 10);
        public bool Accepted { get; private set; }

        private Border? _selectedCard;

        public AddPlayerDialog()
        {
            InitializeComponent();
        }

        public void SetPlayers(IReadOnlyList<PlayerOptionViewModel> players)
        {
            PlayerList.ItemsSource = players;
        }

        private void OnPlayerCardClicked(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border card) return;
            if (card.Tag is not PlayerOptionViewModel player) return;

            if (_selectedCard != null) _selectedCard.Classes.Remove("selected");
            card.Classes.Add("selected");
            _selectedCard = card;

            Selected = player;

            var die = App.PM?.Rules.InitiativeDie ?? 20;
            InitiativeInput.Value = Math.Max(0, DiceManager.RollInitiativeDie() + player.InitiativeMod);
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