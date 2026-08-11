using Avalonia.Controls;
using Dujahit.Models;
using Dujahit.ViewModels;
using System;
using Dujahit.Models.Application;

namespace Dujahit.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            UiScaleService.EnsureLoaded();
            var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
            if (screen != null && screen.Scaling > 0)
            {
                var logicalHeight = screen.WorkingArea.Height / screen.Scaling;
                UiScaleService.ApplyAutoDefault(Math.Clamp(logicalHeight / 1080.0, 0.8, 1.2));
            }
        }
    }
}