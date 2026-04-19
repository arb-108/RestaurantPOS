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
    private enum PosZone { Categories, MenuItems, Tables, OrderGrid, BillingFields }

    private PosZone _currentZone = PosZone.Categories;

    // Ordered billing field list for Enter-to-advance
    private TextBox[] _billingFields = [];
    private bool _isSearchBoxActive;

    public MainPOSView()
    {
        InitializeComponent();
        // NOTE: Loaded="OnLoaded" is wired in XAML — do NOT add Loaded += OnLoaded here
        PreviewKeyDown += OnPreviewKeyDown;

        // Detect mouse clicks in zones to auto-activate them
        CategoryList.PreviewMouseLeftButtonDown += (_, _) => SetZone(PosZone.Categories);
        MenuItemList.PreviewMouseLeftButtonDown += (_, _) => SetZone(PosZone.MenuItems);
        // The Order Grid is intentionally OUTSIDE the zone-navigation flow.
        // Cashier can freely click cells, type, and edit — nothing here
        // drags focus back to the grid or cycles into it via Tab/arrows.
        // Only the inline edit hook below is needed so a single click on
        // Qty / Remarks drops the caret directly into the editing TextBox.
        OrderGrid.AddHandler(DataGridCell.GotFocusEvent, new RoutedEventHandler(OrderGridCell_GotFocus));

        // Detect when zones get focus (mouse click or tab)
        CategoryList.GotFocus += (_, _) => { _currentZone = PosZone.Categories; UpdateZoneIndicator(); };
        MenuItemList.GotFocus += (_, _) => { _currentZone = PosZone.MenuItems; UpdateZoneIndicator(); };
        // NOTE: OrderGrid is deliberately NOT wired to the zone system —
        // its focus / clicks must not affect keyboard-navigation state.
        KBillBtn.GotFocus += (_, _) => { _currentZone = PosZone.BillingFields; UpdateZoneIndicator(); };
        BillPrintBtn.GotFocus += (_, _) => { _currentZone = PosZone.BillingFields; UpdateZoneIndicator(); };
        CheckoutBtn.GotFocus += (_, _) => { _currentZone = PosZone.BillingFields; UpdateZoneIndicator(); };

        // Reset search mode when search box loses focus
        SearchProductBox.LostFocus += (_, _) => _isSearchBoxActive = false;
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
            // The MainPOSViewModel is a cached singleton — when the user navigates
            // away to e.g. Menu Settings while a Delivery/Takeaway full-screen overlay
            // was open and then comes back, the IsDeliveryMaximized/IsTakeawayMaximized
            // flags would still be true and the stale overlay would obscure everything.
            // Reset these flags so the standard POS layout is shown on every (re)load.
            vm.IsDeliveryMaximized = false;
            vm.IsTakeawayMaximized = false;

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

        // Ctrl+Tab → jump to first category
        if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            SelectCategory(0);
            SetZone(PosZone.Categories);
            e.Handled = true;
            return;
        }

        // Ctrl+F → focus search product field (works even from TextBoxes).
        // Plain "F" was stealing the letter whenever the cashier typed in
        // any TextBox that wasn't marked as billing field (e.g. Remarks).
        if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _isSearchBoxActive = true;
            SearchProductBox.Focus();
            SearchProductBox.SelectAll();
            e.Handled = true;
            return;
        }

        // While search box is active, keep focus there and handle Escape/Enter
        if (_isSearchBoxActive && Keyboard.FocusedElement == SearchProductBox)
        {
            if (e.Key == Key.Escape)
            {
                _isSearchBoxActive = false;
                SearchProductBox.Text = "";
                SetZone(PosZone.MenuItems);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter)
            {
                // Select first menu item if available
                _isSearchBoxActive = false;
                if (MenuItemList.Items.Count > 0)
                {
                    SelectMenuItem(0);
                    SetZone(PosZone.MenuItems);
                }
                e.Handled = true;
                return;
            }
            // Let all other keys pass through to the search box (typing)
            return;
        }

        // MobileTextBox has its own PreviewKeyDown handler — let Enter/Up pass through to it
        if (Keyboard.FocusedElement == MobileTextBox && (e.Key == Key.Enter || e.Key == Key.Up))
            return;

        // If a TextBox or Button in the billing section has focus, handle Enter/Tab/Escape specially
        if (IsBillingFieldFocused() || IsBillingButtonFocused())
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
            case PosZone.Tables:
                HandleTableKeys(e);
                break;
            // PosZone.OrderGrid intentionally not handled here — the grid
            // is outside the Enter/Tab flow. Clicking any cell enters free
            // edit mode; keystrokes go straight to the editing TextBox.
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
                // Wrap back to Billing — grid is out of navigation.
                SetZone(PosZone.BillingFields);
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
                if (idx + cols < count)
                {
                    SelectMenuItem(idx + cols);
                }
                else
                {
                    // At bottom row, go to Tables zone
                    SetZone(PosZone.Tables);
                }
                e.Handled = true;
                break;

            case Key.Right:
                if (idx + 1 < count)
                {
                    SelectMenuItem(idx + 1);
                }
                // else: stay at last item — grid is out of the nav flow.
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
                // Add item to order and then jump DIRECTLY to the Mobile
                // number field — the Order Grid is no longer in the
                // Enter-Enter flow (cashier edits it freely with the mouse).
                if (MenuItemList.SelectedItem is Domain.Entities.MenuItem menuItem)
                {
                    _ = vm.AddMenuItemCommand.ExecuteAsync(menuItem);
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                    {
                        MobileTextBox.Focus();
                        MobileTextBox.SelectAll();
                        SetZone(PosZone.BillingFields);
                    });
                }
                e.Handled = true;
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  [REMOVED] HandleOrderGridKeys — the Order Grid has been taken
    //  OUT of the zone-navigation flow. The cashier interacts with it
    //  purely via mouse: click a Qty/Remarks cell → edit freely. No
    //  keystroke from outside the editing TextBox is redirected here.
    //  Leaving an empty stub so the old Delete/SelectedItem logic
    //  doesn't regress when callers are wired up elsewhere.
    // ═══════════════════════════════════════════════════════════
    private void HandleOrderGridKeys_Unused(KeyEventArgs e)
    {
        var vm = DataContext as MainPOSViewModel;
        if (vm == null) return;

        switch (e.Key)
        {
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
                if (idx >= 0)
                {
                    // GstRsBox is last billing field before buttons
                    if (focused == GstRsBox)
                    {
                        // GstRs → K-Bill button
                        KBillBtn.Focus();
                        e.Handled = true;
                    }
                    else if (focused == PayBox)
                    {
                        // Pay → Checkout
                        CheckoutBtn.Focus();
                        e.Handled = true;
                    }
                    else if (idx < _billingFields.Length - 1)
                    {
                        var next = _billingFields[idx + 1];
                        if (next == CommentBox)
                            next = _billingFields[idx + 2];
                        next.Focus();
                        next.SelectAll();
                        e.Handled = true;
                    }
                }
            }
            else if (Keyboard.FocusedElement is Button btn)
            {
                if (btn == KBillBtn)
                {
                    // K-Bill → execute command (opens print preview), then focus BillPrintBtn
                    if (DataContext is MainPOSViewModel vm && vm.KBillCommand.CanExecute(null))
                        _ = vm.KBillCommand.ExecuteAsync(null);
                    // After preview closes, focus BillPrintBtn
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, () => BillPrintBtn.Focus());
                    e.Handled = true;
                }
                else if (btn == BillPrintBtn)
                {
                    // BillPrint → execute command (opens print preview), then focus PayBox
                    if (DataContext is MainPOSViewModel vm && vm.BillPrintCommand.CanExecute(null))
                        _ = vm.BillPrintCommand.ExecuteAsync(null);
                    // After preview closes, focus PayBox
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                    {
                        PayBox.Focus();
                        PayBox.SelectAll();
                    });
                    e.Handled = true;
                }
                else if (btn == CheckoutBtn)
                {
                    if (DataContext is MainPOSViewModel vm && vm.CheckoutCommand.CanExecute(null))
                        _ = vm.CheckoutCommand.ExecuteAsync(null);
                    e.Handled = true;
                }
            }
            return;
        }

        // Down from BillPrintBtn → PayBox
        if (e.Key == Key.Down && Keyboard.FocusedElement is Button downBtn && downBtn == BillPrintBtn)
        {
            PayBox.Focus();
            PayBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            // Grid is out of nav — escape from billing goes back to Menu Items.
            SetZone(PosZone.MenuItems);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up && !IsBillingFieldFocused())
        {
            SetZone(PosZone.MenuItems);
            e.Handled = true;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  ZONE: Tables (Up/Enter→MenuItems)
    // ═══════════════════════════════════════════════════════════
    private void HandleTableKeys(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                SetZone(PosZone.MenuItems);
                e.Handled = true;
                break;

            case Key.Enter:
                // Execute the table button's command, then go to MenuItems
                if (Keyboard.FocusedElement is Button btn && btn.Command != null && btn.Command.CanExecute(btn.CommandParameter))
                {
                    btn.Command.Execute(btn.CommandParameter);
                }
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => SetZone(PosZone.MenuItems));
                e.Handled = true;
                break;

            case Key.Left:
                // Navigate between table buttons
                FocusPreviousTableButton();
                e.Handled = true;
                break;

            case Key.Right:
                FocusNextTableButton();
                e.Handled = true;
                break;

            case Key.Down:
                // Grid is out of nav — go straight to Billing fields.
                SetZone(PosZone.BillingFields);
                e.Handled = true;
                break;
        }
    }

    private void FocusNextTableButton()
    {
        var buttons = GetTableButtons();
        int idx = GetFocusedTableButtonIndex(buttons);
        if (idx < buttons.Count - 1)
            buttons[idx + 1].Focus();
        else if (buttons.Count > 0)
            buttons[0].Focus();
    }

    private void FocusPreviousTableButton()
    {
        var buttons = GetTableButtons();
        int idx = GetFocusedTableButtonIndex(buttons);
        if (idx > 0)
            buttons[idx - 1].Focus();
        else if (buttons.Count > 0)
            buttons[buttons.Count - 1].Focus();
    }

    private List<Button> GetTableButtons()
    {
        var buttons = new List<Button>();
        foreach (var child in TableWrapPanel.Children)
        {
            if (child is Button btn)
                buttons.Add(btn);
            else if (child is System.Windows.Controls.ItemsControl ic)
            {
                for (int i = 0; i < ic.Items.Count; i++)
                {
                    if (ic.ItemContainerGenerator.ContainerFromIndex(i) is ContentPresenter cp)
                    {
                        var btn2 = FindVisualChild<Button>(cp);
                        if (btn2 != null) buttons.Add(btn2);
                    }
                }
            }
        }
        return buttons;
    }

    private int GetFocusedTableButtonIndex(List<Button> buttons)
    {
        var focused = Keyboard.FocusedElement as Button;
        if (focused == null) return -1;
        return buttons.IndexOf(focused);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
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

                case PosZone.Tables:
                    // Switch to Order On tab (index 0) and focus first table button
                    OrderTabControl.SelectedIndex = 0;
                    var tableButtons = GetTableButtons();
                    if (tableButtons.Count > 0)
                        tableButtons[0].Focus();
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
            PosZone.MenuItems => PosZone.Tables,
            PosZone.Tables => PosZone.BillingFields,
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
            PosZone.Tables => PosZone.MenuItems,
            PosZone.BillingFields => PosZone.Tables,
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
            PosZone.Tables => "[Tables]",
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

    private bool IsBillingButtonFocused()
    {
        if (Keyboard.FocusedElement is not Button btn) return false;
        return btn == KBillBtn || btn == BillPrintBtn || btn == CheckoutBtn;
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

    // ───────────────────────────────────────────────────────────
    //  Order Grid single-click-to-edit for Qty + Remarks columns.
    //  A normal DataGridTextColumn needs a second click / F2 to enter
    //  edit mode. We use two handlers:
    //   1. PreviewMouseLeftButtonDown → set current cell + focus it
    //      (without swallowing the event, so the DataGrid still
    //       runs its normal focus logic).
    //   2. DataGridCell.GotFocus → once the cell has focus, call
    //      BeginEdit so the inline TextBox is spawned and receives
    //      keystrokes directly.
    // ───────────────────────────────────────────────────────────
    private void OrderGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SetZone(PosZone.OrderGrid);
    }

    private void OrderGridCell_GotFocus(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not DataGridCell cell) return;
        if (cell.IsEditing || cell.IsReadOnly) return;

        var header = cell.Column?.Header?.ToString();
        if (header != "Qty" && header != "Remarks") return;

        // Select the row so Delete/keyboard ops still target it
        DependencyObject? rowParent = cell;
        while (rowParent != null && rowParent is not DataGridRow)
            rowParent = VisualTreeHelper.GetParent(rowParent);
        if (rowParent is DataGridRow row)
            row.IsSelected = true;

        // Enter edit mode on the focused cell, then put caret into
        // the editing TextBox and select its text for easy overwrite.
        OrderGrid.BeginEdit();

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            var tb = FindCellTextBox(cell);
            if (tb != null)
            {
                tb.Focus();
                tb.SelectAll();
            }
        });
    }

    private static TextBox? FindCellTextBox(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox tb) return tb;
            var inner = FindCellTextBox(child);
            if (inner != null) return inner;
        }
        return null;
    }

    // ───────────────────────────────────────────────────────────
    //  Validate edits when the cashier commits a cell.
    //  Rules:
    //   • Qty must be a whole number ≥ 0.
    //   • If Qty = 0 AND the K-Slip has NOT been sent for this item
    //     → drop the line from the cart (cashier's shortcut for
    //       "I added this by mistake").
    //   • If the K-Slip has already been sent, Qty is locked — show
    //     a warning and revert. (Same rule applies for Dine-In,
    //     Delivery, and Take-Away because they all share this grid.)
    // ───────────────────────────────────────────────────────────
    private void OrderGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_cellEditReverting) return;            // guard against re-entry
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not ViewModels.OrderItemViewModel item) return;

        var header = e.Column?.Header?.ToString();
        if (e.EditingElement is not TextBox tb) return;

        // ── Qty column ─────────────────────────────────────────
        if (header == "Qty")
        {
            var raw = tb.Text?.Trim() ?? string.Empty;

            if (!int.TryParse(raw, out int newQty) || newQty < 0)
            {
                RevertCellEdit(e, tb, item.Quantity.ToString(),
                    "Please enter a valid whole-number quantity (0 or more).",
                    "Invalid Quantity");
                return;
            }

            // K-Slip already sent → cannot change qty anymore
            if (item.KitchenPrinted && newQty != item.Quantity)
            {
                RevertCellEdit(e, tb, item.Quantity.ToString(),
                    "You can't edit the quantity after the Kitchen Slip has been sent.",
                    "K-Slip Already Sent");
                return;
            }

            // Qty = 0 and K-Slip NOT yet sent → delete line after commit
            if (newQty == 0)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    if (DataContext is MainPOSViewModel vm && vm.OrderItems.Contains(item))
                    {
                        vm.OrderItems.Remove(item);
                        // Re-serial & recompute totals
                        for (int i = 0; i < vm.OrderItems.Count; i++)
                            vm.OrderItems[i].SerialNumber = i + 1;
                        vm.RecalculateTotals();
                    }
                });
                return;
            }

            // Valid qty change → recompute SubTotal / Discount / GST after commit
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (DataContext is MainPOSViewModel vm)
                    vm.RecalculateTotals();
            });
        }

        // ── Remarks column ─────────────────────────────────────
        // Remarks are free-form; but if the K-Slip has been sent
        // we still block edits so the kitchen slip and the bill
        // don't disagree.
        if (header == "Remarks" && item.KitchenPrinted)
        {
            RevertCellEdit(e, tb, item.Remarks ?? string.Empty,
                "You can't edit remarks after the Kitchen Slip has been sent.",
                "K-Slip Already Sent");
            return;
        }
    }

    private bool _cellEditReverting;

    private void RevertCellEdit(DataGridCellEditEndingEventArgs e, TextBox tb,
                                string originalText, string message, string title)
    {
        _cellEditReverting = true;
        try
        {
            tb.Text = originalText;
            e.Cancel = true;
            OrderGrid.CancelEdit(DataGridEditingUnit.Cell);
            OrderGrid.CancelEdit(DataGridEditingUnit.Row);
            System.Windows.MessageBox.Show(message, title,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        finally
        {
            _cellEditReverting = false;
        }
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
        if (DataContext is not MainPOSViewModel vm) return;

        // ── Down arrow: navigate dropdown suggestions ──
        if (e.Key == Key.Down && vm.IsPhoneSearchActive && vm.PhoneSearchResults.Count > 0)
        {
            vm.SelectedPhoneSearchIndex = Math.Min(vm.SelectedPhoneSearchIndex + 1, vm.PhoneSearchResults.Count - 1);
            PhoneSearchListBox.ScrollIntoView(vm.PhoneSearchResults[vm.SelectedPhoneSearchIndex]);
            e.Handled = true;
            return;
        }

        // ── Up arrow: navigate dropdown suggestions (or exit to grid) ──
        if (e.Key == Key.Up)
        {
            if (vm.IsPhoneSearchActive && vm.PhoneSearchResults.Count > 0 && vm.SelectedPhoneSearchIndex > 0)
            {
                vm.SelectedPhoneSearchIndex--;
                PhoneSearchListBox.ScrollIntoView(vm.PhoneSearchResults[vm.SelectedPhoneSearchIndex]);
                e.Handled = true;
                return;
            }
            else if (vm.IsPhoneSearchActive && vm.SelectedPhoneSearchIndex == 0)
            {
                vm.SelectedPhoneSearchIndex = -1;
                PhoneSearchListBox.ScrollIntoView(vm.PhoneSearchResults[0]);
                e.Handled = true;
                return;
            }
            else if (!vm.IsPhoneSearchActive)
            {
                // Up from Mobile → Menu Items (grid is out of the nav flow).
                SetZone(PosZone.MenuItems);
                e.Handled = true;
                return;
            }
        }

        // ── Escape: close dropdown ──
        if (e.Key == Key.Escape && vm.IsPhoneSearchActive)
        {
            vm.IsPhoneSearchActive = false;
            vm.SelectedPhoneSearchIndex = -1;
            e.Handled = true;
            return;
        }

        // ── Enter ──
        if (e.Key == Key.Enter)
        {
            // If a dropdown item is highlighted, select it
            if (vm.IsPhoneSearchActive && vm.SelectedPhoneSearchIndex >= 0
                && vm.SelectedPhoneSearchIndex < vm.PhoneSearchResults.Count)
            {
                var selected = vm.PhoneSearchResults[vm.SelectedPhoneSearchIndex];
                await vm.SelectCustomerFromSearchCommand.ExecuteAsync(selected);
                vm.SelectedPhoneSearchIndex = -1;
                DiscPercentBox.Focus();
                DiscPercentBox.SelectAll();
                e.Handled = true;
                return;
            }

            // DineIn: Enter always goes directly to Disc (phone is optional)
            if (vm.SelectedOrderType == Domain.Enums.OrderType.DineIn)
            {
                if (!string.IsNullOrWhiteSpace(vm.CustomerPhone) && !vm.IsPhoneMatched)
                {
                    // Phone entered but not matched — open Add Customer form
                    await vm.PhoneEnterPressedCommand.ExecuteAsync(null);
                }
                else if (vm.IsPhoneMatched)
                {
                    await vm.PhoneEnterPressedCommand.ExecuteAsync(null);
                }

                DiscPercentBox.Focus();
                DiscPercentBox.SelectAll();
            }
            else
            {
                // Delivery/Takeaway: phone is required
                if (vm.IsPhoneMatched)
                {
                    // Phone matched — show customer detail/edit
                    await vm.PhoneEnterPressedCommand.ExecuteAsync(null);
                    DiscPercentBox.Focus();
                    DiscPercentBox.SelectAll();
                }
                else if (!string.IsNullOrWhiteSpace(vm.CustomerPhone))
                {
                    // Phone not matched — open Add Customer form
                    await vm.PhoneEnterPressedCommand.ExecuteAsync(null);
                    // After form closes, if customer was added, move focus
                    if (vm.IsPhoneMatched)
                    {
                        DiscPercentBox.Focus();
                        DiscPercentBox.SelectAll();
                    }
                }
            }
            e.Handled = true;
        }
    }

    private async void CustomerSearchItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Customer customer
            && DataContext is MainPOSViewModel vm)
        {
            await vm.SelectCustomerFromSearchCommand.ExecuteAsync(customer);
            DiscPercentBox.Focus();
            DiscPercentBox.SelectAll();
        }
    }

    private async void CustomerSearchListItem_Click(object sender, MouseButtonEventArgs e)
    {
        // Find the ListBoxItem that was clicked
        // Note: e.OriginalSource can be a Run (not a Visual), so walk up via LogicalTree first
        DependencyObject? current = e.OriginalSource as DependencyObject;
        if (current == null) return;

        // If it's not a Visual (e.g. Run inside TextBlock), get the parent TextBlock via LogicalTree
        while (current != null && current is not System.Windows.Media.Visual)
            current = LogicalTreeHelper.GetParent(current);

        // Now walk up the visual tree to find the ListBoxItem
        while (current != null && current is not ListBoxItem)
            current = VisualTreeHelper.GetParent(current);

        if (current is ListBoxItem lbi && lbi.DataContext is Customer customer
            && DataContext is MainPOSViewModel vm)
        {
            await vm.SelectCustomerFromSearchCommand.ExecuteAsync(customer);
            vm.SelectedPhoneSearchIndex = -1;
            DiscPercentBox.Focus();
            DiscPercentBox.SelectAll();
        }
    }
}
