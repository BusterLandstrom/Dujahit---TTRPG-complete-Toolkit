using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Dujahit.ViewModels;
using System;
using System.IO;
using System.Diagnostics;
using Dujahit.Models;

namespace Dujahit.Views.Map
{
    public partial class MapHubView : UserControl
    {
        private MapHubViewModel? Vm => DataContext as MapHubViewModel;

        public MapHubView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (Vm == null) return;
            Vm.CreateMapRequested += OnCreateMapRequested;
            Vm.CreateBlankMapRequested += OnCreateBlankMapRequested;
        }

        private async void OnCreateBlankMapRequested()
        {
            try
            {
                if (Vm == null) return;
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel is not Window owner) return;

                var dialog = new Dialogs.CreateMapDialog();
                dialog.PrefillBlank("New map");
                await dialog.ShowDialog(owner);
                if (!dialog.Accepted) return;

                await Vm.AddBlankMap(dialog.MapName, dialog.SelectedGridKind, dialog.BlankCols, dialog.BlankRows);
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnCreateBlankMapRequested", ex); }
        }

        private async void OnCreateMapRequested()
        {
            try
            {
                if (Vm == null) return;

                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Choose Map Image",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Images")
                        {
                            Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" }
                        }
                    }
                });

                if (files.Count == 0) return;

                try
                {
                    var path = files[0].Path.LocalPath;
                    await using var stream = await files[0].OpenReadAsync();
                    var full = new Bitmap(stream);
                    var thumb = full.CreateScaledBitmap(new PixelSize(260, 160), BitmapInterpolationMode.HighQuality);

                    var dialog = new Dialogs.CreateMapDialog();
                    dialog.Prefill(Path.GetFileNameWithoutExtension(path));
                    dialog.SetPreviewImage(full);
                    if (topLevel is Window owner) await dialog.ShowDialog(owner);
                    if (!dialog.Accepted) return;

                    await Vm.AddMapFromImage(
                        dialog.MapName, thumb, path,
                        full.PixelSize.Width, full.PixelSize.Height,
                        dialog.SelectedGridKind, dialog.CellScale);
                }
                catch (Exception ex)
                {
                    ErrorLog.Log($"[MapHubView] Failed to load map image", ex);
                }
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnCreateMapRequested", ex); }
        }
    }
}
