using Dujahit.Models;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Dujahit.ViewModels
{
    public class InitViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainWVM;
        public ICommand StartJourneyCommand { get; }

        public InitViewModel(MainWindowViewModel mwvm)
        {
            _mainWVM = mwvm;
            StartJourneyCommand = ReactiveCommand.Create(StartJourney);
        }

        public void StartJourney()
        {
            _mainWVM.NavigateTo(new UserInitViewModel(_mainWVM));
        }
    }
}
