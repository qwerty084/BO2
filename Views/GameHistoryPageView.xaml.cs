using System;
using BO2.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

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

        private void OnBackButtonClick(object sender, RoutedEventArgs e)
        {
            ViewModel?.ShowList();
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
}
