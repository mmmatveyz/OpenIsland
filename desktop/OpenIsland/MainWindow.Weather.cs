using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenIsland;

public partial class MainWindow
{
    readonly WeatherService _weather = new();

    void InitWeather()
    {
        _weather.Changed += () => Dispatcher.Invoke(SyncWeatherUI);
    }

    void SyncWeatherUI()
    {
        CurrentWeather? current = _weather.Current;
        bool loading = _weather.IsLoading;

        if (MenuWeatherTemp != null)
        {
            MenuWeatherTemp.Text = current != null ? current.FormattedTemp : (loading ? "..." : "");
        }

        if (current == null)
        {
            if (WeatherConditionText != null)
                WeatherConditionText.Text = loading ? "Загрузка прогноза..." : (_weather.LastError ?? "Нет данных");
            if (WeatherLoading != null)
                WeatherLoading.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        if (WeatherCityText != null) WeatherCityText.Text = current.CityName;
        if (WeatherBigTemp != null) WeatherBigTemp.Text = current.FormattedTemp;
        if (WeatherConditionText != null) WeatherConditionText.Text = current.Condition.Description;
        if (WeatherFeelsText != null) WeatherFeelsText.Text = $"Ощущается как {current.FormattedApparent}";
        if (WeatherIcon != null) WeatherIcon.Kind = current.Condition.Glyph;

        if (WeatherWindText != null) WeatherWindText.Text = current.FormattedWind;
        if (WeatherHumidityText != null) WeatherHumidityText.Text = current.FormattedHumidity;
        if (WeatherPrecipText != null) WeatherPrecipText.Text = current.FormattedPrecipitation;
        if (WeatherUpdatedText != null) WeatherUpdatedText.Text = $"Обновлено в {current.UpdatedAt:HH:mm}";

        if (WeatherLoading != null)
            WeatherLoading.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;

        BuildHourlyForecast(current);
    }

    void BuildHourlyForecast(CurrentWeather current)
    {
        if (WeatherHourlyList == null) return;
        WeatherHourlyList.Children.Clear();

        foreach (HourlyForecast h in current.Hourly)
        {
            var pill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
                CornerRadius = new CornerRadius(12),
                Width = 52,
                Height = 84,
                Margin = new Thickness(3, 0, 3, 0),
                Padding = new Thickness(4, 6, 4, 6),
            };

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var timeBlock = new TextBlock
            {
                Text = h.HourText,
                FontSize = 10,
                Foreground = _dim,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            };

            var icon = new Icon
            {
                Kind = h.Condition.Glyph,
                Width = 18,
                Height = 18,
                Fill = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            };

            var tempBlock = new TextBlock
            {
                Text = h.FormattedTemp,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            stack.Children.Add(timeBlock);
            stack.Children.Add(icon);
            stack.Children.Add(tempBlock);
            pill.Child = stack;

            WeatherHourlyList.Children.Add(pill);
        }
    }

    void WeatherRow_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel(Panel.Weather);
        _ = _weather.GetWeatherAsync();
    }

    void WeatherBack_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Menu);

    void WeatherRefresh_Click(object sender, RoutedEventArgs e)
    {
        _ = _weather.GetWeatherAsync(force: true);
    }
}
