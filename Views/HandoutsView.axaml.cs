using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Dujahit.ViewModels;
using System;
using System.Threading.Tasks;
using Dujahit.Models;

namespace Dujahit.Views
{
    public partial class HandoutsView : UserControl
    {
        private HandoutsViewModel? Vm => DataContext as HandoutsViewModel;

        public HandoutsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (Vm == null) return;
            Vm.AddHandoutRequested += OnAddHandoutRequested;
            Vm.ViewHandoutRequested -= OnViewHandoutRequested;
            Vm.ViewHandoutRequested += OnViewHandoutRequested;
            Vm.ConfirmAsync -= ConfirmDelete;
            Vm.ConfirmAsync += ConfirmDelete;
        }

        private void OnViewHandoutRequested(HandoutListItem item)
        {
            if (Vm == null || item == null) return;
            if (this.GetVisualRoot() is not Window owner) return;
            var pages = Vm.PageFilesFor(item);
            if (pages.Count == 0) return;
            var dialog = new HandoutViewerDialog(item.Name, pages, path => Vm.RevealPageAsync(item, path));
            _ = dialog.ShowDialog(owner);
        }

        private Task<bool> ConfirmDelete(string title, string message)
            => DialogWindow.ConfirmAsync(TopLevel.GetTopLevel(this) as Window, title, message);

        private async void OnAddHandoutRequested()
        {
            try
            {
                if (Vm == null) return;

                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Choose a handout, image or PDF",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Images and PDF")
                        {
                            Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.pdf" }
                        }
                    }
                });

                if (files.Count == 0) return;

                try
                {
                    var path = files[0].Path.LocalPath;
                    await Vm.AddFromFileAsync(path);
                }
                catch (Exception ex)
                {
                    ErrorLog.Log($"[HandoutsView] Failed to add handout", ex);
                }
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnAddHandoutRequested", ex); }
        }
    }
}
