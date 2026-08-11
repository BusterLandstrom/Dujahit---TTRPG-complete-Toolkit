using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Dujahit.ViewModels;
using System;
using System.Threading.Tasks;

namespace Dujahit.Views
{
    public partial class SoundboardWidgetView : UserControl
    {
        public SoundboardWidgetView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (DataContext is SoundboardViewModel vm)
            {
                vm.ConfirmAsync -= ConfirmDelete;
                vm.ConfirmAsync += ConfirmDelete;
            }
        }

        private Task<bool> ConfirmDelete(string title, string message)
            => DialogWindow.ConfirmAsync(TopLevel.GetTopLevel(this) as Window, title, message);
    }
}
