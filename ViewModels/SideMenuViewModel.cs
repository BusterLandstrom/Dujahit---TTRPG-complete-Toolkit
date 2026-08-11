using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;

namespace Dujahit.ViewModels
{
    public class SideMenuViewModel : ViewModelBase
    {
        private string _campaignName;
        public string CampaignName
        {
            get => _campaignName;
            set => this.RaiseAndSetIfChanged(ref _campaignName, value);
        }

        public ObservableCollection<NavItem> NavItems { get; }

        public ReactiveCommand<Unit, Unit>? SearchCommand { get; }
        public bool CanSearch => SearchCommand != null;

        public ReactiveCommand<Unit, Unit>? SettingsCommand { get; }
        public bool CanOpenSettings => SettingsCommand != null;

        public ReactiveCommand<Unit, Unit>? LeaveCommand { get; }
        public bool CanLeave => LeaveCommand != null;

        public SideMenuViewModel(string campaignName, ObservableCollection<NavItem> navItems, Action? onSearch = null, Action? onSettings = null, Func<Task>? onLeave = null)
        {
            _campaignName = campaignName;
            NavItems = navItems;

            if (onSearch != null) SearchCommand = ReactiveCommand.Create(onSearch);
            if (onSettings != null) SettingsCommand = ReactiveCommand.Create(onSettings);
            if (onLeave != null) LeaveCommand = ReactiveCommand.CreateFromTask(onLeave);

            foreach (var item in NavItems)
                item.NavigateCommand.Subscribe(_ => Select(item));
        }

        public void Highlight(NavItem chosen) => Select(chosen);

        private void Select(NavItem chosen)
        {
            foreach (var item in NavItems) item.IsSelected = item == chosen;
        }
    }
}
