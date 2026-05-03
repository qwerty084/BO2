using System;
using BO2.Services;

namespace BO2.Widgets
{
    internal interface IBoxTrackerWidgetNativeAdapter
    {
        IBoxTrackerWidgetNativeWindow CreateWindow();
    }

    internal interface IBoxTrackerWidgetNativeWindow
    {
        event EventHandler? Closed;

        void Activate();

        void Close();

        void UpdateText(string text);

        void ApplySettings(WidgetSettings settings);

        void CapturePlacement(WidgetSettings settings);
    }
}
