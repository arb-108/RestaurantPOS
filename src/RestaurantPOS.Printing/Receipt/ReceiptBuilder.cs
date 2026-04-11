using System.Text;

namespace RestaurantPOS.Printing.Receipt;

public class ReceiptData
{
    public string RestaurantName { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime DateTime { get; set; }
    public string? TableName { get; set; }
    public string OrderType { get; set; } = string.Empty;
    public string? CashierName { get; set; }
    public string? WaiterName { get; set; }
    public List<ReceiptItem> Items { get; set; } = [];
    public long SubTotal { get; set; }
    public long TaxAmount { get; set; }
    public long DiscountAmount { get; set; }
    public long ServiceCharge { get; set; }
    public long GrandTotal { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public long TenderedAmount { get; set; }
    public long ChangeAmount { get; set; }
    public string? HeaderMessage { get; set; }
    public string? FooterMessage { get; set; }
    public string? RestaurantAddress { get; set; }
    public string? RestaurantPhone { get; set; }

    // ── Delivery-specific fields ──
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerAddress { get; set; }
    public string? DeliveryNote { get; set; }
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }
}

public class ReceiptItem
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public long UnitPrice { get; set; }
    public long LineTotal { get; set; }
    public string? Notes { get; set; }
    public bool IsDealHeader { get; set; }
    public bool IsDealSubItem { get; set; }
}

public class ReceiptBuilder
{
    private readonly int _width;

    public ReceiptBuilder(int paperWidth = 80)
    {
        _width = paperWidth == 58 ? 32 : 48;
    }

    public byte[] Build(ReceiptData d)
    {
        var ms = new MemoryStream();

        Write(ms, EscPos.Init);

        // ═══ RESTAURANT HEADER (centered, bold) ═══
        Write(ms, EscPos.AlignCenter);
        Write(ms, EscPos.DoubleOn);
        WriteText(ms, d.RestaurantName.ToUpper());
        Write(ms, EscPos.NormalSize);

        if (!string.IsNullOrWhiteSpace(d.RestaurantAddress))
        {
            Write(ms, EscPos.BoldOn);
            WriteText(ms, d.RestaurantAddress);
            Write(ms, EscPos.BoldOff);
        }
        if (!string.IsNullOrWhiteSpace(d.RestaurantPhone))
            WriteText(ms, d.RestaurantPhone);

        if (!string.IsNullOrWhiteSpace(d.HeaderMessage))
            WriteText(ms, d.HeaderMessage);

        WriteText(ms, "");

        // ═══ ORDER INFO BLOCK ═══
        Write(ms, EscPos.AlignLeft);

        // Order # (bold number)
        Write(ms, EscPos.BoldOn);
        WriteText(ms, $"Order #  {d.OrderNumber}");
        Write(ms, EscPos.BoldOff);

        // Cashier
        WriteText(ms, $"Cashier : {d.CashierName ?? "Admin"}");

        // Waiter
        if (!string.IsNullOrEmpty(d.WaiterName))
            WriteText(ms, $"Waiter  : {d.WaiterName}");

        // Date / Time row
        var datePart = $"Date: {d.DateTime:dd/MM/yyyy}";
        var timePart = $"Time: {d.DateTime:HH:mm:ss}";
        WriteText(ms, EscPos.PadBetween(datePart, timePart, _width));

        // Type + Payment method
        WriteText(ms, EscPos.PadBetween($"Type : {d.OrderType}", d.PaymentMethod, _width));

        // Table
        if (!string.IsNullOrEmpty(d.TableName))
            WriteText(ms, $"Table   : {d.TableName}");

        // ═══ CUSTOMER / DELIVERY INFO (shown when any customer data exists) ═══
        bool hasCustomerInfo = !string.IsNullOrWhiteSpace(d.CustomerName) || !string.IsNullOrWhiteSpace(d.CustomerPhone)
                            || !string.IsNullOrWhiteSpace(d.CustomerAddress) || !string.IsNullOrWhiteSpace(d.DriverName)
                            || !string.IsNullOrWhiteSpace(d.DeliveryNote);
        if (hasCustomerInfo)
        {
            WriteText(ms, EscPos.DashLine(_width));
            Write(ms, EscPos.BoldOn);
            var sectionTitle = d.OrderType == "Delivery" ? "DELIVERY INFO"
                             : d.OrderType == "TakeAway" ? "TAKEAWAY INFO"
                             : "CUSTOMER INFO";
            WriteText(ms, sectionTitle);
            Write(ms, EscPos.BoldOff);

            if (!string.IsNullOrWhiteSpace(d.CustomerName))
                WriteText(ms, $"Customer: {d.CustomerName}");
            if (!string.IsNullOrWhiteSpace(d.CustomerPhone))
                WriteText(ms, $"Phone   : {d.CustomerPhone}");
            if (!string.IsNullOrWhiteSpace(d.CustomerAddress))
                WriteText(ms, $"Address : {d.CustomerAddress}");
            if (!string.IsNullOrWhiteSpace(d.DriverName))
                WriteText(ms, $"Driver  : {d.DriverName}" + (!string.IsNullOrEmpty(d.DriverPhone) ? $" ({d.DriverPhone})" : ""));
            if (!string.IsNullOrWhiteSpace(d.DeliveryNote))
                WriteText(ms, $"Remarks : {d.DeliveryNote}");
        }

        WriteText(ms, EscPos.DashLine(_width));

        // ═══ ITEMS COLUMN HEADER ═══
        Write(ms, EscPos.BoldOn);
        WriteText(ms, FormatItemLine("Products Detail", "Qty", "Rate", "Amount"));
        Write(ms, EscPos.BoldOff);
        WriteText(ms, EscPos.DashLine(_width));

        // ═══ ITEMS ═══
        int mainItemCount = 0;
        foreach (var item in d.Items)
        {
            var printName = StripNonPrintable(item.Name);
            if (item.Quantity == 0 && item.UnitPrice == 0)
            {
                // Deal sub-item
                WriteText(ms, $"  {printName}");
            }
            else
            {
                WriteText(ms, FormatItemLine(
                    printName,
                    item.Quantity.ToString(),
                    EscPos.FormatCurrency(item.UnitPrice),
                    EscPos.FormatCurrency(item.LineTotal)));
                mainItemCount++;
            }

            if (!string.IsNullOrWhiteSpace(item.Notes))
                WriteText(ms, $"  * {item.Notes}");
        }

        WriteText(ms, EscPos.DashLine(_width));

        // ═══ TOTALS (pharmacy-style: Item(s) count + Gross/Disc/Tax) ═══
        // Item(s) count on left, Gross on right
        var itemsLabel = $"Item(s)  {mainItemCount}";
        var grossLabel = $"Gross : {EscPos.FormatCurrency(d.SubTotal)}";
        WriteText(ms, EscPos.PadBetween(itemsLabel, grossLabel, _width));

        if (d.DiscountAmount > 0)
        {
            var discVal = $"Disc. : -{EscPos.FormatCurrency(d.DiscountAmount)}";
            WriteText(ms, new string(' ', _width - discVal.Length) + discVal);
        }

        if (d.TaxAmount > 0)
        {
            var taxVal = $"Tax (GST) : {EscPos.FormatCurrency(d.TaxAmount)}";
            WriteText(ms, new string(' ', Math.Max(0, _width - taxVal.Length)) + taxVal);
        }

        if (d.ServiceCharge > 0)
        {
            var svcVal = $"Service : {EscPos.FormatCurrency(d.ServiceCharge)}";
            WriteText(ms, new string(' ', Math.Max(0, _width - svcVal.Length)) + svcVal);
        }

        WriteText(ms, EscPos.DashLine(_width));

        // ═══ NET AMOUNT (cashier name on left, Net Amount on right, bold) ═══
        Write(ms, EscPos.BoldOn);
        var netRight = $"Net Amount : {EscPos.FormatCurrency(d.GrandTotal)}";
        var cashierLeft = d.CashierName ?? "Admin";
        WriteText(ms, EscPos.PadBetween(cashierLeft, netRight, _width));
        Write(ms, EscPos.BoldOff);

        WriteText(ms, EscPos.DashLine(_width));

        // ═══ PAYMENT ═══
        WriteText(ms, EscPos.PadBetween($"Payment : {d.PaymentMethod}", EscPos.FormatCurrency(d.TenderedAmount), _width));
        if (d.ChangeAmount > 0)
            WriteText(ms, EscPos.PadBetween("Change :", EscPos.FormatCurrency(d.ChangeAmount), _width));

        WriteText(ms, EscPos.DashLine(_width));

        // ═══ FOOTER ═══
        Write(ms, EscPos.AlignCenter);

        if (d.OrderType == "Delivery")
            WriteText(ms, "Thank you! Enjoy your meal");
        else
            WriteText(ms, "Thank you for dining with us!");

        Write(ms, EscPos.BoldOn);
        WriteText(ms, $"*** {d.RestaurantName.ToUpper()} ***");
        Write(ms, EscPos.BoldOff);

        if (!string.IsNullOrWhiteSpace(d.FooterMessage))
            WriteText(ms, d.FooterMessage);

        WriteText(ms, d.DateTime.ToString("dd/MM/yyyy HH:mm:ss"));

        Write(ms, EscPos.FeedLines5);
        Write(ms, EscPos.PartialCut);

        return ms.ToArray();
    }

    private string FormatItemLine(string name, string qty, string price, string total)
    {
        var nameWidth = _width - 5 - 10 - 10;
        if (name.Length > nameWidth) name = name[..nameWidth];

        return $"{name.PadRight(nameWidth)}{qty.PadLeft(5)}{price.PadLeft(10)}{total.PadLeft(10)}";
    }

    private static void Write(MemoryStream ms, byte[] data) => ms.Write(data);
    private static void WriteText(MemoryStream ms, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\n");
        ms.Write(bytes);
    }

    /// <summary>Strip emoji and non-printable chars for thermal printer.</summary>
    private static string StripNonPrintable(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sb = new System.Text.StringBuilder();
        foreach (var c in input)
        {
            if (c >= 0x20 && c <= 0x7E) sb.Append(c);
            else if (c >= 0xA0 && c <= 0xFF) sb.Append(c);
            else if (c == '\n' || c == '\r') sb.Append(c);
        }
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString().Trim(), @"\s{2,}", " ");
    }
}
