using System;

namespace BO2.Widgets
{
    internal sealed class BoxTrackerWidgetNativeAdapter : IBoxTrackerWidgetNativeAdapter
    {
        public IBoxTrackerWidgetNativeWindow CreateWindow()
        {
            throw new InvalidOperationException(
                "Tests should inject BoxTrackerWidgetRuntime with a fake native adapter.");
        }
    }
}
