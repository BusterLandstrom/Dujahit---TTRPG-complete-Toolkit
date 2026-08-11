using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Dujahit.ViewModels;
using System;
using Dujahit.Models;

namespace Dujahit.Views
{
    public partial class QuickNotesPanelView : UserControl
    {
        public QuickNotesPanelView()
        {
            InitializeComponent();
        }

        private async void OnCopyQuickNoteRef(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Control { DataContext: QuickNoteItemViewModel vm }) return;
                var top = TopLevel.GetTopLevel(this);
                if (top?.Clipboard is { } clipboard)
                    await clipboard.SetTextAsync($"<ref type=\"quicknote\" id=\"{vm.Slug}\"/>");
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnCopyQuickNoteRef", ex); }
        }
    }
}