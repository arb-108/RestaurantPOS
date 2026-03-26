using System.Runtime.InteropServices;
using RestaurantPOS.Printing.KOT;
using RestaurantPOS.Printing.Receipt;

namespace RestaurantPOS.Printing;

public interface IPrintService
{
    Task PrintReceiptAsync(ReceiptData data, string? printerName = null);
    Task PrintKotAsync(KotData data, string? printerName = null);
    Task OpenCashDrawerAsync(string? printerName = null);
}

public class PrintService : IPrintService
{
    public async Task PrintReceiptAsync(ReceiptData data, string? printerName = null)
    {
        var builder = new ReceiptBuilder();
        var bytes = builder.Build(data);
        await SendToPrinterAsync(bytes, printerName);
    }

    public async Task PrintKotAsync(KotData data, string? printerName = null)
    {
        var builder = new KotBuilder();
        var bytes = builder.Build(data);
        await SendToPrinterAsync(bytes, printerName);
    }

    public async Task OpenCashDrawerAsync(string? printerName = null)
    {
        await SendToPrinterAsync(EscPos.OpenDrawer, printerName);
    }

    private static async Task SendToPrinterAsync(byte[] data, string? printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            // Save to file for testing if no printer configured
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RestaurantPOS", "prints");
            Directory.CreateDirectory(path);
            var file = System.IO.Path.Combine(path, $"print-{DateTime.Now:yyyyMMdd-HHmmss}.bin");
            await File.WriteAllBytesAsync(file, data);
            return;
        }

        // Send raw data to Windows printer via RawPrinterHelper
        await Task.Run(() => RawPrinterHelper.SendBytesToPrinter(printerName, data));
    }
}

internal static class RawPrinterHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private class DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)] public string? pDocName;
        [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)] public string? pDataType;
    }

    [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DOCINFOA di);

    [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    public static bool SendBytesToPrinter(string printerName, byte[] data)
    {
        var di = new DOCINFOA { pDocName = "POS Receipt", pDataType = "RAW" };
        if (!OpenPrinter(printerName.Normalize(), out var hPrinter, IntPtr.Zero))
            return false;

        try
        {
            if (!StartDocPrinter(hPrinter, 1, di)) return false;
            if (!StartPagePrinter(hPrinter)) return false;

            var ptr = Marshal.AllocCoTaskMem(data.Length);
            try
            {
                Marshal.Copy(data, 0, ptr, data.Length);
                WritePrinter(hPrinter, ptr, data.Length, out _);
            }
            finally
            {
                Marshal.FreeCoTaskMem(ptr);
            }

            EndPagePrinter(hPrinter);
            EndDocPrinter(hPrinter);
            return true;
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
    }
}
