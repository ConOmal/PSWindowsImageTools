using System.Collections.Generic;
using PSWindowsImageTools.Models;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class HealthCheckModelsTests
    {
        [Fact]
        public void OverallHealth_NoFindings_IsHealthy()
        {
            var report = new HealthCheckReport();
            Assert.Equal(HealthStatus.Healthy, report.OverallHealth);
        }

        [Fact]
        public void OverallHealth_OnlyWarningFindings_IsWarning()
        {
            var report = new HealthCheckReport
            {
                Findings = new List<HealthFinding>
                {
                    new HealthFinding { Category = "MissingRegistryHive", Severity = HealthStatus.Warning, Message = "SYSTEM hive missing" }
                }
            };

            Assert.Equal(HealthStatus.Warning, report.OverallHealth);
        }

        [Fact]
        public void OverallHealth_AnyCorruptionFinding_IsUnhealthy()
        {
            var report = new HealthCheckReport
            {
                Findings = new List<HealthFinding>
                {
                    new HealthFinding { Category = "MissingRegistryHive", Severity = HealthStatus.Warning, Message = "SYSTEM hive missing" },
                    new HealthFinding { Category = "Corruption", Severity = HealthStatus.Unhealthy, Message = "Component store repairable" }
                }
            };

            Assert.Equal(HealthStatus.Unhealthy, report.OverallHealth);
        }
    }
}
