using System;
using System.Collections.Generic;
using System.Printing;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace RestaurantPOS.WPF.Views;

/// <summary>
/// A4 report preview / print window.
/// Uses a FlowDocument so Windows' native paginator handles multi-page A4 output.
/// Supports printing to any system printer (A4 size) or saving as PDF by
/// routing through the built-in "Microsoft Print to PDF" virtual printer.
/// </summary>
public partial class ReportPreviewWindow : System.Windows.Window
{
    // A4 @ 96 DPI = 794 x 1123 device-independent units
    private const double A4WidthPx = 794;
    private const double A4HeightPx = 1123;
    private const double A4MarginPx = 48;   // ~½ inch margin

    private readonly ReportDocument _report;
    private FlowDocument _flowDoc = null!;

    public string WindowHeading { get; }

    public ReportPreviewWindow(ReportDocument report)
    {
        InitializeComponent();
        _report = report ?? throw new ArgumentNullException(nameof(report));
        WindowHeading = report.Title;
        Title = report.Title + " — Preview";

        _flowDoc = BuildFlowDocument(_report);
        Viewer.Document = _flowDoc;
    }

    // ══════════════════════════════════════════════════════════
    //  FLOW DOCUMENT CONSTRUCTION
    // ══════════════════════════════════════════════════════════

    private static FlowDocument BuildFlowDocument(ReportDocument r)
    {
        var doc = new FlowDocument
        {
            PageWidth = A4WidthPx,
            PageHeight = A4HeightPx,
            ColumnWidth = A4WidthPx,   // single column so content flows naturally
            PagePadding = new Thickness(A4MarginPx),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            TextAlignment = TextAlignment.Left
        };

        // ── Header: restaurant name + address ───────────────────
        var nameBlock = new Paragraph(new Run(r.RestaurantName.ToUpperInvariant()))
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2)
        };
        doc.Blocks.Add(nameBlock);

        if (!string.IsNullOrWhiteSpace(r.RestaurantAddress))
            doc.Blocks.Add(new Paragraph(new Run(r.RestaurantAddress!))
            {
                FontSize = 11, TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0)
            });

        if (!string.IsNullOrWhiteSpace(r.RestaurantPhone))
            doc.Blocks.Add(new Paragraph(new Run(r.RestaurantPhone!))
            {
                FontSize = 11, TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            });

        // ── Title ───────────────────────────────────────────────
        doc.Blocks.Add(new Paragraph(new Run(r.Title))
        {
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        });

        if (!string.IsNullOrWhiteSpace(r.Subtitle))
            doc.Blocks.Add(new Paragraph(new Run(r.Subtitle!))
            {
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                Margin = new Thickness(0, 0, 0, 4)
            });

        // ── Generated timestamp ─────────────────────────────────
        doc.Blocks.Add(new Paragraph(new Run($"Generated: {DateTime.Now:dd/MM/yyyy hh:mm:ss tt}"))
        {
            FontSize = 10,
            TextAlignment = TextAlignment.Right,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Margin = new Thickness(0, 0, 0, 6)
        });

        // ── Data table ──────────────────────────────────────────
        if (r.Columns.Count > 0 && r.Rows.Count > 0)
        {
            var table = new Table
            {
                CellSpacing = 0,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(0.5),
                Margin = new Thickness(0, 4, 0, 4)
            };

            // Column widths: first column flexible, rest auto-sized evenly
            for (int i = 0; i < r.Columns.Count; i++)
            {
                var col = new TableColumn();
                if (i == 0)
                    col.Width = new GridLength(2, GridUnitType.Star);
                else
                    col.Width = new GridLength(1, GridUnitType.Star);
                table.Columns.Add(col);
            }

            // Header row
            var headerGroup = new TableRowGroup();
            var headerRow = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)) };
            foreach (var header in r.Columns)
            {
                headerRow.Cells.Add(MakeCell(header, bold: true, foreground: Brushes.White));
            }
            headerGroup.Rows.Add(headerRow);
            table.RowGroups.Add(headerGroup);

            // Body rows (zebra-striped)
            var bodyGroup = new TableRowGroup();
            bool alt = false;
            foreach (var rowData in r.Rows)
            {
                var tr = new TableRow();
                if (alt) tr.Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
                alt = !alt;

                for (int ci = 0; ci < r.Columns.Count; ci++)
                {
                    var value = ci < rowData.Count ? rowData[ci] : string.Empty;
                    var cell = MakeCell(value, bold: false);
                    // Right-align anything after the first column if it looks numeric/currency
                    if (ci > 0) cell.TextAlignment = TextAlignment.Right;
                    tr.Cells.Add(cell);
                }
                bodyGroup.Rows.Add(tr);
            }
            table.RowGroups.Add(bodyGroup);
            doc.Blocks.Add(table);
        }

        // ── Summary block ───────────────────────────────────────
        if (r.Summary.Count > 0)
        {
            var summaryTable = new Table
            {
                CellSpacing = 0,
                Margin = new Thickness(0, 10, 0, 0)
            };
            summaryTable.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
            summaryTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

            var group = new TableRowGroup();
            foreach (var (label, value) in r.Summary)
            {
                var tr = new TableRow();
                tr.Cells.Add(MakeCell(label, bold: true));
                var v = MakeCell(value, bold: true);
                v.TextAlignment = TextAlignment.Right;
                tr.Cells.Add(v);
                group.Rows.Add(tr);
            }
            summaryTable.RowGroups.Add(group);
            doc.Blocks.Add(summaryTable);
        }

        // ── Footer ──────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(r.Footer))
        {
            doc.Blocks.Add(new Paragraph(new Run(r.Footer!))
            {
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                Margin = new Thickness(0, 12, 0, 0)
            });
        }

        return doc;
    }

    private static TableCell MakeCell(string text, bool bold, Brush? foreground = null)
    {
        var run = new Run(text ?? string.Empty);
        var para = new Paragraph(run)
        {
            Margin = new Thickness(0),
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal
        };
        if (foreground != null) para.Foreground = foreground;
        return new TableCell(para)
        {
            Padding = new Thickness(6, 4, 6, 4),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(0, 0, 0, 0.5)
        };
    }

    // ══════════════════════════════════════════════════════════
    //  BUTTON HANDLERS
    // ══════════════════════════════════════════════════════════

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new System.Windows.Controls.PrintDialog();
            if (dlg.ShowDialog() != true) return;

            ApplyA4PageSize(dlg);
            PrintFlowDocument(dlg, _report.Title);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("Print failed: " + ex.Message, "Print",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void SavePdf_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new System.Windows.Controls.PrintDialog();

            // Try to pre-select "Microsoft Print to PDF" — ships with Windows 10/11.
            PrintQueue? pdfQueue = null;
            try
            {
                using var server = new LocalPrintServer();
                foreach (var q in server.GetPrintQueues())
                {
                    if (string.Equals(q.Name, "Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase))
                    {
                        pdfQueue = q;
                        break;
                    }
                }
            }
            catch { /* enumerate failures → fall back to standard dialog */ }

            if (pdfQueue != null)
            {
                dlg.PrintQueue = pdfQueue;
                // Windows will prompt for the PDF filename automatically.
            }
            else
            {
                System.Windows.MessageBox.Show(
                    "\"Microsoft Print to PDF\" printer was not found on this system.\n" +
                    "Please choose any PDF printer from the dialog.",
                    "Save as PDF",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                if (dlg.ShowDialog() != true) return;
            }

            ApplyA4PageSize(dlg);
            PrintFlowDocument(dlg, _report.Title);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("Save as PDF failed: " + ex.Message, "PDF",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ══════════════════════════════════════════════════════════
    //  PRINT HELPERS
    // ══════════════════════════════════════════════════════════

    private static void ApplyA4PageSize(System.Windows.Controls.PrintDialog dlg)
    {
        try
        {
            var ticket = dlg.PrintTicket;
            ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
            ticket.PageOrientation = PageOrientation.Portrait;
            dlg.PrintTicket = ticket;
        }
        catch { /* not all printers support explicit page tickets; ignore */ }
    }

    private void PrintFlowDocument(System.Windows.Controls.PrintDialog dlg, string docName)
    {
        // Rebuild document each print so paginator uses fresh page size matching the current printer.
        var doc = BuildFlowDocument(_report);
        doc.PageWidth = dlg.PrintableAreaWidth;
        doc.PageHeight = dlg.PrintableAreaHeight;
        doc.ColumnWidth = dlg.PrintableAreaWidth;
        doc.PagePadding = new Thickness(A4MarginPx);

        IDocumentPaginatorSource src = doc;
        dlg.PrintDocument(src.DocumentPaginator, docName);
    }
}

// ══════════════════════════════════════════════════════════
//  REPORT DATA MODEL
// ══════════════════════════════════════════════════════════

/// <summary>
/// Simple tabular report model rendered to A4 FlowDocument by ReportPreviewWindow.
/// </summary>
public class ReportDocument
{
    public string Title { get; set; } = "Report";
    public string? Subtitle { get; set; }

    public string RestaurantName { get; set; } = "Restaurant";
    public string? RestaurantAddress { get; set; }
    public string? RestaurantPhone { get; set; }

    /// <summary>Table column headers (optional). Leave empty to render label/value summary only.</summary>
    public List<string> Columns { get; set; } = new();

    /// <summary>Table body rows — each row's values align to <see cref="Columns"/>.</summary>
    public List<List<string>> Rows { get; set; } = new();

    /// <summary>Summary block printed below the table (totals, counts, etc.).</summary>
    public List<(string Label, string Value)> Summary { get; set; } = new();

    public string? Footer { get; set; }
}
