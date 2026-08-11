using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Dujahit.Models;
using Dujahit.Models.Communication;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Dujahit.ViewModels
{
    public class SoundboardViewModel : ViewModelBase
    {
        public ObservableCollection<SoundItemViewModel> Music { get; } = new();
        public ObservableCollection<SoundItemViewModel> Sfx { get; } = new();
        public ObservableCollection<SoundItemViewModel> Favourites { get; } = new();
        public bool HasFavourites => Favourites.Count > 0;

        public ObservableCollection<SoundTargetViewModel> Players { get; } = new();
        public bool HasPlayers => Players.Count > 0;

        private bool _targetEveryone = true;
        public bool TargetEveryone
        {
            get => _targetEveryone;
            set => this.RaiseAndSetIfChanged(ref _targetEveryone, value);
        }

        private bool _isOpen;
        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                this.RaiseAndSetIfChanged(ref _isOpen, value);
                this.RaisePropertyChanged(nameof(WidgetWidth));
            }
        }

        public double WidgetWidth => IsOpen ? 340d : 150d;

        private int _musicVolume = 70;
        public int MusicVolume
        {
            get => _musicVolume;
            set { this.RaiseAndSetIfChanged(ref _musicVolume, value); App.PM?.Sound.SetMusicVolume(value); }
        }

        private int _sfxVolume = 90;
        public int SfxVolume { get => _sfxVolume; set => this.RaiseAndSetIfChanged(ref _sfxVolume, value); }

        public bool IsDm { get; }
        public bool IsListener => !IsDm;

        public int ListenerVolume
        {
            get => App.PM?.Sound.ListenerVolume ?? 100;
            set
            {
                if (App.PM == null) return;
                App.PM.Sound.ListenerVolume = value;
                this.RaisePropertyChanged();
            }
        }

        public bool IsMuted
        {
            get => App.PM?.Sound.Muted ?? false;
            set
            {
                if (App.PM == null) return;
                App.PM.Sound.Muted = value;
                this.RaisePropertyChanged();
            }
        }

        private string _nowPlaying = "";
        public string NowPlaying
        {
            get => _nowPlaying;
            set { this.RaiseAndSetIfChanged(ref _nowPlaying, value); this.RaisePropertyChanged(nameof(HasMusicPlaying)); }
        }
        public bool HasMusicPlaying => NowPlaying.Length > 0;

        public ReactiveCommand<Unit, Unit> ToggleCommand { get; }
        public ReactiveCommand<Unit, Unit> AddMusicCommand { get; }
        public ReactiveCommand<Unit, Unit> AddSfxCommand { get; }
        public ReactiveCommand<Unit, Unit> StopMusicCommand { get; }
        public ReactiveCommand<Unit, Unit> SyncCommand { get; }
        public ReactiveCommand<Unit, Unit> PlayPlaylistCommand { get; }

        private bool _playlistActive;
        private int _playlistIndex = -1;
        private bool _playlistLoop = true;
        public bool PlaylistLoop { get => _playlistLoop; set => this.RaiseAndSetIfChanged(ref _playlistLoop, value); }

        public SoundboardViewModel(bool isDm = true)
        {
            IsDm = isDm;
            ToggleCommand = ReactiveCommand.Create(() => { IsOpen = !IsOpen; });
            AddMusicCommand = ReactiveCommand.CreateFromTask(() => AddAsync("music"));
            AddSfxCommand = ReactiveCommand.CreateFromTask(() => AddAsync("sfx"));
            StopMusicCommand = ReactiveCommand.CreateFromTask(StopMusicAsync);
            SyncCommand = ReactiveCommand.CreateFromTask(SyncAsync);
            PlayPlaylistCommand = ReactiveCommand.Create(StartPlaylist);
            if (App.PM != null) App.PM.Sound.MusicEnded += OnMusicEnded;
            if (isDm) _ = LoadAsync();
            else if (App.PM != null) App.PM.Sound.MusicStarted += name => Dispatcher.UIThread.Post(() => NowPlaying = name);
        }

        private void StartPlaylist()
        {
            if (App.PM == null || Music.Count == 0) return;
            _playlistActive = true;
            _playlistIndex = 0;
            PlayTrack(Music[0]);
        }

        private void OnMusicEnded()
        {
            if (!_playlistActive) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (!_playlistActive || Music.Count == 0) return;
                _playlistIndex++;
                if (_playlistIndex >= Music.Count)
                {
                    if (!PlaylistLoop) { _playlistActive = false; NowPlaying = ""; return; }
                    _playlistIndex = 0;
                }
                PlayTrack(Music[_playlistIndex]);
            });
        }

        private void PlayTrack(SoundItemViewModel item)
        {
            if (App.PM == null) return;
            var vol = MusicVolume;
            App.PM.Sound.PlayClip(item.Clip, loop: false, vol);
            var msg = new PlaySoundMessage(item.Id, "music", false, vol);
            if (TargetEveryone)
                _ = App.PM.ComController.PlaySoundAsync(msg);
            else
            {
                var ids = Players.Where(p => p.IsSelected && p.Online).Select(p => p.UserId).ToList();
                if (ids.Count > 0) _ = App.PM.ComController.PlaySoundForAsync(msg, ids);
            }
            NowPlaying = item.Name;
        }

        public async Task LoadAsync()
        {
            if (App.PM == null) return;
            var clips = await App.PM.LoadSoundClipsAsync();
            Music.Clear();
            Sfx.Clear();
            Favourites.Clear();
            foreach (var c in clips)
            {
                var item = new SoundItemViewModel(c, PlayItem, ToggleFavourite, i => _ = DeleteItem(i));
                if (c.Kind == "music") Music.Add(item); else Sfx.Add(item);
                if (c.IsFavourite) Favourites.Add(item);
            }
            this.RaisePropertyChanged(nameof(HasFavourites));
            RefreshPlayers();
        }

        public void RefreshPlayers()
        {
            if (App.PM == null) return;
            var com = App.PM.ComController;
            var online = new HashSet<string>(com.OnlineUserIds, StringComparer.OrdinalIgnoreCase);
            var byId = Players.ToDictionary(p => p.UserId, StringComparer.OrdinalIgnoreCase);

            Players.Clear();
            foreach (var m in com.Members.Where(m => string.Equals(m.Role, "player", StringComparison.OrdinalIgnoreCase)))
            {
                Players.Add(new SoundTargetViewModel
                {
                    UserId = m.UserId,
                    Name = string.IsNullOrWhiteSpace(m.Username) ? m.UserId : m.Username!,
                    Online = online.Contains(m.UserId),
                    IsSelected = byId.TryGetValue(m.UserId, out var prev) && prev.IsSelected
                });
            }
            this.RaisePropertyChanged(nameof(HasPlayers));
        }

        private async Task AddAsync(string kind)
        {
            if (App.PM == null) return;
            var sp = GetStorage();
            if (sp == null) return;
            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = kind == "music" ? "Add music" : "Add a sound effect",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Audio") { Patterns = new[] { "*.mp3", "*.wav", "*.ogg", "*.m4a", "*.flac" } }
                }
            });
            var file = files.FirstOrDefault();
            if (file == null) return;
            var name = Path.GetFileNameWithoutExtension(file.Name);
            try
            {
                TransferLabel = "Copying " + name;
                TransferProgress = 0;
                IsTransferring = true;

                var copy = new Progress<double>(p => TransferProgress = p);
                var clip = await App.PM.AddSoundClipFromFileAsync(name, kind, file.Path.LocalPath, copy);
                if (clip == null) return;

                TransferLabel = "Sending " + name + " to players";
                TransferProgress = 0;
                await BroadcastShareAsync(clip, new Progress<double>(p => TransferProgress = p));
                await LoadAsync();
            }
            finally
            {
                IsTransferring = false;
                TransferLabel = "";
                TransferProgress = 0;
            }
        }

        private bool _isTransferring;
        public bool IsTransferring
        {
            get => _isTransferring;
            set => this.RaiseAndSetIfChanged(ref _isTransferring, value);
        }

        private double _transferProgress;
        public double TransferProgress
        {
            get => _transferProgress;
            set => this.RaiseAndSetIfChanged(ref _transferProgress, value);
        }

        private string _transferLabel = "";
        public string TransferLabel
        {
            get => _transferLabel;
            set => this.RaiseAndSetIfChanged(ref _transferLabel, value);
        }

        private const int ShareChunkBytes = 1024 * 1024;

        private async Task BroadcastShareAsync(SoundClip clip, IProgress<double>? progress = null)
        {
            try
            {
                var path = App.PM!.SoundFilePath(clip.CampaignId, clip.FileName);
                if (!File.Exists(path)) return;

                await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                var total = (int)Math.Max(1, (fs.Length + ShareChunkBytes - 1) / ShareChunkBytes);
                var buffer = new byte[ShareChunkBytes];

                for (int i = 0; i < total; i++)
                {
                    var read = await fs.ReadAtLeastAsync(buffer.AsMemory(0, ShareChunkBytes), ShareChunkBytes, throwOnEndOfStream: false);
                    if (read <= 0) break;
                    await App.PM.ComController.ShareSoundChunkAsync(new SoundChunkMessage(
                        clip.Id, clip.CampaignId, clip.Name, clip.Kind, clip.FileName,
                        i, total, Convert.ToBase64String(buffer, 0, read)));
                    progress?.Report((i + 1) * 100.0 / total);
                }
            }
            catch (Exception ex) { ErrorLog.Log("Sharing a sound to players failed", ex); }
        }

        private void PlayItem(SoundItemViewModel item)
        {
            if (App.PM == null) return;
            _playlistActive = false;
            bool loop = item.Kind == "music";
            int vol = loop ? MusicVolume : SfxVolume;
            App.PM.Sound.PlayClip(item.Clip, loop, vol);
            var msg = new PlaySoundMessage(item.Id, item.Kind, loop, vol);
            if (TargetEveryone)
                _ = App.PM.ComController.PlaySoundAsync(msg);
            else
            {
                var ids = Players.Where(p => p.IsSelected && p.Online).Select(p => p.UserId).ToList();
                if (ids.Count > 0) _ = App.PM.ComController.PlaySoundForAsync(msg, ids);
            }
            if (loop) NowPlaying = item.Name;
        }

        private async Task StopMusicAsync()
        {
            if (App.PM == null) return;
            _playlistActive = false;
            App.PM.Sound.StopMusic();
            NowPlaying = "";
            await App.PM.ComController.StopMusicForAllAsync();
        }

        private async Task SyncAsync()
        {
            if (App.PM == null) return;
            var clips = await App.PM.LoadSoundClipsAsync();
            if (clips.Count == 0) return;
            try
            {
                IsTransferring = true;
                for (int i = 0; i < clips.Count; i++)
                {
                    TransferLabel = $"Sending {clips[i].Name} to players, {i + 1} of {clips.Count}";
                    TransferProgress = 0;
                    await BroadcastShareAsync(clips[i], new Progress<double>(p => TransferProgress = p));
                }
            }
            finally
            {
                IsTransferring = false;
                TransferLabel = "";
                TransferProgress = 0;
            }
        }

        public Task ResyncLibraryAsync() => SyncAsync();

        private void ToggleFavourite(SoundItemViewModel item)
        {
            item.IsFavourite = !item.IsFavourite;
            _ = App.PM!.SetSoundFavouriteAsync(item.Id, item.IsFavourite);
            if (item.IsFavourite) { if (!Favourites.Contains(item)) Favourites.Add(item); }
            else Favourites.Remove(item);
            this.RaisePropertyChanged(nameof(HasFavourites));
        }

        public event Func<string, string, Task<bool>>? ConfirmAsync;

        private async Task DeleteItem(SoundItemViewModel item)
        {
            if (ConfirmAsync != null && !await ConfirmAsync("Delete sound", $"Delete \"{item.Name}\"?\n\nThis removes it for everyone, this cannot be undone.")) return;
            _ = App.PM!.DeleteSoundClipAsync(item.Id);
            Music.Remove(item);
            Sfx.Remove(item);
            Favourites.Remove(item);
        }

        private static IStorageProvider? GetStorage()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d && d.MainWindow != null)
                return d.MainWindow.StorageProvider;
            return null;
        }
    }

    public class SoundItemViewModel : ViewModelBase
    {
        public SoundClip Clip { get; }
        public string Id => Clip.Id;
        public string Name => Clip.Name;
        public string Kind => Clip.Kind;

        private bool _isFavourite;
        public bool IsFavourite { get => _isFavourite; set => this.RaiseAndSetIfChanged(ref _isFavourite, value); }

        public ReactiveCommand<Unit, Unit> PlayCommand { get; }
        public ReactiveCommand<Unit, Unit> FavouriteCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteCommand { get; }

        public SoundItemViewModel(SoundClip clip, Action<SoundItemViewModel> onPlay, Action<SoundItemViewModel> onFav, Action<SoundItemViewModel> onDel)
        {
            Clip = clip;
            _isFavourite = clip.IsFavourite;
            PlayCommand = ReactiveCommand.Create(() => onPlay(this));
            FavouriteCommand = ReactiveCommand.Create(() => onFav(this));
            DeleteCommand = ReactiveCommand.Create(() => onDel(this));
        }
    }

    public class SoundTargetViewModel : ViewModelBase
    {
        public string UserId { get; set; } = "";
        public string Name { get; set; } = "";

        private bool _online;
        public bool Online { get => _online; set => this.RaiseAndSetIfChanged(ref _online, value); }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => this.RaiseAndSetIfChanged(ref _isSelected, value); }
    }
}
