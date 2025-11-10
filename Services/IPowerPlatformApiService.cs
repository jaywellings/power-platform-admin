using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
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

        // API versions & base URLs
        private const string BapApiVersion = "2020-10-01"; // supports $expand=properties.capacity
        private const string BapBase = "https://api.bap.microsoft.com";
        private const string PpBase = "https://api.powerplatform.com";
        private const string PpApiPreview = "2022-03-01-preview"; // Power Pages + environment ops

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


				var jay = await GetBAPEnvironmentsAsync(httpBap);


				var ppEnvironments = await GetEnvironmentManagementEnvironmentsAsync(httpPp);
								


                // Fetch environments early as others depend on IDs
				//var environmentsTask = GetEnvironmentsAsync(httpBap);
				//await environmentsTask; // ensure we have IDs for downstream

				// Dependent parallel calls
				//var capacityTask = GetCapacityDataAsync(httpBap);
				//var scanReportsTask = GetPowerPagesScanReportsAsync(httpPp, environmentsTask);

				// Example: pull operations for each environment (preview)
				//var opsTask = GetAllEnvironmentOperationsAsync(httpPp, environmentsTask);

				//await Task.WhenAll(capacityTask, scanReportsTask, opsTask);

				//dashboardData.Environments = environmentsTask.Result;
				//dashboardData.CapacityData = capacityTask.Result;
				//dashboardData.ScanReports = scanReportsTask.Result;

				// If your DashboardData has a slot for operations, assign it; otherwise drop or adapt
				if (dashboardData.EnvironmentOperations == null)
					dashboardData.EnvironmentOperations = new List<EnvironmentOperation>();
                //dashboardData.EnvironmentOperations.AddRange(opsTask.Result);
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

    var result = await app.AcquireTokenForClient(new[] { scope })  // Use the parameter!
        .ExecuteAsync();

    Console.WriteLine($"[DEBUG] Requested Scope: {scope}");
    Console.WriteLine($"[DEBUG] Token Scopes in result: {string.Join(", ", result.Scopes)}");

    return result.AccessToken;
}

        // -----------------------------
        // Environments (BAP Admin API)
        // -----------------------------
        private async Task<List<EnvironmentInfo>> GetEnvironmentsAsync(HttpClient httpClient)
        {
            var url = $"{BapBase}/providers/Microsoft.BusinessAppPlatform/scopes/admin/environments?api-version={BapApiVersion}&$expand=properties.capacity,properties.addons";

            try
            {
                Console.WriteLine($"[DEBUG] Calling: {url}");
                var response = await httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"[DEBUG] Status: {response.StatusCode}; Length: {content.Length}");
                response.EnsureSuccessStatusCode();

                using var json = JsonDocument.Parse(content);
                var environments = new List<EnvironmentInfo>();

                if (json.RootElement.TryGetProperty("value", out var valueArray))
                {
                    Console.WriteLine($"[DEBUG] Found {valueArray.GetArrayLength()} environments");
                    foreach (var env in valueArray.EnumerateArray())
                    {
                        var name = env.GetProperty("name").GetString();
                        var props = env.GetProperty("properties");

                        environments.Add(new EnvironmentInfo
                        {
                            Id = name,
                            Name = name,
                            DisplayName = props.TryGetProperty("displayName", out var dn) ? dn.GetString() : "N/A",
                            Type = props.TryGetProperty("environmentSku", out var et) ? et.GetString() : "Unknown",
                            Region = props.TryGetProperty("azureRegion", out var loc) ? loc.GetString() : "Unknown",
                            State = props.TryGetProperty("provisioningState", out var ps) ? ps.GetString() : "Unknown",
                            CreatedTime = props.TryGetProperty("createdTime", out var ct) ? ct.GetDateTime() : DateTime.MinValue,
                            // Optionally store raw capacity JSON for later parsing
                            CapacityJson = props.TryGetProperty("capacity", out var cap) ? cap.ToString() : null
                        });
                    }
                }

                return environments;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Environments exception: {ex.Message}");
                return new List<EnvironmentInfo>();
            }
        }

        // -----------------------------
        // Capacity (derived via $expand)
        // -----------------------------
        private async Task<List<CapacityInfo>> GetCapacityDataAsync(HttpClient httpClient)
        {
            var url = $"{BapBase}/providers/Microsoft.BusinessAppPlatform/scopes/admin/environments?api-version={BapApiVersion}&$expand=properties.capacity";

            try
            {
                var resp = await httpClient.GetAsync(url);
                var content = await resp.Content.ReadAsStringAsync();
                resp.EnsureSuccessStatusCode();

                var result = new List<CapacityInfo>();
                using var json = JsonDocument.Parse(content);

                if (!json.RootElement.TryGetProperty("value", out var items))
                    return result;

                foreach (var env in items.EnumerateArray())
                {
                    var envName = env.GetProperty("name").GetString();
                    if (!env.GetProperty("properties").TryGetProperty("capacity", out var cap))
                        continue;

                    // Common capacity buckets: database, file, log (schema can evolve)
                    result.Add(new CapacityInfo
                    {
                        EnvironmentName = envName,
                        DatabaseConsumedGb = cap.TryGetProperty("database", out var db) && db.TryGetProperty("consumed", out var dbc) ? dbc.GetDouble() : 0,
                        DatabaseAllocatedGb = cap.TryGetProperty("database", out db) && db.TryGetProperty("allocated", out var dba) ? dba.GetDouble() : 0,
                        FileConsumedGb = cap.TryGetProperty("file", out var file) && file.TryGetProperty("consumed", out var fc) ? fc.GetDouble() : 0,
                        FileAllocatedGb = cap.TryGetProperty("file", out file) && file.TryGetProperty("allocated", out var fa) ? fa.GetDouble() : 0,
                        LogConsumedGb = cap.TryGetProperty("log", out var log) && log.TryGetProperty("consumed", out var lc) ? lc.GetDouble() : 0,
                        LogAllocatedGb = cap.TryGetProperty("log", out log) && log.TryGetProperty("allocated", out var la) ? la.GetDouble() : 0
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Capacity exception: {ex.Message}");
                return new List<CapacityInfo>();
            }
        }

        // -------------------------------------------------------
        // Power Pages security scans (preview; per environment)
        // -------------------------------------------------------
        private async Task<List<PowerPagesScanReport>> GetPowerPagesScanReportsAsync(
            HttpClient httpPp,
            Task<List<EnvironmentInfo>> environmentsTask)
        {
            var envs = await environmentsTask;
            var allReports = new List<PowerPagesScanReport>();

            foreach (var env in envs)
            {
                // List websites
                var sitesUrl = $"{PpBase}/powerpages/environments/{env.Id}/websites?api-version={PpApiPreview}";
                var sitesResp = await httpPp.GetAsync(sitesUrl);
                if (!sitesResp.IsSuccessStatusCode)
                    continue;

                using var sitesJson = JsonDocument.Parse(await sitesResp.Content.ReadAsStringAsync());
                if (!sitesJson.RootElement.TryGetProperty("value", out var websites))
                    continue;

                foreach (var site in websites.EnumerateArray())
                {
                    var siteId = site.GetProperty("id").GetString();
                    var scanUrl = $"{PpBase}/powerpages/environments/{env.Id}/websites/{siteId}/scan/deep/getLatestCompletedReport?api-version={PpApiPreview}";

                    var scanResp = await httpPp.GetAsync(scanUrl);
                    if (!scanResp.IsSuccessStatusCode)
                        continue;

                    using var scanJson = JsonDocument.Parse(await scanResp.Content.ReadAsStringAsync());

                    allReports.Add(new PowerPagesScanReport
                    {
                        EnvironmentId = env.Id,
                        WebsiteId = siteId,
                        Score = scanJson.RootElement.TryGetProperty("score", out var sc) ? (sc.ValueKind == JsonValueKind.Number ? sc.GetDouble() : 0) : 0,
                        FindingsCount = scanJson.RootElement.TryGetProperty("issuesCount", out var ic) ? (ic.ValueKind == JsonValueKind.Number ? ic.GetInt32() : 0) : 0,
                        CompletedOn = scanJson.RootElement.TryGetProperty("completedOn", out var co) && co.ValueKind == JsonValueKind.String ? DateTime.Parse(co.GetString()) : DateTime.MinValue,
                        RawJson = scanJson.RootElement.ToString()
                    });
                }
            }

            return allReports;
        }

        // -------------------------------------------------------
        // Environment operations (preview) — per environment
        // -------------------------------------------------------
        private async Task<List<EnvironmentOperation>> GetEnvironmentOperationsAsync(
            HttpClient httpPp,
            string environmentId)
        {
            var url = $"{PpBase}/environmentmanagement/environments/{environmentId}/operations?api-version={PpApiPreview}";

            var response = await httpPp.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[DEBUG] Operations fetch failed for {environmentId}: {response.StatusCode} {content}");
                return new List<EnvironmentOperation>();
            }

            var results = new List<EnvironmentOperation>();
            using var doc = JsonDocument.Parse(content);

            if (doc.RootElement.TryGetProperty("value", out var ops))
            {
                foreach (var op in ops.EnumerateArray())
                {
                    var props = op.GetProperty("properties");
                    results.Add(new EnvironmentOperation
                    {
                        OperationId = op.TryGetProperty("name", out var n) ? n.GetString() : null,
                        EnvironmentId = environmentId,
                        Type = props.TryGetProperty("operationType", out var ot) ? ot.GetString() : null,
                        State = props.TryGetProperty("state", out var st) ? st.GetString() : null,
                        Started = props.TryGetProperty("startTime", out var sTime) && sTime.ValueKind == JsonValueKind.String ? DateTime.Parse(sTime.GetString()) : DateTime.MinValue,
                        Completed = props.TryGetProperty("endTime", out var eTime) && eTime.ValueKind == JsonValueKind.String ? DateTime.Parse(eTime.GetString()) : DateTime.MinValue,
                        RawJson = op.ToString()
                    });
                }
            }

            return results;
        }

private async Task<List<EnvironmentInfo>> GetBAPEnvironmentsAsync(HttpClient httpClient)
{
    var url = "https://api.bap.microsoft.com/providers/Microsoft.BusinessAppPlatform/scopes/admin/environments?api-version=2021-04-01";
    
    try
    {
        Console.WriteLine($"[DEBUG] Calling BAP API: {url}");
        var response = await httpClient.GetAsync(url);
        
        Console.WriteLine($"[DEBUG] BAP Status: {response.StatusCode}");
        
        var content = await response.Content.ReadAsStringAsync();
        
        // OUTPUT THE FULL RESPONSE
        Console.WriteLine($"[DEBUG] Full Response:");
        Console.WriteLine(content);
        Console.WriteLine($"[DEBUG] ==================");
        
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[DEBUG] BAP Error: {content}");
            return new List<EnvironmentInfo>();
        }
        
        using var json = JsonDocument.Parse(content);
        var environments = new List<EnvironmentInfo>();
        
        if (!json.RootElement.TryGetProperty("value", out var valueArray))
        {
            Console.WriteLine("[DEBUG] No 'value' property found");
            return environments;
        }
        
        Console.WriteLine($"[DEBUG] Found {valueArray.GetArrayLength()} environments");
        
        foreach (var env in valueArray.EnumerateArray())
        {
            var props = env.GetProperty("properties");
            
            var envInfo = new EnvironmentInfo
            {
                Id = env.GetProperty("name").GetString(),
                Name = env.GetProperty("name").GetString(),
                DisplayName = props.TryGetProperty("displayName", out var dn) ? 
                    dn.GetString() : "N/A",
                Type = props.TryGetProperty("environmentSku", out var sku) ? 
                    sku.GetString() : "Unknown",
                Region = props.TryGetProperty("azureRegion", out var reg) ? 
                    reg.GetString() : "Unknown",
                State = props.TryGetProperty("provisioningState", out var state) ? 
                    state.GetString() : "Unknown",
                CreatedTime = props.TryGetProperty("createdTime", out var ct) ? 
                    ct.GetDateTime() : DateTime.MinValue
            };
            
            Console.WriteLine($"[DEBUG] Environment: {envInfo.DisplayName} ({envInfo.Type})");
            
            environments.Add(envInfo);
        }
        
        Console.WriteLine($"[DEBUG] Returning {environments.Count} environments");
        return environments;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DEBUG] Exception: {ex.Message}");
        Console.WriteLine($"[DEBUG] Stack: {ex.StackTrace}");
        return new List<EnvironmentInfo>();
    }
}
		private async Task<List<EnvironmentInfo>> GetEnvironmentManagementEnvironmentsAsync(HttpClient httpPp)
		{
			var url = "https://api.powerplatform.com/environmentmanagement/environments?api-version=2022-03-01-preview";
			var resp = await httpPp.GetAsync(url);
			var body = await resp.Content.ReadAsStringAsync();
			Console.WriteLine($"[DEBUG] PP envs status={resp.StatusCode} body={body}");
			if (!resp.IsSuccessStatusCode) return new();
			using var doc = JsonDocument.Parse(body);
			var list = new List<EnvironmentInfo>();
			if (!doc.RootElement.TryGetProperty("value", out var items)) return list;

			foreach (var env in items.EnumerateArray())
			{
				var props = env.GetProperty("properties");
				list.Add(new EnvironmentInfo
				{
					Id = env.GetProperty("name").GetString(),
					Name = props.TryGetProperty("uniqueName", out var un) ? un.GetString() : null,
					DisplayName = props.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
					Region = props.TryGetProperty("azureRegion", out var reg) ? reg.GetString() : null,
					Type = props.TryGetProperty("environmentSku", out var sku) ? sku.GetString() : null,
					State = props.TryGetProperty("provisioningState", out var st) ? st.GetString() : null
				});
			}
			return list;
		}


		private async Task<List<EnvironmentOperation>> GetAllEnvironmentOperationsAsync(
				HttpClient httpPp,
				Task<List<EnvironmentInfo>> environmentsTask)
		{
			var envs = await environmentsTask;
			var all = new List<EnvironmentOperation>();

			foreach (var env in envs)
			{
				var ops = await GetEnvironmentOperationsAsync(httpPp, env.Id);
				if (ops?.Any() == true)
					all.AddRange(ops);
			}

			return all;
		}
    }
}
