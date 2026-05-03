using System;
using BO2.Services;

namespace BO2.Widgets
{
    internal sealed class BoxTrackerWidgetRuntime
    {
        private readonly IBoxTrackerWidgetNativeAdapter _nativeAdapter;
        private IBoxTrackerWidgetNativeWindow? _nativeWindow;

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
            ArgumentNullException.ThrowIfNull(settings);

            if (!settings.Enabled)
            {
                _nativeWindow = null;
            }
        }
    }
}
