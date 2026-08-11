using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dujahit.ViewModels;
using System;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Layout;
using Dujahit.Models;

namespace Dujahit.Views
{
    public partial class CodexView : UserControl
    {
        public CodexView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (_titleBox != null) return;
            _titleBox = this.FindControl<TextBox>("ChapterTitleBox");
            if (_titleBox != null) _titleBox.PropertyChanged += OnChapterTitleBoxPropertyChanged;
        }

        private TextBox? _titleBox;

        private void OnChapterTitleBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != Visual.IsVisibleProperty || _titleBox is not { IsVisible: true } box) return;
            Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); });
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private CodexViewModel? _vm;

        private async Task OnCopyToClipboard(string text)
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_vm != null)
            {
                _vm.Npcs.ConfirmAsync -= ConfirmAsync;
                _vm.Items.ConfirmAsync -= ConfirmAsync;
                _vm.Items.OpenCreateItemRequested -= OnCreateItemRequested;
                _vm.Items.OpenItemRequested -= OnEditItemRequested;
                _vm.Items.ViewItemRequested -= OnViewItemRequested;
                _vm.Chapters.PromptTitleAsync -= PromptTitleAsync;
                _vm.Chapters.ConfirmAsync -= ConfirmAsync;
                _vm.Chapters.CopyToClipboardRequested -= OnCopyToClipboard;
            }

            _vm = DataContext as CodexViewModel;
            if (_vm == null) return;

            _vm.Npcs.ConfirmAsync += ConfirmAsync;
            _vm.Items.ConfirmAsync += ConfirmAsync;
            _vm.Items.OpenCreateItemRequested += OnCreateItemRequested;
            _vm.Items.OpenItemRequested += OnEditItemRequested;
            _vm.Items.ViewItemRequested += OnViewItemRequested;
            _vm.Chapters.PromptTitleAsync += PromptTitleAsync;
            _vm.Chapters.ConfirmAsync += ConfirmAsync;
            _vm.Chapters.CopyToClipboardRequested += OnCopyToClipboard;
        }

        private void OnChapterTitleLostFocus(object? sender, RoutedEventArgs e)
        {
            if (_vm?.Chapters is not { IsRenaming: true } chapters) return;
            chapters.CommitRenameCommand.Execute().Subscribe();
        }

        private async void OnViewItemRequested(string itemId)
        {
            try
            {
                if (App.PM == null) return;
                if (this.GetVisualRoot() is not Window owner) return;
                var item = await App.PM.LoadItemAsync(itemId);
                if (item == null) return;
                var dialog = new ItemViewDialog(item.Value.Name, item.Value.ItemType, item.Value.DataJson);
                _ = dialog.ShowDialog(owner);
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnViewItemRequested", ex); }
        }

        private async void OnEditItemRequested(string itemId)
        {
            try
            {
                if (_vm == null || App.PM == null) return;
                if (this.GetVisualRoot() is not Window owner) return;

                var item = await App.PM.LoadItemAsync(itemId);
                if (item == null) return;

                var catalogs = await App.PM.ReadItemCatalogsAsync();
                var dialog = new ItemEditDialog(item.Value.Name, item.Value.ItemType, item.Value.DataJson, catalogs);
                _ = dialog.ShowDialog(owner);

                var updated = await dialog.GetResultAsync();
                if (string.IsNullOrEmpty(updated)) return;
                await App.PM.SaveItemDataJsonAsync(itemId, updated);
                await _vm.Items.LoadAsync();
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnEditItemRequested", ex); }
        }

        private async void OnCreateItemRequested(ItemDraft initialDraft)
        {
            try
            {
                if (_vm == null) return;
                if (this.GetVisualRoot() is not Window owner) return;
                if (App.PM == null) return;

                var catalogs = await App.PM.ReadItemCatalogsAsync();
                var dialog = new CreateTemplateDialog(catalogs);
                _ = dialog.ShowDialog(owner);

                var result = await dialog.GetResultAsync();
                if (result == null) return;
                await _vm.Items.CreateItemAsync(result);
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnCreateItemRequested", ex); }
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
                Width = 440,
                Height = 190,
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
        private async Task<string?> PromptTitleAsync(string current)
        {
            if (this.GetVisualRoot() is not Window owner) return null;

            var tb = new TextBox { Text = current, MinWidth = 280 };
            var ok = new Button { Content = "OK", Width = 70, IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 70, IsCancel = true };

            var tcs = new TaskCompletionSource<string?>();
            ok.Click += (_, _) => tcs.TrySetResult(tb.Text);
            cancel.Click += (_, _) => tcs.TrySetResult(null);

            var dialog = new Window
            {
                Title = "Rename",
                Width = 360,
                Height = 150,
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
    }
}