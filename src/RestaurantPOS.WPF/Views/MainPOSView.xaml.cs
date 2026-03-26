using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.WPF.ViewModels;

namespace RestaurantPOS.WPF.Views;

public partial class MainPOSView : UserControl
{
    // ── Navigation zones for keyboard-driven POS workflow ──
    private enum PosZone { Categories, MenuItems, OrderGrid, BillingFields }

    private PosZone _currentZone = PosZone.Categories;

    // Ordered billing field list for Enter-to-advance
    private TextBox[] _billingFields = [];

    public MainPOSView()
    {
        InitializeComponent();
        // NOTE: Loaded="OnLoaded" is wired in XAML — do NOT add Loaded += OnLoaded here
        PreviewKeyDown += OnPreviewKeyDown;

        // Detect mouse clicks in zones to auto-activate them
        CategoryList.PreviewMouseLeftButtonDown += (_, _) => SetZone(PosZone.Categories);
        MenuItemList.PreviewMouseLeftButtonDown += (_, _) => SetZone(PosZone.MenuItems);
        OrderGrid.PreviewMouseLeftButtonDown += (_, _) => SetZone(PosZone.OrderGrid);

        // Detect when billing fields get focus (mouse click or tab)
        CategoryList.GotFocus += (_, _) => { _currentZone = PosZone.Categories; UpdateZoneIndicator(); };
        MenuItemList.GotFocus += (_, _) => { _currentZone = PosZone.MenuItems; UpdateZoneIndicator(); };
        OrderGrid.GotFocus += (_, _) => { _currentZone = PosZone.OrderGrid; UpdateZoneIndicator(); };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Build the billing fields array (Tab/Enter order)
        _billingFields = [MobileTextBox, DiscPercentBox, DiscRsBox, CommentBox, TaxPercentBox, GstRsBox, PayBox];

        // Wire GotFocus for billing fields to detect zone
        foreach (var tb in _billingFields)
            tb.GotFocus += (_, _) => { _currentZone = PosZone.BillingFields; UpdateZoneIndicator(); };

        if (DataContext is MainPOSViewModel vm)
        {
            await vm.LoadDataCommand.ExecuteAsync(null);
        }

        // Delay initial focus to after layout completes
        await Dispatcher.InvokeAsync(() =>
        {
            SetZone(PosZone.Categories);
        }, DispatcherPriority.Input);
    }

    // ═══════════════════════════════════════════════════════════
    //  MASTER KEY HANDLER — intercepts keys before children
    // ═══════════════════════════════════════════════════════════
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Let F-keys pass through (F1=NewOrder, F5=Checkout already bound)
        if (e.Key >= Key.F1 && e.Key <= Key.F12)
            return;

        // If a TextBox in the billing section has focus, handle Enter/Tab/Escape specially
        if (IsBillingFieldFocused())
        {
            HandleBillingKeys(e);
            return;
        }

        // If DataGrid cell is being edited, let most keys pass through
        if (IsOrderGridEditing())
        {
            if (e.Key == Key.Escape)
            {
                OrderGrid.CancelEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                OrderGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                // After committing Qty edit, move to next billing field zone
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => SetZone(PosZone.BillingFields));
                e.Handled = true;
            }
            return;
        }

        // Tab / Shift+Tab = zone switching
        if (e.Key == Key.Tab)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                MoveToPreviousZone();
            else
                MoveToNextZone();
            e.Handled = true;
            return;
        }

        // Escape = go back one zone
        if (e.Key == Key.Escape)
        {
            MoveToPreviousZone();
            e.Handled = true;
            return;
        }

        // Zone-specific key handling
        switch (_currentZone)
        {
            case PosZone.Categories:
                HandleCategoryKeys(e);
                break;
            case PosZone.MenuItems:
                HandleMenuItemKeys(e);
                break;
            case PosZone.OrderGrid:
                HandleOrderGridKeys(e);
                break;
            case PosZone.BillingFields:
                HandleBillingKeys(e);
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  ZONE: Categories (Up/Down navigate, Right→MenuItems, Enter=select)
    // ═══════════════════════════════════════════════════════════
    private void HandleCategoryKeys(KeyEventArgs e)
    {
        var vm = DataContext as MainPOSViewModel;
        if (vm == null) return;
        int count = vm.Categories.Count;
        if (count == 0) return;

        int idx = CategoryList.SelectedIndex;

        switch (e.Key)
        {
            case Key.Up:
                idx = idx <= 0 ? count - 1 : idx - 1;
                SelectCategory(idx);
                e.Handled = true;
                break;

            case Key.Down:
                idx = idx >= count - 1 ? 0 : idx + 1;
                SelectCategory(idx);
                e.Handled = true;
                break;

            case Key.Right:
            case Key.Enter:
                if (CategoryList.SelectedItem == null && count > 0)
                    SelectCategory(0);
                SetZone(PosZone.MenuItems);
                e.Handled = true;
                break;

            case Key.Left:
                SetZone(PosZone.OrderGrid);
                e.Handled = true;
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  ZONE: Menu Items (arrows navigate, Enter=add to cart, Left=Categories)
    // ═══════════════════════════════════════════════════════════
    private void HandleMenuItemKeys(KeyEventArgs e)
    {
        var vm = DataContext as MainPOSViewModel;
        if (vm == null) return;
        int count = vm.MenuItems.Count;
        if (count == 0) return;

        int idx = MenuItemList.SelectedIndex;
        if (idx < 0) idx = 0;

        // Approximate columns in WrapPanel (145px cards + margins in ~width area)
        int cols = Math.Max(1, (int)(MenuItemList.ActualWidth / 155));

        switch (e.Key)
        {
            case Key.Up:
                idx = idx - cols >= 0 ? idx - cols : idx;
                SelectMenuItem(idx);
                e.Handled = true;
                break;

            case Key.Down:
                idx = idx + cols < count ? idx + cols : idx;
                SelectMenuItem(idx);
                e.Handled = true;
                break;

            case Key.Right:
                if (idx + 1 < count)
                {
                    SelectMenuItem(idx + 1);
                }
                else
                {
                    // At last item, Right goes to Order Grid
                    SetZone(PosZone.OrderGrid);
                }
                e.Handled = true;
                break;

            case Key.Left:
                if (idx > 0)
                {
                    SelectMenuItem(idx - 1);
                }
                else
                {
                    SetZone(PosZone.Categories);
                }
                e.Handled = true;
                break;

            case Key.Enter:
                // Add item to order AND jump cursor to Order Grid on that item
                if (MenuItemList.SelectedItem is Domain.Entities.MenuItem menuItem)
                {
                    _ = vm.AddMenuItemCommand.ExecuteAsync(menuItem);
                    // Jump to Order Grid — select the last (newly added) row
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                    {
                        int lastRow = vm.OrderItems.Count - 1;
                        if (lastRow >= 0)
                        {
                            OrderGrid.SelectedIndex = lastRow;
                            OrderGrid.ScrollIntoView(OrderGrid.SelectedItem);
                        }
                        SetZone(PosZone.OrderGrid);
                    });
                }
                e.Handled = true;
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  ZONE: Order Grid (Up/Down rows, Enter=edit Qty, Left=MenuItems)
    // ═══════════════════════════════════════════════════════════
    private void HandleOrderGridKeys(KeyEventArgs e)
    {
        var vm = DataContext as MainPOSViewModel;
        if (vm == null) return;
        int count = vm.OrderItems.Count;
        if (count == 0)
        {
            if (e.Key is Key.Left or Key.Up)
                SetZone(PosZone.MenuItems);
            else if (e.Key is Key.Down or Key.Right)
                SetZone(PosZone.BillingFields);
            e.Handled = true;
            return;
        }

        int idx = OrderGrid.SelectedIndex;
        if (idx < 0) idx = 0;

        switch (e.Key)
        {
            case Key.Up:
                if (idx > 0)
                {
                    OrderGrid.SelectedIndex = idx - 1;
                    OrderGrid.ScrollIntoView(OrderGrid.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Down:
                if (idx < count - 1)
                {
                    OrderGrid.SelectedIndex = idx + 1;
                    OrderGrid.ScrollIntoView(OrderGrid.SelectedItem);
                }
                else
                {
                    SetZone(PosZone.BillingFields);
                }
                e.Handled = true;
                break;

            case Key.Left:
                SetZone(PosZone.MenuItems);
                e.Handled = true;
                break;

            case Key.Right:
                SetZone(PosZone.BillingFields);
                e.Handled = true;
                break;

            case Key.Enter:
                if (OrderGrid.SelectedItem != null)
                {
                    OrderGrid.Focus();
                    OrderGrid.CurrentCell = new DataGridCellInfo(
                        OrderGrid.SelectedItem, OrderGrid.Columns[2]); // Qty column
                    OrderGrid.BeginEdit();
                }
                e.Handled = true;
                break;

            case Key.Delete:
                if (vm.DeleteOrderCommand.CanExecute(null))
                    vm.DeleteOrderCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  ZONE: Billing Fields (Enter/Tab=next field, final Enter→Checkout)
    // ═══════════════════════════════════════════════════════════
    private void HandleBillingKeys(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var focused = Keyboard.FocusedElement as TextBox;
            if (focused != null)
            {
                int idx = Array.IndexOf(_billingFields, focused);
                if (idx >= 0 && idx < _billingFields.Length - 1)
                {
                    var next = _billingFields[idx + 1];
                    if (next == CommentBox)
                        next = _billingFields[idx + 2];
                    next.Focus();
                    next.SelectAll();
                    e.Handled = true;
                }
                else if (idx == _billingFields.Length - 1)
                {
                    CheckoutBtn.Focus();
                    e.Handled = true;
                }
            }
            else if (Keyboard.FocusedElement is Button btn && btn == CheckoutBtn)
            {
                if (DataContext is MainPOSViewModel vm && vm.CheckoutCommand.CanExecute(null))
                    _ = vm.CheckoutCommand.ExecuteAsync(null);
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Escape)
        {
            SetZone(PosZone.OrderGrid);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up && !IsBillingFieldFocused())
        {
            SetZone(PosZone.OrderGrid);
            e.Handled = true;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  ZONE MANAGEMENT
    // ═══════════════════════════════════════════════════════════
    private void SetZone(PosZone zone)
    {
        _currentZone = zone;
        UpdateZoneIndicator();

        // Use Dispatcher to ensure focus is set after current event processing
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            switch (zone)
            {
                case PosZone.Categories:
                    if (CategoryList.SelectedIndex < 0 && CategoryList.Items.Count > 0)
                        SelectCategory(0);
                    CategoryList.Focus();
                    break;

                case PosZone.MenuItems:
                    if (MenuItemList.SelectedIndex < 0 && MenuItemList.Items.Count > 0)
                        SelectMenuItem(0);
                    MenuItemList.Focus();
                    ScrollMenuItemIntoView();
                    break;

                case PosZone.OrderGrid:
                    if (OrderGrid.Items.Count > 0)
                    {
                        if (OrderGrid.SelectedIndex < 0)
                            OrderGrid.SelectedIndex = 0;
                        OrderGrid.Focus();
                        OrderGrid.ScrollIntoView(OrderGrid.SelectedItem);
                    }
                    else
                    {
                        _currentZone = PosZone.MenuItems;
                        UpdateZoneIndicator();
                        MenuItemList.Focus();
                    }
                    break;

                case PosZone.BillingFields:
                    if (_billingFields.Length > 0)
                    {
                        _billingFields[0].Focus();
                        _billingFields[0].SelectAll();
                    }
                    break;
            }
        });
    }

    private void MoveToNextZone()
    {
        var next = _currentZone switch
        {
            PosZone.Categories => PosZone.MenuItems,
            PosZone.MenuItems => PosZone.OrderGrid,
            PosZone.OrderGrid => PosZone.BillingFields,
            PosZone.BillingFields => PosZone.Categories,
            _ => PosZone.Categories
        };
        SetZone(next);
    }

    private void MoveToPreviousZone()
    {
        var prev = _currentZone switch
        {
            PosZone.Categories => PosZone.BillingFields,
            PosZone.MenuItems => PosZone.Categories,
            PosZone.OrderGrid => PosZone.MenuItems,
            PosZone.BillingFields => PosZone.OrderGrid,
            _ => PosZone.Categories
        };
        SetZone(prev);
    }

    private void UpdateZoneIndicator()
    {
        ZoneIndicator.Text = _currentZone switch
        {
            PosZone.Categories => "[Categories]",
            PosZone.MenuItems => "[Menu Items]",
            PosZone.OrderGrid => "[Order Items]",
            PosZone.BillingFields => "[Billing]",
            _ => ""
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  SELECTION HELPERS — ensure visual highlight updates
    // ═══════════════════════════════════════════════════════════
    private void SelectCategory(int index)
    {
        CategoryList.SelectedIndex = index;
        CategoryList.ScrollIntoView(CategoryList.SelectedItem);
        // Force the container to refresh its visual state
        if (CategoryList.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem item)
            item.IsSelected = true;
        // Ensure items are loaded for this category
        if (CategoryList.SelectedItem is Category cat && DataContext is MainPOSViewModel vm)
            vm.SelectCategoryCommand.Execute(cat);
    }

    private void SelectMenuItem(int index)
    {
        MenuItemList.SelectedIndex = index;
        ScrollMenuItemIntoView();
        if (MenuItemList.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem item)
            item.IsSelected = true;
    }

    private void ScrollMenuItemIntoView()
    {
        if (MenuItemList.SelectedItem != null)
            MenuItemList.ScrollIntoView(MenuItemList.SelectedItem);
    }

    private bool IsBillingFieldFocused()
    {
        if (Keyboard.FocusedElement is not TextBox focused) return false;
        return Array.IndexOf(_billingFields, focused) >= 0;
    }

    private bool IsOrderGridEditing()
    {
        if (Keyboard.FocusedElement is not TextBox focused) return false;
        DependencyObject? parent = focused;
        while (parent != null)
        {
            if (parent is DataGridCell) return true;
            if (parent is DataGrid) return false;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return false;
    }

    // ═══════════════════════════════════════════════════════════
    //  EXISTING EVENT HANDLERS
    // ═══════════════════════════════════════════════════════════
    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is Category cat && DataContext is MainPOSViewModel vm)
        {
            vm.SelectCategoryCommand.Execute(cat);
        }
    }

    private void Button_Click(object sender, RoutedEventArgs e) { }

    private async void MobileTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainPOSViewModel vm)
        {
            int idx = Array.IndexOf(_billingFields, MobileTextBox);
            if (idx >= 0 && idx < _billingFields.Length - 1)
            {
                await vm.PhoneEnterPressedCommand.ExecuteAsync(null);
                var next = _billingFields[idx + 1];
                next.Focus();
                next.SelectAll();
            }
            else
            {
                await vm.PhoneEnterPressedCommand.ExecuteAsync(null);
            }
            e.Handled = true;
        }
    }

    private void CustomerSearchItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Customer customer
            && DataContext is MainPOSViewModel vm)
        {
            vm.SelectCustomerFromSearchCommand.Execute(customer);
        }
    }
}
