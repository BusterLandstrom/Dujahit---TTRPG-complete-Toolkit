using Dujahit.ViewModels;
using Markdown.Avalonia;
using System.Threading.Tasks;

namespace Dujahit.Views
{
    public class DmScreenPanelDialog : DialogWindow
    {
        public DmScreenPanelDialog(string title, string? content)
        {
            var viewer = new MarkdownScrollViewer
            {
                SelectionEnabled = true,
                Width = 560,
                MaxHeight = 620
            };
            _ = FillAsync(viewer, content);
            Mount(string.IsNullOrWhiteSpace(title) ? "Panel" : title, viewer);
        }

        private static async Task FillAsync(MarkdownScrollViewer viewer, string? content)
        {
            viewer.Markdown = await MarkdownEditorViewModel.PreRenderAsync(content ?? "");
        }
    }
}
