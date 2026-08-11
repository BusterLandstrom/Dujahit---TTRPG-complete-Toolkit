using Dujahit.Models;
using Dujahit.Models.Communication;
using Dujahit.Models.Database;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Dujahit.ViewModels
{
    public class HandoutsViewModel : ViewModelBase
    {
        private readonly string _campaignId = App.PM.GetCampaignId();

        public ObservableCollection<HandoutListItem> Handouts { get; } = new();

        private HandoutListItem? _selected;
        public HandoutListItem? Selected
        {
            get => _selected;
            set => this.RaiseAndSetIfChanged(ref _selected, value);
        }

        // The button can't run the file pick itself, the view owns the StorageProvider, so this pokes the code behind
        public event Action? AddHandoutRequested;

        public ReactiveCommand<Unit, Unit> AddCommand { get; }
        public ReactiveCommand<HandoutListItem, Unit> ViewCommand { get; }
        public ReactiveCommand<HandoutListItem, Unit> RevealCommand { get; }
        public ReactiveCommand<HandoutListItem, Unit> HideCommand { get; }
        public ReactiveCommand<HandoutListItem, Unit> DeleteCommand { get; }

        public event Func<string, string, Task<bool>>? ConfirmAsync;
        public event Action<HandoutListItem>? ViewHandoutRequested;

        public HandoutsViewModel()
        {
            AddCommand = ReactiveCommand.Create(() => AddHandoutRequested?.Invoke());
            ViewCommand = ReactiveCommand.Create<HandoutListItem>(h => { if (h != null) ViewHandoutRequested?.Invoke(h); });
            RevealCommand = ReactiveCommand.CreateFromTask<HandoutListItem>(RevealAsync);
            HideCommand = ReactiveCommand.CreateFromTask<HandoutListItem>(HideAsync);
            DeleteCommand = ReactiveCommand.CreateFromTask<HandoutListItem>(DeleteAsync);
        }

        public async Task LoadAsync()
        {
            Handouts.Clear();
            var rows = await App.PM.GameDataRepo.ListHandoutsAsync(_campaignId);
            foreach (var h in rows)
                Handouts.Add(new HandoutListItem(h.Id, h.Name, h.HandoutPath));
        }

        public async Task AddFromFileAsync(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return;

            var id = Guid.NewGuid().ToString("N");
            var ext = Path.GetExtension(sourcePath);
            var name = Path.GetFileNameWithoutExtension(sourcePath);

            var dir = Path.Combine(GlobalVariables.AppDataLocal, "assets", _campaignId, "handouts");
            Directory.CreateDirectory(dir);

            string handoutPath;
            if (string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var pdfCopy = Path.Combine(dir, id + ".pdf");
                File.Copy(sourcePath, pdfCopy, true);
                var pages = await Task.Run(() => Models.Application.PdfRasterizer.RenderToPngs(pdfCopy, dir, id));
                if (pages <= 0) { NavItem.NavError?.Invoke("Couldn't read that PDF, it may be damaged or empty."); return; }
                handoutPath = Path.Combine(dir, id + "_p0.png");
            }
            else
            {
                handoutPath = Path.Combine(dir, id + ext);
                File.Copy(sourcePath, handoutPath, true);
            }

            var handout = new Handout
            {
                Id = id,
                CampaignId = _campaignId,
                Name = name,
                HandoutPath = handoutPath,
                CreatedAt = DateTime.UtcNow
            };
            await App.PM.GameDataRepo.AddHandoutAsync(handout);
            Handouts.Add(new HandoutListItem(id, name, handoutPath));
        }

        public List<string> PageFilesFor(HandoutListItem item)
        {
            var pages = new List<string>();
            if (item == null || !item.IsPdf) { if (item != null) pages.Add(item.Path); return pages; }
            var prefix = item.Path.Substring(0, item.Path.LastIndexOf("_p", StringComparison.Ordinal) + 2);
            for (var n = 0; ; n++)
            {
                var p = prefix + n + ".png";
                if (!File.Exists(p)) break;
                pages.Add(p);
            }
            return pages;
        }

        public async Task RevealPageAsync(HandoutListItem item, string pagePath)
        {
            if (item == null || string.IsNullOrEmpty(pagePath)) return;
            await App.PM.GameDataRepo.UpdateHandoutPathAsync(item.Id, pagePath);
            await App.PM.ComController.RevealHandoutAsync(new HandoutRevealedMessage(item.Id, item.Name));
        }

        private async Task RevealAsync(HandoutListItem item)
        {
            if (item == null) return;
            await App.PM.ComController.RevealHandoutAsync(new HandoutRevealedMessage(item.Id, item.Name));
        }

        private async Task HideAsync(HandoutListItem item)
        {
            if (item == null) return;
            await App.PM.ComController.HideHandoutAsync(item.Id);
        }

        private async Task DeleteAsync(HandoutListItem item)
        {
            if (item == null) return;
            if (ConfirmAsync != null && !await ConfirmAsync("Delete handout", $"Delete handout \"{item.Name}\"?\n\nThis cannot be undone.")) return;
            await App.PM.GameDataRepo.DeleteHandoutAsync(item.Id);
            Handouts.Remove(item);
            // Not unlinking the file off disk yet, orphan cleanup is its own job
        }
    }

    public class HandoutListItem
    {
        public string Id { get; }
        public string Name { get; }
        public string Path { get; }
        public bool IsPdf => Path != null && Path.EndsWith("_p0.png", StringComparison.OrdinalIgnoreCase);
        public HandoutListItem(string id, string name, string path)
        {
            Id = id;
            Name = name;
            Path = path ?? "";
        }
    }
}
