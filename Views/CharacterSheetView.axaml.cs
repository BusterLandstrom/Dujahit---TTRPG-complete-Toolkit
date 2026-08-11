using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dujahit.Models.Application;
using Dujahit.ViewModels;
using Dujahit.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dujahit.Models;

namespace Dujahit.Views;

public partial class CharacterSheetView : UserControl
{
    private CharacterSheetViewModel? _wired;

    public CharacterSheetView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_wired != null)
        {
            _wired.AddItemRequested -= OnAddItemRequested;
            _wired.ChooseCastLevelRequested -= OnChooseCastLevel;
            _wired.OpenFeatureRequested -= OnOpenFeatureRequested;
            _wired.OpenItemViewRequested -= OnOpenItemViewRequested;
            _wired.OpenItemEditRequested -= OnOpenItemEditRequested;
            _wired.OpenSpellViewRequested -= OnOpenSpellViewRequested;
            _wired.LevelUpChoicesRequested -= OnLevelUpChoicesRequested;
            _wired.TradeRequested -= OnTradeRequested;
            _wired.ExportRequested -= OnExportRequested;
        }
        _wired = DataContext as CharacterSheetViewModel;
        if (_wired != null)
        {
            _wired.AddItemRequested += OnAddItemRequested;
            _wired.ChooseCastLevelRequested += OnChooseCastLevel;
            _wired.OpenFeatureRequested += OnOpenFeatureRequested;
            _wired.OpenItemViewRequested += OnOpenItemViewRequested;
            _wired.OpenItemEditRequested += OnOpenItemEditRequested;
            _wired.OpenSpellViewRequested += OnOpenSpellViewRequested;
            _wired.LevelUpChoicesRequested += OnLevelUpChoicesRequested;
            _wired.TradeRequested += OnTradeRequested;
            _wired.ExportRequested += OnExportRequested;
        }
    }

    private async void OnExportRequested()
    {
        try
        {
            if (DataContext is not CharacterSheetViewModel vm || App.PM == null) return;
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var baseName = string.IsNullOrWhiteSpace(vm.Name) ? "character" : vm.Name;
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export character",
                SuggestedFileName = baseName + ".pdf",
                DefaultExtension = "pdf",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Character sheet PDF") { Patterns = new[] { "*.pdf" } },
                    new FilePickerFileType("Character JSON") { Patterns = new[] { "*.json" } }
                }
            });
            if (file is null) return;
            var path = file.Path.LocalPath;

            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                await App.PM.ExportCharacterToFileAsync(vm.CharacterId, path);
                return;
            }

            var sheet = BuildPdfSheet(vm);
            await Task.Run(() => CharacterPdfExporter.Write(path, sheet));
        }
        catch (Exception ex) { ErrorLog.Log("Unhandled in OnExportRequested", ex); }
    }

    private static CharacterSheetPdf BuildPdfSheet(CharacterSheetViewModel vm)
    {
        var abilities = vm.Abilities
            .Select(a => new PdfAbility(a.ShortName, a.Score + a.BonusToScore, a.ModifierDisplay, a.SaveBonusDisplay))
            .ToList();
        var skills = vm.Skills
            .Select(s => new PdfSkill(s.Name, s.BonusDisplay, s.Expertise ? "expertise" : s.Proficient ? "proficient" : ""))
            .ToList();
        var attacks = vm.EquippedItems.Select(i => i.Name).ToList();
        var features = vm.Features.Select(f => f.Name).ToList();
        var spells = vm.Spellbook.Where(s => s.IsPrepared).Select(s => s.Name).ToList();
        var inventory = vm.Inventory.Select(i => i.Name).ToList();
        var profs = new List<string>(vm.Proficiencies);

        var subtitle = string.IsNullOrWhiteSpace(vm.ClassSplitLabel) ? "Level " + vm.Level : vm.ClassSplitLabel;
        if (!string.IsNullOrWhiteSpace(vm.Race)) subtitle += "    " + vm.Race;

        return new CharacterSheetPdf(
            vm.Name, subtitle,
            vm.CurrentHp + " / " + vm.MaxHp, vm.ArmorClass.ToString(), "+" + vm.ProficiencyBonus, vm.InitiativeDisplay,
            abilities, skills, attacks, features, spells, inventory, profs, vm.Backstory ?? "");
    }

    private void OnOpenFeatureRequested(FeatureEntry f)
    {
        if (this.GetVisualRoot() is not Window owner) return;
        var dialog = new FeatureDetailDialog(f.Name, f.Description, f.Level);
        _ = dialog.ShowDialog(owner);
    }

    private async void OnLevelUpChoicesRequested(string? classId, int newLevel)
    {
        try
        {
            if (this.GetVisualRoot() is not Window owner) return;
            if (DataContext is not CharacterSheetViewModel vm || App.PM == null) return;

            var all = await App.PM.ReadLevelChoicesAsync(classId ?? vm.ClassId, newLevel, true);
            var choices = vm.FilterUnmadeLevelChoices(all, newLevel);

            // Expertise carries no fixed option list, its pool is whatever skills this character is already proficient in, so fill it right before the dialog opens.
            var rules = App.PM.Rules ?? new GameRules();
            foreach (var ch in choices)
                if (ch.Options.Count == 0 && rules.IsExpertiseStore(ch.StoreAs))
                    foreach (var name in vm.ExpertiseCandidateSkills)
                        ch.Options.Add(new LevelChoiceOption(name, name));

            var dialog = new LevelUpDialog(newLevel, choices);
            await dialog.ShowDialog(owner);

            var picked = await dialog.GetResultAsync();
            if (picked == null || picked.Count == 0) return;
            await vm.ApplyLevelChoicesAsync(picked);
            vm.RecordAnsweredLevelChoices(all, choices, dialog.AnsweredChoices);

            await AskFollowUpChoicesAsync(owner, vm, newLevel);
        }
        catch (Exception ex) { ErrorLog.Log("Unhandled in OnLevelUpChoicesRequested", ex); }
    }

    private static async Task AskFollowUpChoicesAsync(Window owner, CharacterSheetViewModel vm, int newLevel)
    {
        for (var round = 0; round < 4; round++)
        {
            var runtime = vm.Runtime;
            if (runtime == null || App.PM == null) return;

            var owed = await App.PM.ResolveFeatChoicesAsync(runtime);
            if (owed.Count == 0) return;

            var asLevelChoices = owed.Select(o => new LevelChoice
            {
                Id = o.Id,
                Kind = o.Kind,
                Label = o.Label,
                Description = o.Description,
                ChooseCount = o.ChooseCount,
                StoreAs = o.StoreAs,
                Level = newLevel,
                Options = o.Options.Select(x => new LevelChoiceOption(x.Id, x.Name)).ToList()
            }).ToList();

            var follow = new LevelUpDialog(newLevel, asLevelChoices);
            await follow.ShowDialog(owner);

            var answered = await follow.GetResultAsync();
            if (answered == null || answered.Count == 0) return;

            await vm.ApplyLevelChoicesAsync(answered);
            vm.RecordAnsweredLevelChoices(asLevelChoices, asLevelChoices, follow.AnsweredChoices);
        }
    }

    private void OnOpenItemViewRequested(InventoryItemViewModel item)
    {
        if (this.GetVisualRoot() is not Window owner) return;
        var dialog = new ItemViewDialog(item.Name, item.Kind.ToString(), item.RawDataJson);
        _ = dialog.ShowDialog(owner);
    }

    private void OnOpenSpellViewRequested(SpellPrepEntry spell)
    {
        if (this.GetVisualRoot() is not Window owner) return;
        var dialog = new SpellViewDialog(spell.Name, spell.DataJson);
        _ = dialog.ShowDialog(owner);
    }

    private async void OnOpenItemEditRequested(InventoryItemViewModel item)
    {
        try
        {
            if (this.GetVisualRoot() is not Window owner) return;
            if (App.PM == null) return;

            var catalogs = await App.PM.ReadItemCatalogsAsync();
            var dialog = new ItemEditDialog(item.Name, item.Kind.ToString(), item.RawDataJson, catalogs);
            _ = dialog.ShowDialog(owner);

            var updated = await dialog.GetResultAsync();
            if (string.IsNullOrEmpty(updated)) return;

            await App.PM.SaveItemDataJsonAsync(item.BaseItemId, updated);
            if (DataContext is CharacterSheetViewModel vm) await vm.ReloadInventoryAsync();
        }
        catch (Exception ex) { ErrorLog.Log("Unhandled in OnOpenItemEditRequested", ex); }
    }

    private async Task<int?> OnChooseCastLevel(string spellName, List<CastLevelOption> options)
    {
        if (this.GetVisualRoot() is not Window owner) return null;
        var dlg = new CastLevelDialog(spellName, options);
        await dlg.ShowDialog(owner);
        return await dlg.GetResultAsync();
    }

    private async void OnAddItemRequested()
    {
        try
        {
            if (DataContext is not CharacterSheetViewModel vm) return;
            if (this.GetVisualRoot() is not Window owner) return;

            var catalog = await vm.LoadCatalogItemsAsync();
            var rows = catalog
                .Select(c => new AddItemDialog.ItemRow { Id = c.Id, Name = c.Name, ItemType = c.ItemType, DataJson = c.DataJson })
                .ToList();

            var heldProf = App.PM != null ? (await App.PM.ResolveProficienciesAsync(vm.ClassId, vm.RaceId)).AllIds : null;
            var armorMap = App.PM != null ? await App.PM.GetArmorProfMapAsync() : null;
            var dialog = new AddItemDialog(rows, heldProf, armorMap);
            _ = dialog.ShowDialog(owner);

            var picked = await dialog.GetResultAsync();
            if (picked == null) return;

            vm.AddCatalogItem(picked.Id, picked.Name, picked.DataJson);
        }
        catch (Exception ex) { ErrorLog.Log("Unhandled in OnAddItemRequested", ex); }
    }

    private async void OnTradeRequested()
    {
        try
        {
            if (DataContext is not CharacterSheetViewModel vm) return;
            if (this.GetVisualRoot() is not Window owner) return;
            if (App.PM == null) return;
            await TradeDialog.OpenInitiatorAsync(owner, vm.CharacterId, vm.Name, vm.OwnerUserId);
        }
        catch (Exception ex) { ErrorLog.Log("Unhandled in OnTradeRequested", ex); }
    }
    private async void OnUploadCharacterToken(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not CharacterSheetViewModel vm) return;
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Upload Character Token",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Images")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" }
                    }
                }
            });

            if (files.Count == 0) return;

            try
            {
                await using var stream = await files[0].OpenReadAsync();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                await vm.SetTokenImageAsync(ms.ToArray());
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[CharacterSheetView] token upload failed", ex);
            }
        }
        catch (Exception ex) { ErrorLog.Log("Unhandled in OnUploadCharacterToken", ex); }
    }

    private void OnNameTapped(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is CharacterSheetViewModel vm)
            vm.NameEditing = true;
    }

    private void OnNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CharacterSheetViewModel vm)
            vm.NameEditing = false;
    }

    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Escape)
        {
            if (DataContext is CharacterSheetViewModel vm)
                vm.NameEditing = false;
        }
    }
}