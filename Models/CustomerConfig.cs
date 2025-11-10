using System;
using System.Collections.Generic;

namespace PowerPlatformDashboard.Models
{
	   public class EnvironmentOperation
    {
        public string OperationId { get; set; }          // from item.name
        public string EnvironmentId { get; set; }        // env Id (GUID)
        public string Type { get; set; }                 // properties.operationType
        public string State { get; set; }                // properties.state
        public DateTime Started { get; set; }            // properties.startTime
        public DateTime Completed { get; set; }          // properties.endTime
        public string RawJson { get; set; }              // whole op payload (optional)
    }
    public class CustomerConfig
	{
		public string CustomerId { get; set; }
		public string CustomerName { get; set; }
		public string TenantId { get; set; }
		public string ClientId { get; set; }
		public string ClientSecret { get; set; }
		public DateTime OnboardedDate { get; set; }
		public string OnboardingStatus { get; set; } // "Pending", "Active"
	}

	public class EnvironmentInfo
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public string DisplayName { get; set; }
		public string Type { get; set; }
		public string Region { get; set; }
		public string State { get; set; }
		public DateTime CreatedTime { get; set; }
				        public string CapacityJson { get; set; }

    }

    public class CapacityInfo
    {

        public double DatabaseConsumedGb  { get; set; }
        public double DatabaseAllocatedGb { get; set; }

        public double FileConsumedGb      { get; set; }
        public double FileAllocatedGb     { get; set; }

        public double LogConsumedGb       { get; set; }
        public double LogAllocatedGb      { get; set; }
        public string EnvironmentId { get; set; }
        public string EnvironmentName { get; set; }
        public double DatabaseCapacityGB { get; set; }
        public double DatabaseUsedGB { get; set; }
        public double FileCapacityGB { get; set; }
        public double FileUsedGB { get; set; }
        public double LogCapacityGB { get; set; }
        public double LogUsedGB { get; set; }
        public int DatabasePercentUsed => DatabaseCapacityGB > 0 ? 
            (int)((DatabaseUsedGB / DatabaseCapacityGB) * 100) : 0;
        public int FilePercentUsed => FileCapacityGB > 0 ? 
            (int)((FileUsedGB / FileCapacityGB) * 100) : 0;
        public int LogPercentUsed => LogCapacityGB > 0 ? 
            (int)((LogUsedGB / LogCapacityGB) * 100) : 0;
    }

	public class PowerPagesScanReport
	{
		public string WebsiteId { get; set; }
		public string WebsiteName { get; set; }
		public DateTime? ScanDate { get; set; }
		public string Status { get; set; }
		public int CriticalIssues { get; set; }
		public int WarningIssues { get; set; }
		public int InfoIssues { get; set; }
				        public string EnvironmentId { get; set; }

        public double?   Score         { get; set; }
        public int?      FindingsCount { get; set; }
        public DateTime? CompletedOn   { get; set; }

        public string RawJson { get; set; }
    }

	public class DashboardData
	{
		public List<EnvironmentInfo> Environments { get; set; } = new();
		public List<CapacityInfo> CapacityData { get; set; } = new();
		public List<PowerPagesScanReport> ScanReports { get; set; } = new();
		public bool IsLoading { get; set; }
		public string ErrorMessage { get; set; }
				        public List<EnvironmentOperation> EnvironmentOperations { get; set; } = new();

    }
}
