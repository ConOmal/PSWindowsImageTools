using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class RegistryDriftServiceTests
    {
        private static RegistrySnapshotValue Val(string hive, string keyPath, string valueName, string valueType = "REG_SZ", string valueData = "")
        {
            return new RegistrySnapshotValue
            {
                Hive = hive,
                KeyPath = keyPath,
                ValueName = valueName,
                ValueType = valueType,
                ValueData = valueData
            };
        }

        private static RegistryDriftKeyDefinition Def(string hive, string keyPath, RegistryKeyCaptureMode mode = RegistryKeyCaptureMode.Values)
        {
            return new RegistryDriftKeyDefinition
            {
                Hive = hive,
                KeyPath = keyPath,
                Mode = mode
            };
        }

        [Fact]
        public void AppendCapture_ValuesMode_ProjectsValueEntries()
        {
            const string hive = "HKLM\\SOFTWARE";
            var definition = Def(hive, @"Microsoft\Windows\CurrentVersion\Run");
            var output = new List<RegistrySnapshotValue>();

            RegistryDriftService.AppendCapture(
                hive,
                definition,
                new[] { ("Agent", "REG_SZ", @"C:\agent.exe"), ("", "REG_SZ", "startup") },
                Array.Empty<string>(),
                output);

            Assert.Equal(2, output.Count);

            var agent = output.Single(v => v.ValueName == "Agent");
            Assert.Equal(hive, agent.Hive);
            Assert.Equal(@"Microsoft\Windows\CurrentVersion\Run", agent.KeyPath);
            Assert.Equal("REG_SZ", agent.ValueType);
            Assert.Equal(@"C:\agent.exe", agent.ValueData);
            Assert.Equal(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run\Agent", agent.FullPath);

            Assert.Contains(output, v => v.ValueName == "(Default)" && v.ValueData == "startup");
        }

        [Fact]
        public void AppendCapture_SubKeyNamesMode_ProjectsNameSignatures()
        {
            const string hive = "HKLM\\SYSTEM";
            var definition = Def(hive, @"ControlSet001\Services", RegistryKeyCaptureMode.SubKeyNames);
            var output = new List<RegistrySnapshotValue>();

            RegistryDriftService.AppendCapture(
                hive,
                definition,
                Array.Empty<(string, string, string)>(),
                new[] { "dhcp", "tcpip", "RpcSs" },
                output);

            Assert.Equal(3, output.Count);
            Assert.All(output, v =>
            {
                Assert.Equal("SubKey", v.ValueType);
                Assert.Equal(string.Empty, v.ValueData);
            });

            var paths = output.Select(v => v.FullPath).ToArray();
            Assert.Equal(paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase), paths);
        }

        [Fact]
        public void AppendCapture_SortsByFullPath()
        {
            const string hive = "HKLM\\SOFTWARE";
            var definition = Def(hive, @"Microsoft\Windows\CurrentVersion\RunOnce");
            var output = new List<RegistrySnapshotValue>();

            RegistryDriftService.AppendCapture(
                hive,
                definition,
                new[] { ("Zulu", "REG_SZ", "z"), ("Alpha", "REG_SZ", "a") },
                Array.Empty<string>(),
                output);

            Assert.Equal("Alpha", output[0].ValueName);
            Assert.Equal("Zulu", output[1].ValueName);
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        [InlineData(" value ", "value")]
        [InlineData("a\r\nb\rc", "a\nb\nc")]
        [InlineData("line1\r\nline2\r\n", "line1\nline2")]
        public void NormalizeValueData_CollapsesAndTrims(string? input, string expected)
        {
            Assert.Equal(expected, RegistryDriftService.NormalizeValueData(input));
        }

        [Fact]
        public void GetDefaultDriftKeyDefinitions_IsStableAndValid()
        {
            var definitions = RegistryDriftService.GetDefaultDriftKeyDefinitions();

            Assert.NotEmpty(definitions);
            Assert.All(definitions, d =>
            {
                Assert.Contains(d.Hive, new[] { RegistryDriftService.SoftwareHiveName, RegistryDriftService.SystemHiveName });
                Assert.False(string.IsNullOrWhiteSpace(d.KeyPath));
                Assert.False(string.IsNullOrWhiteSpace(d.Description));
            });

            Assert.Contains(definitions, d =>
                d.Hive == RegistryDriftService.SoftwareHiveName
                && d.KeyPath == @"Microsoft\Windows\CurrentVersion\Run");
            Assert.Contains(definitions, d =>
                d.Hive == RegistryDriftService.SystemHiveName
                && d.Mode == RegistryKeyCaptureMode.SubKeyNames
                && d.KeyPath == @"ControlSet001\Services");
        }

        [Fact]
        public void ResolveHivePath_MapsKnownHivesToConfigFiles()
        {
            var mount = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + Guid.NewGuid().ToString("N"));

            Assert.Equal(
                Path.Combine(mount, "Windows", "System32", "config", "SOFTWARE"),
                RegistryDriftService.ResolveHivePath(mount, "HKLM\\SOFTWARE"));
            Assert.Equal(
                Path.Combine(mount, "Windows", "System32", "config", "SYSTEM"),
                RegistryDriftService.ResolveHivePath(mount, "hklm\\system"));
        }

        [Fact]
        public void CompareRegistry_EmptyVsEmpty_IsIdenticalWithNoData()
        {
            var result = RegistryDriftService.CompareRegistry("A", "B", new List<RegistrySnapshotValue>(), new List<RegistrySnapshotValue>());

            Assert.True(result.AreIdentical);
            Assert.False(result.HasRegistryData);
            Assert.Empty(result.Hives);
            Assert.Equal(0, result.ReferenceValueCount);
            Assert.Equal(0, result.DifferenceValueCount);
        }

        [Fact]
        public void CompareRegistry_IdenticalValues_IsIdentical()
        {
            var reference = new List<RegistrySnapshotValue>
            {
                Val("HKLM\\SYSTEM", @"ControlSet001\Services\Tcpip\Parameters", "HostName", "REG_SZ", "VM-A")
            };
            var difference = new List<RegistrySnapshotValue>
            {
                Val("HKLM\\SYSTEM", @"ControlSet001\Services\Tcpip\Parameters", "HostName", "REG_SZ", "VM-A")
            };

            var result = RegistryDriftService.CompareRegistry("A", "B", reference, difference);

            Assert.True(result.AreIdentical);
            Assert.True(result.HasRegistryData);
            Assert.Equal(0, result.TotalDifferences);
            Assert.Single(result.Hives);
        }

        [Fact]
        public void CompareRegistry_ReportsAddedRemovedChangedPerHive()
        {
            var reference = new List<RegistrySnapshotValue>
            {
                Val("HKLM\\SOFTWARE", @"Microsoft\Windows\CurrentVersion\Run", "Agent", "REG_SZ", @"C:\agent.exe"),
                Val("HKLM\\SOFTWARE", @"Microsoft\Windows\CurrentVersion\Run", "Old", "REG_SZ", @"C:\old.exe"),
                Val("HKLM\\SYSTEM", @"ControlSet001\Services\Tcpip\Parameters", "HostName", "REG_SZ", "VM-A")
            };

            var difference = new List<RegistrySnapshotValue>
            {
                Val("HKLM\\SOFTWARE", @"Microsoft\Windows\CurrentVersion\Run", "Agent", "REG_SZ", @"C:\agent.exe -upgraded"),
                Val("HKLM\\SOFTWARE", @"Microsoft\Windows\CurrentVersion\Run", "New", "REG_SZ", @"C:\new.exe"),
                Val("HKLM\\SYSTEM", @"ControlSet001\Services\Tcpip\Parameters", "HostName", "REG_SZ", "VM-B")
            };

            var result = RegistryDriftService.CompareRegistry("Before", "After", reference, difference);

            Assert.Equal(4, result.TotalDifferences);
            Assert.False(result.AreIdentical);

            var software = result.Hives.Single(h => h.Hive == "HKLM\\SOFTWARE");
            Assert.Single(software.Removed);
            Assert.Equal("Old", software.Removed[0].ValueName);
            Assert.Single(software.Added);
            Assert.Equal("New", software.Added[0].ValueName);
            Assert.Single(software.Changed);
            Assert.Equal("Agent", software.Changed[0].ValueName);
            Assert.Equal(@"C:\agent.exe", software.Changed[0].PreviousData);
            Assert.Equal(@"C:\agent.exe -upgraded", software.Changed[0].CurrentData);

            var system = result.Hives.Single(h => h.Hive == "HKLM\\SYSTEM");
            Assert.Single(system.Changed);
            Assert.Equal("HostName", system.Changed[0].ValueName);
        }

        [Fact]
        public void CompareRegistry_ChangedTypeIsNotAddedOrRemoved()
        {
            var reference = new List<RegistrySnapshotValue>
            {
                Val("HKLM\\SOFTWARE", @"Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", "REG_DWORD", "1")
            };
            var difference = new List<RegistrySnapshotValue>
            {
                Val("HKLM\\SOFTWARE", @"Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", "REG_SZ", "1")
            };

            var result = RegistryDriftService.CompareRegistry("A", "B", reference, difference);

            var hive = result.Hives.Single();
            Assert.Empty(hive.Added);
            Assert.Empty(hive.Removed);
            Assert.Single(hive.Changed);
            Assert.Equal("REG_SZ", hive.Changed[0].ValueType);
            Assert.Equal(1, result.TotalDifferences);
            Assert.False(result.AreIdentical);
        }
    }
}