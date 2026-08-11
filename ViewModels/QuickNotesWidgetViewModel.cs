using ReactiveUI;
using System.Reactive;

namespace Dujahit.ViewModels
{
    public class QuickNotesWidgetViewModel : ViewModelBase
    {
        public QuickNotesPanelViewModel Panel { get; }

        private bool _isOpen = false;
        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                this.RaiseAndSetIfChanged(ref _isOpen, value);
                this.RaisePropertyChanged(nameof(WidgetHeight));
                this.RaisePropertyChanged(nameof(WidgetWidth));
                this.RaisePropertyChanged(nameof(ToggleGlyph));
            }
        }

        public double WidgetHeight => IsOpen ? 420 : 30;
        public double WidgetWidth => IsOpen ? 360 : 150;

        public string ToggleGlyph => IsOpen ? "▼" : "▲";

        public ReactiveCommand<Unit, Unit> ToggleCommand { get; }

        public QuickNotesWidgetViewModel(QuickNotesPanelViewModel panel)
        {
            Panel = panel;
            ToggleCommand = ReactiveCommand.Create(() => { IsOpen = !IsOpen; });
        }
    }
}