using System.Text;

namespace RestaurantPOS.Printing.KOT;

public class KotData
{
    public string StationName { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string? TableName { get; set; }
    public string OrderType { get; set; } = string.Empty;
    public string? WaiterName { get; set; }
    public DateTime DateTime { get; set; }
    public List<KotItem> Items { get; set; } = [];
}

public class KotItem
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? Notes { get; set; }
    public List<string> Modifiers { get; set; } = [];
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

        // Header — station name in large text
        Write(ms, EscPos.AlignCenter);
        Write(ms, EscPos.DoubleOn);
        WriteText(ms, $"** {data.StationName.ToUpper()} **");
        Write(ms, EscPos.NormalSize);
        WriteText(ms, "");

        Write(ms, EscPos.AlignLeft);
        WriteText(ms, EscPos.DoubleLine(_width));

        // Order info
        Write(ms, EscPos.BoldOn);
        WriteText(ms, $"KOT - {data.OrderNumber}");
        Write(ms, EscPos.BoldOff);

        var label = data.TableName ?? data.OrderType;
        WriteText(ms, EscPos.PadBetween(label, data.DateTime.ToString("HH:mm"), _width));

        if (data.WaiterName != null)
            WriteText(ms, $"Waiter: {data.WaiterName}");

        WriteText(ms, EscPos.DashLine(_width));

        // Items — large and bold for kitchen readability
        foreach (var item in data.Items)
        {
            Write(ms, EscPos.BoldOn);
            Write(ms, EscPos.DoubleHeightOn);
            WriteText(ms, $"{item.Quantity}x  {item.Name}");
            Write(ms, EscPos.NormalSize);
            Write(ms, EscPos.BoldOff);

            if (!string.IsNullOrWhiteSpace(item.Notes))
            {
                Write(ms, EscPos.BoldOn);
                WriteText(ms, $"   >> {item.Notes}");
                Write(ms, EscPos.BoldOff);
            }

            foreach (var mod in item.Modifiers)
                WriteText(ms, $"   + {mod}");

            WriteText(ms, "");
        }

        WriteText(ms, EscPos.DashLine(_width));

        Write(ms, EscPos.AlignCenter);
        WriteText(ms, data.DateTime.ToString("dd/MM/yyyy HH:mm:ss"));

        Write(ms, EscPos.FeedLines3);
        Write(ms, EscPos.PartialCut);

        return ms.ToArray();
    }

    private static void Write(MemoryStream ms, byte[] data) => ms.Write(data);
    private static void WriteText(MemoryStream ms, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\n");
        ms.Write(bytes);
    }
}
