using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using DataTemplate = System.Windows.DataTemplate;
using FrameworkElementFactory = System.Windows.FrameworkElementFactory;
using TextBlock = System.Windows.Controls.TextBlock;
using WpfBinding = System.Windows.Data.Binding;
using WpfColor = System.Windows.Media.Color;
using WpfControl = System.Windows.Controls.Control;
using WpfDataGridColumnHeader = System.Windows.Controls.Primitives.DataGridColumnHeader;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace DrofusWatcher;

public class SelectedSpacesWindow : Window
{
    private readonly Action<RevitSpace>? _onSpaceSelected;
    private readonly Action<RevitSpace>? _onSpaceDoubleClick;
    private readonly Action<IReadOnlyList<RevitSpace>>? _onCheckCompliance;
    private readonly IReadOnlyList<RevitSpace> _spaces;
    private readonly string _roomKeyRevit;
    private readonly ICollectionView _spacesView;
    private readonly TextBlock _statusText;

    public SelectedSpacesWindow(
        IReadOnlyList<RevitSpace> spaces,
        string roomKeyRevit,
        Action<RevitSpace>? onSpaceSelected = null,
        Action<RevitSpace>? onSpaceDoubleClick = null,
        Action<IReadOnlyList<RevitSpace>>? onCheckCompliance = null)
    {
        _onSpaceSelected = onSpaceSelected;
        _onSpaceDoubleClick = onSpaceDoubleClick;
        _onCheckCompliance = onCheckCompliance;
        _spaces = spaces;
        _roomKeyRevit = roomKeyRevit;
        _spacesView = CollectionViewSource.GetDefaultView(_spaces);

        Title = $"Valgte spaces ({spaces.Count})";
        Width = 860;
        Height = 580;
        MinWidth = 720;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(WpfColor.FromRgb(244, 247, 250));

        var slate900 = new SolidColorBrush(WpfColor.FromRgb(34, 47, 62));
        var slate700 = new SolidColorBrush(WpfColor.FromRgb(78, 93, 110));
        var accent = new SolidColorBrush(WpfColor.FromRgb(0, 122, 204));
        var panel = new SolidColorBrush(WpfColor.FromRgb(255, 255, 255));
        var panelBorder = new SolidColorBrush(WpfColor.FromRgb(216, 224, 233));
        var sectionDivider = new SolidColorBrush(WpfColor.FromRgb(204, 214, 224));

        var shell = new WpfGrid
        {
            Margin = new Thickness(12)
        };
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new WpfGrid
        {
            Margin = new Thickness(0, 0, 0, 10)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel
        {
            Orientation = Orientation.Vertical
        };

        var titleText = new TextBlock
        {
            Text = "Valgte spaces",
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Foreground = slate900
        };

        var subtitleText = new TextBlock
        {
            Text = "Enkelt klikk velger i Revit. Dobbeltklikk zoomer inn på rommet.",
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 12,
            Foreground = slate700
        };

        titleStack.Children.Add(titleText);
        titleStack.Children.Add(subtitleText);
        header.Children.Add(titleStack);

        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var checkComplianceButton = new Button
        {
            Content = "Sjekk samsvar",
            MinWidth = 130,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            Background = Brushes.White,
            Foreground = accent,
            BorderBrush = accent,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 4, 14, 4)
        };
        checkComplianceButton.Click += (_, _) => RunComplianceCheck();
        actionsPanel.Children.Add(checkComplianceButton);

        var closeButton = new Button
        {
            Content = "Lukk",
            MinWidth = 90,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = true,
            IsCancel = true,
            Background = accent,
            Foreground = Brushes.White,
            BorderBrush = accent,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 4, 14, 4)
        };
        closeButton.Click += (_, _) => Close();
        actionsPanel.Children.Add(closeButton);

        WpfGrid.SetColumn(actionsPanel, 1);
        header.Children.Add(actionsPanel);

        WpfGrid.SetRow(header, 0);
        shell.Children.Add(header);

        var searchPanel = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = panelBorder,
            Background = panel,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var searchStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        searchStack.Children.Add(new TextBlock
        {
            Text = "Filter:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.SemiBold,
            Foreground = slate700
        });

        var searchBox = new WpfTextBox
        {
            MinWidth = 280,
            MaxWidth = 420,
            Height = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderBrush = panelBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 2, 8, 2),
            Text = string.Empty,
            ToolTip = $"Skriv Element ID, {roomKeyRevit}, eller Status"
        };
        searchBox.TextChanged += (_, _) => ApplyFilter(searchBox.Text);

        searchStack.Children.Add(searchBox);
        searchPanel.Child = searchStack;
        WpfGrid.SetRow(searchPanel, 1);
        shell.Children.Add(searchPanel);

        var gridContainer = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = panelBorder,
            Background = panel,
            Padding = new Thickness(8)
        };

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            GridLinesVisibility = DataGridGridLinesVisibility.None,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            BorderThickness = new Thickness(0),
            RowHeaderWidth = 0,
            AlternationCount = 2,
            ItemsSource = _spacesView
        };
        grid.SelectionChanged += (_, _) => OnGridSelectionChanged(grid);
        grid.MouseDoubleClick += (_, _) => OnGridDoubleClick(grid);

        grid.ColumnHeaderStyle = new Style(typeof(WpfDataGridColumnHeader))
        {
            Setters =
            {
                new Setter(WpfControl.FontWeightProperty, FontWeights.SemiBold),
                new Setter(WpfControl.ForegroundProperty, slate700),
                new Setter(WpfControl.BackgroundProperty, new SolidColorBrush(WpfColor.FromRgb(246, 249, 252))),
                new Setter(WpfControl.BorderBrushProperty, new SolidColorBrush(WpfColor.FromRgb(230, 236, 242))),
                new Setter(WpfControl.BorderThicknessProperty, new Thickness(0, 0, 0, 1)),
                new Setter(WpfControl.PaddingProperty, new Thickness(8, 6, 8, 6))
            }
        };

        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(WpfControl.ForegroundProperty, slate900));
        rowStyle.Setters.Add(new Setter(WpfControl.BackgroundProperty, Brushes.White));
        rowStyle.Setters.Add(new Setter(WpfControl.BorderThicknessProperty, new Thickness(0)));

        var altTrigger = new Trigger { Property = ItemsControl.AlternationIndexProperty, Value = 1 };
        altTrigger.Setters.Add(new Setter(WpfControl.BackgroundProperty, new SolidColorBrush(WpfColor.FromRgb(250, 252, 255))));
        rowStyle.Triggers.Add(altTrigger);

        var hoverTrigger = new Trigger { Property = DataGridRow.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(WpfControl.BackgroundProperty, new SolidColorBrush(WpfColor.FromRgb(234, 245, 255))));
        rowStyle.Triggers.Add(hoverTrigger);

        var selectedTrigger = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(WpfControl.BackgroundProperty, new SolidColorBrush(WpfColor.FromRgb(214, 236, 255))));
        selectedTrigger.Setters.Add(new Setter(WpfControl.ForegroundProperty, new SolidColorBrush(WpfColor.FromRgb(22, 40, 59))));
        rowStyle.Triggers.Add(selectedTrigger);

        grid.RowStyle = rowStyle;

        var actualColumnHeaderStyle = new Style(typeof(WpfDataGridColumnHeader), grid.ColumnHeaderStyle);
        actualColumnHeaderStyle.Setters.Add(new Setter(WpfControl.BorderBrushProperty, sectionDivider));
        actualColumnHeaderStyle.Setters.Add(new Setter(WpfControl.BorderThicknessProperty, new Thickness(2, 0, 0, 1)));

        var actualColumnCellStyle = new Style(typeof(DataGridCell));
        actualColumnCellStyle.Setters.Add(new Setter(WpfControl.BorderBrushProperty, sectionDivider));
        actualColumnCellStyle.Setters.Add(new Setter(WpfControl.BorderThicknessProperty, new Thickness(2, 0, 0, 0)));

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Romnavn",
            Binding = new WpfBinding(nameof(RevitSpace.DrofusRoomNameDisplay)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = roomKeyRevit,
            Binding = new WpfBinding(nameof(RevitSpace.RevitRoomKeyValue)),
            Width = new DataGridLength(120, DataGridLengthUnitType.Auto)
        });

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Romnr",
            Binding = new WpfBinding($"{nameof(RevitSpace.Rfp)}.{nameof(RfpData.ArchitectNo)}"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Auto)
        });

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Normal",
            Binding = new WpfBinding($"{nameof(RevitSpace.Rfp)}.{nameof(RfpData.NormalkraftUttak)}"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Nød",
            Binding = new WpfBinding($"{nameof(RevitSpace.Rfp)}.{nameof(RfpData.NodkraftUttak)}"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "UPS",
            Binding = new WpfBinding($"{nameof(RevitSpace.Rfp)}.{nameof(RfpData.UpsUttak)}"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "IKT",
            Binding = new WpfBinding($"{nameof(RevitSpace.Rfp)}.{nameof(RfpData.IktUttak)}"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        // Faktisk el uttak column with color coding
        var elOutletColumnTemplate = new DataTemplate();
        var elOutletFactory = new FrameworkElementFactory(typeof(TextBlock));
        elOutletFactory.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(RevitSpace.ActualElectricalUttakDisplay)));
        elOutletFactory.SetBinding(TextBlock.ForegroundProperty, new WpfBinding(nameof(RevitSpace.ElectricalOutletColorStatus))
        {
            Converter = new ColorStatusConverter()
        });
        elOutletColumnTemplate.VisualTree = elOutletFactory;

        var elOutletColumn = new DataGridTemplateColumn
        {
            Header = "Revit el-uttak",
            CellTemplate = elOutletColumnTemplate,
            HeaderStyle = actualColumnHeaderStyle,
            CellStyle = actualColumnCellStyle,
            Width = new DataGridLength(1, DataGridLengthUnitType.Auto)
        };
        grid.Columns.Add(elOutletColumn);

        // Faktisk data uttak column with color coding
        var dataOutletColumnTemplate = new DataTemplate();
        var dataOutletFactory = new FrameworkElementFactory(typeof(TextBlock));
        dataOutletFactory.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(RevitSpace.ActualDataUttakDisplay)));
        dataOutletFactory.SetBinding(TextBlock.ForegroundProperty, new WpfBinding(nameof(RevitSpace.DataOutletColorStatus))
        {
            Converter = new ColorStatusConverter()
        });
        dataOutletColumnTemplate.VisualTree = dataOutletFactory;

        var dataOutletColumn = new DataGridTemplateColumn
        {
            Header = "Revit IKT-uttak",
            CellTemplate = dataOutletColumnTemplate,
            Width = new DataGridLength(1, DataGridLengthUnitType.Auto)
        };
        grid.Columns.Add(dataOutletColumn);

        gridContainer.Child = grid;
        WpfGrid.SetRow(gridContainer, 2);
        shell.Children.Add(gridContainer);

        var statusBar = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = panelBorder,
            Background = panel,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 10, 0, 0)
        };

        var statusGrid = new WpfGrid();
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _statusText = new TextBlock
        {
            Foreground = slate700,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusGrid.Children.Add(_statusText);

        var hintText = new TextBlock
        {
            Text = "Tips: Dobbeltklikk på en rad for å zoome inn i Revit",
            Foreground = slate700,
            VerticalAlignment = VerticalAlignment.Center
        };
        WpfGrid.SetColumn(hintText, 1);
        statusGrid.Children.Add(hintText);

        statusBar.Child = statusGrid;
        WpfGrid.SetRow(statusBar, 3);
        shell.Children.Add(statusBar);

        Content = shell;

        UpdateStatus();
    }

    private void ApplyFilter(string text)
    {
        var filterText = text?.Trim();

        _spacesView.Filter = obj =>
        {
            if (obj is not RevitSpace space)
                return false;

            if (string.IsNullOrWhiteSpace(filterText))
                return true;

            return space.IdValue.ToString().Contains(filterText, StringComparison.OrdinalIgnoreCase)
                || (space.RevitRoomKeyValue?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false);
        };

        _spacesView.Refresh();
        UpdateStatus();
    }

    private void OnGridSelectionChanged(DataGrid grid)
    {
        if (grid.SelectedItem is RevitSpace space)
        {
            _onSpaceSelected?.Invoke(space);
        }

        UpdateStatus();
    }

    private void OnGridDoubleClick(DataGrid grid)
    {
        if (grid.SelectedItem is RevitSpace space)
            _onSpaceDoubleClick?.Invoke(space);
    }

    private void RunComplianceCheck()
    {
        _onCheckCompliance?.Invoke(_spaces);
        _spacesView.Refresh();
        UpdateStatus();
    }

    public bool? ShowModal(IntPtr ownerHandle)
    {
        new WindowInteropHelper(this)
        {
            Owner = ownerHandle
        };

        return ShowDialog();
    }

    private void UpdateStatus()
    {
        var selectedCount = _spacesView.Cast<object>().Count();
        _statusText.Text = $"Viser {selectedCount} av {_spaces.Count} rom";
    }
}

public class ColorStatusConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var status = value as string;

        return status switch
        {
            "Red" => new SolidColorBrush(WpfColor.FromRgb(220, 38, 38)),      // Red
            "Green" => new SolidColorBrush(WpfColor.FromRgb(34, 197, 94)),    // Green
            "Blue" => new SolidColorBrush(WpfColor.FromRgb(59, 130, 246)),    // Blue
            _ => new SolidColorBrush(WpfColor.FromRgb(156, 163, 175))         // Gray (default)
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}