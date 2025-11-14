// Services/IPowerPlatformApiService.cs
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using PowerPlatformDashboard.Models;

namespace PowerPlatformDashboard.Services
{
    public interface IPowerPlatformApiService
    {
        Task<DashboardData> GetDashboardDataAsync(CustomerConfig customer);
    }

	public class PowerPlatformApiService : IPowerPlatformApiService
	{
		private readonly IHttpClientFactory _httpClientFactory;

		// Scopes per resource host
		private const string BapScope = "https://api.bap.microsoft.com/.default";
		private const string PowerPlatformScope = "https://api.powerplatform.com/.default";

		public PowerPlatformApiService(IHttpClientFactory httpClientFactory)
		{
			_httpClientFactory = httpClientFactory;
		}

		public async Task<DashboardData> GetDashboardDataAsync(CustomerConfig customer)
		{
			var dashboardData = new DashboardData { IsLoading = true };

			try
			{
				// Acquire tokens per audience
				var bapToken = await GetAccessTokenAsync(customer, BapScope);
				var ppToken = await GetAccessTokenAsync(customer, PowerPlatformScope);

				// Clients per host
				var httpBap = _httpClientFactory.CreateClient();
				httpBap.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bapToken);

				var httpPp = _httpClientFactory.CreateClient();
				httpPp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ppToken);

				// Get data
				dashboardData.Environments = await GetBAPEnvironmentsAsync(httpBap);
				dashboardData.StorageWarnings = await GetStorageWarningsAsync(httpPp);
				dashboardData.AdvisorRecommendations = await GetAdvisorRecommendationsAsync(httpPp);
			}
			catch (Exception ex)
			{
				dashboardData.ErrorMessage = $"Error loading data: {ex.Message}";
				Console.WriteLine($"[ERROR] {ex}");
			}
			finally
			{
				dashboardData.IsLoading = false;
			}

			return dashboardData;
		}

		private async Task<string> GetAccessTokenAsync(CustomerConfig customer, string scope)
		{
			var app = ConfidentialClientApplicationBuilder
					.Create(customer.ClientId)
					.WithClientSecret(customer.ClientSecret)
					.WithAuthority($"https://login.microsoftonline.com/{customer.TenantId}")
					.Build();

			var result = await app.AcquireTokenForClient(new[] { scope })
					.ExecuteAsync();

			Console.WriteLine($"[DEBUG] Requested Scope: {scope}");
			Console.WriteLine($"[DEBUG] Token Scopes: {string.Join(", ", result.Scopes)}");

			return result.AccessToken;
		}

		private async Task<List<EnvironmentInfo>> GetBAPEnvironmentsAsync(HttpClient httpClient)
		{
			var url = "https://api.bap.microsoft.com/providers/Microsoft.BusinessAppPlatform/scopes/admin/environments?api-version=2021-04-01";

			try
			{
				Console.WriteLine($"[DEBUG] Calling BAP API: {url}");
				var response = await httpClient.GetAsync(url);
				var content = await response.Content.ReadAsStringAsync();

				Console.WriteLine($"[DEBUG] BAP Status: {response.StatusCode}");

				if (!response.IsSuccessStatusCode)
				{
					Console.WriteLine($"[DEBUG] BAP Error: {content}");
					return new List<EnvironmentInfo>();
				}

				var jsonResponse = JsonSerializer.Deserialize<BapEnvironmentResponse>(content);
				var environments = new List<EnvironmentInfo>();

				if (jsonResponse?.Value != null)
				{
					Console.WriteLine($"[DEBUG] Found {jsonResponse.Value.Count} environments");

					foreach (var env in jsonResponse.Value)
					{
						var envInfo = new EnvironmentInfo
						{
							Id = env.Name,
							Name = env.Name,
							DisplayName = env.Properties?.DisplayName ?? "N/A",
							Type = env.Properties?.EnvironmentSku ?? "Unknown",
							Region = env.Properties?.AzureRegion ?? "Unknown",
							State = env.Properties?.ProvisioningState ?? "Unknown",
							CreatedTime = env.Properties?.CreatedTime ?? DateTime.MinValue
						};

						Console.WriteLine($"[DEBUG] Environment: {envInfo.DisplayName} ({envInfo.Type})");
						environments.Add(envInfo);
					}
				}

				return environments;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[DEBUG] Exception: {ex.Message}");
				return new List<EnvironmentInfo>();
			}
		}

		private async Task<List<StorageWarning>> GetStorageWarningsAsync(HttpClient httpClient)
		{
			var url = "https://api.powerplatform.com/licensing/storageWarning/getAllStorageWarnings?api-version=2022-03-01-preview";

			try
			{
				Console.WriteLine($"[DEBUG] Calling Storage Warnings API: {url}");
				var response = await httpClient.GetAsync(url);
				var content = await response.Content.ReadAsStringAsync();

				Console.WriteLine($"[DEBUG] Storage Warnings Status: {response.StatusCode}");

				if (!response.IsSuccessStatusCode)
				{
					Console.WriteLine($"[DEBUG] Storage Warnings Error: {content}");
					return new List<StorageWarning>();
				}

				// Deserialize directly to strongly-typed list
				var warnings = JsonSerializer.Deserialize<List<StorageWarning>>(content);

				Console.WriteLine($"[DEBUG] Found {warnings?.Count ?? 0} storage warnings");

				if (warnings != null)
				{
					// Pretty print
					var prettyJson = JsonSerializer.Serialize(warnings, new JsonSerializerOptions { WriteIndented = true });
					Console.WriteLine("[DEBUG] Pretty JSON Response:");
					Console.WriteLine(prettyJson);
					Console.WriteLine("[DEBUG] ==================");

					foreach (var warning in warnings)
					{
						Console.WriteLine($"[DEBUG] Warning: {warning.StorageEntity} ({warning.StorageCategory}) - Active: {warning.IsActive}");
					}
				}

				return warnings ?? new List<StorageWarning>();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[DEBUG] Storage Warnings Exception: {ex.Message}");
				return new List<StorageWarning>();
			}
		}

private async Task<List<AdvisorRecommendation>> GetAdvisorRecommendationsAsync(HttpClient httpClient)
{
    // Docs: GET https://api.powerplatform.com/analytics/advisorRecommendations?api-version=2022-03-01-preview
    // Supports paging via nextLink
    var baseUrl = "https://api.powerplatform.com/analytics/advisorRecommendations?api-version=2024-10-01";
    var results = new List<AdvisorRecommendation>();
    var url = baseUrl;

    try
    {
        do
        {
            Console.WriteLine($"[DEBUG] Calling Advisor Recommendations API: {url}");
            var response = await httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[DEBUG] Advisor Recommendations Status: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[DEBUG] Advisor Recommendations Error: {content}");
                break; // return what we have
            }

            var page = JsonSerializer.Deserialize<AdvisorRecommendationPage>(content);
            if (page?.Value != null && page.Value.Count > 0)
            {
                results.AddRange(page.Value);
                Console.WriteLine($"[DEBUG] Retrieved {page.Value.Count} recommendations (total {results.Count}).");
            }

            url = page?.NextLink; // continue if server provided a nextLink
        }
        while (!string.IsNullOrEmpty(url));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DEBUG] Advisor Recommendations Exception: {ex.Message}");
    }

    return results;
}

	}
		
		

    // Helper classes for BAP API deserialization
    public class BapEnvironmentResponse
    {
        [JsonPropertyName("value")]
        public List<BapEnvironment> Value { get; set; }
    }

    public class BapEnvironment
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("properties")]
        public BapEnvironmentProperties Properties { get; set; }
    }

	public class BapEnvironmentProperties
	{
		[JsonPropertyName("displayName")]
		public string DisplayName { get; set; }

		[JsonPropertyName("environmentSku")]
		public string EnvironmentSku { get; set; }

		[JsonPropertyName("azureRegion")]
		public string AzureRegion { get; set; }

		[JsonPropertyName("provisioningState")]
		public string ProvisioningState { get; set; }

		[JsonPropertyName("createdTime")]
		public DateTime? CreatedTime { get; set; }
	}

}