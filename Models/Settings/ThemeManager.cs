using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Dujahit.Models.Application;
using Dujahit.Models.Database;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Dujahit.Models.Settings
{
    public class ThemeManager
    {
        private Theme _currentTheme;
        private DatabaseManager _dbManager;
        private CampaignManager _campaignManager;

        private const string BaseThemeId = "base";
        private const string ActiveThemeKey = "active_theme_id";

        public ThemeManager(DatabaseManager dbManager, CampaignManager campaignManager)
        {
            _dbManager = dbManager;
            _campaignManager = campaignManager; // I wanna handle these things way better in every other file
        }

        public async Task SetNewTheme(Theme newTheme)
        {
            _currentTheme = newTheme;
            ApplyTheme(_currentTheme);
            await SaveTheme();
            await SetActiveThemeIdAsync(_currentTheme.Id);
        }

        public async Task ApplyActiveThemeAsync(CancellationToken ct = default)
        {
            var activeId = await GetActiveThemeIdAsync(ct);
            var all = await GetAllThemesAsync(ct);
            var theme = all.FirstOrDefault(t => t.Id == activeId)
                        ?? all.FirstOrDefault(t => t.Id == BaseThemeId)
                        ?? all.FirstOrDefault();
            if (theme is null) return;

            _currentTheme = theme;
            await Dispatcher.UIThread.InvokeAsync(() => ApplyTheme(theme));
        }

        private async Task SetActiveThemeIdAsync(string id, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(id)) return;
            await using var conn = await _dbManager.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO AppSettings (Key, Value) VALUES ($k, $v)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
                """;
            cmd.Parameters.AddWithValue("$k", ActiveThemeKey);
            cmd.Parameters.AddWithValue("$v", id);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<string?> GetActiveThemeIdAsync(CancellationToken ct = default)
        {
            await using var conn = await _dbManager.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $k LIMIT 1";
            cmd.Parameters.AddWithValue("$k", ActiveThemeKey);
            var result = await cmd.ExecuteScalarAsync(ct);
            return result as string;
        }

        public async Task MarkActiveAsync(Theme theme, CancellationToken ct = default)
        {
            if (theme is null || string.IsNullOrEmpty(theme.Id)) return;
            _currentTheme = theme;
            await SetActiveThemeIdAsync(theme.Id, ct);
        }

        public async Task SaveTheme()
        {
            if (_currentTheme is null) return;
            if (string.IsNullOrEmpty(_currentTheme.Id))
                _currentTheme.Id = Guid.NewGuid().ToString("N");
            await _campaignManager.SaveThemeAsync(_currentTheme);
        }

        public async Task EnsureDefaultThemeAsync(CancellationToken ct = default)
        {
            await using var conn = await _dbManager.OpenAsync(ct);

            await RetireThemeAsync(conn, "tony-chopper", "cotton-candy", ct);
            await RetireThemeAsync(conn, "doctor-reindeer", "cotton-candy", ct);
            await RetireThemeAsync(conn, "flame-fist", "rubber-captain", ct);

            foreach (var theme in BuiltInThemes())
            {
                try
                {
                    await FreeBuiltInNameAsync(conn, theme, ct);

                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = """
                        INSERT INTO Themes
                            (Id, Name, Background, Foreground, Widget, WidgetForeground,
                             AccentColor, AccentHover, Divider, Danger, Muted)
                        VALUES
                            ($Id, $Name, $Background, $Foreground, $Widget, $WidgetForeground,
                             $AccentColor, $AccentHover, $Divider, $Danger, $Muted)
                        ON CONFLICT(Id) DO UPDATE SET
                            Name = excluded.Name,
                            Background = excluded.Background,
                            Foreground = excluded.Foreground,
                            Widget = excluded.Widget,
                            WidgetForeground = excluded.WidgetForeground,
                            AccentColor = excluded.AccentColor,
                            AccentHover = excluded.AccentHover,
                            Divider = excluded.Divider,
                            Danger = excluded.Danger,
                            Muted = excluded.Muted
                        """;
                    Bind(cmd, theme);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                catch (Exception ex) { ErrorLog.Log($"[Themes] seeding {theme.Id} failed", ex); }
            }
        }

        private static async Task FreeBuiltInNameAsync(SqliteConnection conn, Theme theme, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE Themes
                SET Name = Name || ' (' || substr(Id, 1, 6) || ')'
                WHERE Name = $Name AND Id <> $Id
                """;
            cmd.Parameters.AddWithValue("$Name", theme.Name);
            cmd.Parameters.AddWithValue("$Id", theme.Id);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // One of the built in themes getting renamed or replaced leaves its old row behind, and anybody sitting on it silently falls back to Base
        private static async Task RetireThemeAsync(SqliteConnection conn, string oldId, string newId, CancellationToken ct)
        {
            await using (var move = conn.CreateCommand())
            {
                move.CommandText = "UPDATE AppSettings SET Value = $new WHERE Key = $k AND Value = $old";
                move.Parameters.AddWithValue("$new", newId);
                move.Parameters.AddWithValue("$k", ActiveThemeKey);
                move.Parameters.AddWithValue("$old", oldId);
                await move.ExecuteNonQueryAsync(ct);
            }
            await using var drop = conn.CreateCommand();
            drop.CommandText = "DELETE FROM Themes WHERE Id = $old";
            drop.Parameters.AddWithValue("$old", oldId);
            await drop.ExecuteNonQueryAsync(ct);
        }

        // Buttons paint their text in the Background colour, so a dark theme needs a bright accent and a light theme a deep one or the button label goes unreadable. (Very obvious but idk can help somone new who has never had to handle text readability)
        private static List<Theme> BuiltInThemes() => new()
        {
            new Theme { Id = BaseThemeId, Name = "Base", Background = "#0F0F1A", Foreground = "#F5F0E1", Widget = "#1A1A2A", WidgetForeground = "#ECEAF0", AccentColor = "#FFD700", AccentHover = "#BFA050", Divider = "#3A3A4D", Danger = "#BB4444", Muted = "#9A96A8" },

            new Theme { Id = "midnight", Name = "Midnight", Background = "#0B1020", Foreground = "#E6ECF5", Widget = "#151B30", WidgetForeground = "#DDE6F5", AccentColor = "#55C2F2", AccentHover = "#2E86AB", Divider = "#263252", Danger = "#C0455E", Muted = "#8C99B5" },
            new Theme { Id = "ember", Name = "Ember", Background = "#150F0C", Foreground = "#F2E8DE", Widget = "#241A14", WidgetForeground = "#EFE3D6", AccentColor = "#FF8A50", AccentHover = "#B5502F", Divider = "#3A2C24", Danger = "#C0392B", Muted = "#AC9A8A" },
            new Theme { Id = "forest-night", Name = "Forest Night", Background = "#0C1410", Foreground = "#E4EFE6", Widget = "#14201A", WidgetForeground = "#D8ECDD", AccentColor = "#4FD98A", AccentHover = "#2A8F5A", Divider = "#263A30", Danger = "#C0553E", Muted = "#8FA896" },

            new Theme { Id = "parchment", Name = "Parchment", Background = "#F3EAD3", Foreground = "#2E2618", Widget = "#EADFC2", WidgetForeground = "#3A3020", AccentColor = "#7A4A1E", AccentHover = "#5C3714", Divider = "#D2C4A0", Danger = "#A83A28", Muted = "#786A4C" },
            new Theme { Id = "daylight", Name = "Daylight", Background = "#F6F8FB", Foreground = "#1E2530", Widget = "#FFFFFF", WidgetForeground = "#2A3340", AccentColor = "#2C5FD0", AccentHover = "#1E4299", Divider = "#D8DEE8", Danger = "#C0392B", Muted = "#616C7A" },
            new Theme { Id = "rose-quartz", Name = "Rose Quartz", Background = "#F6EEF1", Foreground = "#33262B", Widget = "#FDF8FA", WidgetForeground = "#3E2E34", AccentColor = "#A8436A", AccentHover = "#83324F", Divider = "#E4D3D9", Danger = "#B33A3A", Muted = "#8A6E77" },

            new Theme { Id = "wizard", Name = "Wizard", Background = "#120C1E", Foreground = "#ECE4F5", Widget = "#1D1330", WidgetForeground = "#E2D6F0", AccentColor = "#A878FF", AccentHover = "#7A47C0", Divider = "#302344", Danger = "#C0455E", Muted = "#9E8FB2" },
            new Theme { Id = "rogue", Name = "Rogue", Background = "#0D0F12", Foreground = "#DBE0E6", Widget = "#16191F", WidgetForeground = "#CFD6DE", AccentColor = "#5CC6BA", AccentHover = "#357E76", Divider = "#262B33", Danger = "#B84A4A", Muted = "#8A94A0" },
            new Theme { Id = "barbarian", Name = "Barbarian", Background = "#150F0E", Foreground = "#EDE2DC", Widget = "#221917", WidgetForeground = "#E5D6CF", AccentColor = "#E8613D", AccentHover = "#A83E24", Divider = "#382A26", Danger = "#C94358", Muted = "#AC9388" },
            new Theme { Id = "druid", Name = "Druid", Background = "#10130D", Foreground = "#E7EDDF", Widget = "#1A2015", WidgetForeground = "#DBE6CF", AccentColor = "#93C24F", AccentHover = "#5F8A2F", Divider = "#2C3623", Danger = "#B5553A", Muted = "#97A587" },
            new Theme { Id = "bard", Name = "Bard", Background = "#16101A", Foreground = "#F0E6F0", Widget = "#221829", WidgetForeground = "#E6D8EA", AccentColor = "#EA66AC", AccentHover = "#A83B77", Divider = "#362A3E", Danger = "#C0455E", Muted = "#A895AE" },
            new Theme { Id = "paladin", Name = "Paladin", Background = "#F5F2E9", Foreground = "#2A2A20", Widget = "#FCFAF2", WidgetForeground = "#33332A", AccentColor = "#8F6816", AccentHover = "#7E5C12", Divider = "#DED8C4", Danger = "#A83A28", Muted = "#78715A" },

            new Theme { Id = "peony", Name = "Peony", Background = "#FBF3F2", Foreground = "#33262A", Widget = "#FEFAF9", WidgetForeground = "#3E2E32", AccentColor = "#B04A72", AccentHover = "#8A3557", Divider = "#EDDCDD", Danger = "#C0453A", Muted = "#8C7075" },
            new Theme { Id = "cotton-candy", Name = "Cotton Candy", Background = "#F7E7EC", Foreground = "#4A2E22", Widget = "#FDF6F8", WidgetForeground = "#5A3A2A", AccentColor = "#B22E42", AccentHover = "#8A2233", Divider = "#C8DAEC", Danger = "#A83A28", Muted = "#7E6068" },

            new Theme { Id = "dragon-hoard", Name = "Dragon Hoard", Background = "#16100F", Foreground = "#F2E6DC", Widget = "#241816", WidgetForeground = "#EBD9CE", AccentColor = "#F5C851", AccentHover = "#B08C2C", Divider = "#3A2823", Danger = "#C0455E", Muted = "#A89086" },
            new Theme { Id = "lich-study", Name = "Lich Study", Background = "#0B1210", Foreground = "#DFEDE7", Widget = "#131E1A", WidgetForeground = "#D3E6DE", AccentColor = "#CFC49A", AccentHover = "#948B68", Divider = "#1F2E28", Danger = "#B8465C", Muted = "#8FA39B" },
            new Theme { Id = "deep-delve", Name = "Deep Delve", Background = "#101418", Foreground = "#E2E8EE", Widget = "#1A2028", WidgetForeground = "#D6DEE6", AccentColor = "#C98A3C", AccentHover = "#8F5F26", Divider = "#2A333D", Danger = "#BE4A4A", Muted = "#93A0AE" },

            new Theme { Id = "cola-cyborg", Name = "Cola Cyborg", Background = "#0A1219", Foreground = "#E0EEF5", Widget = "#12202B", WidgetForeground = "#D4E6F0", AccentColor = "#34D8F0", AccentHover = "#1E93A6", Divider = "#1F3240", Danger = "#C0455E", Muted = "#8AA0B0" },
            new Theme { Id = "rubber-captain", Name = "Rubber Captain", Background = "#100C0B", Foreground = "#F7EAD4", Widget = "#1E1614", WidgetForeground = "#EFDFC6", AccentColor = "#F5493C", AccentHover = "#A82418", Divider = "#322322", Danger = "#C43D72", Muted = "#A89484" },
            new Theme { Id = "navigators-chart", Name = "Navigator's Chart", Background = "#FAF4E4", Foreground = "#2B2A20", Widget = "#FFFCF2", WidgetForeground = "#35342A", AccentColor = "#A34812", AccentHover = "#7C360C", Divider = "#E4DBC2", Danger = "#B23755", Muted = "#7C7358" },
            new Theme { Id = "tall-tale", Name = "Tall Tale", Background = "#14100A", Foreground = "#EFE6D6", Widget = "#21190F", WidgetForeground = "#E6DAC6", AccentColor = "#7FD94E", AccentHover = "#4E8A2E", Divider = "#362A1B", Danger = "#C0455E", Muted = "#A6957C" },
            new Theme { Id = "devils-kitchen", Name = "Devil's Kitchen", Background = "#0D0D0F", Foreground = "#F0E8D4", Widget = "#18181C", WidgetForeground = "#E4DCC6", AccentColor = "#4C8DFF", AccentHover = "#2E5CB8", Divider = "#26262C", Danger = "#C0455E", Muted = "#9C9688" },
            new Theme { Id = "blossom-snow", Name = "Blossom Snow", Background = "#0E1318", Foreground = "#EDF2F6", Widget = "#181F26", WidgetForeground = "#E0E8EF", AccentColor = "#F58CB0", AccentHover = "#B05677", Divider = "#242E37", Danger = "#C0553E", Muted = "#8E9BA6" },
            new Theme { Id = "stone-reader", Name = "Stone Reader", Background = "#101012", Foreground = "#E9E4EF", Widget = "#1B1B1F", WidgetForeground = "#DDD7E4", AccentColor = "#CE80E0", AccentHover = "#8A4FB8", Divider = "#282830", Danger = "#C0455E", Muted = "#98939F" },
            new Theme { Id = "lost-swordsman", Name = "Lost Swordsman", Background = "#0C0F0D", Foreground = "#E4EAE5", Widget = "#161A17", WidgetForeground = "#D8E0DA", AccentColor = "#4ED95F", AccentHover = "#2A8A47", Divider = "#222A25", Danger = "#C0455E", Muted = "#8F9B92" }
        };

        public async Task ImportTheme(string jsonPath, CancellationToken ct = default)
        {
            if (!File.Exists(jsonPath)) return;

            var json = await File.ReadAllTextAsync(jsonPath, ct);
            var theme = JsonConvert.DeserializeObject<Theme>(json);
            if (theme is null) return;

            theme.Id = Guid.NewGuid().ToString("N");
            await _campaignManager.SaveThemeAsync(theme, ct);
        }

        public async Task ExportTheme(Theme theme, string outputPath, CancellationToken ct = default)
        {
            if (theme is null) return;
            var json = JsonConvert.SerializeObject(theme, Formatting.Indented);
            await File.WriteAllTextAsync(outputPath, json, ct);
        }

        public async Task<List<Theme>> GetAllThemesAsync(CancellationToken ct = default)
        {
            var list = new List<Theme>();
            await using var conn = await _dbManager.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Name, Background, Foreground, Widget, WidgetForeground,
                       AccentColor, AccentHover, Divider, Danger, Muted
                FROM Themes
                ORDER BY Name COLLATE NOCASE
            """;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new Theme
                {
                    Id = r.GetString(0),
                    Name = r.GetString(1),
                    Background = r.GetString(2),
                    Foreground = r.GetString(3),
                    Widget = r.GetString(4),
                    WidgetForeground = r.GetString(5),
                    AccentColor = r.GetString(6),
                    AccentHover = r.GetString(7),
                    Divider = r.GetString(8),
                    Danger = r.GetString(9),
                    Muted = r.GetString(10)
                });
            }
            return list;
        }

        private static void Bind(SqliteCommand cmd, Theme t)
        {
            cmd.Parameters.AddWithValue("$Id", t.Id);
            cmd.Parameters.AddWithValue("$Name", string.IsNullOrEmpty(t.Name) ? "Untitled" : t.Name);
            cmd.Parameters.AddWithValue("$Background", t.Background);
            cmd.Parameters.AddWithValue("$Foreground", t.Foreground);
            cmd.Parameters.AddWithValue("$Widget", t.Widget);
            cmd.Parameters.AddWithValue("$WidgetForeground", t.WidgetForeground);
            cmd.Parameters.AddWithValue("$AccentColor", t.AccentColor);
            cmd.Parameters.AddWithValue("$AccentHover", t.AccentHover);
            cmd.Parameters.AddWithValue("$Divider", t.Divider);
            cmd.Parameters.AddWithValue("$Danger", t.Danger);
            cmd.Parameters.AddWithValue("$Muted", t.Muted);
        }

        public void ApplyTheme(Theme t)
        {
            var res = Avalonia.Application.Current?.Resources;
            if (res is null) return;

            res["Background"] = Brush(t.Background);
            res["Foreground"] = Brush(t.Foreground);
            res["Widget"] = Brush(t.Widget);
            res["WidgetForeground"] = Brush(t.WidgetForeground);
            res["AccentColor"] = Brush(t.AccentColor);
            res["AccentHover"] = Brush(t.AccentHover);
            res["Divider"] = Brush(t.Divider);
            res["Danger"] = Brush(t.Danger);
            res["Muted"] = Brush(string.IsNullOrWhiteSpace(t.Muted) ? "#8A8A99" : t.Muted);

            // The built in fluent controls, checkboxes toggles sliders combo selection focus rings, all paint from SystemAccentColor and its shades, so unless we push the theme accent into those too they stay stuck on the startup gold no matter the theme
            var accent = ParseColor(t.AccentColor);
            res["SystemAccentColor"] = accent;
            res["SystemAccentColorLight1"] = Lighten(accent, 0.20);
            res["SystemAccentColorLight2"] = Lighten(accent, 0.40);
            res["SystemAccentColorLight3"] = Lighten(accent, 0.60);
            res["SystemAccentColorDark1"] = Darken(accent, 0.20);
            res["SystemAccentColorDark2"] = Darken(accent, 0.40);
            res["SystemAccentColorDark3"] = Darken(accent, 0.60);

            // Fluent draws its own borders from these and not from our Divider. Miss them and input boxes and separators keep stock gray lines on every theme.
            var divider = Brush(t.Divider);
            res["TextControlBorderBrush"] = divider;
            res["TextControlBorderBrushPointerOver"] = Brush(t.AccentHover);
            res["ComboBoxBorderBrush"] = divider;
            res["ComboBoxBorderBrushPointerOver"] = Brush(t.AccentHover);
            res["ComboBoxDropDownBorderBrush"] = divider;
            res["MenuFlyoutSeparatorBackground"] = divider;

            var chopper = string.Equals(t.Id, "tony-chopper", StringComparison.OrdinalIgnoreCase);
            res["ThemeEasterEgg"] = chopper
                ? "Chopper's here. And no, calling this theme cute does not make him happy at all..."
                : "";
            res["ThemeEasterEggVisible"] = chopper;

            var glyph = BackdropColor(t.Background);
            res["BackdropGlyph"] = new SolidColorBrush(glyph);
            res["BackdropBrush"] = BuildBackdrop(glyph);

            // The built in controls, combo boxes and text boxes and the like, take their text colour from the fluent variant not our brushes, so flip the variant or a light theme leaves light text on light.
            var app = Avalonia.Application.Current;
            if (app != null)
                app.RequestedThemeVariant = IsLightBackground(t.Background)
                    ? ThemeVariant.Light
                    : ThemeVariant.Dark;
        }

        private static bool IsLightBackground(string bgHex)
        {
            if (!string.IsNullOrEmpty(bgHex) && bgHex[0] != '#') bgHex = "#" + bgHex;
            var c = Color.Parse(bgHex);
            return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0 > 0.5;
        }

        private static SolidColorBrush Brush(string hex)
        {
            if (!string.IsNullOrEmpty(hex) && hex[0] != '#') hex = "#" + hex;
            return new SolidColorBrush(Color.Parse(hex));
        }

        private static Color ParseColor(string hex)
        {
            if (!string.IsNullOrEmpty(hex) && hex[0] != '#') hex = "#" + hex;
            return Color.Parse(hex);
        }

        private static Color Lighten(Color c, double amount)
        {
            byte Up(byte v) => (byte)Math.Clamp(v + (255 - v) * amount, 0, 255);
            return Color.FromArgb(c.A, Up(c.R), Up(c.G), Up(c.B));
        }

        private static Color Darken(Color c, double amount)
        {
            byte Down(byte v) => (byte)Math.Clamp(v * (1 - amount), 0, 255);
            return Color.FromArgb(c.A, Down(c.R), Down(c.G), Down(c.B));
        }

        // Off the background, a good bit lighter on a dark theme so the glyphs read there at all, a touch darker on a light one, matched to the theme either way
        private static Color BackdropColor(string bgHex)
        {
            if (!string.IsNullOrEmpty(bgHex) && bgHex[0] != '#') bgHex = "#" + bgHex;
            var c = Color.Parse(bgHex);
            var lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            var dark = lum < 0.5;
            var f = dark ? 1.75 : 0.78;
            byte alpha = dark ? (byte)74 : (byte)46;
            byte Shift(byte v) => (byte)Math.Clamp(v * f, 0, 255);
            return Color.FromArgb(alpha, Shift(c.R), Shift(c.G), Shift(c.B));
        }

        private static DrawingBrush BuildBackdrop(Color glyph)
        {
            var geometry = Geometry.Parse("M20 5 L22 18 L35 20 L22 22 L20 35 L18 22 L5 20 L18 18 Z");
            var drawing = new GeometryDrawing { Geometry = geometry, Brush = new SolidColorBrush(glyph) };
            return new DrawingBrush(drawing)
            {
                TileMode = TileMode.Tile,
                DestinationRect = new RelativeRect(0, 0, 72, 72, RelativeUnit.Absolute),
                Stretch = Stretch.None,
                Transform = new RotateTransform(-18)
            };
        }
    }

    public class Theme
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public required string Background { get; set; }
        public required string Foreground { get; set; }
        public required string Widget { get; set; }
        public required string WidgetForeground { get; set; }
        public required string AccentColor { get; set; }
        public required string AccentHover { get; set; }
        public required string Divider { get; set; }
        public required string Danger { get; set; }
        public required string Muted { get; set; }
    }
}