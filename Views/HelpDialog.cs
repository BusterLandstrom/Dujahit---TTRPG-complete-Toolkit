namespace Dujahit.Views
{
    public class HelpDialog : DialogWindow
    {
        public HelpDialog()
        {
            // Tab strip wants about 840, the 760 default just hides the last two tabs.
            ContentWidthCap = 980;
            Width = 660;
            Height = 600;
            Mount("Help", new HelpView());
        }
    }
}
