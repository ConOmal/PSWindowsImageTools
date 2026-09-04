using System;
using System.Collections.Generic;
using System.Linq;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class WindowsImageDriverServiceTests
    {
        private static WindowsImageDriverInfo MakeDriver(
            string published, string original, string provider, string version, bool inBox = false)
        {
            return new WindowsImageDriverInfo
            {
                PublishedName = published,
                OriginalFileName = original,
                ProviderName = provider,
                Version = version,
                InBox = inBox
            };
        }

        [Fact]
        public void Compare_DetectsAdded()
        {
            var reference = new List<WindowsImageDriverInfo>();
            var current = new List<WindowsImageDriverInfo> { MakeDriver("oem1.inf", "net.inf", "Acme", "1.0.0.0") };

            var result = new WindowsImageDriverService().Compare(reference, current);

            Assert.Single(result.Added);
            Assert.Equal("oem1.inf", result.Added[0].PublishedName);
            Assert.Empty(result.Removed);
        }

        [Fact]
        public void Compare_DetectsRemoved()
        {
            var reference = new List<WindowsImageDriverInfo> { MakeDriver("oem1.inf", "net.inf", "Acme", "1.0.0.0") };
            var current = new List<WindowsImageDriverInfo>();

            var result = new WindowsImageDriverService().Compare(reference, current);

            Assert.Single(result.Removed);
            Assert.Empty(result.Added);
        }

        [Fact]
        public void Compare_DetectsSuperseded_SameOriginalFileNameAndProvider_HigherVersion()
        {
            var reference = new List<WindowsImageDriverInfo> { MakeDriver("oem1.inf", "net.inf", "Acme", "1.0.0.0") };
            var current = new List<WindowsImageDriverInfo> { MakeDriver("oem2.inf", "net.inf", "Acme", "2.0.0.0") };

            var result = new WindowsImageDriverService().Compare(reference, current);

            Assert.Single(result.Superseded);
            Assert.Equal("oem2.inf", result.Superseded[0].PublishedName);
        }

        [Fact]
        public void Compare_DetectsDuplicateOem_SameOriginalFileNameAndProvider_SamePublishedNameSet_DifferentEntries()
        {
            var driverA = MakeDriver("oem1.inf", "net.inf", "Acme", "1.0.0.0");
            var driverB = MakeDriver("oem2.inf", "net.inf", "Acme", "1.0.0.0");
            var current = new List<WindowsImageDriverInfo> { driverA, driverB };

            var result = new WindowsImageDriverService().Compare(new List<WindowsImageDriverInfo>(), current);

            Assert.Equal(2, result.DuplicateOem.Count);
        }

        [Fact]
        public void Compare_IdenticalLists_ReportsNoDifferences()
        {
            var reference = new List<WindowsImageDriverInfo> { MakeDriver("oem1.inf", "net.inf", "Acme", "1.0.0.0") };
            var current = new List<WindowsImageDriverInfo> { MakeDriver("oem1.inf", "net.inf", "Acme", "1.0.0.0") };

            var result = new WindowsImageDriverService().Compare(reference, current);

            Assert.Empty(result.Added);
            Assert.Empty(result.Removed);
            Assert.Empty(result.Superseded);
            Assert.Empty(result.DuplicateOem);
        }
    }
}
