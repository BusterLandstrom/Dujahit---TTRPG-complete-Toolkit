using Avalonia.Controls;
using Dujahit.ViewModels;
using Dujahit.Views.Map.Dialogs;
using System;
using Dujahit.Models;

namespace Dujahit.Views.Map.Panels;

public partial class DmCombatPanelView : UserControl
{
    private DmCombatPanelViewModel? Vm => DataContext as DmCombatPanelViewModel;

    private DmCombatPanelViewModel? _subscribedVm;

    public DmCombatPanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm != null)
        {
            _subscribedVm.AddNpcRequested -= OnAddNpcRequested;
            _subscribedVm.AddPlayerRequested -= OnAddPlayerRequested;
            _subscribedVm.AddEncounterRequested -= OnAddEncounterRequested;
        }
        _subscribedVm = Vm;
        if (_subscribedVm == null) return;
        _subscribedVm.AddNpcRequested += OnAddNpcRequested;
        _subscribedVm.AddPlayerRequested += OnAddPlayerRequested;
        _subscribedVm.AddEncounterRequested += OnAddEncounterRequested;
    }

    private async void OnAddNpcRequested()
    {
        try
        {
            if (Vm == null) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is not Window owner) return;

            var npcs = await Vm.LoadAvailableNpcsAsync();
            if (npcs.Count == 0)
            {
                NavItem.NavError?.Invoke(Vm.WhyNobodyToAdd(false));
                return;
            }

            var dialog = new AddPlayerDialog();
            dialog.SetPlayers(npcs);
            await dialog.ShowDialog(owner);

            if (dialog.Accepted && dialog.Selected != null)
            {
                var combatant = dialog.Selected.Monster != null
                    ? Vm.BuildCombatantFromMonster(dialog.Selected.Monster, dialog.InitiativeRoll)
                    : Vm.BuildCombatantFromNpc(dialog.Selected, dialog.InitiativeRoll);
                Vm.AddCombatant(combatant);
                if (dialog.Selected.Monster == null) Vm.RaisePlayerPulledIn(combatant, dialog.Selected);
            }
        }
        catch (Exception ex) { ErrorLog.Log("Unhandled in OnAddNpcRequested", ex); }
    }

    private async void OnAddPlayerRequested()
    {
        try
        {
            if (Vm == null) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is not Window owner) return;

            var players = await Vm.LoadAvailablePlayersAsync();
            if (players.Count == 0)
            {
                NavItem.NavError?.Invoke(Vm.WhyNobodyToAdd(true));
                return;
            }

            var dialog = new AddPlayerDialog();
            dialog.SetPlayers(players);
            await dialog.ShowDialog(owner);

            if (dialog.Accepted && dialog.Selected != null)
            {
                var combatant = Vm.BuildCombatantFromPlayer(dialog.Selected, dialog.InitiativeRoll);
                Vm.AddCombatant(combatant);
                Vm.RaisePlayerPulledIn(combatant, dialog.Selected);
            }
        }
        catch (Exception ex) { ErrorLog.Log("Unhandled in OnAddPlayerRequested", ex); }
    }

    private async void OnAddEncounterRequested()
    {
        try
        {
            if (Vm == null) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is not Window owner) return;

            var encounters = await Vm.LoadSavedEncountersAsync();
            if (encounters.Count == 0)
            {
                NavItem.NavError?.Invoke("No saved encounters yet, build one on the Encounters page first.");
                return;
            }

            var dialog = new AddEncounterDialog();
            dialog.SetEncounters(encounters);
            await dialog.ShowDialog(owner);

            if (dialog.Accepted && dialog.Selected != null)
                Vm.RaiseEncounterChosen(dialog.Selected.Preset);
        }
        catch (Exception ex) { ErrorLog.Log("Unhandled in OnAddEncounterRequested", ex); }
    }
}