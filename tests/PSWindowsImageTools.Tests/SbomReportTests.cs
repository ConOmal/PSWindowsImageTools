using System;
using System.Collections.Generic;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class SbomReportTests
    {
        [Fact]
        public void BuildSbom_MapsSnapshotFieldsToSbomReport()
        {
            var snapshot = new ImageSnapshot
            {
                ImageName = "Windows 11 Pro",
                ImagePath = @"C:\images\install.wim",
                Packages = new List<SnapshotItem> { new SnapshotItem { Name = "Package-A" } },
                Drivers = new List<SnapshotItem> { new SnapshotItem { Name = "net.inf" } },
                Features = new List<SnapshotItem> { new SnapshotItem { Name = "Feature-1" } },
                Capabilities = new List<SnapshotItem> { new SnapshotItem { Name = "Cap.X" } },
                Software = new List<SnapshotItem> { new SnapshotItem { Name = "Tool" } }
            };

            var sbom = new ImageComparisonService().BuildSbom(snapshot);

            Assert.Equal("Windows 11 Pro", sbom.ImageName);
            Assert.Equal(@"C:\images\install.wim", sbom.ImagePath);
            Assert.Single(sbom.Packages);
            Assert.Single(sbom.Drivers);
            Assert.Single(sbom.Features);
            Assert.Single(sbom.Capabilities);
            Assert.Single(sbom.Applications);
        }
    }
}
