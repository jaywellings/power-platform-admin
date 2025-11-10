using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PowerPlatformDashboard.Models;

namespace PowerPlatformDashboard.Services
{
    public interface ICustomerConfigService
    {
        List<CustomerConfig> GetAllCustomers();
        CustomerConfig GetCustomer(string customerId);
        CustomerConfig GetCustomerByTenantId(string tenantId);
        void AddCustomer(CustomerConfig customer);
        void UpdateCustomer(CustomerConfig customer);
        void DeleteCustomer(string customerId);
    }

    public class CustomerConfigService : ICustomerConfigService
    {
        private readonly string _configFilePath;
        private List<CustomerConfig> _customers;
        private readonly object _lock = new object();

        public CustomerConfigService(IConfiguration configuration)
        {
            _configFilePath = Path.Combine(Directory.GetCurrentDirectory(), "customers.json");
            LoadCustomers();
        }

        private void LoadCustomers()
        {
            lock (_lock)
            {
                if (File.Exists(_configFilePath))
                {
                    var json = File.ReadAllText(_configFilePath);
                    _customers = JsonSerializer.Deserialize<List<CustomerConfig>>(json) ?? new List<CustomerConfig>();
                }
                else
                {
                    _customers = new List<CustomerConfig>();
                    SaveCustomers();
                }
            }
        }

        private void SaveCustomers()
        {
            lock (_lock)
            {
                var json = JsonSerializer.Serialize(_customers, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
            }
        }

        public List<CustomerConfig> GetAllCustomers()
        {
            lock (_lock)
            {
                return _customers.ToList();
            }
        }

        public CustomerConfig GetCustomer(string customerId)
        {
            lock (_lock)
            {
                return _customers.FirstOrDefault(c => c.CustomerId == customerId);
            }
        }

        public CustomerConfig GetCustomerByTenantId(string tenantId)
        {
            lock (_lock)
            {
                return _customers.FirstOrDefault(c => c.TenantId == tenantId);
            }
        }

        public void AddCustomer(CustomerConfig customer)
        {
            lock (_lock)
            {
                customer.CustomerId = Guid.NewGuid().ToString();
                customer.OnboardedDate = DateTime.UtcNow;
                _customers.Add(customer);
                SaveCustomers();
            }
        }

        public void UpdateCustomer(CustomerConfig customer)
        {
            lock (_lock)
            {
                var existing = _customers.FirstOrDefault(c => c.CustomerId == customer.CustomerId);
                if (existing != null)
                {
                    existing.CustomerName = customer.CustomerName;
                    existing.TenantId = customer.TenantId;
                    existing.ClientId = customer.ClientId;
                    existing.ClientSecret = customer.ClientSecret;
                    existing.OnboardingStatus = customer.OnboardingStatus;
                    SaveCustomers();
                }
            }
        }

        public void DeleteCustomer(string customerId)
        {
            lock (_lock)
            {
                var customer = _customers.FirstOrDefault(c => c.CustomerId == customerId);
                if (customer != null)
                {
                    _customers.Remove(customer);
                    SaveCustomers();
                }
            }
        }
    }
}
