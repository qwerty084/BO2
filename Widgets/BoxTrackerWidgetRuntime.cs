using System;
using BO2.Services;

namespace BO2.Widgets
{
    internal sealed class BoxTrackerWidgetRuntime
    {
        private readonly IBoxTrackerWidgetNativeAdapter _nativeAdapter;
        private IBoxTrackerWidgetNativeWindow? _nativeWindow;
        private WidgetSettings? _settings;
        private Action? _persistSettings;
        private Action? _notifySettingsChanged;
        private GameEventMonitorStatus _latestEventStatus = GameEventMonitorStatus.WaitingForMonitor;

        public BoxTrackerWidgetRuntime(IBoxTrackerWidgetNativeAdapter nativeAdapter)
        {
            _nativeAdapter = nativeAdapter ?? throw new ArgumentNullException(nameof(nativeAdapter));
        }

        public bool HasNativeWindow => _nativeWindow is not null;

        public IBoxTrackerWidgetNativeWindow EnsureNativeWindow()
        {
            if (_nativeWindow is null)
            {
                _nativeWindow = _nativeAdapter.CreateWindow();
                _nativeWindow.Closed += OnNativeWindowClosed;
            }

            return _nativeWindow;
        }

        public bool SetEnabled(
            WidgetSettings settings,
            bool enabled,
            Action persistSettings,
            Action? notifySettingsChanged = null)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(persistSettings);
            BindSettings(settings, persistSettings, notifySettingsChanged);

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

        public void Restore(
            WidgetSettings settings,
            GameEventMonitorStatus eventStatus,
            Action? persistSettings = null,
            Action? notifySettingsChanged = null)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(eventStatus);

            BindSettings(settings, persistSettings, notifySettingsChanged);
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

        public void ApplySettings(
            WidgetSettings settings,
            Action? persistSettings = null,
            Action? notifySettingsChanged = null)
        {
            ArgumentNullException.ThrowIfNull(settings);

            BindSettings(settings, persistSettings, notifySettingsChanged);
            if (!settings.Enabled)
            {
                CloseNativeWindow(settings);
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

        public bool Shutdown(WidgetSettings settings, Action persistSettings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(persistSettings);

            BindSettings(settings, persistSettings, notifySettingsChanged: null);
            if (!CloseNativeWindow(settings))
            {
                return false;
            }

            persistSettings();
            return true;
        }

        private void BindSettings(
            WidgetSettings settings,
            Action? persistSettings,
            Action? notifySettingsChanged)
        {
            _settings = settings;
            _persistSettings = persistSettings;
            _notifySettingsChanged = notifySettingsChanged;
        }

        private bool CloseNativeWindow(WidgetSettings settings)
        {
            if (_nativeWindow is null)
            {
                return false;
            }

            IBoxTrackerWidgetNativeWindow nativeWindow = _nativeWindow;
            nativeWindow.Closed -= OnNativeWindowClosed;
            nativeWindow.CapturePlacement(settings);
            _nativeWindow = null;
            nativeWindow.Close();
            return true;
        }

        private void OnNativeWindowClosed(object? sender, EventArgs args)
        {
            if (sender is not IBoxTrackerWidgetNativeWindow nativeWindow)
            {
                return;
            }

            nativeWindow.Closed -= OnNativeWindowClosed;
            if (!ReferenceEquals(nativeWindow, _nativeWindow))
            {
                return;
            }

            if (_settings is not null)
            {
                nativeWindow.CapturePlacement(_settings);
                _settings.Enabled = false;
            }

            _nativeWindow = null;
            _persistSettings?.Invoke();
            _notifySettingsChanged?.Invoke();
        }
    }
}
