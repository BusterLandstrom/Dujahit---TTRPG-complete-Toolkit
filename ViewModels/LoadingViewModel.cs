using Avalonia.Threading;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Reactive;

namespace Dujahit.ViewModels
{
    public class LoadingViewModel : ViewModelBase
    {
        private readonly Action? _onBack;

        private string _title;
        public string Title
        {
            get => _title;
            set => this.RaiseAndSetIfChanged(ref _title, value);
        }

        private string _status = "Warming up the dice";
        public string Status
        {
            get => _status;
            set => this.RaiseAndSetIfChanged(ref _status, value);
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => this.RaiseAndSetIfChanged(ref _progress, value);
        }

        private bool _isIndeterminate = true;
        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set => this.RaiseAndSetIfChanged(ref _isIndeterminate, value);
        }

        private bool _hasFailed;
        public bool HasFailed
        {
            get => _hasFailed;
            set => this.RaiseAndSetIfChanged(ref _hasFailed, value);
        }

        public ObservableCollection<string> Log { get; } = new();

        public ReactiveCommand<Unit, Unit> BackCommand { get; }

        public LoadingViewModel(string title = "Loading", Action? onBack = null)
        {
            _title = title;
            _onBack = onBack;
            BackCommand = ReactiveCommand.Create(() => _onBack?.Invoke());
        }

        // The heavy work runs off the ui thread, so every stage bump has to hop back onto it or the bound text and bar just sits there frozen untill the whole thing finishes
        public void Report(string message, double fraction)
        {
            void apply()
            {
                IsIndeterminate = false;
                Status = message;
                Progress = Math.Clamp(fraction * 100.0, 0, 100);
                Log.Add(message);
            }

            if (Dispatcher.UIThread.CheckAccess()) apply();
            else Dispatcher.UIThread.Post(apply);
        }

        public void Fail(string message)
        {
            void apply()
            {
                IsIndeterminate = false;
                HasFailed = true;
                Status = message;
                Log.Add("Stopped here: " + message);
            }

            if (Dispatcher.UIThread.CheckAccess()) apply();
            else Dispatcher.UIThread.Post(apply);
        }
    }
}
