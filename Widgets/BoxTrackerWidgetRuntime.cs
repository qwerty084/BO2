using System;
using BO2.Services;

namespace BO2.Widgets
{
    internal sealed class BoxTrackerWidgetRuntime
    {
        private readonly IBoxTrackerWidgetNativeAdapter _nativeAdapter;
        private IBoxTrackerWidgetNativeWindow? _nativeWindow;
        private GameEventMonitorStatus _latestEventStatus = GameEventMonitorStatus.WaitingForMonitor;

        public BoxTrackerWidgetRuntime(IBoxTrackerWidgetNativeAdapter nativeAdapter)
        {
            _nativeAdapter = nativeAdapter ?? throw new ArgumentNullException(nameof(nativeAdapter));
        }

        public bool HasNativeWindow => _nativeWindow is not null;

        public IBoxTrackerWidgetNativeWindow EnsureNativeWindow()
        {
            _nativeWindow ??= _nativeAdapter.CreateWindow();
            return _nativeWindow;
        }

        public void Restore(WidgetSettings settings)
        {
            Restore(settings, _latestEventStatus);
        }

        public void Restore(WidgetSettings settings, GameEventMonitorStatus eventStatus)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(eventStatus);

            _latestEventStatus = eventStatus;

            if (!settings.Enabled)
            {
                _nativeWindow = null;
                return;
            }

            IBoxTrackerWidgetNativeWindow nativeWindow = EnsureNativeWindow();
            nativeWindow.UpdateText(GameEventFormatter.FormatBoxTrackerEvents(_latestEventStatus));
            nativeWindow.Activate();
            nativeWindow.ApplySettings(settings);
        }

        public void UpdateEventStatus(GameEventMonitorStatus eventStatus)
        {
            ArgumentNullException.ThrowIfNull(eventStatus);

            _latestEventStatus = eventStatus;
            _nativeWindow?.UpdateText(GameEventFormatter.FormatBoxTrackerEvents(eventStatus));
        }
    }
}
