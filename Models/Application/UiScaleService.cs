using System;
using System.Globalization;
using System.IO;
using Dujahit.Models.Database;

namespace Dujahit.Models.Application
{
    // Global UI zoom applied to the whole app via a LayoutTransform, auto-picked from the screen but overridable in settings. Seems to work fine.
    public static class UiScaleService
    {
        public const double Min = 0.5;
        public const double Max = 2.0;

        private static readonly string _path = Path.Combine(GlobalVariables.AppDataLocal, "ui_scale.txt");
        private static double _scale = 1.0;
        private static bool _loaded;

        public static event Action? Changed;

        public static bool HasUserValue { get; private set; }

        public static double Scale => _scale;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (File.Exists(_path) &&
                    double.TryParse(File.ReadAllText(_path).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    _scale = Math.Clamp(v, Min, Max);
                    HasUserValue = true;
                }
            }
            catch { }
        }

        public static void SetUserScale(double value)
        {
            var v = Math.Clamp(value, Min, Max);
            HasUserValue = true;
            if (Math.Abs(_scale - v) < 0.001) { Save(); return; }
            _scale = v;
            Save();
            Changed?.Invoke();
        }

        // A per-screen guess, only applied when the user has never chosen a value.
        public static void ApplyAutoDefault(double value)
        {
            if (HasUserValue) return;
            var v = Math.Clamp(value, Min, Max);
            if (Math.Abs(_scale - v) < 0.001) return;
            _scale = v;
            Changed?.Invoke();
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, _scale.ToString(CultureInfo.InvariantCulture));
            }
            catch { }
        }
    }
}
