using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LinuxTerm.Gui.Controls;
using LinuxTerm.Gui.Themes;

namespace LinuxTerm.Gui;

public partial class MainWindow : Window
{
    private class TerminalTab
    {
        public string Title { get; set; } = "Terminal";
        public TerminalControl Control { get; }
        public Border HeaderElement { get; }
        public TextBlock HeaderTextBlock { get; }

        public TerminalTab(TerminalControl control, Border headerElement, TextBlock headerTextBlock)
        {
            Control = control;
            HeaderElement = headerElement;
            HeaderTextBlock = headerTextBlock;
        }
    }

    private readonly List<TerminalTab> _tabs = new();
    private int _activeTabIndex = -1;
    private TerminalTheme _currentTheme = TerminalTheme.VsCodeDark;
    private double _currentFontSize = 13.5;
    private int _tabCounter = 1;

    public MainWindow()
    {
        InitializeComponent();

        // Populate Theme Selector
        ThemeSelector.ItemsSource = TerminalTheme.AllThemes;
        ThemeSelector.DisplayMemberPath = "Name";
        ThemeSelector.SelectedItem = TerminalTheme.VsCodeDark;

        // Open initial tab
        AddNewTab();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void AddNewTab(string? initialDir = null)
    {
        var control = new TerminalControl(initialDir);
        control.ApplyTheme(_currentTheme);
        control.SetFontSize(_currentFontSize);

        int tabNum = _tabCounter++;
        string initialTitle = $"bash {tabNum} (~)";

        // Create Tab Header
        var headerBorder = new Border
        {
            Background = _currentTheme.TabActiveBackgroundBrush,
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(10, 5, 8, 5),
            Margin = new Thickness(0),
            Cursor = Cursors.Hand,
            Height = 32
        };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleBlock = new TextBlock
        {
            Text = initialTitle,
            Foreground = _currentTheme.ForegroundBrush,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(titleBlock, 0);
        headerGrid.Children.Add(titleBlock);

        var closeBtn = new Button
        {
            Content = "×",
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
            BorderThickness = new Thickness(0),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Width = 18,
            Height = 18,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(closeBtn, 1);
        headerGrid.Children.Add(closeBtn);

        headerBorder.Child = headerGrid;

        var tab = new TerminalTab(control, headerBorder, titleBlock)
        {
            Title = initialTitle
        };

        int tabIndex = _tabs.Count;
        headerBorder.MouseLeftButtonDown += (s, e) =>
        {
            SelectTab(_tabs.IndexOf(tab));
        };

        closeBtn.Click += (s, e) =>
        {
            e.Handled = true;
            CloseTab(_tabs.IndexOf(tab));
        };

        control.TitleChanged += posixPath =>
        {
            Dispatcher.Invoke(() =>
            {
                tab.Title = $"bash {tabNum} ({posixPath})";
                tab.HeaderTextBlock.Text = tab.Title;
            });
        };

        _tabs.Add(tab);
        TabHeaderPanel.Children.Add(headerBorder);

        SelectTab(_tabs.Count - 1);
    }

    private void SelectTab(int index)
    {
        if (index < 0 || index >= _tabs.Count)
            return;

        _activeTabIndex = index;

        // Update header highlights
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool isActive = i == index;
            _tabs[i].HeaderElement.Background = isActive ? _currentTheme.TabActiveBackgroundBrush : _currentTheme.TabInactiveBackgroundBrush;
            _tabs[i].HeaderTextBlock.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
            _tabs[i].HeaderTextBlock.Foreground = isActive ? _currentTheme.ForegroundBrush : new SolidColorBrush(Color.FromRgb(140, 140, 140));
        }

        // Show active control
        TerminalContainer.Children.Clear();
        TerminalContainer.Children.Add(_tabs[index].Control);
    }

    private void CloseTab(int index)
    {
        if (index < 0 || index >= _tabs.Count)
            return;

        TabHeaderPanel.Children.Remove(_tabs[index].HeaderElement);
        _tabs.RemoveAt(index);

        if (_tabs.Count == 0)
        {
            Close();
            return;
        }

        int newIndex = Math.Clamp(index, 0, _tabs.Count - 1);
        SelectTab(newIndex);
    }

    private void BtnNewTab_Click(object sender, RoutedEventArgs e)
    {
        AddNewTab();
    }

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
    {
        _currentFontSize = Math.Min(28, _currentFontSize + 1);
        foreach (var tab in _tabs)
        {
            tab.Control.SetFontSize(_currentFontSize);
        }
    }

    private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
    {
        _currentFontSize = Math.Max(10, _currentFontSize - 1);
        foreach (var tab in _tabs)
        {
            tab.Control.SetFontSize(_currentFontSize);
        }
    }

    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeSelector.SelectedItem is TerminalTheme theme)
        {
            _currentTheme = theme;
            TitleBarBorder.Background = theme.HeaderBackgroundBrush;
            Background = theme.BackgroundBrush;

            foreach (var tab in _tabs)
            {
                tab.Control.ApplyTheme(theme);
            }

            if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
            {
                SelectTab(_activeTabIndex);
            }
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+T: New Tab
        if (e.Key == Key.T && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            AddNewTab();
            return;
        }

        // Ctrl+W: Close Tab
        if (e.Key == Key.W && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            if (_activeTabIndex >= 0)
                CloseTab(_activeTabIndex);
            return;
        }

        // Ctrl+Tab: Switch Tab
        if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            if (_tabs.Count > 1)
            {
                int nextIndex = (_activeTabIndex + 1) % _tabs.Count;
                SelectTab(nextIndex);
            }
            return;
        }
    }
}