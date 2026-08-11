namespace Dujahit.ViewModels
{
    public class SoundboardScreenViewModel : ViewModelBase
    {
        public SoundboardViewModel Board { get; }
        public SoundboardScreenViewModel(SoundboardViewModel board) => Board = board;
    }
}
