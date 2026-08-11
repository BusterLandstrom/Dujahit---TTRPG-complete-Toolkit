using Dujahit.Models;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Dujahit.ViewModels
{
    public class UserInitViewModel : ViewModelBase
    {
        private MainWindowViewModel _mainWVM;
        private string _username;
        public string Username 
        {
            get => _username;
            set => this.RaiseAndSetIfChanged(ref _username, value);
        }

        public ObservableCollection<User> Users { get; set; }

        private User _selectedUser;
        public User SelectedUser 
        {
            get => _selectedUser;
            set => this.RaiseAndSetIfChanged(ref _selectedUser, value);
        }

        public ICommand SelectLogin {  get; set; }

        public ICommand SelectCreate { get; set; }

        public UserInitViewModel(MainWindowViewModel mwvm) 
        {
            _mainWVM = mwvm;

            SelectCreate = ReactiveCommand.CreateFromTask(CreateUser);

            this.WhenAnyValue(x => x.SelectedUser)
               .Where(user => user != null)
               .ObserveOn(RxApp.MainThreadScheduler)
               .Subscribe(async user =>
               {
                   await LoginUserAsync(user);
               });

            _ = LoadUsersAsync();
        }

        private async Task LoginUserAsync(User user)
        {
            try
            {
                await App.PM.AuthAcc(user.Username);
                _mainWVM.NavigateTo(new StartDashViewModel(_mainWVM));
            }
            catch (Exception ex) 
            {
                ErrorLog.Log($"Failed login", ex);
            }
        }

        private async Task LoadUsersAsync()
        {
            try
            {
                var list = await App.PM.ListMyUsers();
                Users = new ObservableCollection<User>(list);
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"Failed to list users", ex);
            }
        }

        public async Task CreateUser()
        {
            if (Username != null && Username != "")
            {
                try
                {
                    await App.PM.AuthAcc(Username);
                    _mainWVM.NavigateTo(new StartDashViewModel(_mainWVM));
                }
                catch (Exception ex) 
                {
                    ErrorLog.Log($"Failed to create User", ex);
                }
            }
        }
    }
}
