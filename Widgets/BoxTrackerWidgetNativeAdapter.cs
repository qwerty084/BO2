namespace BO2.Widgets
{
    internal sealed class BoxTrackerWidgetNativeAdapter : IBoxTrackerWidgetNativeAdapter
    {
        public IBoxTrackerWidgetNativeWindow CreateWindow()
        {
            return new BoxTrackerWidgetWindow();
        }
    }
}
