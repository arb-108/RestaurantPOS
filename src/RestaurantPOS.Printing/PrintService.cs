using System.Runtime.InteropServices;
using System.Text.Json;
using RestaurantPOS.Printing.KOT;
using RestaurantPOS.Printing.Receipt;

namespace RestaurantPOS.Printing;

public interface IPrintService
{
    Task PrintReceiptAsync(ReceiptData data, string? printerName = null);
    Task PrintKotAsync(KotData data, string? printerName = null);
    Task OpenCashDrawerAsync(string? printerName = null);

    /// <summary>Get count of queued (failed) print jobs.</summary>
    int GetQueuedJobCount();

    /// <summary>Retry all queued print jobs. Returns (success, failed) counts.</summary>
    Task<(int success, int failed)> RetryQueuedJobsAsync();

    /// <summary>Clear all queued jobs.</summary>
    void ClearQueue();
}

public class PrintService : IPrintService
{
    private static readonly string QueueDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RestaurantPOS", "print-queue");

    public async Task PrintReceiptAsync(ReceiptData data, string? printerName = null)
    {
        var builder = new ReceiptBuilder();
        var bytes = builder.Build(data);
        await SendToPrinterWithQueueAsync(bytes, printerName, "Receipt", data.OrderNumber);
    }

    public async Task PrintKotAsync(KotData data, string? printerName = null)
    {
        var builder = new KotBuilder();
        var bytes = builder.Build(data);
        await SendToPrinterWithQueueAsync(bytes, printerName, "KOT", data.OrderNumber);
    }

    public async Task OpenCashDrawerAsync(string? printerName = null)
    {
        await SendToPrinterAsync(EscPos.OpenDrawer, printerName);
    }

    /// <summary>Send to printer; if fails, queue the job for retry.</summary>
    private async Task SendToPrinterWithQueueAsync(byte[] data, string? printerName, string jobType, string orderNumber)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            // No printer configured — save debug file
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RestaurantPOS", "prints");
            Directory.CreateDirectory(path);
            var file = Path.Combine(path, $"print-{DateTime.Now:yyyyMMdd-HHmmss}.bin");
            await File.WriteAllBytesAsync(file, data);
            return;
        }

        bool success = false;
        try
        {
            success = await Task.Run(() => RawPrinterHelper.SendBytesToPrinter(printerName, data));
        }
        catch
        {
            success = false;
        }

        if (!success)
        {
            // Printer failed — queue the job
            await QueueJobAsync(data, printerName, jobType, orderNumber);
            System.Diagnostics.Debug.WriteLine($"[PrintQueue] Job queued: {jobType} {orderNumber} → {printerName}");
        }
    }

    private static async Task QueueJobAsync(byte[] data, string printerName, string jobType, string orderNumber)
    {
        Directory.CreateDirectory(QueueDir);
        var id = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}_{jobType}_{orderNumber}";
        var metaFile = Path.Combine(QueueDir, $"{id}.json");
        var dataFile = Path.Combine(QueueDir, $"{id}.bin");

        var meta = new PrintQueueJob
        {
            PrinterName = printerName,
            JobType = jobType,
            OrderNumber = orderNumber,
            QueuedAt = DateTime.Now,
            DataFile = dataFile
        };

        await File.WriteAllBytesAsync(dataFile, data);
        await File.WriteAllTextAsync(metaFile, JsonSerializer.Serialize(meta));
    }

    public int GetQueuedJobCount()
    {
        if (!Directory.Exists(QueueDir)) return 0;
        return Directory.GetFiles(QueueDir, "*.json").Length;
    }

    public async Task<(int success, int failed)> RetryQueuedJobsAsync()
    {
        if (!Directory.Exists(QueueDir)) return (0, 0);

        var jsonFiles = Directory.GetFiles(QueueDir, "*.json").OrderBy(f => f).ToArray();
        int successCount = 0, failCount = 0;

        foreach (var jsonFile in jsonFiles)
        {
            try
            {
                var json = await File.ReadAllTextAsync(jsonFile);
                var job = JsonSerializer.Deserialize<PrintQueueJob>(json);
                if (job == null || !File.Exists(job.DataFile)) { failCount++; continue; }

                var data = await File.ReadAllBytesAsync(job.DataFile);
                var sent = await Task.Run(() => RawPrinterHelper.SendBytesToPrinter(job.PrinterName, data));

                if (sent)
                {
                    // Success — remove from queue
                    File.Delete(jsonFile);
                    File.Delete(job.DataFile);
                    successCount++;
                }
                else
                {
                    failCount++;
                }
            }
            catch
            {
                failCount++;
            }
        }

        return (successCount, failCount);
    }

    public void ClearQueue()
    {
        if (!Directory.Exists(QueueDir)) return;
        foreach (var f in Directory.GetFiles(QueueDir))
            File.Delete(f);
    }

    private static async Task SendToPrinterAsync(byte[] data, string? printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName)) return;
        await Task.Run(() => RawPrinterHelper.SendBytesToPrinter(printerName, data));
    }
}

internal class PrintQueueJob
{
    public string PrinterName { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime QueuedAt { get; set; }
    public string DataFile { get; set; } = string.Empty;
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
