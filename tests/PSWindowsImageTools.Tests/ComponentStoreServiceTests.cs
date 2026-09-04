using System.Collections.Generic;
using Microsoft.Dism;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class ComponentStoreServiceTests
    {
        [Fact]
        public void ClassifyPackages_CountsInstalledSupersededAndPending()
        {
            var report = new ComponentStoreReport();
            var packages = new List<(string Name, DismPackageFeatureState State)>
            {
                ("Package-A", DismPackageFeatureState.Installed),
                ("Package-B", DismPackageFeatureState.Superseded),
                ("Package-C", DismPackageFeatureState.InstallPending),
                ("Package-D", DismPackageFeatureState.UninstallPending),
                ("Package-E", DismPackageFeatureState.Installed),
            };

            ComponentStoreService.ClassifyPackages(packages, report);

            Assert.Equal(5, report.TotalPackages);
            Assert.Equal(2, report.InstalledPackages);
            Assert.Equal(1, report.SupersededPackages);
            Assert.Equal(2, report.PendingPackages);
            Assert.Equal(new[] { "Package-B" }, report.SupersededPackageNames);
        }
    }
}
