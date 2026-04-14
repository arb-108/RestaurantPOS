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

    /// <summary>System printer name from DB config (if set, skip print dialog).</summary>
    public string? ConfiguredPrinterName { get; set; }

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
        var d = _receiptData;

        // ═══ RESTAURANT HEADER (centered, like pharmacy receipt) ═══
        AddSpacer(p, 4);
        p.Children.Add(new TextBlock
        {
            Text = d.RestaurantName.ToUpper(),
            FontSize = 16, FontWeight = FontWeights.ExtraBold,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        });
        if (!string.IsNullOrWhiteSpace(d.RestaurantAddress))
        {
            p.Children.Add(new TextBlock
            {
                Text = d.RestaurantAddress,
                FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });
        }
        if (!string.IsNullOrWhiteSpace(d.RestaurantPhone))
        {
            p.Children.Add(new TextBlock
            {
                Text = d.RestaurantPhone,
                FontSize = 8,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });
        }

        if (!string.IsNullOrWhiteSpace(d.HeaderMessage))
        {
            AddSpacer(p, 2);
            AddCenteredText(p, d.HeaderMessage, 8, FontWeights.SemiBold, "#333");
        }

        AddSpacer(p, 6);

        // ═══ ORDER INFO BLOCK ═══
        AddInfoRow(p, "Order #", d.OrderNumber, true);
        AddInfoRow(p, "Cashier", d.CashierName ?? "Admin");
        if (!string.IsNullOrEmpty(d.WaiterName))
            AddInfoRow(p, "Waiter", d.WaiterName);

        // Date / Time / Page on one row (3-column)
        var dtGrid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        dtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var dateCol = new TextBlock { Text = $"Date: {d.DateTime:dd/MM/yyyy}", FontSize = 8, Foreground = Brushes.Black };
        var timeCol = new TextBlock { Text = $"Time: {d.DateTime:hh:mm:ss tt}", FontSize = 8, Foreground = Brushes.Black };
        var pageCol = new TextBlock { Text = "Page 1 of 1", FontSize = 8, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(dateCol, 0); Grid.SetColumn(timeCol, 1); Grid.SetColumn(pageCol, 2);
        dtGrid.Children.Add(dateCol); dtGrid.Children.Add(timeCol); dtGrid.Children.Add(pageCol);
        p.Children.Add(dtGrid);

        // Order Type + Payment method row
        AddTwoColumnRow(p, $"Type : {d.OrderType}", d.PaymentMethod, 9, FontWeights.Normal);

        // Table info
        if (!string.IsNullOrEmpty(d.TableName))
            AddInfoRow(p, "Table", d.TableName);

        // ═══ CUSTOMER / DELIVERY INFO (shown when any customer data exists) ═══
        bool hasCustomerInfo = !string.IsNullOrEmpty(d.CustomerName) || !string.IsNullOrEmpty(d.CustomerPhone)
                            || !string.IsNullOrEmpty(d.CustomerAddress) || !string.IsNullOrEmpty(d.DriverName)
                            || !string.IsNullOrEmpty(d.DeliveryNote);
        if (hasCustomerInfo)
        {
            AddDashLine(p);
            var sectionTitle = d.OrderType == "Delivery" ? "DELIVERY INFO"
                             : d.OrderType == "TakeAway" ? "TAKEAWAY INFO"
                             : "CUSTOMER INFO";
            AddCenteredText(p, sectionTitle, 9, FontWeights.Bold, "#000");
            if (!string.IsNullOrEmpty(d.CustomerName))
                AddInfoRow(p, "Customer", d.CustomerName);
            if (!string.IsNullOrEmpty(d.CustomerPhone))
                AddInfoRow(p, "Phone", d.CustomerPhone);
            if (!string.IsNullOrEmpty(d.CustomerAddress))
                AddInfoRow(p, "Address", d.CustomerAddress);
            if (!string.IsNullOrEmpty(d.DriverName))
                AddInfoRow(p, "Driver", $"{d.DriverName}" + (!string.IsNullOrEmpty(d.DriverPhone) ? $" ({d.DriverPhone})" : ""));
            if (!string.IsNullOrEmpty(d.DeliveryNote))
                AddInfoRow(p, "Remarks", d.DeliveryNote);
        }

        AddSpacer(p, 2);
        AddDashLine(p);

        // ═══ ITEMS COLUMN HEADER ═══
        AddItemHeaderRow(p);
        AddDashLine(p);

        // ═══ ITEMS LIST ═══
        int mainItemCount = 0;
        foreach (var item in d.Items)
        {
            if (item.Quantity == 0 && item.UnitPrice == 0)
            {
                // Deal sub-item — indented
                p.Children.Add(new TextBlock
                {
                    Text = $"  {item.Name.TrimStart()}",
                    FontSize = 8,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444")!),
                    Margin = new Thickness(8, 0, 0, 1)
                });
                continue;
            }
            AddItemRow(p, item);
            mainItemCount++;
            if (!string.IsNullOrWhiteSpace(item.Notes))
            {
                p.Children.Add(new TextBlock
                {
                    Text = $"  * {item.Notes}",
                    FontSize = 8,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666")!),
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(8, 0, 0, 2)
                });
            }
        }

        AddDashLine(p);

        // ═══ TOTALS SECTION (pharmacy-style: Item(s) count + Gross/Disc/Tax) ═══
        // Item count + Gross on same area
        var summaryGrid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        var itemCountBlock = new TextBlock { FontSize = 9, FontWeight = FontWeights.SemiBold };
        itemCountBlock.Inlines.Add(new Run("Item(s)  ") { FontWeight = FontWeights.Normal });
        itemCountBlock.Inlines.Add(new Run(mainItemCount.ToString()) { FontWeight = FontWeights.Bold });
        Grid.SetColumn(itemCountBlock, 0);

        var grossLabel = new TextBlock { Text = "Gross :", FontSize = 9, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(grossLabel, 1);

        var grossValue = new TextBlock { Text = Fmt(d.SubTotal), FontSize = 9, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(grossValue, 2);

        summaryGrid.Children.Add(itemCountBlock);
        summaryGrid.Children.Add(grossLabel);
        summaryGrid.Children.Add(grossValue);
        p.Children.Add(summaryGrid);

        // Discount
        if (d.DiscountAmount > 0)
            AddSummaryRow(p, "Disc. :", $"-{Fmt(d.DiscountAmount)}");

        // Tax / GST
        if (d.TaxAmount > 0)
            AddSummaryRow(p, "Tax (GST) :", Fmt(d.TaxAmount));

        // Service Charge
        if (d.ServiceCharge > 0)
            AddSummaryRow(p, "Service :", Fmt(d.ServiceCharge));

        AddDashLine(p);

        // ═══ NET AMOUNT (prominent, like pharmacy receipt) ═══
        var netGrid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        netGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        netGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        netGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        var cashierBottom = new TextBlock { Text = d.CashierName ?? "Admin", FontSize = 9, FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(cashierBottom, 0);

        var netLabel = new TextBlock { Text = "Net Amount :", FontSize = 10, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(netLabel, 1);

        var netValue = new TextBlock
        {
            Text = Fmt(d.GrandTotal), FontSize = 13, FontWeight = FontWeights.ExtraBold,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            TextDecorations = TextDecorations.Underline
        };
        Grid.SetColumn(netValue, 2);

        netGrid.Children.Add(cashierBottom);
        netGrid.Children.Add(netLabel);
        netGrid.Children.Add(netValue);
        p.Children.Add(netGrid);

        AddDashLine(p);

        // ═══ PAYMENT INFO ═══
        AddTwoColumnRow(p, $"Payment : {d.PaymentMethod}", Fmt(d.TenderedAmount), 9, FontWeights.Normal);
        if (d.ChangeAmount > 0)
            AddTwoColumnRow(p, "Change :", Fmt(d.ChangeAmount), 9, FontWeights.Normal);

        AddDashLine(p);
        AddSpacer(p, 4);

        // ═══ FOOTER ═══
        if (d.OrderType == "Delivery")
            AddCenteredText(p, "Thank you! Enjoy your meal", 8, FontWeights.SemiBold, "#333");
        else
            AddCenteredText(p, "Thank you for dining with us!", 8, FontWeights.SemiBold, "#333");

        AddCenteredText(p, $"*** {d.RestaurantName.ToUpper()} ***", 8, FontWeights.Bold, "#000");

        if (!string.IsNullOrWhiteSpace(d.FooterMessage))
            AddCenteredText(p, d.FooterMessage, 7, FontWeights.Normal, "#666");

        AddCenteredText(p, d.DateTime.ToString("dd/MM/yyyy hh:mm:ss tt"), 7, FontWeights.Normal, "#999");
        AddSpacer(p, 4);

        // Barcode-style decoration
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

    /// <summary>Label : Value info row (like "Order # 49179")</summary>
    private static void AddInfoRow(StackPanel p, string label, string value, bool bold = false)
    {
        var tb = new TextBlock { FontSize = 9, Margin = new Thickness(0, 1, 0, 1) };
        tb.Inlines.Add(new Run($"{label} : ") { FontWeight = FontWeights.Normal });
        tb.Inlines.Add(new Run(value) { FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold, FontSize = bold ? 11 : 9 });
        p.Children.Add(tb);
    }

    /// <summary>Right-aligned summary row (label + value in last 2 columns)</summary>
    private static void AddSummaryRow(StackPanel p, string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        var lbl = new TextBlock { Text = label, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(lbl, 1);
        var val = new TextBlock { Text = value, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(val, 2);

        grid.Children.Add(lbl);
        grid.Children.Add(val);
        p.Children.Add(grid);
    }

    /// <summary>Format paisa to decimal string without currency symbol.</summary>
    private static string Fmt(long paisa) => $"{paisa / 100m:N2}";

    // ══════════════════════════════════════════════
    //  KITCHEN ORDER TICKET (Page 2 or standalone)
    // ══════════════════════════════════════════════

    private void BuildKitchenSlipContent(ReceiptData data)
    {
        var p = _activePanel;
        p.Children.Clear();

        // ═══ HEADER (centered, same clean style as bill) ═══
        AddSpacer(p, 4);
        p.Children.Add(new TextBlock
        {
            Text = "KITCHEN ORDER",
            FontSize = 16, FontWeight = FontWeights.ExtraBold,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        });

        // ═══ REPRINT BANNER ═══
        if (!string.IsNullOrEmpty(data.HeaderMessage))
        {
            AddSpacer(p, 2);
            AddCenteredText(p, data.HeaderMessage, 10, FontWeights.Bold, "#000");
        }

        AddSpacer(p, 6);

        // ═══ ORDER NUMBER — large bold for kitchen readability ═══
        AddDashLine(p);
        p.Children.Add(new TextBlock
        {
            Text = data.OrderNumber,
            FontSize = 22, FontWeight = FontWeights.ExtraBold,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4)
        });
        AddDashLine(p);

        // ═══ ORDER TYPE — highlighted ═══
        p.Children.Add(new TextBlock
        {
            Text = data.OrderType.ToUpper(),
            FontSize = 16, FontWeight = FontWeights.ExtraBold,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4)
        });
        if (!string.IsNullOrEmpty(data.TableName))
            AddInfoRow(p, "Table", data.TableName);
        if (!string.IsNullOrEmpty(data.CashierName))
            AddInfoRow(p, "Cashier", data.CashierName);

        // Date / Time row
        var dtGrid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        dtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var dateCol = new TextBlock { Text = $"Date: {data.DateTime:dd/MM/yyyy}", FontSize = 8, Foreground = Brushes.Black };
        var timeCol = new TextBlock { Text = $"Time: {data.DateTime:hh:mm:ss tt}", FontSize = 8, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(dateCol, 0); Grid.SetColumn(timeCol, 1);
        dtGrid.Children.Add(dateCol); dtGrid.Children.Add(timeCol);
        p.Children.Add(dtGrid);

        AddDashLine(p);

        // ═══ ITEMS HEADER ═══
        var headerGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var qtyH = new TextBlock { Text = "Qty", FontSize = 9, FontWeight = FontWeights.Bold };
        var itemH = new TextBlock { Text = "Item", FontSize = 9, FontWeight = FontWeights.Bold };
        Grid.SetColumn(qtyH, 0); Grid.SetColumn(itemH, 1);
        headerGrid.Children.Add(qtyH); headerGrid.Children.Add(itemH);
        p.Children.Add(headerGrid);
        AddDashLine(p);

        // ═══ ITEMS ═══
        int totalQty = 0;
        foreach (var item in data.Items)
        {
            if (item.IsDealSubItem)
            {
                // Deal sub-item — indented with dash
                var subGrid = new Grid { Margin = new Thickness(0, 0, 0, 1) };
                subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var subQty = new TextBlock { Text = item.Quantity.ToString(), FontSize = 9, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444")!) };
                var subName = new TextBlock { Text = $"  - {item.Name.TrimStart()}", FontSize = 9, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444")!), TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(subQty, 0); Grid.SetColumn(subName, 1);
                subGrid.Children.Add(subQty); subGrid.Children.Add(subName);
                p.Children.Add(subGrid);
            }
            else if (item.IsDealHeader)
            {
                // Deal header — bold with markers
                var itemGrid = new Grid { Margin = new Thickness(0, 4, 0, 1) };
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var qtyBlock = new TextBlock { Text = item.Quantity.ToString(), FontSize = 10, FontWeight = FontWeights.Bold };
                var nameBlock = new TextBlock { Text = $"[DEAL] {item.Name}", FontSize = 10, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(qtyBlock, 0); Grid.SetColumn(nameBlock, 1);
                itemGrid.Children.Add(qtyBlock); itemGrid.Children.Add(nameBlock);
                p.Children.Add(itemGrid);

                totalQty += item.Quantity;
            }
            else if (item.Quantity == 0 && item.UnitPrice == 0)
            {
                // Customer receipt deal sub-item (legacy)
                p.Children.Add(new TextBlock
                {
                    Text = $"       {item.Name.TrimStart()}",
                    FontSize = 8, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444")!),
                    Margin = new Thickness(0, 0, 0, 1)
                });
            }
            else
            {
                // Regular item row
                var itemGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var qtyBlock = new TextBlock { Text = item.Quantity.ToString(), FontSize = 10, FontWeight = FontWeights.Bold };
                var nameBlock = new TextBlock { Text = item.Name, FontSize = 10, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(qtyBlock, 0); Grid.SetColumn(nameBlock, 1);
                itemGrid.Children.Add(qtyBlock); itemGrid.Children.Add(nameBlock);
                p.Children.Add(itemGrid);

                totalQty += item.Quantity;
            }

            // Notes
            if (!string.IsNullOrWhiteSpace(item.Notes))
            {
                p.Children.Add(new TextBlock
                {
                    Text = $"       >> {item.Notes}",
                    FontSize = 8, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555")!),
                    Margin = new Thickness(0, 0, 0, 1)
                });
            }
        }

        AddDashLine(p);

        // ═══ TOTAL ITEMS ═══
        AddTwoColumnRow(p, "Total Item(s)", totalQty.ToString(), 10, FontWeights.Bold);

        AddDashLine(p);

        // ═══ FOOTER ═══
        AddSpacer(p, 2);
        AddCenteredText(p, $"Printed: {data.DateTime:dd/MM/yyyy hh:mm:ss tt}", 8, FontWeights.Normal, "#999");
        AddSpacer(p, 6);
    }

    // Kitchen item rows are now built inline in BuildKitchenSlipContent

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

    // AddNetAmountRow no longer used — Net Amount is now inline in BuildCustomerBillContent

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

    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        // If a thermal printer is configured, use ESC/POS raw printing only
        if (!string.IsNullOrWhiteSpace(ConfiguredPrinterName))
        {
            try
            {
                if (_isKitchenSlip)
                {
                    await _printService.PrintKotAsync(BuildKotData(_receiptData), ConfiguredPrinterName);
                }
                else
                {
                    await _printService.PrintReceiptAsync(_receiptData, ConfiguredPrinterName);
                }

                // Combined mode: also print kitchen slip
                if (_isCombined && _kitchenData != null)
                {
                    await _printService.PrintKotAsync(BuildKotData(_kitchenData), ConfiguredPrinterName);
                }

                Close();
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Print failed: {ex.Message}",
                    "Print Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        // No thermal printer configured — visual print to default Windows printer
        try
        {
            PrintDirectToConfiguredPrinter();
        }
        catch
        {
            // Last resort: show print dialog
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true) return;

            printDialog.PrintVisual(ReceiptPanel,
                _isKitchenSlip ? "KFC - Kitchen Order" : "KFC - Customer Bill");

            if (_isCombined && KitchenPanel.Children.Count > 0)
            {
                printDialog.PrintVisual(KitchenPanel, "KFC - Kitchen Order");
            }
        }
    }

    /// <summary>
    /// Sends the receipt visual directly to the configured (or default) printer
    /// without showing any print dialog.
    /// </summary>
    private void PrintDirectToConfiguredPrinter()
    {
        PrintQueue? queue = null;

        if (!string.IsNullOrWhiteSpace(ConfiguredPrinterName))
        {
            try
            {
                var server = new LocalPrintServer();
                queue = server.GetPrintQueues()
                    .FirstOrDefault(q => q.Name == ConfiguredPrinterName
                                      || q.FullName == ConfiguredPrinterName);
            }
            catch { /* fall through to default */ }
        }

        // If we couldn't find the named printer, use default
        if (queue == null)
        {
            try
            {
                queue = LocalPrintServer.GetDefaultPrintQueue();
            }
            catch { }
        }

        if (queue == null)
            throw new InvalidOperationException("No printer found.");

        var printDialog = new PrintDialog { PrintQueue = queue };

        // Print without showing dialog
        printDialog.PrintVisual(ReceiptPanel,
            _isKitchenSlip ? "KFC - Kitchen Order" : "KFC - Customer Bill");

        if (_isCombined && KitchenPanel.Children.Count > 0)
        {
            printDialog.PrintVisual(KitchenPanel, "KFC - Kitchen Order");
        }
    }

    /// <summary>
    /// Converts ReceiptData into KotData for ESC/POS kitchen printing.
    /// </summary>
    private static RestaurantPOS.Printing.KOT.KotData BuildKotData(ReceiptData src)
    {
        return new RestaurantPOS.Printing.KOT.KotData
        {
            OrderNumber = src.OrderNumber,
            TableName = src.TableName,
            OrderType = src.OrderType,
            DateTime = src.DateTime,
            CashierName = src.CashierName,
            WaiterName = src.WaiterName,
            HeaderBanner = src.HeaderMessage,
            Items = src.Items.Select(i => new RestaurantPOS.Printing.KOT.KotItem
            {
                Name = i.Name.TrimStart(),
                Quantity = i.Quantity,
                Notes = i.Notes,
                IsSubItem = !i.IsDealHeader && !i.IsDealSubItem && i.Quantity == 0 && i.UnitPrice == 0,
                IsDealHeader = i.IsDealHeader,
                IsDealSubItem = i.IsDealSubItem
            }).ToList()
        };
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
