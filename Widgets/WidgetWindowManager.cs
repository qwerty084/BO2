using System;
using BO2.Services;

namespace BO2.Widgets
{
    internal sealed class WidgetWindowManager : IDisposable
    {
        private readonly WidgetSettingsStore _settingsStore;
        private readonly WidgetSettingsDocument _settingsDocument;
        private readonly BoxTrackerWidgetRuntime _boxTrackerRuntime;
        private BoxTrackerWidgetWindow? _boxTrackerWindow;
        private GameEventMonitorStatus _latestEventStatus = GameEventMonitorStatus.WaitingForMonitor;
        private bool _isShuttingDown;

        public WidgetWindowManager()
            : this(WidgetSettingsStore.CreateDefault())
        {
        }

        public WidgetWindowManager(WidgetSettingsStore settingsStore)
            : this(settingsStore, new BoxTrackerWidgetRuntime(new BoxTrackerWidgetNativeAdapter()))
        {
        }

        internal WidgetWindowManager(
            WidgetSettingsStore settingsStore,
            BoxTrackerWidgetRuntime boxTrackerRuntime)
        {
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            _settingsDocument = _settingsStore.Load();
            _boxTrackerRuntime = boxTrackerRuntime ?? throw new ArgumentNullException(nameof(boxTrackerRuntime));
        }

        public event EventHandler? SettingsChanged;

        public WidgetSettings BoxTrackerSettings => _settingsDocument.GetWidget(WidgetKind.BoxTracker);

        public WidgetSettingsLoadRecovery? SettingsLoadRecovery => _settingsStore.LastLoadRecovery;

        public bool IsBoxTrackerEnabled => BoxTrackerSettings.Enabled;

        public void RestoreEnabledWidgets()
        {
            _boxTrackerRuntime.Restore(BoxTrackerSettings, _latestEventStatus);
        }

        public void UpdateEventStatus(GameEventMonitorStatus eventStatus)
        {
            ArgumentNullException.ThrowIfNull(eventStatus);

            _latestEventStatus = eventStatus;
            _boxTrackerRuntime.UpdateEventStatus(eventStatus);
            _boxTrackerWindow?.UpdateText(GameEventFormatter.FormatBoxTrackerEvents(eventStatus));
        }

        public void SetBoxTrackerEnabled(bool enabled)
        {
            if (_boxTrackerRuntime.SetEnabled(BoxTrackerSettings, enabled, SaveSettings))
            {
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void ApplyBoxTrackerSettings(WidgetSettings settings)
        {
            settings.Normalize();
            _settingsDocument.SetWidget(WidgetKind.BoxTracker, settings);
            if (settings.Enabled)
            {
                OpenBoxTracker();
                _boxTrackerWindow?.ApplySettings(settings);
            }
            else
            {
                CloseBoxTracker(disable: false);
            }

            SaveSettings();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _isShuttingDown = true;
            CaptureOpenWindowPlacement();
            SaveSettings();
            _boxTrackerWindow?.Close();
            _boxTrackerWindow = null;
        }

        private void OpenBoxTracker()
        {
            if (_boxTrackerWindow is not null)
            {
                return;
            }

            WidgetSettings settings = BoxTrackerSettings;
            _boxTrackerWindow = new BoxTrackerWidgetWindow();
            _boxTrackerWindow.Closed += OnBoxTrackerClosed;
            _boxTrackerWindow.UpdateText(GameEventFormatter.FormatBoxTrackerEvents(_latestEventStatus));
            _boxTrackerWindow.Activate();
            _boxTrackerWindow.ApplySettings(settings);
        }

        private void CloseBoxTracker(bool disable)
        {
            if (_boxTrackerWindow is null)
            {
                if (disable)
                {
                    BoxTrackerSettings.Enabled = false;
                }

                return;
            }

            BoxTrackerWidgetWindow window = _boxTrackerWindow;
            window.CapturePlacement(BoxTrackerSettings);
            _boxTrackerWindow = null;
            window.Closed -= OnBoxTrackerClosed;
            if (disable)
            {
                BoxTrackerSettings.Enabled = false;
            }

            window.Close();
        }

        private void OnBoxTrackerClosed(object? sender, EventArgs args)
        {
            if (sender is BoxTrackerWidgetWindow window)
            {
                window.CapturePlacement(BoxTrackerSettings);
                window.Closed -= OnBoxTrackerClosed;
            }

            _boxTrackerWindow = null;
            if (!_isShuttingDown)
            {
                BoxTrackerSettings.Enabled = false;
                SaveSettings();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void CaptureOpenWindowPlacement()
        {
            _boxTrackerWindow?.CapturePlacement(BoxTrackerSettings);
        }

        private void SaveSettings()
        {
            _settingsStore.Save(_settingsDocument);
        }
    }
}
