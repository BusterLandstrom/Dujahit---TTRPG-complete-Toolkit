using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dujahit.Models.Application;
using Dujahit.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Layout;
using Dujahit.Models;

namespace Dujahit.Views
{
    public partial class NotesView : UserControl
    {
        public NotesView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (_titleBox != null) return;
            _titleBox = this.FindControl<TextBox>("TitleBox");
            if (_titleBox != null) _titleBox.PropertyChanged += OnTitleBoxPropertyChanged;
        }

        private TextBox? _titleBox;

        private void OnTitleBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != Visual.IsVisibleProperty || _titleBox is not { IsVisible: true } box) return;
            Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); });
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private NotesViewModel? _vm;

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_vm != null)
            {
                _vm.PromptTitleAsync -= PromptTitleAsync;
                _vm.ShareRequested -= OnShareRequested;
                _vm.ConfirmAsync -= ConfirmAsync;
                _vm.CopyToClipboardRequested -= OnCopyToClipboard;
                _vm.ExportRequested -= OnExportRequested;
                _vm.ImportRequested -= OnImportRequested;
                _vm.ImportExampleRequested -= OnImportExampleRequested;
            }

            _vm = DataContext as NotesViewModel;

            if (_vm == null) return;

            _vm.PromptTitleAsync += PromptTitleAsync;
            _vm.ShareRequested += OnShareRequested;
            _vm.ConfirmAsync += ConfirmAsync;
            _vm.CopyToClipboardRequested += OnCopyToClipboard;
            _vm.ExportRequested += OnExportRequested;
            _vm.ImportRequested += OnImportRequested;
            _vm.ImportExampleRequested += OnImportExampleRequested;
        }

        private async void OnImportRequested()
        {
            try
            {
                if (_vm == null) return;
                if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;

                var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import notes from a zip",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Zip") { Patterns = new[] { "*.zip" } }
                    }
                });
                if (files.Count == 0) return;
                await _vm.ImportFromZipAsync(files[0].Path.LocalPath);
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnImportRequested", ex); }
        }

        private async void OnImportExampleRequested()
        {
            try
            {
                if (_vm == null) return;
                if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;

                var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save an example import zip",
                    SuggestedFileName = "dujahit-notes-example",
                    DefaultExtension = "zip",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Zip") { Patterns = new[] { "*.zip" } }
                    }
                });
                if (file == null) return;
                await _vm.SaveImportExampleAsync(file.Path.LocalPath);
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnImportExampleRequested", ex); }
        }

        private void OnTitleLostFocus(object? sender, RoutedEventArgs e)
        {
            if (_vm is not { IsRenaming: true }) return;
            _vm.CommitRenameCommand.Execute().Subscribe();
        }

        private async void OnExportRequested(string format)
        {
            try
            {
                if (_vm == null) return;
                if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;

                var isPdf = string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase);
                var ext = isPdf ? "pdf" : "md";
                var name = new string(_vm.SelectedExportName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());

                var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = isPdf ? "Export note and its subpages to PDF" : "Export note and its subpages to Markdown",
                    SuggestedFileName = string.IsNullOrWhiteSpace(name) ? "note" : name,
                    DefaultExtension = ext,
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType(isPdf ? "PDF" : "Markdown") { Patterns = new[] { "*." + ext } }
                    }
                });
                if (file == null) return;
                await _vm.ExportToAsync(format, file.Path.LocalPath);
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnExportRequested", ex); }
        }

        private async Task OnCopyToClipboard(string text)
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
        }

        private async Task<string?> PromptTitleAsync(string current)
        {
            var owner = this.GetVisualRoot() as Window;
            if (owner == null) return null;

            var tb = new TextBox { Text = current, MinWidth = 280 };
            var ok = new Button { Content = "OK", Width = 70, IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 70, IsCancel = true };

            var tcs = new TaskCompletionSource<string?>();
            ok.Click += (_, _) => tcs.TrySetResult(tb.Text);
            cancel.Click += (_, _) => tcs.TrySetResult(null);

            var dialog = new Window
            {
                Title = "Rename page",
                Width = 360,
                Height = 140,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(16),
                    Spacing = 10,
                    Children =
                    {
                        tb,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 8,
                            Children = { cancel, ok }
                        }
                    }
                }
            };
            dialog.Closed += (_, _) => tcs.TrySetResult(null);
            _ = dialog.ShowDialog(owner);
            tb.Focus();
            tb.SelectAll();

            var result = await tcs.Task;
            dialog.Close();
            return result;
        }

        private async void OnShareRequested(NotePage page)
        {
            try
            {
                var owner = this.GetVisualRoot() as Window;
                if (owner == null) return;
                var dialog = new ShareNoteDialog(page);
                await dialog.ShowDialog(owner);
                if (DataContext is ViewModels.NotesViewModel vm) await vm.ReloadAfterShareAsync(page.Id);
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnShareRequested", ex); }
        }
        private async Task<bool> ConfirmAsync(string title, string message)
        {
            var owner = this.GetVisualRoot() as Window;
            if (owner == null) return false;

            var ok = new Button
            {
                Content = "Delete",
                Width = 90,
                IsDefault = true,
                Foreground = Brushes.IndianRed
            };
            var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };

            var tcs = new TaskCompletionSource<bool>();
            ok.Click += (_, _) => tcs.TrySetResult(true);
            cancel.Click += (_, _) => tcs.TrySetResult(false);

            var dialog = new Window
            {
                Title = title,
                Width = 420,
                Height = 180,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(20),
                    Spacing = 14,
                    Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, ok }
                }
            }
                }
            };
            dialog.Closed += (_, _) => tcs.TrySetResult(false);
            _ = dialog.ShowDialog(owner);

            var result = await tcs.Task;
            dialog.Close();
            return result;
        }
    }
}