using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Dujahit.ViewModels
{
    public class CreateUserViewModel : ViewModelBase
    {
        private MainWindowViewModel _mainWVM;

        private string _username;

        public string Username
        {
            get => _username;
            set => this.RaiseAndSetIfChanged(ref _username, value);
        }

        public ICommand CreateAcc {  get; set; }

        public ICommand Back { get; set; }

        public CreateUserViewModel(MainWindowViewModel mwvm) 
        {
            _mainWVM = mwvm;
            CreateAcc = ReactiveCommand.CreateFromTask(CreateAccount);
            Back = ReactiveCommand.Create(BackProcess);
        }

        public async Task CreateAccount() 
        {
            await App.PM.AuthAcc(Username);
            _mainWVM.NavigateTo(new StartDashViewModel(_mainWVM));

        }

        public void BackProcess()
        {
            _mainWVM.NavigateTo(new UserInitViewModel(_mainWVM));
        }
    }
}
