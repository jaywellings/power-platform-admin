using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using PowerPlatformDashboard.Models;
using PowerPlatformDashboard.Services;

namespace PowerPlatformDashboard.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ConsentController : ControllerBase
    {
        private readonly ICustomerConfigService _customerConfigService;
        private readonly IConfiguration _configuration;

        public ConsentController(ICustomerConfigService customerConfigService, IConfiguration configuration)
        {
            _customerConfigService = customerConfigService;
            _configuration = configuration;
        }

        [HttpGet("callback")]
        public IActionResult Callback(
            [FromQuery] string tenant, 
            [FromQuery] string state,
            [FromQuery] string admin_consent,
            [FromQuery] string error,
            [FromQuery] string error_description)
        {
            // Handle consent errors
            if (!string.IsNullOrEmpty(error))
            {
                return Redirect($"/onboarding-complete?success=false&error={error}&description={error_description}");
            }

            // Check if admin consented
            if (admin_consent != "True")
            {
                return Redirect("/onboarding-complete?success=false&error=consent_declined");
            }

            try
            {
                // Check if customer already exists
                var existingCustomer = _customerConfigService.GetCustomerByTenantId(tenant);
                
                if (existingCustomer != null)
                {
                    // Update status to Active
                    existingCustomer.OnboardingStatus = "Active";
                    _customerConfigService.UpdateCustomer(existingCustomer);
                }
                else
                {
                    // Create new customer record
                    var customer = new CustomerConfig
                    {
                        CustomerName = state ?? "New Customer",
                        TenantId = tenant,
                        ClientId = _configuration["MultiTenantApp:ClientId"],
                        ClientSecret = _configuration["MultiTenantApp:ClientSecret"],
                        OnboardingStatus = "Active"
                    };
                    
                    _customerConfigService.AddCustomer(customer);
                }

                return Redirect("/onboarding-complete?success=true");
            }
            catch (Exception ex)
            {
                return Redirect($"/onboarding-complete?success=false&error=processing_error&description={ex.Message}");
            }
        }
    }
}