using Dujahit.Models.Application;
using ReactiveUI;
using System.Collections.ObjectModel;

namespace Dujahit.ViewModels
{
    public class NotePageTreeNode : ViewModelBase
    {
        public string Id => Page.Id;
        public NotePage Page { get; }
        public ObservableCollection<NotePageTreeNode> Children { get; } = new();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        public string DisplayTitle =>
            string.IsNullOrEmpty(Page.Icon)
                ? Page.Title
                : $"{Page.Icon}  {Page.Title}";

        public NotePageTreeNode(NotePage page) => Page = page;

        public void RefreshDisplay()
        {
            this.RaisePropertyChanged(nameof(DisplayTitle));
        }
    }
}
