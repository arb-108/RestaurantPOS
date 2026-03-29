# KFC Restaurant POS System

A full-featured Point-of-Sale system for restaurants built with **.NET 10**, **WPF**, and **SQLite**, following **Clean Architecture** with the **MVVM** pattern.

> **Build Status:** 0 errors, 0 warnings — All 13 phases complete.

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| **Framework** | .NET 10.0 (SDK 10.0.103) |
| **UI** | WPF + MahApps.Metro |
| **MVVM** | CommunityToolkit.Mvvm |
| **Database** | SQLite + EF Core 10.0 + Dapper |
| **Logging** | Serilog (file sink) |
| **Auth** | BCrypt (passwords) + SHA256 (PINs) |
| **Encryption** | DPAPI via ProtectedData |
| **Printing** | ESC/POS (USB, Network, Bluetooth, Serial) |
| **DI** | Microsoft.Extensions.Hosting |

---

## Solution Structure

```
RestaurantPOS.slnx
├── src/
│   ├── RestaurantPOS.Domain/          (net10.0)        — Entities, Enums, Base abstractions
│   ├── RestaurantPOS.Application/     (net10.0)        — Service interfaces
│   ├── RestaurantPOS.Infrastructure/  (net10.0-windows) — DbContext, Services, Data access
│   ├── RestaurantPOS.Printing/        (net10.0-windows) — ESC/POS receipt & KOT printing
│   └── RestaurantPOS.WPF/            (net10.0-windows) — Views, ViewModels, App shell
```

---

## Domain Layer — 35+ Entities

### Authentication & Security
- **Role** — Admin, Manager, Cashier, Waiter, Kitchen
- **Permission** — 23 feature permissions with module grouping
- **RolePermission** — Role-permission mapping with AccessLevel (0–5)
- **User** — Credentials, phone, email, login tracking
- **UserSession** — Login sessions with terminal/IP tracking
- **AuditLog** — Action audit trail with before/after values

### Menu & Inventory
- **Category** — Hierarchical menu categories (parent/child)
- **MenuItem** — Items with pricing, tax, station, allergens, variants
- **MenuItemVariant** — Size/flavor variants with price overrides
- **ModifierGroup / Modifier** — Configurable modifiers (min/max selections)
- **Ingredient** — Stock items with reorder levels and cost
- **Recipe** — MenuItem-Ingredient mapping with quantities
- **StockMovement** — Inventory tracking (Purchase, Consumption, Waste, Adjustment)

### Orders & Payments
- **Order** — DineIn, TakeAway, Delivery with full financial totals
- **OrderItem** — Line items with quantity, price, modifiers
- **PaymentMethod** — Cash, Debit/Credit Card, JazzCash, EasyPaisa, Bank Transfer
- **Payment** — Payment transactions with change calculation

### Customers & Loyalty
- **Customer** — Profiles with phone, loyalty points, tier (Regular → Platinum)
- **CustomerAddress** — Multiple delivery addresses
- **LoyaltyTransaction** — Points earn/redeem/adjust/expire history

### Tables & Reservations
- **FloorPlan** — Restaurant layout sections
- **Table** — Tables with capacity, position, shape, status
- **TableSession** — Active table usage with waiter and guest count
- **Reservation** — Bookings with guest info, time, duration

### Kitchen
- **KitchenStation** — 7 stations (Main Kitchen, Grill, Pizza, Fry, Beverage, Dessert, Tandoor)
- **KitchenOrder** — Orders sent to kitchen with status and priority
- **KitchenOrderItem** — Individual items with cooking status

### Configuration & Operations
- **TaxRate** — GST 16%, FED 16%, 0%
- **Discount** — Percentage, fixed amount, buy-X-get-Y
- **Terminal / Printer** — POS terminal and printer configuration
- **AppSetting** — Key-value configuration pairs
- **Shift** — Cashier shifts with opening/closing balance
- **CashDrawerLog** — Drawer transactions (Sale, Refund, PayIn, PayOut, Tip)
- **DailySummary** — Daily sales by category, payment method, hour

### HR & Finance
- **Employee** — Staff records with CNIC, salary, category
- **Payroll** — Monthly payroll with salary components
- **Supplier** — Vendor info with outstanding balance
- **SupplierExpense** — Purchase invoices and expenses
- **Deal / DealItem** — Combo deals

---

## Application Layer — 8 Service Interfaces

| Interface | Responsibility |
|-----------|---------------|
| `IAuthService` | Login (username/password + PIN), permission checks |
| `IMenuService` | Category/item queries, search, barcode lookup |
| `IOrderService` | Order lifecycle — create, checkout, void, hold/recall |
| `ITableService` | Table & session management, status updates |
| `ICustomerService` | Customer CRUD, addresses, loyalty operations |
| `IKitchenService` | Kitchen order creation, status updates |
| `ISettingsService` | System settings, tax, discount, printers |
| `IReportService` | Daily summaries, sales analytics |

---

## Infrastructure Layer

### Services
All 8 service interfaces are fully implemented with EF Core and Dapper queries.

### Data Access
- **PosDbContext** — 30+ DbSets with model configuration, indexes, and seed data
- **DatabaseConfig** — Initialization and connection setup
- **SqlitePragmaInterceptor** — SQLite performance optimizations
- **EnsureCreated** fallback (no migrations)

---

## Printing Module

| Component | Description |
|-----------|------------|
| `PrintService` | ESC/POS commands via Windows API (`RawPrinterHelper`) |
| `ReceiptBuilder` | Formatted receipts for DineIn, TakeAway, Delivery |
| `KotBuilder` | Kitchen Order Tickets — large bold text, station routing |
| `EscPosCommands` | ESC/POS command constants |

Falls back to file output in `%LOCALAPPDATA%/RestaurantPOS/prints/` when no printer is configured.

---

## WPF UI Layer

### Views (14 main + 19 dialog windows)

| View | Purpose |
|------|---------|
| **LoginView** | Username/password or quick PIN login |
| **MainPOSView** | Primary selling interface — tables, items, cart, payment |
| **MenuManagementView** | Category and item CRUD |
| **OrderHistoryView** | Search and view past orders |
| **KitchenDisplayView** | Live kitchen order board |
| **CustomerManagementView** | Customer profiles and loyalty |
| **EmployeeManagementView** | Staff records |
| **SupplierManagementView** | Vendor and expense tracking |
| **TableManagementView** | Floor plan and table layout |
| **ShiftManagementView** | Open/close shifts, cash drawer |
| **StockManagementView** | Inventory levels and movements |
| **ExpenseManagementView** | Expense entry and tracking |
| **SettingsView** | 6-tab system configuration |
| **ReportsView** | Sales analytics and daily summaries |

### Dialog Windows
AddCustomer, AddDeal, AddEmployee, AddExpense, AddProduct, AddStockItem, AddSupplier, AddTable, CashDrawerEntry, CloseShift, DriverAssign, GeneratePayroll, InputDialog, ManageRecipe, OrderNote, OpenShift, PrintPreview, QuickSettle, UnPaidBill

### Navigation
ViewModel-first navigation via DataTemplates in `MainWindow.xaml`.

---

## Key Technical Patterns

| Pattern | Details |
|---------|---------|
| **Monetary values** | Stored as `long` (paisa); displayed via `PaisaToCurrencyConverter` |
| **Enums** | Stored as strings in SQLite (`HasConversion<string>()`) |
| **Namespace alias** | `RestaurantPOS.Application` vs `System.Windows.Application` → `AppInterfaces` alias |
| **Encryption** | DPAPI key at `%LOCALAPPDATA%/RestaurantPOS/poskey.dat` |
| **Auth** | BCrypt cost-12 passwords, SHA256 PINs, session tracking |

---

## Seed Data (out-of-box)

| Data | Count | Details |
|------|-------|---------|
| **Roles** | 5 | admin, manager, cashier, waiter, kitchen |
| **Permissions** | 23 | Grouped by module (General, Sales, Inventory, HR) |
| **Default Admin** | 1 | username: `admin`, password: `admin123`, PIN: `1234` |
| **Payment Methods** | 6 | Cash, Debit/Credit Card, JazzCash, EasyPaisa, Bank Transfer |
| **Tax Rates** | 3 | GST 16% (inclusive), GST 0%, FED 16% (exclusive) |
| **Kitchen Stations** | 7 | Main Kitchen, Grill, Pizza, Fry, Beverage, Dessert, Tandoor |
| **Floor Plan** | 1 | Main Hall |
| **Tables** | 11 | Tbl 1–6 (4-seat), Tbl 7–9 (6-seat), Family 1–2 (8-seat) |
| **Categories** | 10 | Burgers, Wraps, Wings, Fish, Sandwiches, Sides, Bread, Beverages, Desserts, Combos |
| **App Settings** | 15 | Restaurant name, currency (PKR), loyalty config, receipt text, etc. |

---

## Getting Started

### Prerequisites
- .NET 10.0 SDK (10.0.103+)
- Windows 10/11 (WPF requirement)

### Build & Run
```bash
dotnet build RestaurantPOS.slnx
dotnet run --project src/RestaurantPOS.WPF
```

### Default Login
- **Username:** `admin`
- **Password:** `admin123`
- **Quick PIN:** `1234`

The SQLite database is auto-created on first run with all seed data.

---

## Currency & Localization

- Default currency: **PKR (Rs.)**
- All monetary values stored in **paisa** (1 PKR = 100 paisa)
- Configurable via Settings → AppSettings

---

## License

Private — All rights reserved.
