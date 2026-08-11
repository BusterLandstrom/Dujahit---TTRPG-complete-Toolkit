using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Dujahit.Models;
using Dujahit.Models.Application;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text;
using System;
using Avalonia.Controls.Primitives;

namespace Dujahit.Views
{
    public class ItemViewDialog : DialogWindow
    {
        private static readonly HashSet<string> _handled = new(StringComparer.OrdinalIgnoreCase)
        {
            "$type", "Type", "Description", "Name", "DamageValues", "WeaponCategory", "Mastery",
            "IsRanged", "RangeNormal", "RangeMax", "BaseAC", "AcBonus", "AllowsDexBonus", "MaxDexBonus",
            "ArmorType", "HitBonus", "DamageBonus", "IsMagic", "Senses", "Effects", "BonusIds", "MechanicalProperties",
            "TemplateId", "ProfRequiredId", "Version", "IsConsumable", "VariantTier", "BaseItemId"
        };

        public ItemViewDialog(string name, string itemType, string dataJson, string? itemId = null)
        {
            var d = ItemDisplay.FromJson(name, itemType, dataJson);

            Width = 480;
            Height = 520;

            var panel = new StackPanel { Spacing = 10 };

            var tags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (!string.IsNullOrWhiteSpace(d.TypeLine)) tags.Children.Add(Badge(d.TypeLine, false));
            if (d.HasMagic) tags.Children.Add(Badge(d.MagicTag, true));
            if (tags.Children.Count > 0) panel.Children.Add(tags);

            if (d.HasStats) panel.Children.Add(Row(itemType.Equals("Armor", StringComparison.OrdinalIgnoreCase) ? "Armor" : "Damage", d.StatLine));
            if (d.HasProperties) panel.Children.Add(Row("Properties", d.PropertiesLine));

            foreach (var (label, value) in RawFields(dataJson))
                panel.Children.Add(Row(label, value));

            if (!string.IsNullOrWhiteSpace(d.Description))
            {
                panel.Children.Add(new TextBlock { Text = "Description", Classes = { "sectionLabel" }, Margin = new Thickness(0, 8, 0, 0) });
                panel.Children.Add(new TextBlock { Text = d.Description, Classes = { "body" }, TextWrapping = TextWrapping.Wrap });
            }

            var scroll = new ScrollViewer
            {
                Content = panel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Height = 400
            };

            var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };

            if (!string.IsNullOrEmpty(itemId))
            {
                var status = new TextBlock { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                var add = PrimaryButton("Add to user");
                add.Click += async (_, _) =>
                {
                    add.IsEnabled = false;
                    var ok = await AddToUserAsync(itemId!);
                    status.Text = ok ? "Added to your character." : "You have no character to add it to.";
                    if (!ok) add.IsEnabled = true;
                };
                footer.Children.Add(status);
                footer.Children.Add(add);
            }

            var close = GhostButton("Close");
            close.Click += (_, _) => Close();
            footer.Children.Add(close);

            var body = new StackPanel { Spacing = 0 };
            body.Children.Add(scroll);
            body.Children.Add(footer);

            Mount(string.IsNullOrWhiteSpace(name) ? "Item" : name, body);
        }

        private static async Task<bool> AddToUserAsync(string baseItemId)
        {
            if (App.PM == null) return false;
            var target = await App.PM.GetPrimaryCharacterIdAsync() ?? await App.PM.GetSoleOwnedCharacterIdAsync();
            if (string.IsNullOrEmpty(target)) return false;
            var inst = new ItemInstance
            {
                Id = Guid.NewGuid().ToString("N"),
                CampaignId = App.PM.GetCampaignId(),
                BaseItemId = baseItemId,
                OwnerCharacterId = target,
                Quantity = 1
            };
            await App.PM.GameDataRepo.SaveInstanceAsync(inst);
            await App.PM.BroadcastInstanceAsync(inst);
            return true;
        }

        private Border Badge(string text, bool accent) => new()
        {
            Background = (accent ? Brush("AccentColor") : Brush("Divider")) ?? Brushes.Gray,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = (accent ? Brush("Background") : Brush("WidgetForeground")) ?? Brushes.White
            }
        };

        private static Control Row(string label, string value)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*") };
            grid.Children.Add(new TextBlock { Text = label, Classes = { "fieldLabel" }, VerticalAlignment = VerticalAlignment.Top });
            var val = new TextBlock { Text = value, Classes = { "body" }, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(val, 1);
            grid.Children.Add(val);
            return grid;
        }


        private static IEnumerable<(string, string)> RawFields(string dataJson)
        {
            var list = new List<(string, string)>();
            try
            {
                using var doc = JsonDocument.Parse(dataJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return list;
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (_handled.Contains(p.Name)) continue;
                    var text = Scalar(p.Value);
                    if (!string.IsNullOrWhiteSpace(text)) list.Add((Humanize(p.Name), text));
                }
            }
            catch (JsonException) { }
            return list;
        }

        private static string Scalar(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.String => e.GetString() ?? "",
            JsonValueKind.Number => e.GetRawText(),
            JsonValueKind.True => "yes",
            JsonValueKind.False => "no",
            _ => ""
        };

        private static string Humanize(string key)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < key.Length; i++)
            {
                var c = key[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(key[i - 1])) sb.Append(' ');
                sb.Append(i == 0 ? char.ToUpper(c, CultureInfo.InvariantCulture) : char.ToLower(c, CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
