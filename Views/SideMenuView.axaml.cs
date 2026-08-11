using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Dujahit.Models.Application;
using System.Collections.Generic;
using System.Windows.Input;
using System;
using Dujahit.Models;

namespace Dujahit.Views;

public partial class SideMenuView : UserControl
{
    public SideMenuView()
    {
        InitializeComponent();
    }

    private async void OnOpenHelp(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (this.GetVisualRoot() is not Window owner) return;
            await new HelpDialog().ShowDialog(owner);
        }
        catch (Exception ex) { ErrorLog.Log("Unhandled in OnOpenHelp", ex); }
    }
}