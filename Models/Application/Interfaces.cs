using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.Threading.Tasks;

namespace Dujahit.Models.Application
{
    public interface IFileDialogService
    {
        Task<string?> PickFileAsync(string title = "Open File");
    }
    
    public class FileDialogService : IFileDialogService
    {
        private readonly TopLevel _topLevel;

        public FileDialogService(TopLevel topLevel) => _topLevel = topLevel;

        public async Task<string?> PickFileAsync(string title = "Open File")
        {
            var files = await _topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });

            return files.Count >= 1 ? files[0].Path.LocalPath : null;
        }
    }
}
