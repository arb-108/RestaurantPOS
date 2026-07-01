using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Domain.Enums;
using RestaurantPOS.Infrastructure.Data;

namespace RestaurantPOS.WPF.ViewModels;

/// <summary>
/// Resolves which Windows system printer to use for each print role
/// (Kitchen Slip / Bill / Report).
///
/// The user assigns a printer per role in Settings → Printers → "Print Role
/// Assignments". That choice is stored in AppSettings by printer NAME. At print
/// time we look up the printer's SystemPrinterName.
///
/// If no explicit role assignment exists, we fall back to the legacy
/// PrinterType-based resolution (default-of-type → any-of-type → any printer)
/// so existing installs keep working without configuration.
/// </summary>
public static class PrinterRoleResolver
{
    // AppSettings keys — one per print role.
    public const string KSlipKey  = "PrinterRole_KSlip";
    public const string BillKey   = "PrinterRole_Bill";
    public const string ReportKey = "PrinterRole_Report";

    /// <summary>Resolve the system printer name for the Kitchen-Slip role.</summary>
    public static Task<string?> ResolveKSlipAsync(PosDbContext db)
        => ResolveAsync(db, KSlipKey, PrinterType.KOT);

    /// <summary>Resolve the system printer name for the Bill / Receipt role (also used for reprints).</summary>
    public static Task<string?> ResolveBillAsync(PosDbContext db)
        => ResolveAsync(db, BillKey, PrinterType.Receipt);

    /// <summary>Resolve the system printer name for the Reports role.</summary>
    public static Task<string?> ResolveReportAsync(PosDbContext db)
        => ResolveAsync(db, ReportKey, PrinterType.Report);

    /// <summary>
    /// Core resolution. 1) explicit role assignment (by printer name),
    /// 2) default printer of the given type, 3) any printer of that type,
    /// 4) for KOT/Report fall back to a Receipt printer, 5) any active printer.
    /// </summary>
    private static async Task<string?> ResolveAsync(PosDbContext db, string roleKey, PrinterType type)
    {
        // 1) Explicit role assignment by printer name.
        var assignedName = (await db.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == roleKey))?.Value;

        if (!string.IsNullOrWhiteSpace(assignedName))
        {
            var assigned = await db.Printers
                .AsNoTracking()
                .Where(p => p.IsActive && p.Name == assignedName && p.SystemPrinterName != null)
                .FirstOrDefaultAsync();
            if (assigned?.SystemPrinterName != null)
                return assigned.SystemPrinterName;
            // Assigned printer was deleted/renamed → fall through to type-based.
        }

        // 2) Default printer of the requested type.
        var printer = await db.Printers.AsNoTracking()
            .Where(p => p.IsActive && p.IsDefault && p.Type == type && p.SystemPrinterName != null)
            .FirstOrDefaultAsync()
        // 3) Any printer of the requested type.
            ?? await db.Printers.AsNoTracking()
                .Where(p => p.IsActive && p.Type == type && p.SystemPrinterName != null)
                .FirstOrDefaultAsync();

        // 4) KOT / Report fall back to a Receipt printer if none of their type exist.
        if (printer == null && type != PrinterType.Receipt)
        {
            printer = await db.Printers.AsNoTracking()
                .Where(p => p.IsActive && p.Type == PrinterType.Receipt && p.SystemPrinterName != null)
                .FirstOrDefaultAsync();
        }

        // 5) Last resort — any active printer with a system name.
        printer ??= await db.Printers.AsNoTracking()
            .Where(p => p.IsActive && p.SystemPrinterName != null)
            .FirstOrDefaultAsync();

        return printer?.SystemPrinterName;
    }
}
