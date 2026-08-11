using LibVLCSharp.Shared;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Dujahit.Models
{
    public class SoundPlayer : IDisposable
    {
        private readonly LibVLC? _vlc;
        private MediaPlayer? _music;
        private readonly List<MediaPlayer> _sfx = new();
        private int _musicVolume = 80;
        private bool _musicLoops;
        private string _musicPath = "";

        public SoundPlayer()
        {
            try
            {
                Core.Initialize();
                _vlc = new LibVLC();
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Audio init failed, the soundboard will be silent", ex);
                _vlc = null;
            }
        }

        public bool Available => _vlc != null;

        public event Action? MusicEnded;

        public void PlayMusic(string path, int volume, bool loop)
        {
            if (_vlc == null) return;
            try
            {
                _musicVolume = Clamp(volume);
                _musicLoops = loop;
                _musicPath = path;
                StopMusic();
                var player = MusicPlayer();
                player.Volume = _musicVolume;
                using var media = new Media(_vlc, path, FromType.FromPath);
                if (loop) media.AddOption(":input-repeat=65535");
                player.Play(media);
            }
            catch (Exception ex) { ErrorLog.Log("PlayMusic failed", ex); }
        }

        private MediaPlayer MusicPlayer()
        {
            if (_music != null) return _music;
            var p = new MediaPlayer(_vlc!) { Volume = _musicVolume };
            p.EndReached += (_, _) => { if (!_musicLoops) MusicEnded?.Invoke(); };
            p.EncounteredError += (_, _) => ErrorLog.Log("VLC could not open the music track " + _musicPath);
            _music = p;
            return p;
        }

        public void StopMusic()
        {
            try { _music?.Stop(); } catch { }
        }

        public void SetMusicVolume(int volume)
        {
            _musicVolume = Clamp(volume);
            try { if (_music != null) _music.Volume = _musicVolume; } catch { }
        }

        public void PlaySfx(string path, int volume)
        {
            if (_vlc == null) return;
            try
            {
                var p = new MediaPlayer(_vlc) { Volume = Clamp(volume) };
                using var media = new Media(_vlc, path, FromType.FromPath);
                p.EndReached += (_, _) => { lock (_sfx) _sfx.Remove(p); DisposeLater(p); };
                lock (_sfx) _sfx.Add(p);
                p.Play(media);
            }
            catch (Exception ex) { ErrorLog.Log("PlaySfx failed", ex); }
        }

        public void StopAll()
        {
            StopMusic();
            MediaPlayer[] snapshot;
            lock (_sfx) { snapshot = _sfx.ToArray(); _sfx.Clear(); }
            foreach (var p in snapshot) DisposeLater(p);
        }

        private static void DisposeLater(MediaPlayer? p)
        {
            if (p == null) return;
            Task.Run(() =>
            {
                try { p.Stop(); } catch { }
                try { p.Dispose(); } catch { }
            });
        }

        private static int Clamp(int v) => v < 0 ? 0 : v > 100 ? 100 : v;

        public void Dispose()
        {
            StopAll();
            var m = _music;
            _music = null;
            try { m?.Dispose(); } catch { }
            try { _vlc?.Dispose(); } catch { }
        }
    }
}
