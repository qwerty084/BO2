using System;
using BO2.Services;

namespace BO2.Widgets
{
    internal sealed class WidgetWindowManager : IDisposable
    {
        private readonly WidgetSettingsStore _settingsStore;
        private readonly WidgetSettingsDocument _settingsDocument;
        private readonly BoxTrackerWidgetRuntime _boxTrackerRuntime;
        private GameEventMonitorStatus _latestEventStatus = GameEventMonitorStatus.WaitingForMonitor;

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
            _boxTrackerRuntime.Restore(
                BoxTrackerSettings,
                _latestEventStatus,
                SaveSettings,
                NotifySettingsChanged);
        }

        public void UpdateEventStatus(GameEventMonitorStatus eventStatus)
        {
            ArgumentNullException.ThrowIfNull(eventStatus);

            _latestEventStatus = eventStatus;
            _boxTrackerRuntime.UpdateEventStatus(eventStatus);
        }

        public void SetBoxTrackerEnabled(bool enabled)
        {
            if (_boxTrackerRuntime.SetEnabled(
                BoxTrackerSettings,
                enabled,
                SaveSettings,
                NotifySettingsChanged))
            {
                NotifySettingsChanged();
            }
        }

        public void ApplyBoxTrackerSettings(WidgetSettings settings)
        {
            settings.Normalize();
            _settingsDocument.SetWidget(WidgetKind.BoxTracker, settings);
            _boxTrackerRuntime.ApplySettings(
                BoxTrackerSettings,
                SaveSettings,
                NotifySettingsChanged);

            SaveSettings();
            NotifySettingsChanged();
        }

        public void Dispose()
        {
            _boxTrackerRuntime.Shutdown(BoxTrackerSettings, SaveSettings);
        }

        private void SaveSettings()
        {
            _settingsStore.Save(_settingsDocument);
        }

        private void NotifySettingsChanged()
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
