using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Dujahit.Models.Application;
using Dujahit.ViewModels;
using Markdown.Avalonia;
using ReactiveUI;
using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows.Input;

namespace Dujahit.Views
{
    public partial class MarkdownEditorView : UserControl
    {
        public MarkdownEditorView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private MarkdownEditorViewModel? _vm;
        private TextBox? _sourceBox;
        private MarkdownScrollViewer? _viewer;

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            HookSelectionTracking();
            HookHyperlinkRouting();
            _ = RefreshRender();
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_vm != null)
            {
                _vm.PropertyChanged -= OnVmPropertyChanged;
                _vm.SelectionChangeRequested -= OnSelectionChangeRequested;
            }

            _vm = DataContext as MarkdownEditorViewModel;
            if (_vm == null) return;

            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.SelectionChangeRequested += OnSelectionChangeRequested;

            _ = RefreshRender();
        }

        private void HookHyperlinkRouting()
        {
            _viewer ??= this.FindControl<MarkdownScrollViewer>("MdViewer");
            if (_viewer == null) return;

            // HyperlinkCommand lives on the Engine (Markdown class), NOT on MarkdownScrollViewer itself. Cast through the interface to get there.
            if (_viewer.Engine is not Markdown.Avalonia.Markdown mdEngine)
                return;

            mdEngine.HyperlinkCommand = new RelayCommand(uriObj =>
            {
                var s = uriObj?.ToString();
                if (string.IsNullOrEmpty(s)) return;

                if (!s.StartsWith("dujahit://ref/", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        Process.Start(
                            new ProcessStartInfo(s)
                            { UseShellExecute = true });
                    }
                    catch { }
                    return;
                }

                var rest = s["dujahit://ref/".Length..];
                var slash = rest.IndexOf('/');
                if (slash <= 0) return;

                var type = rest[..slash];
                var id = rest[(slash + 1)..];
                RefResolver.RaiseNavigateRequested(type, id);
            });
        }

        private void HookSelectionTracking()
        {
            if (_sourceBox != null)
                _sourceBox.PropertyChanged -= OnSourceBoxPropertyChanged;

            _sourceBox = this.FindControl<TextBox>("SourceBox");
            _viewer = this.FindControl<MarkdownScrollViewer>("MdViewer");

            if (_sourceBox != null)
            {
                _sourceBox.PropertyChanged += OnSourceBoxPropertyChanged;
                MarkdownListEditing.Attach(_sourceBox);
                _sourceBox.RemoveHandler(InputElement.KeyDownEvent, OnSourceShortcut);
                _sourceBox.AddHandler(InputElement.KeyDownEvent, OnSourceShortcut, RoutingStrategies.Tunnel);
            }
        }

        private void OnSourceShortcut(object? sender, KeyEventArgs e)
        {
            if (_vm == null || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            switch (e.Key)
            {
                case Key.B when !shift: _vm.BoldCommand.Execute().Subscribe(); break;
                case Key.I when !shift: _vm.ItalicCommand.Execute().Subscribe(); break;
                case Key.K when !shift: _vm.LinkCommand.Execute().Subscribe(); break;
                case Key.E when !shift: _vm.InlineCodeCommand.Execute().Subscribe(); break;
                case Key.X when shift: _vm.StrikeCommand.Execute().Subscribe(); break;
                case Key.D1 when !shift: _vm.HeadingCommand.Execute("# ").Subscribe(); break;
                case Key.D2 when !shift: _vm.HeadingCommand.Execute("## ").Subscribe(); break;
                case Key.D3 when !shift: _vm.HeadingCommand.Execute("### ").Subscribe(); break;
                case Key.D7 when shift: _vm.NumberedListCommand.Execute().Subscribe(); break;
                case Key.D8 when shift: _vm.BulletListCommand.Execute().Subscribe(); break;
                case Key.D9 when shift: _vm.TaskListCommand.Execute().Subscribe(); break;
                default: return;
            }
            e.Handled = true;
        }

        private void OnSourceBoxPropertyChanged(object? sender,
            Avalonia.AvaloniaPropertyChangedEventArgs e)
        {
            if (_vm == null || _sourceBox == null) return;
            if (e.Property != TextBox.SelectionStartProperty &&
                e.Property != TextBox.SelectionEndProperty) return;

            var start = Math.Min(_sourceBox.SelectionStart, _sourceBox.SelectionEnd);
            var end = Math.Max(_sourceBox.SelectionStart, _sourceBox.SelectionEnd);
            _vm.SelectionStart = start;
            _vm.SelectionLength = end - start;
        }

        private void OnVmPropertyChanged(object? sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MarkdownEditorViewModel.Markdown) ||
                e.PropertyName == nameof(MarkdownEditorViewModel.IsEditing))
            {
                _ = RefreshRender();
            }
            else if (e.PropertyName == nameof(MarkdownEditorViewModel.RefPickerOpen) && _vm?.RefPickerOpen == true)
            {
                Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("RefSearchBox")?.Focus());
            }
        }

        private async Task RefreshRender()
        {
            if (_vm == null) return;
            _viewer ??= this.FindControl<MarkdownScrollViewer>("MdViewer");
            if (_viewer == null) return;

            _viewer.Markdown = await MarkdownEditorViewModel.PreRenderAsync(_vm.Markdown, _vm.ViewerOwnsPage);
        }


        private void OnSelectionChangeRequested(int start, int length)
        {
            _sourceBox ??= this.FindControl<TextBox>("SourceBox");
            if (_sourceBox == null) return;
            _sourceBox.SelectionStart = start;
            _sourceBox.SelectionEnd = start + length;
            _sourceBox.Focus();
        }

        private sealed class RelayCommand : ICommand
        {
            private readonly Action<object?> _exec;
            public RelayCommand(Action<object?> exec) { _exec = exec; }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => _exec(parameter);
            public event EventHandler? CanExecuteChanged;
        }

    }
}