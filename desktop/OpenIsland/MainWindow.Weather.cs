using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        if (WeatherCityChangeButton != null)
        {
            WeatherCityChangeButton.Content = "Город";
            WeatherCityChangeButton.ToolTip = _weather.IsManualLocation
                ? "Город задан вручную (нажмите для смены или сброса на авто по IP)"
                : "Выбрать другой город вручную";
        }

        if (current == null)
        {
            if (WeatherConditionText != null)
                WeatherConditionText.Text = loading ? "Загрузка прогноза..." : (_weather.LastError ?? "Нет данных");
            if (WeatherLoading != null)
                WeatherLoading.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        if (WeatherCityText != null)
        {
            WeatherCityText.Text = current.CityName.ToUpperInvariant();
        }

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
                Background = _cardSectionBackground,
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
                Fill = _textPrimary,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            };

            var tempBlock = new TextBlock
            {
                Text = h.FormattedTemp,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = _textPrimary,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            stack.Children.Add(timeBlock);
            stack.Children.Add(icon);
            stack.Children.Add(tempBlock);
            pill.Child = stack;

            WeatherHourlyList.Children.Add(pill);
        }
    }

    void WeatherHourlyScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (WeatherHourlyScroll != null)
        {
            WeatherHourlyScroll.ScrollToHorizontalOffset(WeatherHourlyScroll.HorizontalOffset - (e.Delta * 0.75));
            e.Handled = true;
        }
    }

    void WeatherRow_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel(Panel.Weather);
        _ = _weather.GetWeatherAsync();
    }

    void WeatherBack_Click(object sender, RoutedEventArgs e)
    {
        LeaveTextEntry();
        if (WeatherCityPanel != null) WeatherCityPanel.Visibility = Visibility.Collapsed;
        ShowPanel(Panel.Menu);
    }

    void WeatherRefresh_Click(object sender, RoutedEventArgs e)
    {
        SoundFx.Play(SoundCue.Click);
        _ = _weather.GetWeatherAsync(force: true);
    }

    void WeatherCityChange_Click(object sender, RoutedEventArgs e)
    {
        if (WeatherCityPanel == null) return;
        bool opening = WeatherCityPanel.Visibility != Visibility.Visible;
        WeatherCityPanel.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
        SoundFx.Play(opening ? SoundCue.Pop : SoundCue.Tuck);

        if (opening)
        {
            EnterTextEntry();
            if (WeatherCitySearchBox != null)
            {
                WeatherCitySearchBox.Text = "";
                WeatherCitySearchBox.Focus();
            }
            if (WeatherCitySearchResults != null) WeatherCitySearchResults.Children.Clear();
            if (WeatherCitySearchStatus != null) WeatherCitySearchStatus.Visibility = Visibility.Collapsed;
        }
        else
        {
            LeaveTextEntry();
        }
    }

    void WeatherCityPanelClose_Click(object sender, RoutedEventArgs e)
    {
        if (WeatherCityPanel != null) WeatherCityPanel.Visibility = Visibility.Collapsed;
        LeaveTextEntry();
        SoundFx.Play(SoundCue.Tuck);
    }

    void WeatherCitySearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        EnterTextEntry();
    }

    void WeatherCitySearchBox_MouseDown(object sender, MouseButtonEventArgs e)
    {
        EnterTextEntry();
        WeatherCitySearchBox?.Focus();
    }

    void WeatherCitySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (WeatherCitySearchHint == null || WeatherCitySearchBox == null) return;
        bool empty = string.IsNullOrEmpty(WeatherCitySearchBox.Text);
        WeatherCitySearchHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (!empty && _panel == Panel.Weather)
        {
            _collapseTimeout.Cancel();
            _collapseTimeout.Start(LongCollapseDelay);
        }
    }

    void WeatherCitySearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            PerformCitySearch();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            WeatherCityPanelClose_Click(sender, e);
        }
    }

    void WeatherCityFind_Click(object sender, RoutedEventArgs e)
    {
        PerformCitySearch();
    }

    async void PerformCitySearch()
    {
        string query = WeatherCitySearchBox?.Text?.Trim() ?? "";
        if (query.Length < 2)
        {
            if (WeatherCitySearchStatus != null)
            {
                WeatherCitySearchStatus.Text = "Введите минимум 2 символа";
                WeatherCitySearchStatus.Visibility = Visibility.Visible;
            }
            return;
        }

        if (WeatherCitySearchStatus != null)
        {
            WeatherCitySearchStatus.Text = "Поиск...";
            WeatherCitySearchStatus.Visibility = Visibility.Visible;
        }
        if (WeatherCitySearchResults != null) WeatherCitySearchResults.Children.Clear();

        IReadOnlyList<GeoCity> cities = await _weather.SearchCitiesAsync(query);

        if (WeatherCitySearchResults == null || WeatherCitySearchStatus == null) return;

        WeatherCitySearchResults.Children.Clear();

        if (cities.Count == 0)
        {
            WeatherCitySearchStatus.Text = "Ничего не найдено";
            WeatherCitySearchStatus.Visibility = Visibility.Visible;
            return;
        }

        WeatherCitySearchStatus.Visibility = Visibility.Collapsed;

        foreach (GeoCity city in cities)
        {
            var btn = new Button
            {
                Style = (Style)FindResource("RowButton"),
                Height = 32,
                Margin = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(6, 4, 6, 4),
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new Icon
            {
                Kind = Glyph.Pin,
                Width = 14,
                Height = 14,
                Fill = _dim,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var nameBlock = new TextBlock
            {
                Text = city.DisplayName,
                FontSize = 12,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            stack.Children.Add(icon);
            stack.Children.Add(nameBlock);
            btn.Content = stack;

            btn.Click += async (_, _) =>
            {
                SoundFx.Play(SoundCue.Click);
                if (WeatherCityPanel != null) WeatherCityPanel.Visibility = Visibility.Collapsed;
                LeaveTextEntry();
                await _weather.SetManualCityAsync(city);
            };

            WeatherCitySearchResults.Children.Add(btn);
        }
    }

    async void WeatherAutoIp_Click(object sender, RoutedEventArgs e)
    {
        SoundFx.Play(SoundCue.Click);
        if (WeatherCityPanel != null) WeatherCityPanel.Visibility = Visibility.Collapsed;
        LeaveTextEntry();
        await _weather.ResetToAutoLocationAsync();
    }
}
