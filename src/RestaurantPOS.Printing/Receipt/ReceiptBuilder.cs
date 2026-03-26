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
}

public class ReceiptBuilder
{
    private readonly int _width;

    public ReceiptBuilder(int paperWidth = 80)
    {
        _width = paperWidth == 58 ? 32 : 48;
    }

    public byte[] Build(ReceiptData data)
    {
        var ms = new MemoryStream();

        Write(ms, EscPos.Init);

        // Header (compact)
        Write(ms, EscPos.AlignCenter);
        Write(ms, EscPos.DoubleOn);
        WriteText(ms, data.RestaurantName);
        Write(ms, EscPos.NormalSize);

        if (!string.IsNullOrWhiteSpace(data.HeaderMessage))
            WriteText(ms, data.HeaderMessage);

        Write(ms, EscPos.AlignLeft);
        WriteText(ms, EscPos.DoubleLine(_width));

        // Order info (compact — combined on fewer lines)
        WriteText(ms, EscPos.PadBetween($"{data.OrderNumber} {data.OrderType}", data.DateTime.ToString("dd/MM/yy HH:mm"), _width));
        if (!string.IsNullOrEmpty(data.TableName))
            WriteText(ms, EscPos.PadBetween(data.TableName, data.CashierName ?? "", _width));
        else if (data.CashierName != null)
            WriteText(ms, $"Cashier: {data.CashierName}");

        // Delivery info section (compact)
        if (data.OrderType == "Delivery")
        {
            WriteText(ms, EscPos.DashLine(_width));
            Write(ms, EscPos.BoldOn);
            WriteText(ms, "DELIVERY INFO");
            Write(ms, EscPos.BoldOff);

            if (!string.IsNullOrWhiteSpace(data.CustomerName))
                WriteText(ms, EscPos.PadBetween(data.CustomerName, data.CustomerPhone ?? "", _width));
            if (!string.IsNullOrWhiteSpace(data.CustomerAddress))
                WriteText(ms, $"Addr: {data.CustomerAddress}");
            if (!string.IsNullOrWhiteSpace(data.DriverName))
                WriteText(ms, $"Driver: {data.DriverName} ({data.DriverPhone})");
            if (!string.IsNullOrWhiteSpace(data.DeliveryNote))
                WriteText(ms, $"Note: {data.DeliveryNote}");
        }

        WriteText(ms, EscPos.DashLine(_width));

        // Column header
        Write(ms, EscPos.BoldOn);
        WriteText(ms, FormatItemLine("Item", "Qty", "Price", "Total"));
        Write(ms, EscPos.BoldOff);
        WriteText(ms, EscPos.DashLine(_width));

        // Items
        foreach (var item in data.Items)
        {
            if (item.Quantity == 0 && item.UnitPrice == 0)
            {
                // Deal sub-item — just print indented name, no qty/price
                WriteText(ms, item.Name);
            }
            else
            {
                WriteText(ms, FormatItemLine(
                    item.Name,
                    item.Quantity.ToString(),
                    EscPos.FormatCurrency(item.UnitPrice),
                    EscPos.FormatCurrency(item.LineTotal)));
            }

            if (!string.IsNullOrWhiteSpace(item.Notes))
                WriteText(ms, $"  * {item.Notes}");
        }

        WriteText(ms, EscPos.DashLine(_width));

        // Totals (compact)
        WriteText(ms, EscPos.PadBetween("Sub Total:", EscPos.FormatCurrency(data.SubTotal), _width));

        if (data.DiscountAmount > 0)
            WriteText(ms, EscPos.PadBetween("Disc:", $"-{EscPos.FormatCurrency(data.DiscountAmount)}", _width));

        if (data.TaxAmount > 0)
            WriteText(ms, EscPos.PadBetween("GST:", EscPos.FormatCurrency(data.TaxAmount), _width));

        if (data.ServiceCharge > 0)
            WriteText(ms, EscPos.PadBetween("Svc:", EscPos.FormatCurrency(data.ServiceCharge), _width));

        WriteText(ms, EscPos.DoubleLine(_width));

        Write(ms, EscPos.BoldOn);
        Write(ms, EscPos.DoubleHeightOn);
        WriteText(ms, EscPos.PadBetween("TOTAL:", EscPos.FormatCurrency(data.GrandTotal), _width));
        Write(ms, EscPos.NormalSize);
        Write(ms, EscPos.BoldOff);

        WriteText(ms, EscPos.DashLine(_width));

        // Payment (compact)
        WriteText(ms, EscPos.PadBetween($"Pay:{data.PaymentMethod}", EscPos.FormatCurrency(data.TenderedAmount), _width));
        if (data.ChangeAmount > 0)
            WriteText(ms, EscPos.PadBetween("Change:", EscPos.FormatCurrency(data.ChangeAmount), _width));

        // Footer (compact)
        Write(ms, EscPos.AlignCenter);
        if (!string.IsNullOrWhiteSpace(data.FooterMessage))
            WriteText(ms, data.FooterMessage);

        WriteText(ms, data.DateTime.ToString("dd/MM/yy HH:mm"));

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
}
