using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Dujahit.ViewModels;
using Dujahit.Views.Map.Dialogs;
using System;
using Dujahit.Models;

namespace Dujahit.Views
{
    public partial class EncounterRunnerView : UserControl
    {
        private EncounterRunnerViewModel? Vm => DataContext as EncounterRunnerViewModel;
        private EncounterRunnerViewModel? _subscribedVm;

        public EncounterRunnerView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_subscribedVm != null)
            {
                _subscribedVm.Combat.AddPlayerRequested -= OnAddPlayerRequested;
                _subscribedVm.Combat.AddNpcRequested -= OnAddNpcRequested;
            }
            _subscribedVm = Vm;
            if (_subscribedVm == null) return;
            _subscribedVm.Combat.AddPlayerRequested += OnAddPlayerRequested;
            _subscribedVm.Combat.AddNpcRequested += OnAddNpcRequested;
        }

        private async void OnAddPlayerRequested()
        {
            try
            {
                if (Vm == null) return;
                if (TopLevel.GetTopLevel(this) is not Window owner) return;

                var players = await Vm.Combat.LoadAvailablePlayersAsync();
                if (players.Count == 0)
                {
                    NavItem.NavError?.Invoke(Vm.Combat.WhyNobodyToAdd(true));
                    return;
                }

                var dialog = new AddPlayerDialog();
                dialog.SetPlayers(players);
                await dialog.ShowDialog(owner);

                if (dialog.Accepted && dialog.Selected != null)
                {
                    var combatant = Vm.Combat.BuildCombatantFromPlayer(dialog.Selected, dialog.InitiativeRoll);
                    Vm.Combat.AddCombatant(combatant);
                }
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnAddPlayerRequested", ex); }
        }

        private async void OnAddNpcRequested()
        {
            try
            {
                if (Vm == null) return;
                if (TopLevel.GetTopLevel(this) is not Window owner) return;

                var npcs = await Vm.Combat.LoadAvailableNpcsAsync();
                if (npcs.Count == 0)
                {
                    NavItem.NavError?.Invoke(Vm.Combat.WhyNobodyToAdd(false));
                    return;
                }

                var dialog = new AddPlayerDialog();
                dialog.SetPlayers(npcs);
                await dialog.ShowDialog(owner);

                if (dialog.Accepted && dialog.Selected != null)
                {
                    var combatant = dialog.Selected.Monster != null
                        ? Vm.Combat.BuildCombatantFromMonster(dialog.Selected.Monster, dialog.InitiativeRoll)
                        : Vm.Combat.BuildCombatantFromNpc(dialog.Selected, dialog.InitiativeRoll);
                    Vm.Combat.AddCombatant(combatant);
                }
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnAddNpcRequested", ex); }
        }
    }
}
