using Avalonia.Controls;
using Avalonia.Layout;
using Dujahit.Models.Application;
using Dujahit.Models.Communication;
using Dujahit.Models.Database;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Dujahit.Models;
using Avalonia.Threading;

namespace Dujahit.ViewModels
{
    public class NotesViewModel : ViewModelBase
    {
        private readonly NotePageRepository _repo;
        private readonly string _campaignId;
        private readonly string _userId;
        private readonly bool _isDm;

        public ObservableCollection<NotePageTreeNode> MyNotes { get; } = new();
        public ObservableCollection<NotePageTreeNode> SharedNotes { get; } = new();
        public QuickNotesPanelViewModel QuickNotes { get; }

        public ObservableCollection<BacklinkEntry> Backlinks { get; } = new();

        private bool _hasBacklinks;
        public bool HasBacklinks
        {
            get => _hasBacklinks;
            private set => this.RaiseAndSetIfChanged(ref _hasBacklinks, value);
        }

        public MarkdownEditorViewModel Editor { get; } = new();

        private NotePageTreeNode? _selectedNode;
        private bool _isNewPage; // Fresh note, skip the re-read and the whole-corpus backlink scan, it can't have either yet.
        public NotePageTreeNode? SelectedNode
        {
            get => _selectedNode;
            set
            {
                var previous = _selectedNode;
                var pending = previous != null && !ReferenceEquals(previous, value) && Editor.IsDirty ? Editor.Markdown : null;
                this.RaiseAndSetIfChanged(ref _selectedNode, value);
                if (pending != null) _ = FlushPendingEditAsync(previous!, pending);
                _ = OnSelectionChangedAsync(value);
                this.RaisePropertyChanged(nameof(HasSelection));
                this.RaisePropertyChanged(nameof(CanEditSelection));
                this.RaisePropertyChanged(nameof(CanShareSelection));
                this.RaisePropertyChanged(nameof(SelectedTitle));
                this.RaisePropertyChanged(nameof(SelectedScopeLabel));
            }
        }

        private async Task FlushPendingEditAsync(NotePageTreeNode node, string text)
        {
            var page = node.Page;
            var mayEdit = (page.Scope != NotePageScope.CampaignStory || _isDm)
                && (page.Scope != NotePageScope.Private || page.OwnerUserId == _userId);
            if (!mayEdit) return;
            await _crdtGate.WaitAsync();
            try
            {
                var rev = await _repo.UpdateContentAsync(page.Id, null, null, text);
                page.ContentMarkdown = text;
                page.RevisionNumber = rev;

                // Leaving a page mid sentence has to reach the document as well, the markdown column on its own is not what the next open reads from.
                if (_crdt != null && string.Equals(_crdtPageId, page.Id, StringComparison.Ordinal))
                {
                    var before = _crdt.StateVector();
                    if (_crdt.ApplyLocalText(text))
                    {
                        var delta = _crdt.DiffAgainst(before);
                        await _repo.AppendCrdtUpdateAsync(page.Id, delta, _crdt.Text, _crdt.HasEverHeldText);
                        await App.PM.ComController.SendNoteUpdateAsync(page.Id, delta);
                    }
                }

                await _repo.RecordChangeAsync(_campaignId, page.Id, "updated", rev, page);
                await App.PM.ComController.NotifyPageChangedAsync(page, "updated");
            }
            catch (Exception ex)
            {
                ErrorLog.Log("[Notes] flushing an unsaved edit failed, the last keystrokes may be gone", ex);
            }
            finally { _crdtGate.Release(); }
        }

        public bool HasSelection => _selectedNode != null;
        public bool CanEditSelection =>
            _selectedNode != null &&
            (_selectedNode.Page.Scope != NotePageScope.CampaignStory || _isDm) &&
            (_selectedNode.Page.Scope != NotePageScope.Private || _selectedNode.Page.OwnerUserId == _userId);

        public bool CanShareSelection =>
            _selectedNode != null
            && (_selectedNode.Page.Scope == NotePageScope.Shared || _selectedNode.Page.Scope == NotePageScope.Private)
            && string.Equals(_selectedNode.Page.OwnerUserId, _userId, StringComparison.Ordinal);

        public string SelectedTitle => _selectedNode?.Page.Title ?? "";
        public string SelectedScopeLabel => _selectedNode?.Page.Scope switch
        {
            NotePageScope.Private => "Private",
            NotePageScope.Shared => "Shared",
            NotePageScope.CampaignStory => "Campaign Story",
            _ => ""
        };

        private bool _selectedIsPinned;
        public bool SelectedIsPinned
        {
            get => _selectedIsPinned;
            private set
            {
                this.RaiseAndSetIfChanged(ref _selectedIsPinned, value);
                this.RaisePropertyChanged(nameof(PinButtonLabel));
            }
        }

        public string PinButtonLabel => _selectedIsPinned ? "Unpin" : "Pin to dashboard";

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

        public bool IsDm => _isDm;

        public ReactiveCommand<string, Unit> AddRootPageCommand { get; }
        public ReactiveCommand<NotePageTreeNode?, Unit> AddChildPageCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteSelectedCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> BeginRenameCommand { get; }
        public ReactiveCommand<Unit, Unit> CommitRenameCommand { get; }
        public ReactiveCommand<Unit, bool> CancelRenameCommand { get; }
        public ReactiveCommand<Unit, Unit> ShareSelectedCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleDashboardPinCommand { get; }
        public ReactiveCommand<Unit, Unit> CopyReferenceCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportMarkdownCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportPdfCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportZipCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveImportExampleCommand { get; }

        public event Action<NotePage>? ShareRequested;
        public event Func<string, Task>? CopyToClipboardRequested;
        public event Action<string>? ExportRequested;
        public event Action? ImportRequested;
        public event Action? ImportExampleRequested;

        public event Func<string, Task<string?>>? PromptTitleAsync;

        public event Func<string, string, Task<bool>>? ConfirmAsync;

        private DateTime _lastSave = DateTime.MinValue;
        private string? _pendingMarkdown;
        private bool _applyingRemote;
        private NoteCrdt? _crdt;
        private string _crdtPageId = "";

        /* Every one of these paths reads the editor, awaits something slow like a ref rewrite or a round trip, then writes the document back.
           Without a queue a remote edit lands inside somebody else's await and the text that gets written back is the version from before it
           arrived, which does not just lose their letters on screen, the next diff reads them as mine to delete and sends that out to everybody.
        */
        private readonly SemaphoreSlim _crdtGate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, string> _pendingTitles = new();

        public NotesViewModel(NotePageRepository repo, string campaignId, string userId, bool isDm, QuickNotesPanelViewModel quickNotes)
        {
            _repo = repo;
            _campaignId = campaignId;
            _userId = userId;
            _isDm = isDm;
            QuickNotes = quickNotes;

            Editor.MarkdownChanged += async _ =>
            {
                if (_applyingRemote) return;
                if (_selectedNode == null) return;
                if (!CanEditSelection) return;
                await PersistEditAsync();
            };

            AddRootPageCommand = ReactiveCommand.CreateFromTask<string>(AddRootAsync);
            AddChildPageCommand = ReactiveCommand.CreateFromTask<NotePageTreeNode?>(AddChildAsync);
            DeleteSelectedCommand = ReactiveCommand.CreateFromTask(DeleteSelectedAsync);
            SaveCommand = ReactiveCommand.CreateFromTask(PersistEditAsync);
            BeginRenameCommand = ReactiveCommand.Create(BeginRename);
            CommitRenameCommand = ReactiveCommand.CreateFromTask(CommitRenameAsync);
            CancelRenameCommand = ReactiveCommand.Create(() => IsRenaming = false);
            ToggleDashboardPinCommand = ReactiveCommand.CreateFromTask(ToggleDashboardPinAsync);
            ShareSelectedCommand = ReactiveCommand.Create(() =>
            {
                if (_selectedNode != null) ShareRequested?.Invoke(_selectedNode.Page);
            });

            CopyReferenceCommand = ReactiveCommand.Create(() =>
            {
                var id = _selectedNode?.Page.Id;
                if (!string.IsNullOrEmpty(id)) CopyToClipboardRequested?.Invoke($"<ref type=\"note\" id=\"{id}\"/>");
            });

            ExportMarkdownCommand = ReactiveCommand.Create(() => { if (_selectedNode != null) ExportRequested?.Invoke("md"); });
            ExportPdfCommand = ReactiveCommand.Create(() => { if (_selectedNode != null) ExportRequested?.Invoke("pdf"); });
            ImportZipCommand = ReactiveCommand.Create(() => ImportRequested?.Invoke());
            SaveImportExampleCommand = ReactiveCommand.Create(() => ImportExampleRequested?.Invoke());

            Editor.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(MarkdownEditorViewModel.IsEditing) || Editor.IsEditing) return;
                if (Editor.IsDirty && _selectedNode != null && CanEditSelection) _ = PersistEditAsync();
                _ = SendPresenceAsync(false);
            };

            _presenceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _presenceTimer.Tick += (_, _) => PresenceTick();
            _presenceTimer.Start();
        }

        public ObservableCollection<CollaboratorPresence> Collaborators { get; } = new();
        public bool HasCollaborators => Collaborators.Count > 0;

        private string _noteCollision = "";
        public string NoteCollision
        {
            get => _noteCollision;
            set { this.RaiseAndSetIfChanged(ref _noteCollision, value ?? ""); this.RaisePropertyChanged(nameof(HasNoteCollision)); }
        }

        public bool HasNoteCollision => _noteCollision.Length > 0;
        private readonly DispatcherTimer _presenceTimer;
        private bool _presenceAnnounced;

        private void PresenceTick()
        {
            var now = DateTime.UtcNow;
            for (var i = Collaborators.Count - 1; i >= 0; i--)
                if ((now - Collaborators[i].LastSeen).TotalSeconds > 8) Collaborators.RemoveAt(i);
            this.RaisePropertyChanged(nameof(HasCollaborators));

            if (Editor.IsEditing && _selectedNode != null && _selectedNode.Page.Scope != NotePageScope.Private)
                _ = SendPresenceAsync(true);
            else if (_presenceAnnounced)
                _ = SendPresenceAsync(false);
        }

        private async Task SendPresenceAsync(bool editing)
        {
            var node = _selectedNode;
            if (App.PM?.ComController == null || node == null) return;
            if (!editing && !_presenceAnnounced) return;
            _presenceAnnounced = editing;

            var text = Editor.Markdown ?? "";
            var upTo = Math.Min(Math.Max(Editor.SelectionStart, 0), text.Length);
            var line = 1;
            for (var i = 0; i < upTo; i++) if (text[i] == '\n') line++;

            // The chip wears the player's character color, the dm has no character so their color is the settings one.
            var color = _isDm
                ? App.PM.DmPresenceColor
                : App.PM.CurrentCharacterService?.Current?.ColorHex is { Length: > 0 } cc
                    ? cc
                    : App.PM.ComController.GetColorForUser(_userId);

            try
            {
                await App.PM.ComController.SendNotePresenceAsync(
                    new NotePresenceMessage(node.Page.Id, App.PM.GetUID(), App.PM.GetUsername(), line, editing, color));
            }
            catch (Exception ex)
            {
                ErrorLog.Log("[Notes] presence send failed", ex);
            }
        }

        public void ApplyPresence(NotePresenceMessage p)
        {
            if (p == null || string.Equals(p.UserId, _userId, StringComparison.Ordinal)) return;
            var existing = Collaborators.FirstOrDefault(c => c.UserId == p.UserId);
            var onThisPage = p.IsEditing && _selectedNode?.Id == p.PageId;
            if (!onThisPage)
            {
                if (existing != null) Collaborators.Remove(existing);
            }
            else if (existing == null)
            {
                var color = !string.IsNullOrWhiteSpace(p.ColorHex)
                    ? p.ColorHex
                    : App.PM?.ComController?.GetColorForUser(p.UserId) ?? "#FFD700";
                Collaborators.Add(new CollaboratorPresence(p.UserId, p.Username, color)
                { Line = p.Line, LastSeen = DateTime.UtcNow });
            }
            else
            {
                existing.Line = p.Line;
                existing.LastSeen = DateTime.UtcNow;
            }
            this.RaisePropertyChanged(nameof(HasCollaborators));
        }

        public string SelectedExportName => string.IsNullOrWhiteSpace(SelectedTitle) ? "note" : SelectedTitle;

        // The selected page drags its subpages along and depth becomes the heading level
        public async Task ExportToAsync(string format, string path)
        {
            if (_selectedNode == null || string.IsNullOrEmpty(path)) return;
            try
            {
                var pages = await BuildExportPagesAsync(_selectedNode);
                if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
                    await Task.Run(() => Models.Application.NoteExporter.ToPdf(path, pages));
                else
                    await File.WriteAllTextAsync(path, Models.Application.NoteExporter.ToMarkdown(pages));
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Exporting a note failed", ex);
                NavItem.NavError?.Invoke("Couldn't export that note, the log has the details.");
            }
        }

        public async Task SaveImportExampleAsync(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                await Task.Run(() => NoteImporter.WriteExampleZip(path));
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Writing the import example failed", ex);
                NavItem.NavError?.Invoke("Couldn't write the example zip, the log has the details.");
            }
        }

        /* Folders become pages and what is inside them becomes their subpages, so the zip's shape is the tree's shape.
           A selected page takes the import as its children, otherwise it lands at the root of my own notes.
        */
        public async Task ImportFromZipAsync(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var parsed = await Task.Run(() => NoteImporter.ReadZip(path));

                foreach (var w in parsed.Warnings) ErrorLog.Log("[note import] " + w);

                if (parsed.PageCount == 0)
                {
                    NavItem.NavError?.Invoke(parsed.Warnings.Count > 0 ? parsed.Warnings[0] : "That zip had no markdown in it.");
                    return;
                }

                var host = _selectedNode;
                var scope = host?.Page.Scope ?? NotePageScope.Private;
                if (scope == NotePageScope.CampaignStory && !_isDm) scope = NotePageScope.Private;
                var owner = scope == NotePageScope.CampaignStory ? null : _userId;

                var made = 0;
                async Task Walk(List<ImportedNote> notes, string? parentId)
                {
                    foreach (var n in notes)
                    {
                        var page = await _repo.CreateAsync(_campaignId, owner, scope, n.Title, parentPageId: parentId);
                        var rev = page.RevisionNumber;
                        if (n.Markdown.Length > 0)
                        {
                            rev = await _repo.UpdateContentAsync(page.Id, null, null, n.Markdown);
                            page.ContentMarkdown = n.Markdown;
                            page.RevisionNumber = rev;
                        }
                        made++;
                        _ = _repo.RecordChangeAsync(_campaignId, page.Id, "added", rev, page);
                        _ = App.PM.ComController.NotifyPageChangedAsync(page, "added");
                        await Walk(n.Children, page.Id);
                    }
                }

                await Walk(parsed.Roots, host?.Page.Id);

                await LoadAsync();
                if (host != null) host.IsExpanded = true;

                var msg = "Imported " + made + (made == 1 ? " page" : " pages");
                if (parsed.Warnings.Count > 0) msg += ", " + parsed.Warnings.Count + " skipped, see the log";
                NavItem.NavError?.Invoke(msg + ".");
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Importing notes from a zip failed", ex);
                NavItem.NavError?.Invoke("Couldn't import that zip, the log has the details.");
            }
        }

        private async Task<List<Models.Application.NoteExportPage>> BuildExportPagesAsync(NotePageTreeNode root)
        {
            var pages = new List<Models.Application.NoteExportPage>();
            async Task Walk(NotePageTreeNode node, int depth)
            {
                var fresh = await _repo.GetByIdAsync(node.Page.Id);
                var title = fresh?.Title ?? node.Page.Title;
                var content = fresh?.ContentMarkdown ?? node.Page.ContentMarkdown;
                pages.Add(new Models.Application.NoteExportPage(depth, title, content ?? ""));
                foreach (var child in node.Children) await Walk(child, depth + 1);
            }
            await Walk(root, 0);
            return pages;
        }

        public async Task LoadAsync()
        {
            MyNotes.Clear();
            SharedNotes.Clear();

            var tree = await _repo.GetVisibleTreeAsync(_campaignId, _userId, _isDm);
            foreach (var root in tree)
            {
                var node = ToObservable(root);
                switch(root.Page.Scope)
                {
                   case NotePageScope.Private: MyNotes.Add(node); break;
                   case NotePageScope.Shared:  SharedNotes.Add(node); break;
                }

            }
        }

        public async Task ReloadAfterShareAsync(string pageId)
        {
            await LoadAsync();
            var node = FindNode(pageId, MyNotes) ?? FindNode(pageId, SharedNotes);
            if (node != null) SelectedNode = node;
        }

        private static NotePageTreeNode? FindNode(string id, ObservableCollection<NotePageTreeNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n.Id == id) return n;
                var hit = FindNode(id, n.Children);
                if (hit != null) return hit;
            }
            return null;
        }

        private static NotePageTreeNode ToObservable(NotePageNode src)
        {
            var node = new NotePageTreeNode(src.Page);
            foreach (var c in src.Children)
                node.Children.Add(ToObservable(c));
            return node;
        }

        private void LoadIntoEditor(string text)
        {
            _applyingRemote = true;
            try { Editor.Reset(text); }
            finally { _applyingRemote = false; }
            NoteCollision = ThreeWayMerge.HasCollision(text)
                ? "This page still has a collision on it, both versions are between the arrow markers, delete the one you do not want."
                : "";
        }

        private void CloseCrdt()
        {
            _crdt?.Dispose();
            _crdt = null;
            _crdtPageId = "";
        }

        /* Opening a page has to do three things in order:
           A. a page written before any of this existed has no state at all, so its markdown is what the document gets seeded from
           B. the host is asked for whatever it has that we do not, which is the same request a reconnect makes
           C. the editor only gets reset once, at the end, so the caret does not jump twice on open
        */
        private async Task OpenCrdtForAsync(NotePage page)
        {
            if (page == null) return;
            await _crdtGate.WaitAsync();
            try
            {
                await OpenCrdtCoreAsync(page);
            }
            finally { _crdtGate.Release(); }
        }

        private async Task OpenCrdtCoreAsync(NotePage page)
        {
            CloseCrdt();

            var stored = await _repo.LoadCrdtStateAsync(page.Id);
            var tail = await _repo.LoadCrdtUpdatesAsync(page.Id);

            // Seeding from the markdown column while any history exists writes a second copy of the same words next to the first, so history wins whenever there is any
            var seeded = (stored == null || stored.Length == 0) && tail.Count == 0;
            _crdt = seeded
                ? NoteCrdt.FromText(page.ContentMarkdown ?? "")
                : NoteCrdt.FromState(stored, tail);
            _crdtPageId = page.Id;

            if (seeded)
                await _repo.SaveCrdtStateAsync(page.Id, _crdt.FullState(), _crdt.Text);

            // The markdown column only seeds a document that has never held anything, because once there is history the column is just a projection of it, and letting it win is how a page somebody cleared on purpose comes back from the dead.
            var onDisk = page.ContentMarkdown ?? "";
            if (onDisk.Length > 0 && !_crdt.HasEverHeldText && _crdt.ApplyLocalText(onDisk))
                await _repo.SaveCrdtStateAsync(page.Id, _crdt.FullState(), _crdt.Text);

            await CatchUpAsync(page.Id, 0);
            await _repo.CompactIfNeededAsync(page.Id);

            page.ContentMarkdown = _crdt.Text;
            LoadIntoEditor(_crdt.Text);
        }

        private async Task<int> CatchUpAsync(string pageId, int caret)
        {
            if (_crdt == null || App.PM?.ComController == null) return caret;
            var delta = await App.PM.ComController.RequestNoteCatchUpAsync(pageId, _crdt.StateVector());
            if (delta.Length == 0) return caret;
            if (!_crdt.ApplyUpdate(delta, caret, out var moved)) return caret;
            await _repo.AppendCrdtUpdateAsync(pageId, delta, _crdt.Text, _crdt.HasEverHeldText);
            return moved;
        }

        public async Task ResyncAfterReconnectAsync()
        {
            if (_crdt == null || string.IsNullOrEmpty(_crdtPageId)) return;
            await _crdtGate.WaitAsync();
            try
            {
                if (_crdt == null || string.IsNullOrEmpty(_crdtPageId)) return;

                // Whatever I typed since the last save is only in the editor, it has to go in before the catch up or the reconnect hands me back the older text
                var onScreen = _selectedNode?.Page.Id == _crdtPageId;
                if (onScreen && !string.Equals(Editor.Markdown, _crdt.Text, StringComparison.Ordinal))
                    _crdt.ApplyLocalText(Editor.Markdown);

                var caret = await CatchUpAsync(_crdtPageId, onScreen ? Editor.SelectionStart : 0);
                var merged = _crdt.Text;
                if (!onScreen || string.Equals(merged, Editor.Markdown, StringComparison.Ordinal)) return;

                _applyingRemote = true;
                try
                {
                    Editor.Markdown = merged;
                    Editor.AcceptBaseline(merged);
                    Editor.MoveCaretTo(caret);
                }
                finally { _applyingRemote = false; }
            }
            finally { _crdtGate.Release(); }
        }

        public async Task ApplyRemoteNoteUpdateAsync(NoteUpdatePayload payload)
        {
            if (payload == null || _crdt == null) return;
            if (!string.Equals(payload.PageId, _crdtPageId, StringComparison.Ordinal)) return;
            if (string.Equals(payload.FromUserId, _userId, StringComparison.Ordinal)) return;

            await _crdtGate.WaitAsync();
            try
            {
                if (_crdt == null || !string.Equals(payload.PageId, _crdtPageId, StringComparison.Ordinal)) return;

                var onScreen = _selectedNode?.Page.Id == payload.PageId;

                byte[]? unsent = null;
                if (onScreen && !string.Equals(Editor.Markdown, _crdt.Text, StringComparison.Ordinal))
                {
                    var mineBefore = _crdt.StateVector();
                    if (_crdt.ApplyLocalText(Editor.Markdown)) unsent = _crdt.DiffAgainst(mineBefore);
                }

                var moved = _crdt.ApplyUpdate(payload.Update, onScreen ? Editor.SelectionStart : 0, out var caret);
                if (!moved && unsent == null) return;

                var merged = _crdt.Text;
                var node = FindNode(payload.PageId);
                if (node != null) node.Page.ContentMarkdown = merged;

                if (onScreen)
                {
                    _applyingRemote = true;
                    try
                    {
                        Editor.Markdown = merged;
                        Editor.AcceptBaseline(merged);
                        Editor.MoveCaretTo(caret);
                    }
                    finally { _applyingRemote = false; }
                }

                if (unsent != null) await _repo.AppendCrdtUpdateAsync(payload.PageId, unsent, merged, _crdt.HasEverHeldText);
                await _repo.AppendCrdtUpdateAsync(payload.PageId, payload.Update, merged, _crdt.HasEverHeldText);
                if (unsent != null) await App.PM.ComController.SendNoteUpdateAsync(payload.PageId, unsent);
            }
            finally { _crdtGate.Release(); }
        }

        private async Task OnSelectionChangedAsync(NotePageTreeNode? node)
        {
            IsRenaming = false;
            Collaborators.Clear();
            this.RaisePropertyChanged(nameof(HasCollaborators));

            if (node == null)
            {
                CloseCrdt();
                LoadIntoEditor("");
                Editor.IsReadOnly = true;
                Editor.IsEditing = false;
                SelectedIsPinned = false;
                Backlinks.Clear();
                HasBacklinks = false;
                return;
            }

            // On my own page a dead ref is worth knowing about, on somebody else's it is just their private stuff and reads as plain words
            Editor.ViewerOwnsPage = string.Equals(node.Page.OwnerUserId, _userId, StringComparison.Ordinal)
                || (node.Page.OwnerUserId == null && _isDm);

            if (_isNewPage)
            {
                _isNewPage = false;
                Editor.IsReadOnly = !CanEditSelection;
                Editor.IsEditing = false;
                await OpenCrdtForAsync(node.Page);
                SelectedIsPinned = false;
                Backlinks.Clear();
                HasBacklinks = false;
                return;
            }

            var fresh = await _repo.GetByIdAsync(node.Page.Id);
            if (fresh != null)
            {
                node.Page.ContentMarkdown = fresh.ContentMarkdown;
                if (!_pendingTitles.TryGetValue(node.Page.Id, out var heldTitle)) node.Page.Title = fresh.Title;
                else node.Page.Title = heldTitle;
                node.Page.RevisionNumber = fresh.RevisionNumber;
                node.RefreshDisplay();
            }

            Editor.IsReadOnly = !CanEditSelection;
            Editor.IsEditing = false;
            await OpenCrdtForAsync(node.Page);

            SelectedIsPinned = await ReadPinnedAsync(node.Page.Id);
            await RecomputeBacklinksAsync();
        }

        public void SelectPageById(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return;

            NotePageTreeNode? Search(IEnumerable<NotePageTreeNode> nodes)
            {
                foreach (var n in nodes)
                {
                    if (n.Id == pageId) return n;
                    var found = Search(n.Children);
                    if (found != null) { n.IsExpanded = true; return found; }
                }
                return null;
            }

            var node = Search(MyNotes) ?? Search(SharedNotes);
            if (node != null) SelectedNode = node;
        }

        private async Task RecomputeBacklinksAsync()
        {
            var target = _selectedNode?.Page.Id;
            if (string.IsNullOrEmpty(target))
            {
                Backlinks.Clear();
                HasBacklinks = false;
                return;
            }

            var rows = await _repo.GetVisibleRowsAsync(_campaignId, _userId, _isDm);

            var counts = new Dictionary<string, int>();
            var meta = new Dictionary<string, NotePage>();
            foreach (var row in rows)
            {
                if (row.Id == target) continue;
                if (string.IsNullOrEmpty(row.ContentMarkdown)) continue;

                var hits = 0;
                foreach (var rf in RefParser.ParseAll(row.ContentMarkdown))
                    if ((rf.Type == "note" || rf.Type == "codex") && string.Equals(rf.Id, target, StringComparison.Ordinal))
                        hits++;

                if (hits == 0) continue;
                counts[row.Id] = hits;
                meta[row.Id] = row;
            }

            Backlinks.Clear();
            foreach (var kv in counts.OrderBy(c => meta[c.Key].Title, StringComparer.OrdinalIgnoreCase))
            {
                var page = meta[kv.Key];
                Backlinks.Add(new BacklinkEntry(page.Id, page.Title, ScopeLabelFor(page.Scope), kv.Value, SelectPageById));
            }
            HasBacklinks = Backlinks.Count > 0;
        }

        private static string ScopeLabelFor(string scope) => scope switch
        {
            NotePageScope.Private => "Private",
            NotePageScope.Shared => "Shared",
            NotePageScope.CampaignStory => "Campaign Story",
            _ => ""
        };

        private async Task AddRootAsync(string scope)
        {
            if (scope == NotePageScope.CampaignStory && !_isDm) return;

            var owner = scope == NotePageScope.CampaignStory ? null : _userId;
            var page = await _repo.CreateAsync(_campaignId, owner, scope, "Untitled");

            var node = new NotePageTreeNode(page);
            switch (scope)
            {
                case NotePageScope.Private: MyNotes.Add(node); break;
                case NotePageScope.Shared: SharedNotes.Add(node); break;
            }
            _isNewPage = true;
            SelectedNode = node;

            _ = _repo.RecordChangeAsync(_campaignId, page.Id, "added", page.RevisionNumber, page);
            _ = App.PM.ComController.NotifyPageChangedAsync(page, "added");
        }

        private async Task AddChildAsync(NotePageTreeNode? under)
        {
            var host = under ?? _selectedNode;
            if (host == null) return;
            var parent = host.Page;

            if (parent.Scope == NotePageScope.CampaignStory && !_isDm) return;

            var owner = parent.Scope == NotePageScope.CampaignStory ? null : _userId;

            var page = await _repo.CreateAsync(
                _campaignId, owner, parent.Scope, "Untitled",
                parentPageId: parent.Id);

            var node = new NotePageTreeNode(page);
            host.Children.Add(node);
            host.IsExpanded = true;
            _isNewPage = true;
            SelectedNode = node;

            _ = _repo.RecordChangeAsync(_campaignId, page.Id, "added", page.RevisionNumber, page);
            _ = App.PM.ComController.NotifyPageChangedAsync(page, "added");
        }

        private async Task DeleteSelectedAsync()
        {
            if (_selectedNode == null) return;
            var page = _selectedNode.Page;

            if (page.Scope == NotePageScope.CampaignStory && !_isDm) return;
            if (page.Scope != NotePageScope.CampaignStory && page.OwnerUserId != _userId && !_isDm) return;

            var childCount = await _repo.CountDescendantsAsync(page.Id);
            var message = childCount == 0
                ? $"Delete \"{page.Title}\"?\n\nThis cannot be undone."
                : $"Delete \"{page.Title}\" and {childCount} sub-page" +
                  (childCount == 1 ? "" : "s") + "?\n\nThis cannot be undone.";

            if (ConfirmAsync != null)
            {
                var ok = await ConfirmAsync("Delete page", message);
                if (!ok) return;
            }

            await _repo.DeleteAsync(page.Id);
            await _repo.RecordChangeAsync(_campaignId, page.Id, "removed", page.RevisionNumber, null);
            await App.PM.ComController.NotifyPageChangedAsync(page.Id, "removed");

            RemoveNode(page.Id, MyNotes);
            RemoveNode(page.Id, SharedNotes);
            SelectedNode = null;
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
            if (_selectedNode == null || !CanEditSelection) return;
            RenameDraft = _selectedNode.Page.Title;
            IsRenaming = true;
        }

        private async Task CommitRenameAsync()
        {
            if (!_isRenaming) return;
            IsRenaming = false;

            var node = _selectedNode;
            if (node == null) return;

            var newTitle = RenameDraft?.Trim();
            if (string.IsNullOrWhiteSpace(newTitle) || newTitle == node.Page.Title) return;

            // A rename in flight has to be held against every echo until the round trip brings it back, since somebody else saving their own body carries the title they last saw at a higher revision and the page quietly reverts.
            var pageId = node.Page.Id;
            _pendingTitles[pageId] = newTitle;

            var rev = await _repo.UpdateContentAsync(pageId, newTitle, null, null);

            node.Page.Title = newTitle;
            node.Page.RevisionNumber = rev;
            node.RefreshDisplay();
            this.RaisePropertyChanged(nameof(SelectedTitle));

            await _repo.RecordChangeAsync(_campaignId, node.Page.Id, "updated", rev, node.Page);
            await App.PM.ComController.NotifyPageChangedAsync(node.Page, "updated");
        }

        private async Task ToggleDashboardPinAsync()
        {
            if (_selectedNode == null) return;
            var pageId = _selectedNode.Page.Id;
            var next = !_selectedIsPinned;

            await using var conn = await App.PM.DbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE NotePages SET PinnedToDashboard = $p WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$p", next ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", pageId);
            await cmd.ExecuteNonQueryAsync();

            SelectedIsPinned = next;
        }

        private static async Task<bool> ReadPinnedAsync(string pageId)
        {
            await using var conn = await App.PM.DbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT PinnedToDashboard FROM NotePages WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", pageId);
            var val = await cmd.ExecuteScalarAsync();
            return val != null && val != DBNull.Value && Convert.ToInt32(val) == 1;
        }

        private async Task PersistEditAsync()
        {
            if (_selectedNode == null) return;
            if (!CanEditSelection) return;
            if (_applyingRemote) return;

            var mine = Editor.Markdown;
            _pendingMarkdown = mine;
            if ((DateTime.UtcNow - _lastSave).TotalMilliseconds < 750)
            {
                await Task.Delay(800);
                if (_pendingMarkdown != mine || _pendingMarkdown != Editor.Markdown) return;
            }
            _lastSave = DateTime.UtcNow;

            var page = _selectedNode.Page;

            // An editor that came up before its page did reports itself empty, and saving that would wipe real writing
            var editorHoldsThisPage = _crdt != null && string.Equals(_crdtPageId, page.Id, StringComparison.Ordinal);
            if (!editorHoldsThisPage && string.IsNullOrWhiteSpace(mine) && !string.IsNullOrWhiteSpace(page.ContentMarkdown))
            {
                ErrorLog.Log("[Notes] refused to save an empty body over " + page.Title, new InvalidOperationException("blank over non-empty"));
                NavItem.NavError?.Invoke("That would have wiped " + page.Title + ", so it was not saved. Delete the page if you meant to clear it.");
                return;
            }

            var ownsPage = string.Equals(page.OwnerUserId, _userId, StringComparison.Ordinal)
                || (page.OwnerUserId == null && _isDm);

            int rev;
            string saved;
            await _crdtGate.WaitAsync();
            try
            {
                var source = Editor.Markdown;
                var rewritten = await RefRewriter.RewriteForSaveAsync(
                    source,
                    _campaignId,
                    _repo,
                    async (pageId, ct) => await App.PM.ComController.NotifyPageChangedAsync(pageId, ct),
                    ownsPage);

                // The rewrite is slow enough to type through, so its result is only safe to put back when the editor has not moved underneath it, and if it has then those keystrokes are the newer ones and the refs in them get their turn on the next save.
                var settled = string.Equals(source, Editor.Markdown, StringComparison.Ordinal);
                if (settled && !string.Equals(rewritten, source, StringComparison.Ordinal))
                    Editor.Markdown = rewritten;
                saved = settled ? rewritten : Editor.Markdown;

                rev = await _repo.UpdateContentAsync(page.Id, null, null, saved);

                page.ContentMarkdown = saved;
                page.RevisionNumber = rev;
                if (ReferenceEquals(_selectedNode?.Page, page))
                {
                    Editor.AcceptBaseline(saved);
                    if (!ThreeWayMerge.HasCollision(saved)) NoteCollision = "";
                }

                // State vector goes first, so the diff after is exactly this keystroke run.
                if (_crdt != null && string.Equals(_crdtPageId, page.Id, StringComparison.Ordinal))
                {
                    var before = _crdt.StateVector();
                    if (_crdt.ApplyLocalText(saved))
                    {
                        var delta = _crdt.DiffAgainst(before);
                        await _repo.AppendCrdtUpdateAsync(page.Id, delta, _crdt.Text, _crdt.HasEverHeldText);
                        await App.PM.ComController.SendNoteUpdateAsync(page.Id, delta);
                    }
                }
            }
            finally { _crdtGate.Release(); }

            await _repo.RecordChangeAsync(_campaignId, page.Id, "updated", rev, page);
            await App.PM.ComController.NotifyPageChangedAsync(page, "updated");
            await RecomputeBacklinksAsync();
        }

        public void ApplyRemoteChange(string entityId, string changeType, NotePage? payload)
        {
            switch (changeType)
            {
                case "added":
                    if (payload != null) MergeIncoming(payload);
                    break;
                case "updated":
                    if (payload != null) MergeIncoming(payload);
                    break;
                case "removed":
                    RemoveNode(entityId, MyNotes);
                    RemoveNode(entityId, SharedNotes);
                    if (_selectedNode?.Id == entityId) SelectedNode = null;
                    // Without the local delete a reload resurrects a page everyone else watched die.
                    _ = DeleteIncomingAsync(entityId);
                    break;
            }

            _ = RecomputeBacklinksAsync();
        }

        private void MergeIncoming(NotePage incoming)
        {
            var existing = FindNode(incoming.Id);
            if (existing != null)
            {
                if (incoming.RevisionNumber <= existing.Page.RevisionNumber) return;

                if (_pendingTitles.TryGetValue(incoming.Id, out var mineInFlight))
                {
                    if (string.Equals(incoming.Title, mineInFlight, StringComparison.Ordinal)) _pendingTitles.Remove(incoming.Id);
                    else incoming.Title = mineInFlight;
                }

                existing.Page.Title = incoming.Title;
                existing.Page.Icon = incoming.Icon;
                existing.Page.RevisionNumber = incoming.RevisionNumber;
                existing.RefreshDisplay();

                // The recievers row has to carry the incoming revision or the next local save reuses a number the others already applied and gets dropped as an echo
                _ = PersistIncomingAsync(incoming);

                // A page with a live document open takes its words from the deltas only, the whole body rides this same message for anybody without it open, and letting that through would overwrite the document and fight the caret.
                if (_crdt != null && string.Equals(_crdtPageId, incoming.Id, StringComparison.Ordinal)) return;

                var weAreEditingThis = _selectedNode?.Id == incoming.Id && Editor.IsDirty;
                if (!weAreEditingThis)
                {
                    existing.Page.ContentMarkdown = incoming.ContentMarkdown;
                    if (_selectedNode?.Id == incoming.Id)
                    {
                        _applyingRemote = true;
                        try { Editor.Reset(incoming.ContentMarkdown); }
                        finally { _applyingRemote = false; }
                    }
                }
                else
                {
                    var merged = ThreeWayMerge.Merge(Editor.Baseline, Editor.Markdown, incoming.ContentMarkdown ?? "");
                    existing.Page.ContentMarkdown = merged;
                    _applyingRemote = true;
                    try
                    {
                        var caret = ThreeWayMerge.TransformCaret(Editor.Markdown, merged, Editor.SelectionStart);
                        Editor.Markdown = merged;
                        // Baseline has to become the merged text, not theirs, or the next remote edit reads my half of the merge as brand new typing and folds it in a second time.
                        Editor.AcceptBaseline(merged);
                        Editor.MoveCaretTo(caret);
                    }
                    finally { _applyingRemote = false; }
                    if (!string.Equals(merged, incoming.ContentMarkdown, StringComparison.Ordinal)) _ = PersistEditAsync();
                }
            }
            else
            {
                _ = PersistIncomingAsync(incoming);
                // A page I have never seen lands as a root in its scope and the next LoadAsync sorts the real parenting out, hanging it under a parent I already hold can come later.
                var node = new NotePageTreeNode(incoming);
                ObservableCollection<NotePageTreeNode>? bucket = incoming.Scope switch
                {
                    NotePageScope.Private => MyNotes,
                    NotePageScope.Shared => SharedNotes,
                    _ => null
                };
                if (bucket == null) return;

                if (!string.IsNullOrEmpty(incoming.ParentPageId))
                {
                    var parent = FindNode(incoming.ParentPageId);
                    if (parent != null) { parent.Children.Add(node); return; }
                }
                bucket.Add(node);
            }
        }

        private async Task PersistIncomingAsync(NotePage page)
        {
            try { await _repo.UpsertRemoteAsync(page); }
            catch (Exception ex) { ErrorLog.Log("[Notes] persisting a synced page failed, a restart may lose it", ex); }
        }

        private async Task DeleteIncomingAsync(string pageId)
        {
            try { await _repo.DeleteAsync(pageId); }
            catch (Exception ex) { ErrorLog.Log("[Notes] deleting a synced removal failed", ex); }
        }

        private NotePageTreeNode? FindNode(string id)
        {
            NotePageTreeNode? Search(IEnumerable<NotePageTreeNode> nodes)
            {
                foreach (var n in nodes)
                {
                    if (n.Id == id) return n;
                    var found = Search(n.Children);
                    if (found != null) return found;
                }
                return null;
            }
            return Search(MyNotes) ?? Search(SharedNotes);
        }
    }

    public class CollaboratorPresence : ReactiveObject
    {
        public string UserId { get; }
        public string Name { get; }
        public string ColorHex { get; }

        private int _line;
        public int Line
        {
            get => _line;
            set { this.RaiseAndSetIfChanged(ref _line, value); this.RaisePropertyChanged(nameof(Label)); }
        }

        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
        public string Label => Name + ", line " + Line;

        public CollaboratorPresence(string userId, string name, string colorHex)
        {
            UserId = userId;
            Name = name;
            ColorHex = colorHex;
        }
    }

    public class BacklinkEntry
    {
        public string PageId { get; }
        public string Title { get; }
        public string ScopeLabel { get; }
        public int LinkCount { get; }
        public string CountLabel => LinkCount > 1 ? "x" + LinkCount : "";
        public bool HasCount => LinkCount > 1;
        public ReactiveCommand<Unit, Unit> OpenCommand { get; }

        public BacklinkEntry(string pageId, string title, string scopeLabel, int linkCount, Action<string> onOpen)
        {
            PageId = pageId;
            Title = title;
            ScopeLabel = scopeLabel;
            LinkCount = linkCount;
            OpenCommand = ReactiveCommand.Create(() => onOpen?.Invoke(pageId));
        }
    }
}
