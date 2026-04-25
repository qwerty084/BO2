---
description: 'WinUI 3 and Windows App SDK guidelines. Prevents common UWP API misuse, enforces correct XAML namespaces, threading, windowing, and MVVM patterns for desktop Windows apps.'
applyTo: '**/*.xaml, **/*.cs, **/*.csproj'
---

# WinUI 3 / Windows App SDK

## Critical Rules — NEVER Use Legacy UWP APIs

These UWP patterns are **wrong** for WinUI 3 desktop apps. Always use the Windows App SDK equivalent.

- **NEVER** use `Windows.UI.Popups.MessageDialog`. Use `ContentDialog` with `XamlRoot` set.
- **NEVER** show a `ContentDialog` without setting `dialog.XamlRoot = this.Content.XamlRoot` (or `this.XamlRoot` from a Page) first.
- **NEVER** use `CoreDispatcher.RunAsync` or `Dispatcher.RunAsync`. Use `DispatcherQueue.TryEnqueue`.
- **NEVER** use `Window.Current`. Track the main window via `App.MainWindow` (static property).
- **NEVER** use `Windows.UI.Xaml.*` namespaces. Use `Microsoft.UI.Xaml.*`.
- **NEVER** use `Windows.UI.Composition`. Use `Microsoft.UI.Composition`.
- **NEVER** use `Windows.UI.Colors`. Use `Microsoft.UI.Colors`.
- **NEVER** use `ApplicationView` or `CoreWindow` for window management. Use `Microsoft.UI.Windowing.AppWindow`.
- **NEVER** use `CoreApplicationViewTitleBar`. Use `AppWindowTitleBar`.
- **NEVER** use `GetForCurrentView()` patterns (e.g., `UIViewSettings.GetForCurrentView()`). These do not exist in desktop WinUI 3.
- **NEVER** use UWP `PrintManager` directly. Use `IPrintManagerInterop` with a window handle.
- **NEVER** use `DataTransferManager` directly for sharing. Use `IDataTransferManagerInterop` with a window handle.
- **NEVER** use UWP `IBackgroundTask`. Use `Microsoft.Windows.AppLifecycle` activation.
- **NEVER** use `WebAuthenticationBroker`. Use `OAuth2Manager` (Windows App SDK 1.7+).

## XAML Patterns

- The default XAML namespace maps to `Microsoft.UI.Xaml`, not `Windows.UI.Xaml`.
- Prefer `{x:Bind}` over `{Binding}` for compiled, type-safe, higher-performance bindings.
- **Known project exception:** The status indicator `Ellipse` in `MainWindow.xaml` uses `{Binding}` with a converter because `x:Bind` with converters at the Window root generates invalid code. All other bindings must use `x:Bind`.
- Set `x:DataType` on `DataTemplate` elements when using `{x:Bind}` — required for compiled bindings in templates.
- Use `Mode=OneWay` for dynamic values, `Mode=OneTime` for static, `Mode=TwoWay` only for editable inputs.
- Do not bind static constants — set them directly in XAML.
- Use `x:Uid` for all user-visible text (localization via `.resw` files) — no hard-coded `Text`/`Content` strings.

## Threading

- Use `DispatcherQueue.TryEnqueue(() => { ... })` to update UI from background threads.
- `TryEnqueue` returns `bool`, not a `Task` — it is fire-and-forget.
- Check thread access with `DispatcherQueue.HasThreadAccess` before dispatching.
- WinUI 3 uses standard STA (not ASTA). No built-in reentrancy protection — be cautious with async code that pumps messages.

## Windowing

- Get the `AppWindow` from a WinUI 3 `Window` via `WindowNative.GetWindowHandle` → `Win32Interop.GetWindowIdFromWindow` → `AppWindow.GetFromWindowId`.
- Use `AppWindow` for resize, move, title, and presenter operations.
- Custom title bar: use `AppWindow.TitleBar` properties, not `CoreApplicationViewTitleBar`.
- Track the main window as `App.MainWindow` (static property set in `OnLaunched`).

## Dialogs and Pickers

- **ContentDialog**: Always set `dialog.XamlRoot` before calling `ShowAsync()`.
- **Project-specific:** This project uses `WinUISDKReferences=false`, so `ContentDialog.ShowAsync()` cannot be directly awaited. Use event-based pattern: `dialog.PrimaryButtonClick += ...; _ = dialog.ShowAsync();`
- **File/Folder Pickers**: Initialize with `WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd)` where `hwnd` comes from `WindowNative.GetWindowHandle(App.MainWindow)`.

## MVVM and Data Binding

- This project uses **manual `INotifyPropertyChanged`** with `[CallerMemberName]` — not CommunityToolkit.Mvvm.
- Use the custom `RelayCommand` / `RelayCommand<T>` from `Commands/` for ICommand bindings.
- Keep UI (Views) focused on layout and bindings; keep logic in ViewModels and services.
- Use `async`/`await` for I/O and long-running work to keep the UI responsive.

## Project Setup

- Target `net8.0-windows10.0.19041.0` with `<Platforms>x86</Platforms>` (games are 32-bit).
- Set `<UseWinUI>true</UseWinUI>` and `<WinUISDKReferences>false</WinUISDKReferences>` in the project file.
- Reference the latest stable `Microsoft.WindowsAppSDK` NuGet package.

## C# Code Style

- Use block-scoped namespaces (not file-scoped) — this is the project convention.
- Enable nullable reference types. Use `is null` / `is not null` instead of `== null`.
- Prefer pattern matching over `as`/`is` with null checks.
- PascalCase for types, methods, properties. `_camelCase` (underscore prefix) for private fields.
- Allman brace style (opening brace on its own line).

## Accessibility

- Set `AutomationProperties.Name` on all interactive controls.
- Use `AutomationProperties.HeadingLevel` on section headers.
- Hide decorative elements with `AutomationProperties.AccessibilityView="Raw"`.
- Ensure full keyboard navigation (Tab, Enter, Space, arrow keys).
- Meet WCAG color contrast requirements.

## Performance

- Prefer `{x:Bind}` (compiled) over `{Binding}` (reflection-based).
- Use `x:Load` or `x:DeferLoadStrategy` for UI elements that are not immediately needed.
- Use `ItemsRepeater` with virtualization for large lists.
- Avoid deep layout nesting — prefer `Grid` over nested `StackPanel` chains.
- Use `async`/`await` for all I/O; never block the UI thread.

## App Settings

- **Packaged apps**: `ApplicationData.Current.LocalSettings` works as expected.
- **Unpackaged apps**: Settings may not persist — use a custom settings file as fallback.
- This project uses `ApplicationData.Current.LocalSettings` for language preference (`LanguageOverride` key).

## Typography

- Use built-in TextBlock styles (`CaptionTextBlockStyle`, `BodyTextBlockStyle`, `BodyStrongTextBlockStyle`, `SubtitleTextBlockStyle`, `TitleTextBlockStyle`, `TitleLargeTextBlockStyle`, `DisplayTextBlockStyle`).
- Prefer built-in styles over hardcoding `FontSize`, `FontWeight`, or `FontFamily`.
- Font: Segoe UI Variable is the default — do not change it.
- Use sentence casing for all UI text.

## Theming & Colors

- **Always** use `{ThemeResource}` for brushes and colors to support Light, Dark, and High Contrast themes.
- **Never** hardcode color values (`#FFFFFF`, `Colors.White`) for UI elements. Use theme resources like `TextFillColorPrimaryBrush`, `CardBackgroundFillColorDefaultBrush`.
- Use `SystemAccentColor` for the user's accent color palette.
- For borders: use `CardStrokeColorDefaultBrush` or `ControlStrokeColorDefaultBrush`.

## Spacing & Layout

- Use a **4px grid system**: all margins, padding, and spacing values must be multiples of 4px.
- Standard spacing: 4 (compact), 8 (controls), 12 (small gutters), 16 (content padding), 24 (large gutters).
- Prefer `Grid` over deeply nested `StackPanel` chains for performance.
- Use `Auto` for content-sized rows/columns, `*` for proportional sizing. Avoid fixed pixel sizes.
- Use `VisualStateManager` with `AdaptiveTrigger` for responsive layouts at breakpoints (640px, 1008px).
- Use `ControlCornerRadius` (4px) for small controls and `OverlayCornerRadius` (8px) for cards, dialogs, flyouts.

## Materials & Elevation

- Use **Mica** (`MicaBackdrop`) for the app window backdrop. Requires transparent layers above to show through.
- Use **Acrylic** for transient surfaces only (flyouts, menus, navigation panes).
- Use `LayerFillColorDefaultBrush` for content layers above Mica.
- Use `ThemeShadow` with Z-axis `Translation` for elevation.

## Motion & Transitions

- Use built-in theme transitions (`EntranceThemeTransition`, `RepositionThemeTransition`, `ContentThemeTransition`, `AddDeleteThemeTransition`).
- Avoid custom storyboard animations when a built-in transition exists.

## Control Selection

- Use `NavigationView` for primary app navigation (not custom sidebars).
- Use `InfoBar` for persistent in-app notifications (not custom banners).
- Use `TeachingTip` for contextual guidance (not custom popups).
- Use `NumberBox` for numeric input (not TextBox with manual validation).
- Use `ToggleSwitch` for on/off settings (not CheckBox).
- Use `ItemsView` for modern collection display with built-in selection and virtualization.
- Use `ListView`/`GridView` for standard virtualized lists and grids.
- Use `ItemsRepeater` only for fully custom virtualizing layouts.
- Use `Expander` for collapsible sections (not custom visibility toggling).

## Error Handling

- Always wrap `async void` event handlers in try/catch to prevent unhandled crashes.
- Use `InfoBar` (with `Severity = Error`) for user-facing error messages, not `ContentDialog` for routine errors.
- Handle `App.UnhandledException` for logging and graceful recovery.

## Testing

- This project uses a separate xUnit test project (`ZombieForge.Tests/`) that **links source files** (not a project reference) to avoid WinAppSDK runtime auto-init failures.
- Pure logic tests (models, services) use standard `[Fact]`/`[Theory]` with xUnit.
- Tests that create WinUI XAML types require a **Unit Test App (WinUI in Desktop)** project.

## Resources & Localization

- Store user-facing strings in `Resources.resw` files (`Strings/<locale>/`), not in code or XAML literals.
- Use `x:Uid` in XAML for localized text binding.
- Use DPI-qualified image assets (`logo.scale-200.png`); reference without scale qualifier (`ms-appx:///Assets/logo.png`).
