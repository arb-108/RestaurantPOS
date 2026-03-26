namespace RestaurantPOS.Printing;

public static class EscPos
{
    // Initialize
    public static readonly byte[] Init = [0x1B, 0x40];

    // Text formatting
    public static readonly byte[] AlignLeft = [0x1B, 0x61, 0x00];
    public static readonly byte[] AlignCenter = [0x1B, 0x61, 0x01];
    public static readonly byte[] AlignRight = [0x1B, 0x61, 0x02];

    public static readonly byte[] BoldOn = [0x1B, 0x45, 0x01];
    public static readonly byte[] BoldOff = [0x1B, 0x45, 0x00];

    public static readonly byte[] DoubleHeightOn = [0x1B, 0x21, 0x10];
    public static readonly byte[] DoubleWidthOn = [0x1B, 0x21, 0x20];
    public static readonly byte[] DoubleOn = [0x1B, 0x21, 0x30];
    public static readonly byte[] NormalSize = [0x1B, 0x21, 0x00];

    public static readonly byte[] UnderlineOn = [0x1B, 0x2D, 0x01];
    public static readonly byte[] UnderlineOff = [0x1B, 0x2D, 0x00];

    // Line feeds
    public static readonly byte[] LineFeed = [0x0A];
    public static readonly byte[] FeedLines3 = [0x1B, 0x64, 0x03];
    public static readonly byte[] FeedLines5 = [0x1B, 0x64, 0x05];

    // Cut
    public static readonly byte[] FullCut = [0x1D, 0x56, 0x00];
    public static readonly byte[] PartialCut = [0x1D, 0x56, 0x01];

    // Cash drawer
    public static readonly byte[] OpenDrawer = [0x1B, 0x70, 0x00, 0x19, 0xFA];

    // Separator line (48 chars for 80mm)
    public static string DashLine(int width = 48) => new('-', width);
    public static string DoubleLine(int width = 48) => new('=', width);

    // Format helpers
    public static string PadBetween(string left, string right, int width = 48)
    {
        var spaces = width - left.Length - right.Length;
        if (spaces < 1) spaces = 1;
        return left + new string(' ', spaces) + right;
    }

    public static string FormatCurrency(long paisa)
    {
        return $"Rs. {paisa / 100m:N2}";
    }
}
