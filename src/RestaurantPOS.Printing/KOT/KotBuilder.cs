using System.Text;

namespace RestaurantPOS.Printing.KOT;

public class KotData
{
    public string StationName { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string? TableName { get; set; }
    public string OrderType { get; set; } = string.Empty;
    public string? WaiterName { get; set; }
    public string? CashierName { get; set; }
    public DateTime DateTime { get; set; }
    public List<KotItem> Items { get; set; } = [];
    public string? HeaderBanner { get; set; }
}

public class KotItem
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? Notes { get; set; }
    public List<string> Modifiers { get; set; } = [];
    public bool IsSubItem { get; set; }
    public bool IsDealHeader { get; set; }
    public bool IsDealSubItem { get; set; }
}

public class KotBuilder
{
    private readonly int _width;

    public KotBuilder(int paperWidth = 80)
    {
        _width = paperWidth == 58 ? 32 : 48;
    }

    public byte[] Build(KotData data)
    {
        var ms = new MemoryStream();

        Write(ms, EscPos.Init);

        // ═══ HEADER ═══
        Write(ms, EscPos.AlignCenter);
        Write(ms, EscPos.DoubleOn);
        WriteText(ms, "KITCHEN ORDER");
        Write(ms, EscPos.NormalSize);
        WriteText(ms, "");

        // ═══ REPRINT BANNER ═══
        if (!string.IsNullOrEmpty(data.HeaderBanner))
        {
            Write(ms, EscPos.BoldOn);
            WriteText(ms, data.HeaderBanner);
            Write(ms, EscPos.BoldOff);
            WriteText(ms, "");
        }

        Write(ms, EscPos.AlignLeft);

        // ═══ ORDER INFO (pharmacy-style: label : value) ═══
        Write(ms, EscPos.BoldOn);
        WriteText(ms, $"Order #  {data.OrderNumber}");
        Write(ms, EscPos.BoldOff);

        WriteText(ms, $"Type    : {data.OrderType}");

        if (!string.IsNullOrEmpty(data.TableName))
            WriteText(ms, $"Table   : {data.TableName}");

        if (!string.IsNullOrEmpty(data.CashierName))
            WriteText(ms, $"Cashier : {data.CashierName}");

        if (!string.IsNullOrEmpty(data.WaiterName))
            WriteText(ms, $"Waiter  : {data.WaiterName}");

        // Date / Time row
        var datePart = $"Date: {data.DateTime:dd/MM/yyyy}";
        var timePart = $"Time: {data.DateTime:HH:mm:ss}";
        WriteText(ms, EscPos.PadBetween(datePart, timePart, _width));

        WriteText(ms, EscPos.DashLine(_width));

        // ═══ ITEMS HEADER ═══
        Write(ms, EscPos.BoldOn);
        WriteText(ms, FormatKotLine("Qty", "Item", _width));
        Write(ms, EscPos.BoldOff);
        WriteText(ms, EscPos.DashLine(_width));

        // ═══ ITEMS ═══
        int totalQty = 0;
        foreach (var item in data.Items)
        {
            if (item.IsDealHeader)
            {
                // Deal header — bold with [DEAL] tag
                Write(ms, EscPos.BoldOn);
                WriteText(ms, FormatKotLine(item.Quantity.ToString(), $"[DEAL] {StripNonPrintable(item.Name)}", _width));
                Write(ms, EscPos.BoldOff);
                totalQty += item.Quantity;
            }
            else if (item.IsDealSubItem)
            {
                // Deal sub-item — indented with qty
                WriteText(ms, $"     {FormatKotLine(item.Quantity.ToString(), $"- {item.Name}", _width)}");
            }
            else if (item.IsSubItem)
            {
                // Legacy sub-item
                WriteText(ms, $"       {item.Name}");
            }
            else
            {
                // Regular item
                Write(ms, EscPos.BoldOn);
                WriteText(ms, FormatKotLine(item.Quantity.ToString(), StripNonPrintable(item.Name), _width));
                Write(ms, EscPos.BoldOff);
                totalQty += item.Quantity;
            }

            // Notes (special instructions)
            if (!string.IsNullOrWhiteSpace(item.Notes))
            {
                WriteText(ms, $"       >> {item.Notes}");
            }

            // Modifiers
            foreach (var mod in item.Modifiers)
                WriteText(ms, $"       + {mod}");
        }

        WriteText(ms, EscPos.DashLine(_width));

        // ═══ TOTAL ITEMS ═══
        Write(ms, EscPos.BoldOn);
        WriteText(ms, EscPos.PadBetween("Total Item(s)", totalQty.ToString(), _width));
        Write(ms, EscPos.BoldOff);

        WriteText(ms, EscPos.DashLine(_width));

        // ═══ FOOTER ═══
        Write(ms, EscPos.AlignCenter);
        WriteText(ms, $"Printed: {data.DateTime:dd/MM/yyyy HH:mm:ss}");

        Write(ms, EscPos.FeedLines5);
        Write(ms, EscPos.PartialCut);

        return ms.ToArray();
    }

    private static string FormatKotLine(string qty, string name, int width)
    {
        var qtyCol = qty.PadLeft(4);
        return $"{qtyCol}   {name}";
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
        var sb = new StringBuilder();
        foreach (var c in input)
        {
            if (c >= 0x20 && c <= 0x7E) sb.Append(c);
            else if (c >= 0xA0 && c <= 0xFF) sb.Append(c);
            else if (c == '\n' || c == '\r') sb.Append(c);
        }
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString().Trim(), @"\s{2,}", " ");
    }
}
