using System;
using BO2.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace BO2.Views
{
    public sealed partial class GameHistoryPageView : UserControl
    {
        public GameHistoryPageView()
        {
            InitializeComponent();
        }

        private GameHistoryPageViewModel? ViewModel => DataContext as GameHistoryPageViewModel;

        private void OnSavedGameItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is GameHistorySummaryViewModel summary)
            {
                ViewModel?.SelectGame(summary);
            }
        }

        private void OnHideHistoryRailButtonClick(object sender, RoutedEventArgs e)
        {
            ViewModel?.HideHistoryRail();
        }

        private void OnShowHistoryRailButtonClick(object sender, RoutedEventArgs e)
        {
            ViewModel?.ShowHistoryRail();
        }
    }

    public sealed class BooleanToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isVisible = value is bool boolValue && boolValue;
            if (Invert)
            {
                isVisible = !isVisible;
            }

            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class BooleanToGridLengthConverter : IValueConverter
    {
        public double VisibleLength { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isVisible = value is bool boolValue && boolValue;
            return isVisible ? new GridLength(VisibleLength) : new GridLength(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class SignedStatBrushConverter : DependencyObject, IValueConverter
    {
        public static readonly DependencyProperty PositiveBrushProperty =
            DependencyProperty.Register(
                nameof(PositiveBrush),
                typeof(Brush),
                typeof(SignedStatBrushConverter),
                new PropertyMetadata(null));

        public static readonly DependencyProperty NegativeBrushProperty =
            DependencyProperty.Register(
                nameof(NegativeBrush),
                typeof(Brush),
                typeof(SignedStatBrushConverter),
                new PropertyMetadata(null));

        public static readonly DependencyProperty NeutralBrushProperty =
            DependencyProperty.Register(
                nameof(NeutralBrush),
                typeof(Brush),
                typeof(SignedStatBrushConverter),
                new PropertyMetadata(null));

        public static readonly DependencyProperty InvertProperty =
            DependencyProperty.Register(
                nameof(Invert),
                typeof(bool),
                typeof(SignedStatBrushConverter),
                new PropertyMetadata(false));

        public Brush? PositiveBrush
        {
            get => (Brush?)GetValue(PositiveBrushProperty);
            set => SetValue(PositiveBrushProperty, value);
        }

        public Brush? NegativeBrush
        {
            get => (Brush?)GetValue(NegativeBrushProperty);
            set => SetValue(NegativeBrushProperty, value);
        }

        public Brush? NeutralBrush
        {
            get => (Brush?)GetValue(NeutralBrushProperty);
            set => SetValue(NeutralBrushProperty, value);
        }

        public bool Invert
        {
            get => (bool)GetValue(InvertProperty);
            set => SetValue(InvertProperty, value);
        }

        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            string text = value as string ?? string.Empty;
            int sign = text.TrimStart() switch
            {
                string trimmed when trimmed.StartsWith('+') => 1,
                string trimmed when trimmed.StartsWith('-') => -1,
                _ => 0
            };

            if (Invert)
            {
                sign *= -1;
            }

            return sign switch
            {
                > 0 => PositiveBrush ?? NeutralBrush,
                < 0 => NegativeBrush ?? NeutralBrush,
                _ => NeutralBrush
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }
}
