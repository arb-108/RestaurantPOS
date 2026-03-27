using RestaurantPOS.Domain.Entities;

namespace RestaurantPOS.Application.Interfaces;

public interface ICustomerService
{
    Task<Customer?> GetByPhoneAsync(string phone);
    Task<Customer> CreateCustomerAsync(string name, string phone, string? email = null, string? address = null);
    Task<IEnumerable<Customer>> SearchCustomersAsync(string query);
    Task<IEnumerable<Customer>> GetAllCustomersAsync();
    Task<Customer?> GetByIdWithOrdersAsync(int id);
    Task UpdateCustomerAsync(Customer customer);
    Task DeleteCustomerAsync(int id);
}
