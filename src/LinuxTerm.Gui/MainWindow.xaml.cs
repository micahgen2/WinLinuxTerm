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
        public Grid ContainerGrid { get; }
        public TerminalControl PrimaryControl { get; }
        public TerminalControl? SecondaryControl { get; set; }
        public TerminalControl ActiveControl { get; set; }
        public Border HeaderElement { get; }
        public TextBlock HeaderTextBlock { get; }

        public TerminalTab(Grid containerGrid, TerminalControl primaryControl, Border headerElement, TextBlock headerTextBlock)
        {
            ContainerGrid = containerGrid;
            PrimaryControl = primaryControl;
            ActiveControl = primaryControl;
            HeaderElement = headerElement;
            HeaderTextBlock = headerTextBlock;
        }

        public IEnumerable<TerminalControl> AllControls()
        {
            yield return PrimaryControl;
            if (SecondaryControl != null)
                yield return SecondaryControl;
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

        // Populate Theme Selector with custom visual template
        ThemeSelector.ItemsSource = TerminalTheme.AllThemes;
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
        var containerGrid = new Grid();
        var primaryControl = new TerminalControl(initialDir);
        primaryControl.ApplyTheme(_currentTheme);
        primaryControl.SetFontSize(_currentFontSize);
        containerGrid.Children.Add(primaryControl);

        int tabNum = _tabCounter++;
        string initialTitle = $"bash {tabNum} (~)";

        // Create Tab Header
        var headerBorder = new Border
        {
            Background = _currentTheme.TabActiveBackgroundBrush,
            BorderBrush = _currentTheme.AccentBrush,
            BorderThickness = new Thickness(0, 2, 1, 0),
            Padding = new Thickness(12, 5, 8, 5),
            Margin = new Thickness(0),
            Cursor = Cursors.Hand,
            Height = 34
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

        var tab = new TerminalTab(containerGrid, primaryControl, headerBorder, titleBlock)
        {
            Title = initialTitle
        };

        headerBorder.MouseLeftButtonDown += (s, e) =>
        {
            SelectTab(_tabs.IndexOf(tab));
        };

        headerBorder.MouseEnter += (s, e) =>
        {
            int idx = _tabs.IndexOf(tab);
            if (idx != _activeTabIndex)
            {
                headerBorder.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            }
        };

        headerBorder.MouseLeave += (s, e) =>
        {
            int idx = _tabs.IndexOf(tab);
            if (idx != _activeTabIndex)
            {
                headerBorder.Background = _currentTheme.TabInactiveBackgroundBrush;
            }
        };

        closeBtn.Click += (s, e) =>
        {
            e.Handled = true;
            CloseTab(_tabs.IndexOf(tab));
        };

        void WireControlEvents(TerminalControl ctrl)
        {
            ctrl.GotFocus += (s, e) => tab.ActiveControl = ctrl;
            ctrl.TitleChanged += posixPath =>
            {
                Dispatcher.Invoke(() =>
                {
                    tab.Title = $"bash {tabNum} ({posixPath})";
                    tab.HeaderTextBlock.Text = tab.Title;
                });
            };
            ctrl.SplitVerticalRequested += () => SplitTab(tab, Orientation.Vertical);
            ctrl.SplitHorizontalRequested += () => SplitTab(tab, Orientation.Horizontal);
        }

        WireControlEvents(primaryControl);

        _tabs.Add(tab);
        TabHeaderPanel.Children.Add(headerBorder);

        SelectTab(_tabs.Count - 1);
    }

    private void SplitTab(TerminalTab tab, Orientation orientation)
    {
        if (tab.SecondaryControl != null)
            return; // Already split

        var secondary = new TerminalControl(tab.ActiveControl.Context.CurrentDirectory);
        secondary.ApplyTheme(_currentTheme);
        secondary.SetFontSize(_currentFontSize);
        tab.SecondaryControl = secondary;

        tab.ContainerGrid.Children.Clear();
        tab.ContainerGrid.ColumnDefinitions.Clear();
        tab.ContainerGrid.RowDefinitions.Clear();

        if (orientation == Orientation.Vertical)
        {
            // Side-by-side
            tab.ContainerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tab.ContainerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            tab.ContainerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetColumn(tab.PrimaryControl, 0);
            tab.ContainerGrid.Children.Add(tab.PrimaryControl);

            var splitter = new GridSplitter
            {
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48))
            };
            Grid.SetColumn(splitter, 1);
            tab.ContainerGrid.Children.Add(splitter);

            Grid.SetColumn(secondary, 2);
            tab.ContainerGrid.Children.Add(secondary);
        }
        else
        {
            // Stacked top/bottom
            tab.ContainerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            tab.ContainerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
            tab.ContainerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(tab.PrimaryControl, 0);
            tab.ContainerGrid.Children.Add(tab.PrimaryControl);

            var splitter = new GridSplitter
            {
                Height = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48))
            };
            Grid.SetRow(splitter, 1);
            tab.ContainerGrid.Children.Add(splitter);

            Grid.SetRow(secondary, 2);
            tab.ContainerGrid.Children.Add(secondary);
        }

        secondary.GotFocus += (s, e) => tab.ActiveControl = secondary;
        secondary.SplitVerticalRequested += () => SplitTab(tab, Orientation.Vertical);
        secondary.SplitHorizontalRequested += () => SplitTab(tab, Orientation.Horizontal);

        tab.ActiveControl = secondary;
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
            _tabs[i].HeaderElement.BorderThickness = isActive ? new Thickness(0, 2, 1, 0) : new Thickness(0, 0, 1, 0);
            _tabs[i].HeaderElement.BorderBrush = isActive ? _currentTheme.AccentBrush : new SolidColorBrush(Color.FromRgb(45, 45, 48));
            _tabs[i].HeaderTextBlock.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
            _tabs[i].HeaderTextBlock.Foreground = isActive ? _currentTheme.ForegroundBrush : new SolidColorBrush(Color.FromRgb(160, 160, 160));
        }

        // Show active control
        TerminalContainer.Children.Clear();
        TerminalContainer.Children.Add(_tabs[index].ContainerGrid);
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

    private void BtnNewTab_Click(object sender, RoutedEventArgs e) => AddNewTab();

    private void BtnSplitVertical_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
        {
            SplitTab(_tabs[_activeTabIndex], Orientation.Vertical);
        }
    }

    private void BtnSplitHorizontal_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
        {
            SplitTab(_tabs[_activeTabIndex], Orientation.Horizontal);
        }
    }

    private void BtnFind_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
        {
            _tabs[_activeTabIndex].ActiveControl.OpenSearchBar();
        }
    }

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
    {
        _currentFontSize = Math.Min(28, _currentFontSize + 1);
        foreach (var tab in _tabs)
        {
            foreach (var ctrl in tab.AllControls())
                ctrl.SetFontSize(_currentFontSize);
        }
    }

    private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
    {
        _currentFontSize = Math.Max(10, _currentFontSize - 1);
        foreach (var tab in _tabs)
        {
            foreach (var ctrl in tab.AllControls())
                ctrl.SetFontSize(_currentFontSize);
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
                foreach (var ctrl in tab.AllControls())
                    ctrl.ApplyTheme(theme);
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

        // Ctrl+Shift+E: Split Vertical
        if (e.Key == Key.E && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            BtnSplitVertical_Click(sender, e);
            return;
        }

        // Ctrl+Shift+O: Split Horizontal
        if (e.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            BtnSplitHorizontal_Click(sender, e);
            return;
        }

        // Ctrl+F: Search
        if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            BtnFind_Click(sender, e);
            return;
        }
    }
}