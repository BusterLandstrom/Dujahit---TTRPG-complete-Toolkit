using Dujahit.Models.Communication;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dujahit.Models
{
    public class SoundService
    {
        private readonly SoundPlayer _player = new();
        private readonly ProgramManager _pm;

        public SoundService(ProgramManager pm) { _pm = pm; }
        public bool Available => _player.Available;
        public event Action? MusicEnded { add => _player.MusicEnded += value; remove => _player.MusicEnded -= value; }

        private int _listenerVolume = 100;
        private bool _muted;
        private int _musicSend = 80;

        /* The number that arrives with a clip is the level it was mixed at, not how loud this machine has to be, so it stays a send level
           and whoever is listening owns the last multiplication. Nobody else gets to make somebody's headphones louder than they set them.
        */
        public int ListenerVolume
        {
            get => _listenerVolume;
            set
            {
                _listenerVolume = Math.Clamp(value, 0, 100);
                _player.SetMusicVolume(Effective(_musicSend));
                _ = _pm.SetSettingAsync("ListenerVolume", _listenerVolume.ToString(CultureInfo.InvariantCulture));
            }
        }

        public bool Muted
        {
            get => _muted;
            set
            {
                _muted = value;
                _player.SetMusicVolume(Effective(_musicSend));
                _ = _pm.SetSettingAsync("ListenerMuted", _muted ? "1" : "0");
            }
        }

        public async Task LoadListenerSettingsAsync()
        {
            var v = await _pm.GetSettingAsync("ListenerVolume");
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                _listenerVolume = Math.Clamp(parsed, 0, 100);
            _muted = await _pm.GetSettingAsync("ListenerMuted") == "1";
            _player.SetMusicVolume(Effective(_musicSend));
        }

        private int Effective(int send)
        {
            if (_muted) return 0;
            return (int)Math.Round(Math.Clamp(send, 0, 100) * (_listenerVolume / 100.0));
        }

        public void Attach(CommunicationController com)
        {
            com.OnSoundChunkShared += chunk => _ = CacheChunkAsync(chunk);
            com.OnSoundPlayed += msg => _ = PlayByIdAsync(msg.Id, msg.Loop, msg.Volume);
            com.OnMusicStopped += StopMusic;
        }

        public void PlayClip(SoundClip clip, bool loop, int volume)
        {
            if (clip == null) return;
            var path = _pm.SoundFilePath(clip.CampaignId, clip.FileName);
            if (clip.Kind == "music")
            {
                _musicSend = volume;
                _player.PlayMusic(path, Effective(volume), loop);
                MusicStarted?.Invoke(clip.Name ?? "");
            }
            else _player.PlaySfx(path, Effective(volume));
        }

        public event Action<string>? MusicStarted;

        public void StopMusic()
        {
            _player.StopMusic();
            MusicStarted?.Invoke("");
        }

        public void SetMusicVolume(int v)
        {
            _musicSend = v;
            _player.SetMusicVolume(Effective(v));
        }

        private readonly SemaphoreSlim _chunkLock = new(1, 1);
        private readonly Dictionary<string, (bool Loop, int Volume)> _waitingToPlay = new();

        public event Action<string>? IncomingClipChanged;

        private string _incomingClip = "";
        public string IncomingClip
        {
            get => _incomingClip;
            private set { _incomingClip = value; IncomingClipChanged?.Invoke(value); }
        }

        private async Task CacheChunkAsync(SoundChunkMessage chunk)
        {
            var landed = false;
            await _chunkLock.WaitAsync();
            try
            {
                await _pm.AppendSoundChunkAsync(chunk.CampaignId, chunk.FileName, Convert.FromBase64String(chunk.Base64), chunk.Index == 0);
                IncomingClip = chunk.Index == chunk.Total - 1
                    ? ""
                    : $"{chunk.Name}, {(chunk.Index + 1) * 100 / Math.Max(1, chunk.Total)}%";

                if (chunk.Index == chunk.Total - 1)
                {
                    await _pm.SaveSoundClipRowAsync(new SoundClip
                    {
                        Id = chunk.Id,
                        CampaignId = chunk.CampaignId,
                        Name = chunk.Name,
                        Kind = chunk.Kind,
                        FileName = chunk.FileName
                    });
                    landed = true;
                }
            }
            catch (Exception ex) { ErrorLog.Log("Caching a shared sound failed", ex); }
            finally { _chunkLock.Release(); }

            if (landed) await StartIfWaitingAsync(chunk.Id);
        }

        private async Task StartIfWaitingAsync(string id)
        {
            (bool Loop, int Volume) pending;
            lock (_waitingToPlay)
            {
                if (!_waitingToPlay.TryGetValue(id, out pending)) return;
                _waitingToPlay.Remove(id);
            }
            await PlayByIdAsync(id, pending.Loop, pending.Volume, false);
        }

        private Task PlayByIdAsync(string id, bool loop, int volume) => PlayByIdAsync(id, loop, volume, true);

        private async Task PlayByIdAsync(string id, bool loop, int volume, bool mayWait)
        {
            try
            {
                var clip = (await _pm.LoadSoundClipsAsync()).FirstOrDefault(c => c.Id == id);
                if (clip != null) { PlayClip(clip, loop, volume); return; }
                if (!mayWait) return;
                lock (_waitingToPlay) _waitingToPlay[id] = (loop, volume);
            }
            catch (Exception ex) { ErrorLog.Log("Playing a received sound failed", ex); }
        }
    }
}
