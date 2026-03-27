using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Infrastructure.Data;

namespace RestaurantPOS.Infrastructure.Services;

public class CustomerService : ICustomerService
{
    private readonly PosDbContext _db;

    public CustomerService(PosDbContext db) => _db = db;

    public async Task<Customer?> GetByPhoneAsync(string phone)
    {
        return await _db.Customers
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Phone == phone);
    }

    public async Task<Customer> CreateCustomerAsync(string name, string phone, string? email = null, string? address = null)
    {
        var customer = new Customer
        {
            Name = name,
            Phone = phone,
            Email = email
        };

        if (!string.IsNullOrWhiteSpace(address))
        {
            customer.Addresses.Add(new CustomerAddress
            {
                Label = "Primary",
                AddressLine1 = address,
                IsDefault = true
            });
        }

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        return customer;
    }

    public async Task<IEnumerable<Customer>> SearchCustomersAsync(string query)
    {
        var lower = query.ToLowerInvariant();
        return await _db.Customers
            .Include(c => c.Addresses)
            .Where(c => c.Phone.Contains(lower) || c.Name.ToLower().Contains(lower))
            .Take(10)
            .ToListAsync();
    }

    public async Task<IEnumerable<Customer>> GetAllCustomersAsync()
    {
        return await _db.Customers
            .Include(c => c.Addresses)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<Customer?> GetByIdWithOrdersAsync(int id)
    {
        return await _db.Customers
            .Include(c => c.Addresses)
            .Include(c => c.Orders.OrderByDescending(o => o.CreatedAt).Take(50))
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task UpdateCustomerAsync(Customer customer)
    {
        _db.Customers.Update(customer);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteCustomerAsync(int id)
    {
        var customer = await _db.Customers.FindAsync(id);
        if (customer != null)
        {
            _db.Customers.Remove(customer);
            await _db.SaveChangesAsync();
        }
    }
}
