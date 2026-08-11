using Dujahit.Models.Application;
using Dujahit.Models.Database;
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
    public class ChaptersViewModel : ViewModelBase
    {
        private readonly NotePageRepository _repo;
        private readonly string _campaignId;
        private readonly string _userId;

        public ObservableCollection<NotePageTreeNode> Chapters { get; } = new();
        public MarkdownEditorViewModel Editor { get; } = new();

        private NotePageTreeNode? _selectedNode;
        public NotePageTreeNode? SelectedNode
        {
            get => _selectedNode;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedNode, value);
                _ = OnSelectionChangedAsync(value);
                this.RaisePropertyChanged(nameof(HasSelection));
                this.RaisePropertyChanged(nameof(SelectedTitle));
            }
        }

        public bool HasSelection => _selectedNode != null;
        public string SelectedTitle => _selectedNode?.Page.Title ?? "";

        private bool _isRenaming;
        public bool IsRenaming
        {
            get => _isRenaming;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isRenaming, value);
                this.RaisePropertyChanged(nameof(IsNotRenaming));
            }
        }
        public bool IsNotRenaming => !_isRenaming;

        private string _renameDraft = "";
        public string RenameDraft
        {
            get => _renameDraft;
            set => this.RaiseAndSetIfChanged(ref _renameDraft, value);
        }

        public ReactiveCommand<Unit, Unit> BeginRenameCommand { get; }
        public ReactiveCommand<Unit, Unit> CommitRenameCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelRenameCommand { get; }

        public ReactiveCommand<Unit, Unit> AddRootCommand { get; }
        public ReactiveCommand<NotePageTreeNode?, Unit> AddChildCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
        public ReactiveCommand<Unit, Unit> RenameCommand { get; }
        public ReactiveCommand<Unit, Unit> CopyReferenceCommand { get; }

        public event Func<string, Task<string?>>? PromptTitleAsync;
        public event Func<string, string, Task<bool>>? ConfirmAsync;
        public event Func<string, Task>? CopyToClipboardRequested;

        public ChaptersViewModel(NotePageRepository repo, string campaignId, string userId)
        {
            _repo = repo;
            _campaignId = campaignId;
            _userId = userId;

            Editor.MarkdownChanged += async _ => await PersistEditAsync();

            // Codex not note, they are seperate things even though the row is the same.
            CopyReferenceCommand = ReactiveCommand.Create(() =>
            {
                var id = SelectedNode?.Page.Id;
                if (!string.IsNullOrEmpty(id)) CopyToClipboardRequested?.Invoke($"<ref type=\"codex\" id=\"{id}\"/>");
            });

            AddRootCommand = ReactiveCommand.CreateFromTask(AddRootAsync);
            AddChildCommand = ReactiveCommand.CreateFromTask<NotePageTreeNode?>(AddChildAsync);
            DeleteCommand = ReactiveCommand.CreateFromTask(DeleteAsync);
            RenameCommand = ReactiveCommand.CreateFromTask(RenameAsync);
            BeginRenameCommand = ReactiveCommand.Create(BeginRename);
            CommitRenameCommand = ReactiveCommand.CreateFromTask(CommitRenameAsync);
            CancelRenameCommand = ReactiveCommand.Create(() => { IsRenaming = false; });
        }

        public async Task LoadAsync()
        {
            Chapters.Clear();
            var tree = await _repo.GetVisibleTreeAsync(_campaignId, _userId, isDm: true);
            foreach (var root in tree)
                if (root.Page.Scope == NotePageScope.CampaignStory)
                    Chapters.Add(ToObservable(root));
        }

        private static NotePageTreeNode ToObservable(NotePageNode src)
        {
            var node = new NotePageTreeNode(src.Page);
            foreach (var c in src.Children)
                node.Children.Add(ToObservable(c));
            return node;
        }

        private async Task OnSelectionChangedAsync(NotePageTreeNode? node)
        {
            if (node == null)
            {
                Editor.Reset("");
                Editor.IsEditing = false;
                return;
            }

            var fresh = await _repo.GetByIdAsync(node.Page.Id);
            if (fresh != null)
            {
                node.Page.ContentMarkdown = fresh.ContentMarkdown;
                node.Page.Title = fresh.Title;
                node.Page.RevisionNumber = fresh.RevisionNumber;
                node.RefreshDisplay();
            }
            Editor.IsEditing = false;
            Editor.Reset(node.Page.ContentMarkdown);
        }

        private async Task AddRootAsync()
        {
            var page = await _repo.CreateAsync(
                _campaignId, ownerUserId: null,
                scope: NotePageScope.CampaignStory,
                title: "Untitled Chapter");
            var node = new NotePageTreeNode(page);
            Chapters.Add(node);
            SelectedNode = node;
            await _repo.RecordChangeAsync(_campaignId, page.Id, "added", page.RevisionNumber, page);
            await App.PM.ComController.NotifyPageChangedAsync(page.Id, "added");
        }

        private async Task AddChildAsync(NotePageTreeNode? under)
        {
            var host = under ?? _selectedNode;
            if (host == null) return;
            var parent = host.Page;
            var page = await _repo.CreateAsync(
                _campaignId, ownerUserId: null,
                scope: NotePageScope.CampaignStory,
                title: "Untitled Page",
                parentPageId: parent.Id);
            var node = new NotePageTreeNode(page);
            host.Children.Add(node);
            host.IsExpanded = true;
            SelectedNode = node;
            await _repo.RecordChangeAsync(_campaignId, page.Id, "added", page.RevisionNumber, page);
            await App.PM.ComController.NotifyPageChangedAsync(page.Id, "added");
        }

        private async Task DeleteAsync()
        {
            if (_selectedNode == null) return;
            var childCount = await _repo.CountDescendantsAsync(_selectedNode.Page.Id);
            if (ConfirmAsync != null)
            {
                var msg = childCount == 0
                    ? $"Delete \"{_selectedNode.Page.Title}\"?"
                    : $"Delete \"{_selectedNode.Page.Title}\" and {childCount} sub-page" +
                        (childCount == 1 ? "" : "s") + "?";
                var ok = await ConfirmAsync("Delete chapter", msg);
                if (!ok) return;
            }

            var page = _selectedNode.Page;
            await _repo.DeleteAsync(page.Id);
            await _repo.RecordChangeAsync(_campaignId, page.Id, "removed", page.RevisionNumber, null);
            await App.PM.ComController.NotifyPageChangedAsync(page.Id, "removed");

            RemoveNode(page.Id, Chapters);
            SelectedNode = null;
        }

        public void SelectPageById(string id)
        {
            var node = FindNode(id, Chapters);
            if (node != null) SelectedNode = node;
        }

        private static NotePageTreeNode? FindNode(string id, ObservableCollection<NotePageTreeNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n.Page.Id == id) return n;
                var hit = FindNode(id, n.Children);
                if (hit != null) return hit;
            }
            return null;
        }

        private static bool RemoveNode(string id, ObservableCollection<NotePageTreeNode> nodes)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Id == id) { nodes.RemoveAt(i); return true; }
                if (RemoveNode(id, nodes[i].Children)) return true;
            }
            return false;
        }

        private void BeginRename()
        {
            if (_selectedNode == null) return;
            RenameDraft = _selectedNode.Page.Title;
            IsRenaming = true;
        }

        private async Task CommitRenameAsync()
        {
            if (!_isRenaming) return;
            IsRenaming = false;

            if (_selectedNode == null) return;
            var newTitle = (RenameDraft ?? "").Trim();
            if (newTitle.Length == 0 || newTitle == _selectedNode.Page.Title) return;
            await ApplyTitleAsync(newTitle);
        }

        private async Task ApplyTitleAsync(string newTitle)
        {
            var node = _selectedNode;
            if (node == null) return;
            var rev = await _repo.UpdateContentAsync(node.Page.Id, newTitle, null, null);
            node.Page.Title = newTitle;
            node.Page.RevisionNumber = rev;
            node.RefreshDisplay();
            this.RaisePropertyChanged(nameof(SelectedTitle));
            await _repo.RecordChangeAsync(_campaignId, node.Page.Id, "updated", rev, node.Page);
            await App.PM.ComController.NotifyPageChangedAsync(node.Page.Id, "updated");
        }

        private async Task RenameAsync()
        {
            if (_selectedNode == null || PromptTitleAsync == null) return;
            var newTitle = await PromptTitleAsync(_selectedNode.Page.Title);
            if (string.IsNullOrWhiteSpace(newTitle)) return;
            await ApplyTitleAsync(newTitle);
        }

        private DateTime _lastSave = DateTime.MinValue;
        private string? _pending;

        private async Task PersistEditAsync()
        {
            if (_selectedNode == null) return;
            _pending = Editor.Markdown;
            if ((DateTime.UtcNow - _lastSave).TotalMilliseconds < 750)
            {
                await Task.Delay(800);
                if (_pending != Editor.Markdown) return;
            }
            _lastSave = DateTime.UtcNow;

            var page = _selectedNode.Page;
            var rev = await _repo.UpdateContentAsync(page.Id, null, null, Editor.Markdown);
            if (rev > 0)
            {
                page.ContentMarkdown = Editor.Markdown;
                page.RevisionNumber = rev;
                Editor.AcceptBaseline();
                await _repo.RecordChangeAsync(_campaignId, page.Id, "updated", rev, page);
                await App.PM.ComController.NotifyPageChangedAsync(page.Id, "updated");
            }
        }
    }
}
