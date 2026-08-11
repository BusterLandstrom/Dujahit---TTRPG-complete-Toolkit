using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Dujahit.ViewModels;
using System;

namespace Dujahit.Views;

public partial class CharacterCreationView : UserControl
{
    private CharacterCreationViewModel? _wired;

    public CharacterCreationView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_wired != null) _wired.OpenSpellViewRequested -= OnOpenSpellViewRequested;
        _wired = DataContext as CharacterCreationViewModel;
        if (_wired != null) _wired.OpenSpellViewRequested += OnOpenSpellViewRequested;
    }

    private void OnOpenSpellViewRequested(SpellPrepEntry spell)
    {
        if (this.GetVisualRoot() is not Window owner) return;
        var dialog = new SpellViewDialog(spell.Name, spell.DataJson);
        _ = dialog.ShowDialog(owner);
    }
}