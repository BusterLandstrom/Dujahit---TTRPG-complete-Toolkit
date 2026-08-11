using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Dujahit.ViewModels;
using System;
using System.Linq;
using Dujahit.Models;

namespace Dujahit.Views
{
    public partial class DmCommandsView : UserControl
    {
        private DmCommandsViewModel? _wired;

        public DmCommandsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_wired != null) _wired.GiftItemRequested -= OnGiftItemRequested;
            _wired = DataContext as DmCommandsViewModel;
            if (_wired != null) _wired.GiftItemRequested += OnGiftItemRequested;
        }

        private async void OnGiftItemRequested()
        {
            try
            {
                if (DataContext is not DmCommandsViewModel vm) return;
                if (this.GetVisualRoot() is not Window owner) return;
                if (App.PM == null) return;

                var catalog = await vm.LoadCatalogItemsAsync();
                var rows = catalog
                    .Select(c => new AddItemDialog.ItemRow { Id = c.Id, Name = c.Name, ItemType = c.ItemType, DataJson = c.DataJson })
                    .ToList();

                var dialog = new AddItemDialog(rows);
                _ = dialog.ShowDialog(owner);

                var picked = await dialog.GetResultAsync();
                if (picked == null) return;
                await vm.GiftItemToSelectedAsync(picked.Id, picked.Name);
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnGiftItemRequested", ex); }
        }
    }
}