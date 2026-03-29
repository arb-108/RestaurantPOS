using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;
using Microsoft.Win32;
using RestaurantPOS.Printing;
using RestaurantPOS.Printing.Receipt;

namespace RestaurantPOS.WPF.Views;

public partial class PrintPreviewWindow : Window
{
    private readonly ReceiptData _receiptData;
    private readonly ReceiptData? _kitchenData;
    private readonly IPrintService _printService;
    private readonly bool _isKitchenSlip;
    private readonly bool _isCombined;
    private int _totalPages = 1;
    private int _currentPage = 1;

    // ── Target panel pointer: switches between ReceiptPanel and KitchenPanel ──
    private StackPanel _activePanel = null!;

    /// <summary>
    /// Single receipt mode: customer bill OR kitchen slip.
    /// </summary>
    public PrintPreviewWindow(ReceiptData receiptData, IPrintService printService, bool isKitchenSlip = false)
    {
        InitializeComponent();
        _receiptData = receiptData;
        _printService = printService;
        _isKitchenSlip = isKitchenSlip;
        _isCombined = false;
        _totalPages = 1;

        if (_isKitchenSlip)
        {
            Title = "Kitchen Order Ticket - Print Preview";
            _activePanel = ReceiptPanel;
            BuildKitchenSlipContent(_receiptData);
        }
        else
        {
            Title = "Bill Print Preview";
            _activePanel = ReceiptPanel;
            BuildCustomerBillContent();
        }

        PageInfo.Text = "Page 1 of 1";
        SetupKeyboardNavigation();
    }

    /// <summary>
    /// Combined mode: customer bill (Page 1) + kitchen slip (Page 2) in ONE window.
    /// Two separate receipt "pages" stacked vertically, each prints separately.
    /// </summary>
    public PrintPreviewWindow(ReceiptData customerBill, ReceiptData kitchenSlip, IPrintService printService)
    {
        InitializeComponent();
        _receiptData = customerBill;
        _kitchenData = kitchenSlip;
        _printService = printService;
        _isKitchenSlip = false;
        _isCombined = true;
        _totalPages = 2;

        Title = "Bill + Kitchen Order - Print Preview";

        // Page 1: Customer bill
        _activePanel = ReceiptPanel;
        BuildCustomerBillContent();

        // Page 2: Kitchen slip (in separate panel)
        KitchenPageBorder.Visibility = Visibility.Visible;
        _activePanel = KitchenPanel;
        BuildKitchenSlipContent(_kitchenData);

        // Show page nav arrows
        PageNavPanel.Visibility = Visibility.Visible;
        PageInfo.Text = $"Page 1 of {_totalPages}";
        SetupKeyboardNavigation();
    }

    // ══════════════════════════════════════════════
    //  KEYBOARD NAVIGATION
    // ══════════════════════════════════════════════

    private void SetupKeyboardNavigation()
    {
        // Auto-focus Print button when window loads
        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                BtnPrint.Focus();
            });
        };

        // Enter = print + close, Escape = close
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Print_Click(this, new RoutedEventArgs());
                Close();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    // ══════════════════════════════════════════════
    //  PAGE NAVIGATION (scroll-based)
    // ══════════════════════════════════════════════

    private void PreviewScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_isCombined || _totalPages < 2) return;

        // Determine which page is visible based on scroll position
        // KitchenPageBorder is the second page
        var kitchenTop = KitchenPageBorder.TranslatePoint(new Point(0, 0), PagesContainer).Y;
        var scrollOffset = PreviewScroller.VerticalOffset;
        var viewportHeight = PreviewScroller.ViewportHeight;
        var midPoint = scrollOffset + (viewportHeight / 2);

        // Scale-adjusted kitchen top
        var scaledKitchenTop = kitchenTop * ZoomTransform.ScaleY;

        int newPage = midPoint >= scaledKitchenTop ? 2 : 1;
        if (newPage != _currentPage)
        {
            _currentPage = newPage;
            PageInfo.Text = $"Page {_currentPage} of {_totalPages}";
        }
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        // Scroll to top (Page 1)
        PreviewScroller.ScrollToTop();
        _currentPage = 1;
        PageInfo.Text = $"Page {_currentPage} of {_totalPages}";
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (!_isCombined) return;
        // Scroll to kitchen page
        KitchenPageBorder.BringIntoView();
        _currentPage = 2;
        PageInfo.Text = $"Page {_currentPage} of {_totalPages}";
    }

    // ══════════════════════════════════════════════
    //  CUSTOMER BILL RECEIPT (Page 1)
    // ══════════════════════════════════════════════

    private void BuildCustomerBillContent()
    {
        var p = _activePanel;
        p.Children.Clear();

        // ── Restaurant Header ──
        var headerBorder = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2")!),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 4)
        };
        var headerStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        headerStack.Children.Add(new TextBlock
        {
            Text = "KFC RESTAURANT",
            FontSize = 16, FontWeight = FontWeights.ExtraBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D32F2F")!),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = "Stadium Road, Daska | 0300-1234567",
            FontSize = 8, FontWeight = FontWeights.Normal,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555")!),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        headerBorder.Child = headerStack;
        p.Children.Add(headerBorder);
        AddSpacer(p, 2);
        AddDoubleLine(p);
        AddSpacer(p, 2);

        // ── Order Info (compact) ──
        AddTwoColumnRow(p, $"Order: {_receiptData.OrderNumber}", _receiptData.DateTime.ToString("dd/MM/yy HH:mm"), 9, FontWeights.SemiBold);
        AddTwoColumnRow(p, $"Type: {_receiptData.OrderType}", !string.IsNullOrEmpty(_receiptData.TableName) ? _receiptData.TableName : "", 9, FontWeights.Normal);
        if (!string.IsNullOrEmpty(_receiptData.CashierName))
            AddTwoColumnRow(p, $"Cashier: {_receiptData.CashierName}", !string.IsNullOrEmpty(_receiptData.WaiterName) ? $"Waiter: {_receiptData.WaiterName}" : "", 9, FontWeights.Normal);

        // ── Delivery Details Section (compact) ──
        if (_receiptData.OrderType == "Delivery")
        {
            AddDashLine(p);
            AddCenteredText(p, "DELIVERY INFO", 9, FontWeights.Bold, "#1565C0");
            if (!string.IsNullOrEmpty(_receiptData.CustomerName))
                AddTwoColumnRow(p, _receiptData.CustomerName, _receiptData.CustomerPhone ?? "", 9, FontWeights.SemiBold);
            if (!string.IsNullOrEmpty(_receiptData.CustomerAddress))
                AddTwoColumnRow(p, "Addr:", _receiptData.CustomerAddress, 9, FontWeights.Normal);
            if (!string.IsNullOrEmpty(_receiptData.DriverName))
                AddTwoColumnRow(p, "Driver:", $"{_receiptData.DriverName} ({_receiptData.DriverPhone})", 9, FontWeights.Normal);
            if (!string.IsNullOrEmpty(_receiptData.DeliveryNote))
                AddTwoColumnRow(p, "Note:", _receiptData.DeliveryNote, 9, FontWeights.Normal, "#E65100");
        }

        AddSpacer(p, 2);
        AddDashLine(p);

        // ── Item Header ──
        AddItemHeaderRow(p);
        AddDashLine(p);

        // ── Items ──
        foreach (var item in _receiptData.Items)
        {
            if (item.Quantity == 0 && item.UnitPrice == 0)
            {
                // Deal sub-item — just show indented name
                p.Children.Add(new TextBlock
                {
                    Text = item.Name,
                    FontSize = 8,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555")!),
                    Margin = new Thickness(10, 0, 0, 1)
                });
                continue;
            }
            AddItemRow(p, item);
            if (!string.IsNullOrWhiteSpace(item.Notes))
            {
                p.Children.Add(new TextBlock
                {
                    Text = $"  * {item.Notes}",
                    FontSize = 8,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888")!),
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 0, 0, 2)
                });
            }
        }

        AddDashLine(p);

        // ── Summary (compact) ──
        AddTwoColumnRow(p, $"Items: {_receiptData.Items.Count}  Gross:", FormatCurrency(_receiptData.SubTotal), 9, FontWeights.SemiBold);
        if (_receiptData.DiscountAmount > 0)
            AddTwoColumnRow(p, "Disc.:", $"-{FormatCurrency(_receiptData.DiscountAmount)}", 9, FontWeights.Normal, "#E53935");
        if (_receiptData.TaxAmount > 0)
            AddTwoColumnRow(p, "GST:", FormatCurrency(_receiptData.TaxAmount), 9, FontWeights.Normal);
        if (_receiptData.ServiceCharge > 0)
            AddTwoColumnRow(p, "Service:", FormatCurrency(_receiptData.ServiceCharge), 9, FontWeights.Normal);

        AddSpacer(p, 2);
        AddDoubleLine(p);
        AddSpacer(p, 2);

        // ── Net Amount ──
        AddNetAmountRow(p, _receiptData.GrandTotal);

        AddSpacer(p, 2);
        AddDoubleLine(p);
        AddSpacer(p, 2);

        // ── Payment Info (compact) ──
        AddTwoColumnRow(p, $"Pay: {_receiptData.PaymentMethod}", FormatCurrency(_receiptData.TenderedAmount), 9, FontWeights.SemiBold);
        if (_receiptData.ChangeAmount > 0)
            AddTwoColumnRow(p, "Change:", FormatCurrency(_receiptData.ChangeAmount), 9, FontWeights.Normal, "#2E7D32");

        AddSpacer(p, 3);
        AddDashLine(p);
        AddSpacer(p, 3);

        // ── Footer (compact) ──
        if (_receiptData.OrderType == "Delivery")
            AddCenteredText(p, "Thank you! Enjoy your meal - KFC Delivery", 8, FontWeights.SemiBold, "#333");
        else
            AddCenteredText(p, "Thank you for dining with us!", 8, FontWeights.SemiBold, "#333");

        AddCenteredText(p, "*** KFC RESTAURANT ***", 8, FontWeights.Bold, "#D32F2F");

        if (!string.IsNullOrWhiteSpace(_receiptData.FooterMessage))
            AddCenteredText(p, _receiptData.FooterMessage, 7, FontWeights.Normal, "#888");

        AddCenteredText(p, _receiptData.DateTime.ToString("dd/MM/yy HH:mm"), 7, FontWeights.Normal, "#999");
        AddSpacer(p, 4);

        p.Children.Add(new TextBlock
        {
            Text = "||||| |||| ||||| |||| ||||| ||||",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 9,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 4)
        });
    }

    // ══════════════════════════════════════════════
    //  KITCHEN ORDER TICKET (Page 2 or standalone)
    // ══════════════════════════════════════════════

    private void BuildKitchenSlipContent(ReceiptData data)
    {
        var p = _activePanel;
        p.Children.Clear();

        // ── Reprint banner ──
        if (!string.IsNullOrEmpty(data.HeaderMessage))
        {
            AddSpacer(p, 4);
            AddCenteredBoldBanner(p, data.HeaderMessage, 14, "#D32F2F");
            AddSpacer(p, 6);
        }

        // ── Kitchen Header ──
        AddSpacer(p, 4);
        AddCenteredText(p, "*** KITCHEN ***", 12, FontWeights.Bold, "#333");
        AddSpacer(p, 8);

        // ── Large Order Number ──
        AddCenteredText(p, data.OrderNumber, 26, FontWeights.ExtraBold, "#000");
        AddSpacer(p, 4);

        // ── Table Name ──
        if (!string.IsNullOrEmpty(data.TableName))
        {
            AddCenteredText(p, data.TableName, 22, FontWeights.Bold, "#000");
            AddSpacer(p, 4);
        }

        // ── Date/Time and Cashier ──
        AddLeftText(p, $"{data.DateTime:dd/MM/yyyy}  {data.DateTime:HH:mm}", 10, "#555");
        if (!string.IsNullOrEmpty(data.CashierName))
            AddLeftText(p, $"{data.CashierName}, POS 1", 10, "#555");

        AddSpacer(p, 4);
        AddDottedLine(p);
        AddSpacer(p, 4);

        // ── Order Type ──
        AddCenteredText(p, data.OrderType, 14, FontWeights.Bold, "#000");

        AddSpacer(p, 4);
        AddDottedLine(p);
        AddSpacer(p, 6);

        // ── Items ──
        foreach (var item in data.Items)
        {
            AddKitchenItemRow(p, item);
        }

        AddSpacer(p, 4);
        AddDottedLine(p);
        AddSpacer(p, 6);

        // ── Total items ──
        // Count only main items (not deal sub-items)
        var totalItems = data.Items.Where(i => !i.Name.StartsWith("      ")).Sum(i => i.Quantity);
        AddCenteredText(p, $"Total Items: {totalItems}", 11, FontWeights.SemiBold, "#333");
        AddSpacer(p, 4);

        // ── Timestamp ──
        AddCenteredText(p, $"Printed: {data.DateTime:dd/MM/yyyy HH:mm:ss}", 8, FontWeights.Normal, "#999");
        AddSpacer(p, 10);
    }

    // ══════════════════════════════════════════════
    //  HELPER: Kitchen item row
    // ══════════════════════════════════════════════

    private static void AddKitchenItemRow(StackPanel p, ReceiptItem item)
    {
        var itemPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 2) };

        // Deal sub-item (indented component) — smaller font, no bold
        bool isSubItem = item.Name.StartsWith("      ");
        // Deal header line
        bool isDealHeader = item.Name.Contains("──");

        var mainLine = new TextBlock
        {
            FontSize = isSubItem ? 12 : 14,
            FontWeight = isSubItem ? FontWeights.SemiBold : FontWeights.Bold,
            Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap,
            Margin = isSubItem ? new Thickness(16, 0, 0, 0) : new Thickness(0)
        };
        mainLine.Inlines.Add(new Run($"{item.Quantity} x ")
        {
            FontWeight = FontWeights.ExtraBold,
            FontSize = isSubItem ? 12 : 14
        });
        mainLine.Inlines.Add(new Run(isSubItem ? item.Name.TrimStart() : item.Name)
        {
            FontWeight = isDealHeader ? FontWeights.ExtraBold : (isSubItem ? FontWeights.SemiBold : FontWeights.Bold),
            FontSize = isSubItem ? 12 : 14
        });
        itemPanel.Children.Add(mainLine);

        if (!string.IsNullOrWhiteSpace(item.Notes))
        {
            itemPanel.Children.Add(new TextBlock
            {
                Text = $"    {item.Notes}",
                FontSize = 11,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555")!),
                FontWeight = FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 0, 0, 0)
            });
        }

        p.Children.Add(itemPanel);
    }

    private static void AddCenteredBoldBanner(StackPanel p, string text, double fontSize, string colorHex)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)!),
            BorderThickness = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        border.Child = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = FontWeights.ExtraBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)!),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        p.Children.Add(border);
    }

    private static void AddLeftText(StackPanel p, string text, double fontSize, string colorHex)
    {
        p.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)!),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 1, 0, 1)
        });
    }

    private static void AddDottedLine(StackPanel p)
    {
        p.Children.Add(new TextBlock
        {
            Text = new string('.', 60),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 8,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#999")!),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2)
        });
    }

    // ══════════════════════════════════════════════
    //  SHARED HELPER METHODS (panel-targeted)
    // ══════════════════════════════════════════════

    private static void AddNetAmountRow(StackPanel p, long grandTotal)
    {
        var netBorder = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF8F8")!),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 2, 0, 2)
        };
        var netPanel = new Grid();
        netPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        netPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var netLabel = new TextBlock
        {
            Text = "Net Amount :",
            FontSize = 13, FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(netLabel, 0);

        var netValue = new TextBlock
        {
            Text = $"Rs. {grandTotal / 100m:N2}",
            FontSize = 15, FontWeight = FontWeights.ExtraBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D32F2F")!),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(netValue, 1);

        netPanel.Children.Add(netLabel);
        netPanel.Children.Add(netValue);
        netBorder.Child = netPanel;
        p.Children.Add(netBorder);
    }

    private static void AddCenteredText(StackPanel p, string text, double fontSize, FontWeight weight, string colorHex)
    {
        p.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)!),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 1, 0, 1)
        });
    }

    private static void AddTwoColumnRow(StackPanel p, string label, string value, double fontSize, FontWeight weight, string? valueColor = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lblBlock = new TextBlock { Text = label, FontSize = fontSize, FontWeight = weight, Foreground = Brushes.Black };
        Grid.SetColumn(lblBlock, 0);

        var valBlock = new TextBlock
        {
            Text = value,
            FontSize = fontSize,
            FontWeight = weight,
            Foreground = valueColor != null
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(valueColor)!)
                : Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(valBlock, 1);

        grid.Children.Add(lblBlock);
        grid.Children.Add(valBlock);
        p.Children.Add(grid);
    }

    private static void AddItemHeaderRow(StackPanel p)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        AddHeaderCell(grid, "Products Detail", 0);
        AddHeaderCell(grid, "Qty", 1, HorizontalAlignment.Center);
        AddHeaderCell(grid, "Rate", 2, HorizontalAlignment.Right);
        AddHeaderCell(grid, "Amount", 3, HorizontalAlignment.Right);

        p.Children.Add(grid);
    }

    private static void AddHeaderCell(Grid grid, string text, int col, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = 9, FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black, HorizontalAlignment = align
        };
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    private static void AddItemRow(StackPanel p, ReceiptItem item)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        var nameBlock = new TextBlock { Text = item.Name, FontSize = 9, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(nameBlock, 0);

        var qtyBlock = new TextBlock { Text = item.Quantity.ToString(), FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(qtyBlock, 1);

        var rateBlock = new TextBlock { Text = (item.UnitPrice / 100m).ToString("N2"), FontSize = 9, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(rateBlock, 2);

        var totalBlock = new TextBlock { Text = (item.LineTotal / 100m).ToString("N2"), FontSize = 9, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(totalBlock, 3);

        grid.Children.Add(nameBlock);
        grid.Children.Add(qtyBlock);
        grid.Children.Add(rateBlock);
        grid.Children.Add(totalBlock);
        p.Children.Add(grid);
    }

    private static void AddDashLine(StackPanel p)
    {
        p.Children.Add(new TextBlock
        {
            Text = new string('-', 48),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 8,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#999")!),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2)
        });
    }

    private static void AddDoubleLine(StackPanel p)
    {
        p.Children.Add(new TextBlock
        {
            Text = new string('=', 48),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 8,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666")!),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2)
        });
    }

    private static void AddSpacer(StackPanel p, double height)
    {
        p.Children.Add(new Border { Height = height });
    }

    private static string FormatCurrency(long paisa)
    {
        return $"{paisa / 100m:N2}";
    }

    // ══════════════════════════════════════════════
    //  EVENT HANDLERS
    // ══════════════════════════════════════════════

    private void Zoom_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tagStr && double.TryParse(tagStr,
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var scale))
        {
            ZoomTransform.ScaleX = scale;
            ZoomTransform.ScaleY = scale;
        }
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() != true) return;

        // Page 1: Always print the customer bill (or kitchen slip in standalone mode)
        printDialog.PrintVisual(ReceiptPanel,
            _isKitchenSlip ? "KFC - Kitchen Order" : "KFC - Customer Bill");

        // Page 2: If combined mode, print kitchen slip as a SEPARATE print job
        if (_isCombined && KitchenPanel.Children.Count > 0)
        {
            printDialog.PrintVisual(KitchenPanel, "KFC - Kitchen Order");
        }
    }

    private void Pdf_Click(object sender, RoutedEventArgs e)
    {
        var prefix = _isCombined ? "Bill_KOT" : _isKitchenSlip ? "KOT" : "Bill";
        var saveDialog = new SaveFileDialog
        {
            Filter = "XPS Document|*.xps|All Files|*.*",
            DefaultExt = ".xps",
            FileName = $"{prefix}_{_receiptData.OrderNumber}_{DateTime.Now:yyyyMMdd_HHmmss}"
        };

        if (saveDialog.ShowDialog() == true)
        {
            try
            {
                using var package = System.IO.Packaging.Package.Open(
                    saveDialog.FileName, FileMode.Create);
                using var xpsDoc = new XpsDocument(package);
                var writer = XpsDocument.CreateXpsDocumentWriter(xpsDoc);
                // Export the entire pages container
                writer.Write(PagesContainer);

                MessageBox.Show($"Document saved successfully!\n{saveDialog.FileName}",
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export: {ex.Message}",
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
