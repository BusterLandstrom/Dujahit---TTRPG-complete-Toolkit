using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dujahit.Models.Communication;
using Dujahit.ViewModels;
using System;
using System.Threading.Tasks;
using Dujahit.Models;

namespace Dujahit.Views;

public partial class CampaignView : UserControl
{
    private CampaignViewModel? _wired;

    public CampaignView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_wired == null) return;

        if (e.Key == Key.Q && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _wired.OpenSearch();
            if (_wired.SearchOverlayVisible)
                Dispatcher.UIThread.Post(() => SearchBox?.Focus());
            e.Handled = true;
            return;
        }

        if (!_wired.SearchOverlayVisible) return;

        if (e.Key == Key.Escape)
        {
            _wired.SearchOverlayVisible = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            _wired.Search.ChooseFirst();
            e.Handled = true;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_wired != null) { _wired.IncomingTradeRequested -= OnIncomingTrade; _wired.ItemPopupRequested -= OnItemPopup; _wired.ConfirmLeaveAsync -= ConfirmLeave; _wired.ConfirmDeleteAsync -= ConfirmDelete; }
        _wired = DataContext as CampaignViewModel;
        if (_wired != null) { _wired.IncomingTradeRequested += OnIncomingTrade; _wired.ItemPopupRequested += OnItemPopup; _wired.ConfirmLeaveAsync += ConfirmLeave; _wired.ConfirmDeleteAsync += ConfirmDelete; }
    }

    private Task<bool> ConfirmLeave(string title, string message) =>
        DialogWindow.ConfirmAsync(this.GetVisualRoot() as Window, title, message, "Leave");

    private Task<bool> ConfirmDelete(string title, string message) =>
        DialogWindow.ConfirmAsync(this.GetVisualRoot() as Window, title, message, "Delete");

    private async void OnIncomingTrade(TradeOfferMessage offer)
    {
        try
        {
            if (this.GetVisualRoot() is not Window owner) return;
            await TradeDialog.OpenRecipientAsync(owner, offer);
        }
        catch (Exception ex) { ErrorLog.Log("Unhandled in OnIncomingTrade", ex); }
    }

    private async void OnItemPopup(ItemPopupRequest req)
    {
        try
        {
            if (this.GetVisualRoot() is not Window owner) return;
            var dialog = new ItemViewDialog(req.Name, req.ItemType, req.DataJson, req.Id);
            await dialog.ShowDialog(owner);
        }
        catch (Exception ex) { ErrorLog.Log("Unhandled in OnItemPopup", ex); }
    }
}