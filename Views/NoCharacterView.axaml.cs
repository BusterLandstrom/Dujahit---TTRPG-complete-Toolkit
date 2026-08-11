using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace Dujahit.Views;

public partial class NoCharacterView : UserControl
{
    private int _taps;

    public NoCharacterView()
    {
        InitializeComponent();
        HeaderText.PointerPressed += OnHeaderPressed;
    }

    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DoctorArt.IsVisible || ++_taps < 5) return;
        using var stream = AssetLoader.Open(new Uri("avares://Dujahit/Assets/chopper.txt"));
        using var reader = new StreamReader(stream);
        DoctorArt.Text = reader.ReadToEnd();
        DoctorArt.IsVisible = true;
    }
}
