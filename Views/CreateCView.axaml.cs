using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Dujahit.Models.Application;
using Dujahit.ViewModels;

namespace Dujahit.Views;

public partial class CreateCView : UserControl
{
    public CreateCView()
    {
        InitializeComponent();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is CreateCViewModel vm)
        {
            var topLevel = TopLevel.GetTopLevel(this)!;
            vm.SetFileDialogService(new FileDialogService(topLevel));
        }
    }
}