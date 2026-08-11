using Avalonia.Controls;
using Avalonia.Layout;
using Dujahit.Models.Application;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media;

namespace Dujahit.Views
{
    public class ShareNoteDialog : DialogWindow
    {
        private readonly NotePage _page;
        private readonly List<CheckBox> _checks = new();
        private readonly List<(NotePage Page, CheckBox Box)> _privateRefChecks = new();

        public ShareNoteDialog(NotePage page)
        {
            _page = page;
            Title = $"Share \"{page.Title}\"";
            Width = 420;
            Height = 480;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new StackPanel { Margin = new Avalonia.Thickness(18), Spacing = 12 };

            root.Children.Add(new TextBlock
            {
                Text = "Invite players to collaborate on this page.",
                Opacity = 0.8
            });

            var list = new StackPanel { Spacing = 6 };
            root.Children.Add(new ScrollViewer { Height = 320, Content = list });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };
            var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true, Classes = { "ghost" } };
            var save = new Button { Content = "Save", Width = 90, IsDefault = true, Classes = { "primary" } };
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            root.Children.Add(buttons);

            Content = root;

            cancel.Click += (_, _) => Close();
            save.Click += async (_, _) =>
            {
                save.IsEnabled = false;
                await ApplyAsync();
                Close();
            };

            Opened += async (_, _) => await PopulateAsync(list);
        }

        private async Task PopulateAsync(StackPanel list)
        {
            var pm = App.PM;
            var repo = pm.NoteRepo;
            var current = await repo.ListSharesAsync(_page.Id);
            var alreadyInvited = new HashSet<string>(current.Select(s => s.UserId));

            foreach (var m in pm.ComController.Members)
            {
                if (string.Equals(m.UserId, _page.OwnerUserId, StringComparison.Ordinal)) continue;

                var cb = new CheckBox
                {
                    Content = m.Username,
                    Tag = m.UserId,
                    IsChecked = alreadyInvited.Contains(m.UserId)
                };
                _checks.Add(cb);
                list.Children.Add(cb);
            }

            if (_checks.Count == 0)
                list.Children.Add(new TextBlock
                {
                    Text = "No other members in the campaign yet.",
                    Opacity = 0.6
                });

            var (privateNotes, quickNotes) = await RefRewriter.CollectPrivateRefsAsync(
                _page, _page.OwnerUserId ?? "", repo);
            if (privateNotes.Count > 0 || quickNotes.Count > 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "This page points at private things. Invitees see those references as plain text unless you tick a note to share it along, and that covers everything the ticked note points at only if you tick those too.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.8,
                    Margin = new Avalonia.Thickness(0, 10, 0, 0)
                });
                foreach (var note in privateNotes)
                {
                    var cb = new CheckBox { Content = "Share \"" + note.Title + "\" too", IsChecked = false };
                    _privateRefChecks.Add((note, cb));
                    list.Children.Add(cb);
                }
                if (quickNotes.Count > 0)
                    list.Children.Add(new TextBlock
                    {
                        Text = quickNotes.Count == 1
                            ? "1 quick note is already embedded in the text and travels as plain words."
                            : quickNotes.Count + " quick notes are already embedded in the text and travel as plain words.",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.6
                    });
            }
        }

        private async Task ApplyAsync()
        {
            var pm = App.PM;
            var repo = pm.NoteRepo;
            var comm = pm.ComController;

            var existing = (await repo.ListSharesAsync(_page.Id))
                .Select(s => s.UserId)
                .ToHashSet();

            var desired = _checks
                .Where(c => c.IsChecked == true)
                .Select(c => c.Tag as string)
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!)
                .ToHashSet();

            // Promotes it on the spot, handing a page to a friend should not mean learning the scope model first.
            if (_page.Scope == NotePageScope.Private && desired.Count > 0)
            {
                var rev = await repo.SetScopeAsync(_page.Id, NotePageScope.Shared);
                _page.Scope = NotePageScope.Shared;
                _page.RevisionNumber = rev;
            }

            foreach (var add in desired.Except(existing))
            {
                await repo.ShareAsync(_page.Id, add);
                await comm.NotifyShareAddedAsync(_page, add);
            }
            foreach (var rm in existing.Except(desired))
            {
                await repo.UnshareAsync(_page.Id, rm);
                await comm.NotifyShareRemovedAsync(_page.Id, rm);
            }

            foreach (var (note, box) in _privateRefChecks)
            {
                if (box.IsChecked != true || desired.Count == 0) continue;
                var rev = await repo.SetScopeAsync(note.Id, NotePageScope.Shared);
                note.Scope = NotePageScope.Shared;
                note.RevisionNumber = rev;
                foreach (var uid in desired)
                {
                    await repo.ShareAsync(note.Id, uid);
                    await comm.NotifyShareAddedAsync(note, uid);
                }
            }
        }
    }
}
