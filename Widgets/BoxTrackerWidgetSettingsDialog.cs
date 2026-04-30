using BO2.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace BO2.Widgets
{
    internal static class BoxTrackerWidgetSettingsDialog
    {
        public static async Task<WidgetSettings?> ShowAsync(
            XamlRoot xamlRoot,
            WidgetSettings sourceSettings,
            ElementTheme requestedTheme)
        {
            ArgumentNullException.ThrowIfNull(xamlRoot);
            ArgumentNullException.ThrowIfNull(sourceSettings);

            WidgetSettings settings = sourceSettings.Clone();
            NumberBox widthBox = CreateNumberBox(AppStrings.Get("WidgetWidthLabel"), settings.Width, 160, 3840);
            NumberBox heightBox = CreateNumberBox(AppStrings.Get("WidgetHeightLabel"), settings.Height, 80, 2160);
            ColorPicker backgroundColorPicker = CreateColorPicker(settings.BackgroundColor, Colors.White);
            ColorPicker textColorPicker = CreateColorPicker(settings.TextColor, Colors.Black);
            CheckBox transparentBackgroundCheckBox = new()
            {
                Content = AppStrings.Get("WidgetTransparentBackgroundLabel"),
                IsChecked = settings.TransparentBackground
            };
            CheckBox alwaysOnTopCheckBox = new()
            {
                Content = AppStrings.Get("WidgetAlwaysOnTopLabel"),
                IsChecked = settings.AlwaysOnTop
            };
            CheckBox centerAlignCheckBox = new()
            {
                Content = AppStrings.Get("WidgetCenterAlignLabel"),
                IsChecked = settings.CenterAlign
            };

            StackPanel content = new()
            {
                Spacing = 12,
                MaxWidth = 520
            };
            content.Children.Add(widthBox);
            content.Children.Add(heightBox);
            content.Children.Add(CreateLabeledColorPicker(AppStrings.Get("WidgetBackgroundColorLabel"), backgroundColorPicker));
            content.Children.Add(CreateLabeledColorPicker(AppStrings.Get("WidgetTextColorLabel"), textColorPicker));
            content.Children.Add(transparentBackgroundCheckBox);
            content.Children.Add(alwaysOnTopCheckBox);
            content.Children.Add(centerAlignCheckBox);

            ScrollViewer dialogContent = new()
            {
                Content = content,
                IsTabStop = true,
                Padding = new Thickness(0, 0, 20, 0),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled
            };

            ContentDialog dialog = new()
            {
                Title = AppStrings.Get("BoxTrackerWidgetConfigTitle"),
                Content = dialogContent,
                PrimaryButtonText = AppStrings.Get("WidgetConfigSaveButton"),
                CloseButtonText = AppStrings.Get("WidgetConfigCancelButton"),
                DefaultButton = ContentDialogButton.Primary,
                RequestedTheme = requestedTheme,
                XamlRoot = xamlRoot
            };

            dialog.Opened += (_, _) =>
            {
                _ = dialog.DispatcherQueue.TryEnqueue(
                    DispatcherQueuePriority.Low,
                    () =>
                    {
                        _ = dialogContent.Focus(FocusState.Programmatic);
                        dialogContent.IsTabStop = false;
                    });
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            settings.Width = ReadNumberBoxValue(widthBox, WidgetSettings.DefaultWidth);
            settings.Height = ReadNumberBoxValue(heightBox, WidgetSettings.DefaultHeight);
            settings.BackgroundColor = WidgetColorSerializer.Format(backgroundColorPicker.Color);
            settings.TextColor = WidgetColorSerializer.Format(textColorPicker.Color);
            settings.TransparentBackground = transparentBackgroundCheckBox.IsChecked == true;
            settings.AlwaysOnTop = alwaysOnTopCheckBox.IsChecked == true;
            settings.CenterAlign = centerAlignCheckBox.IsChecked == true;
            return settings;
        }

        private static NumberBox CreateNumberBox(string header, int value, int minimum, int maximum)
        {
            return new NumberBox
            {
                Header = header,
                Value = value,
                Minimum = minimum,
                Maximum = maximum,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
            };
        }

        private static ColorPicker CreateColorPicker(string value, Windows.UI.Color defaultColor)
        {
            return new ColorPicker
            {
                Color = WidgetColorSerializer.ParseOrDefault(value, defaultColor),
                IsAlphaEnabled = true,
                IsColorSliderVisible = true,
                IsColorChannelTextInputVisible = true,
                IsHexInputVisible = true
            };
        }

        private static FrameworkElement CreateLabeledColorPicker(string label, ColorPicker colorPicker)
        {
            StackPanel panel = new()
            {
                Spacing = 6
            };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Style = (Style)Application.Current.Resources["BO2CaptionTextBlockStyle"]
            });
            panel.Children.Add(colorPicker);
            return panel;
        }

        private static int ReadNumberBoxValue(NumberBox numberBox, int fallback)
        {
            if (double.IsNaN(numberBox.Value))
            {
                return fallback;
            }

            return (int)Math.Round(numberBox.Value);
        }
    }
}
