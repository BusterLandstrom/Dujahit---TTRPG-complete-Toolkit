using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Dujahit.ViewModels;
using System.Threading.Tasks;

namespace Dujahit.Views;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? _wired;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_wired != null) { _wired.CopyToClipboardRequested -= OnCopyToClipboard; _wired.ConfirmRestoreAsync -= ConfirmRestore; }
            _wired = DataContext as SettingsViewModel;
            if (_wired != null) { _wired.CopyToClipboardRequested += OnCopyToClipboard; _wired.ConfirmRestoreAsync += ConfirmRestore; }
        };
    }

    private async Task OnCopyToClipboard(string text)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    private Task<bool> ConfirmRestore(string title, string message) =>
        DialogWindow.ConfirmAsync(this.GetVisualRoot() as Window, title, message, "Restore");
}