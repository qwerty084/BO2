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

        public bool SetEnabled(WidgetSettings settings, bool enabled, Action persistSettings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(persistSettings);

            if (settings.Enabled == enabled)
            {
                return false;
            }

            settings.Enabled = enabled;
            if (enabled)
            {
                IBoxTrackerWidgetNativeWindow nativeWindow = EnsureNativeWindow();
                nativeWindow.UpdateText(GameEventFormatter.FormatBoxTrackerEvents(_latestEventStatus));
                nativeWindow.Activate();
                nativeWindow.ApplySettings(settings);
            }
            else
            {
                CloseNativeWindow(settings);
            }

            persistSettings();
            return true;
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

        private void CloseNativeWindow(WidgetSettings settings)
        {
            if (_nativeWindow is null)
            {
                return;
            }

            IBoxTrackerWidgetNativeWindow nativeWindow = _nativeWindow;
            _nativeWindow = null;
            nativeWindow.CapturePlacement(settings);
            nativeWindow.Close();
        }
    }
}
