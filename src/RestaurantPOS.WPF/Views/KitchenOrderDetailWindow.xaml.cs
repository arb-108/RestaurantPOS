using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Infrastructure.Data;
using RestaurantPOS.WPF.ViewModels;

namespace RestaurantPOS.WPF.Views;

public partial class KitchenOrderDetailWindow : Window
{
    public KitchenOrderDetailWindow(KitchenReportRow row)
    {
        InitializeComponent();

        // ── Order Info Header ──
        OrderNum.Text = row.OrderNumber;
        OrderType.Text = row.OrderType;
        OrderDate.Text = row.Date;
        OrderStatus.Text = row.Status;
        CashierName.Text = row.Cashier;
        TableName.Text = row.Table;
        TotalItems.Text = row.ItemCount.ToString();
        SlipCount.Text = row.SlipCount.ToString();

        // Color the status
        OrderStatus.Foreground = row.Status switch
        {
            "Ready" => new SolidColorBrush(Color.FromRgb(22, 101, 52)),
            "In Progress" => new SolidColorBrush(Color.FromRgb(146, 64, 14)),
            _ => new SolidColorBrush(Color.FromRgb(59, 130, 246))
        };

        // Load fresh data from DB and build slips
        LoadAndBuildSlips(row);
    }

    private void LoadAndBuildSlips(KitchenReportRow row)
    {
        try
        {
            // Get a fresh DbContext to load complete data
            var db = ((App)System.Windows.Application.Current).Services.GetRequiredService<PosDbContext>();

            // Get all KitchenOrder IDs for this row
            var koIds = row.KitchenOrders.Select(ko => ko.Id).ToList();

            // Load fresh from DB with ALL navigation properties
            var slips = db.KitchenOrders
                .Include(ko => ko.Station)
                .Include(ko => ko.Order).ThenInclude(o => o.Cashier)
                .Include(ko => ko.Order).ThenInclude(o => o.TableSession).ThenInclude(ts => ts!.Table)
                .Include(ko => ko.Items).ThenInclude(koi => koi.OrderItem).ThenInclude(oi => oi.MenuItem)
                .Where(ko => koIds.Contains(ko.Id))
                .OrderBy(ko => ko.CreatedAt)
                .ToList();

            // Update header with fresh data if available
            if (slips.Count > 0)
            {
                var order = slips[0].Order;
                if (order != null)
                {
                    OrderNum.Text = order.OrderNumber ?? row.OrderNumber;
                    OrderType.Text = order.OrderType.ToString();
                    OrderDate.Text = order.CreatedAt.ToLocalTime().ToString("dd/MM/yy hh:mm tt");
                    CashierName.Text = order.Cashier?.FullName ?? "-";
                    TableName.Text = order.TableSession?.Table?.Name ?? "-";

                    // Show actual order status (Closed = paid, Open = unpaid, etc.)
                    var orderStatus = order.Status.ToString();
                    OrderStatus.Text = orderStatus;
                    OrderStatus.Foreground = order.Status switch
                    {
                        Domain.Enums.OrderStatus.Closed => new SolidColorBrush(Color.FromRgb(22, 101, 52)),
                        Domain.Enums.OrderStatus.Void => new SolidColorBrush(Color.FromRgb(185, 28, 28)),
                        _ => new SolidColorBrush(Color.FromRgb(59, 130, 246))
                    };
                }

                // Recalculate total items from fresh data
                var totalItems = slips.SelectMany(s => s.Items).Count();
                TotalItems.Text = totalItems.ToString();
                SlipCount.Text = slips.Count.ToString();
            }

            BuildSlips(slips);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[KitchenDetail] Load error: {ex.Message}");
            // Fallback: try using the in-memory data
            BuildSlips(row.KitchenOrders.OrderBy(ko => ko.CreatedAt).ToList());
        }
    }

    private void BuildSlips(List<KitchenOrder> slips)
    {
        SlipsPanel.Children.Clear();

        for (int i = 0; i < slips.Count; i++)
        {
            var slip = slips[i];
            bool isLaterSlip = i > 0;

            // Slip container
            var slipBorder = new Border
            {
                Background = isLaterSlip
                    ? new SolidColorBrush(Color.FromRgb(255, 251, 235))
                    : new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                BorderBrush = isLaterSlip
                    ? new SolidColorBrush(Color.FromRgb(253, 224, 71))
                    : new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var slipStack = new StackPanel();

            // ── Slip header row ──
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var slipTitle = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
            };
            slipTitle.Inlines.Add(new System.Windows.Documents.Run($"Slip #{i + 1}  "));
            slipTitle.Inlines.Add(new System.Windows.Documents.Run($"({slip.Station?.Name ?? "Unknown Station"})")
            {
                FontWeight = FontWeights.Normal,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128))
            });
            Grid.SetColumn(slipTitle, 0);
            headerGrid.Children.Add(slipTitle);

            // Later added badge
            if (isLaterSlip)
            {
                var badge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(254, 202, 202)),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                badge.Child = new TextBlock
                {
                    Text = "LATER ADDED",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28))
                };
                Grid.SetColumn(badge, 1);
                headerGrid.Children.Add(badge);
            }

            // Slip status badge
            var statusColor = slip.Status switch
            {
                Domain.Enums.KitchenOrderStatus.Ready => Color.FromRgb(22, 101, 52),
                Domain.Enums.KitchenOrderStatus.InProgress => Color.FromRgb(146, 64, 14),
                _ => Color.FromRgb(59, 130, 246)
            };
            var statusBg = slip.Status switch
            {
                Domain.Enums.KitchenOrderStatus.Ready => Color.FromRgb(220, 252, 231),
                Domain.Enums.KitchenOrderStatus.InProgress => Color.FromRgb(254, 243, 199),
                _ => Color.FromRgb(219, 234, 254)
            };
            var statusBorder = new Border
            {
                Background = new SolidColorBrush(statusBg),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            statusBorder.Child = new TextBlock
            {
                Text = slip.Status.ToString(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(statusColor)
            };
            Grid.SetColumn(statusBorder, 2);
            headerGrid.Children.Add(statusBorder);

            slipStack.Children.Add(headerGrid);

            // ── Slip timestamp ──
            var timeText = new TextBlock
            {
                Text = $"Printed: {slip.CreatedAt.ToLocalTime():dd/MM/yyyy hh:mm:ss tt}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                Margin = new Thickness(0, 2, 0, 6)
            };
            slipStack.Children.Add(timeText);

            // Separator
            slipStack.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
                Margin = new Thickness(0, 0, 0, 6)
            });

            // ── Items header ──
            var itemHeaderGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            itemHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            itemHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            itemHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            AddToGrid(itemHeaderGrid, "QTY", 0, 10, FontWeights.Bold, "#6B7280");
            AddToGrid(itemHeaderGrid, "ITEM", 1, 10, FontWeights.Bold, "#6B7280");
            AddToGrid(itemHeaderGrid, "STATUS", 2, 10, FontWeights.Bold, "#6B7280", TextAlignment.Right);
            slipStack.Children.Add(itemHeaderGrid);

            // ── Items ──
            if (slip.Items == null || !slip.Items.Any())
            {
                slipStack.Children.Add(new TextBlock
                {
                    Text = "(No items recorded)",
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
                    Margin = new Thickness(0, 4, 0, 4)
                });
            }
            else
            {
                foreach (var koi in slip.Items)
                {
                    var itemName = koi.OrderItem?.MenuItem?.Name ?? "Unknown Item";
                    var qty = koi.OrderItem?.Quantity ?? 1;

                    var itemGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

                    AddToGrid(itemGrid, qty.ToString(), 0, 12, FontWeights.SemiBold, "#1E293B");

                    // Item name with "later added" indicator
                    var namePanel = new StackPanel { Orientation = Orientation.Horizontal };
                    namePanel.Children.Add(new TextBlock
                    {
                        Text = itemName,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
                    });

                    if (isLaterSlip)
                    {
                        namePanel.Children.Add(new TextBlock
                        {
                            Text = " (later added)",
                            FontSize = 10,
                            FontStyle = FontStyles.Italic,
                            Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                            VerticalAlignment = VerticalAlignment.Center
                        });
                    }
                    Grid.SetColumn(namePanel, 1);
                    itemGrid.Children.Add(namePanel);

                    // Item status
                    var itemStatusText = koi.Status.ToString();
                    var itemStatusColor = koi.Status switch
                    {
                        Domain.Enums.KitchenItemStatus.Done => "#166534",
                        Domain.Enums.KitchenItemStatus.Cooking => "#92400E",
                        _ => "#3730A3"
                    };
                    AddToGrid(itemGrid, itemStatusText, 2, 10, FontWeights.SemiBold, itemStatusColor, TextAlignment.Right);

                    slipStack.Children.Add(itemGrid);

                    // Notes
                    if (!string.IsNullOrWhiteSpace(koi.OrderItem?.Notes))
                    {
                        slipStack.Children.Add(new TextBlock
                        {
                            Text = $"   Note: {koi.OrderItem.Notes}",
                            FontSize = 10,
                            FontStyle = FontStyles.Italic,
                            Foreground = new SolidColorBrush(Color.FromRgb(234, 88, 12)),
                            Margin = new Thickness(50, 0, 0, 2)
                        });
                    }
                }
            }

            slipBorder.Child = slipStack;
            SlipsPanel.Children.Add(slipBorder);
        }

        // Empty state
        if (slips.Count == 0)
        {
            SlipsPanel.Children.Add(new TextBlock
            {
                Text = "No kitchen slips found for this order.",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 20)
            });
        }
    }

    private static void AddToGrid(Grid grid, string text, int col, double fontSize,
        FontWeight weight, string colorHex, TextAlignment align = TextAlignment.Left)
    {
        var c = (Color)ColorConverter.ConvertFromString(colorHex);
        var tb = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight,
            Foreground = new SolidColorBrush(c),
            TextAlignment = align,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
