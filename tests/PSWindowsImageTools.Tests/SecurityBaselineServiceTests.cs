using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class SecurityBaselineServiceTests
    {
        private static WindowsImageSecurityBaselineEntry Entry(
            string hive,
            string keyPath,
            string valueName,
            string expectedValue,
            RegistryValueKind kind = RegistryValueKind.DWord)
        {
            return new WindowsImageSecurityBaselineEntry
            {
                Hive = hive,
                KeyPath = keyPath,
                ValueName = valueName,
                ExpectedValue = expectedValue,
                ValueType = kind,
                Rationale = "test rationale"
            };
        }

        [Fact]
        public void GetBaselineEntries_IsCuratedAndWellFormed()
        {
            var entries = SecurityBaselineService.GetBaselineEntries();

            Assert.InRange(entries.Count, 15, 25);
            Assert.Equal(22, entries.Count);

            var knownHives = new[]
            {
                SecurityBaselineService.SoftwareHiveName,
                SecurityBaselineService.SystemHiveName,
                SecurityBaselineService.DefaultUserHiveName
            };

            Assert.All(entries, e =>
            {
                Assert.Contains(e.Hive, knownHives);
                Assert.False(string.IsNullOrWhiteSpace(e.KeyPath));
                Assert.False(string.IsNullOrWhiteSpace(e.ValueName));
                Assert.False(string.IsNullOrWhiteSpace(e.ExpectedValue));
                Assert.False(string.IsNullOrWhiteSpace(e.Rationale));
                Assert.True(
                    e.ValueType == RegistryValueKind.DWord || e.ValueType == RegistryValueKind.String,
                    $"Unexpected value kind {e.ValueType} on {e}");
                if (e.ValueType == RegistryValueKind.DWord)
                {
                    Assert.True(uint.TryParse(e.ExpectedValue, out _), $"DWord entry {e} must carry a decimal value");
                }
            });

            // Unique identity and stable order across calls
            Assert.Equal(entries.Count, entries.Select(e => $"{e.Hive}\\{e.KeyPath}\\{e.ValueName}").Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(entries.Select(e => e.ValueName), SecurityBaselineService.GetBaselineEntries().Select(e => e.ValueName));

            // All three curated hives are represented
            Assert.Contains(SecurityBaselineService.SoftwareHiveName, entries.Select(e => e.Hive));
            Assert.Contains(SecurityBaselineService.SystemHiveName, entries.Select(e => e.Hive));
            Assert.Contains(SecurityBaselineService.DefaultUserHiveName, entries.Select(e => e.Hive));
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        [InlineData(" value ", "value")]
        [InlineData("a\r\nb\rc", "a\nb\nc")]
        public void NormalizeValueData_CollapsesAndTrims(string? input, string expected)
        {
            Assert.Equal(expected, SecurityBaselineService.NormalizeValueData(input));
        }

        [Theory]
        [InlineData("1", "1", true)]
        [InlineData("255", "255", true)]
        [InlineData(" 900 ", "900", true)]
        [InlineData("007", "7", true)]
        [InlineData("1", "2", false)]
        [InlineData("ScreenSaver", "screensaver", true)]
        [InlineData("1", "0", false)]
        [InlineData("abc", "1", false)]
        public void ValuesEquivalent_ComparesNumericallyThenCaseInsensitively(string? expected, string? observed, bool isEqual)
        {
            Assert.Equal(isEqual, SecurityBaselineService.ValuesEquivalent(expected, observed));
        }

        [Fact]
        public void ValuesEquivalent_NullOnlyEqualsNull()
        {
            Assert.True(SecurityBaselineService.ValuesEquivalent(null, null));
            Assert.False(SecurityBaselineService.ValuesEquivalent(null, "0"));
            Assert.False(SecurityBaselineService.ValuesEquivalent("0", null));
        }

        [Fact]
        public void CompareEntry_MapsComplianceStates()
        {
            var entry = Entry(SecurityBaselineService.SoftwareHiveName, @"Policies\System", "EnableLUA", "1");

            Assert.Equal(WindowsImageBaselineComplianceState.NotPresent, SecurityBaselineService.CompareEntry(entry, null));
            Assert.Equal(WindowsImageBaselineComplianceState.Compliant, SecurityBaselineService.CompareEntry(entry, "1"));
            Assert.Equal(WindowsImageBaselineComplianceState.Compliant, SecurityBaselineService.CompareEntry(entry, " 1 "));
            Assert.Equal(WindowsImageBaselineComplianceState.NonCompliant, SecurityBaselineService.CompareEntry(entry, "0"));
        }

        [Fact]
        public void ResolveHivePath_MapsKnownHivesToFiles()
        {
            var mount = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + Guid.NewGuid().ToString("N"));

            Assert.Equal(
                Path.Combine(mount, "Windows", "System32", "config", "SOFTWARE"),
                SecurityBaselineService.ResolveHivePath(mount, SecurityBaselineService.SoftwareHiveName));
            Assert.Equal(
                Path.Combine(mount, "Windows", "System32", "config", "SYSTEM"),
                SecurityBaselineService.ResolveHivePath(mount, "hklm\\system"));
            Assert.Equal(
                Path.Combine(mount, "Users", "Default", "NTUSER.DAT"),
                SecurityBaselineService.ResolveHivePath(mount, SecurityBaselineService.DefaultUserHiveName));
            Assert.Equal(
                Path.Combine(mount, "Users", "Default", "NTUSER.DAT"),
                SecurityBaselineService.ResolveHivePath(mount, "hku\\defaultuser"));
            Assert.Equal(
                Path.Combine(mount, "Windows", "System32", "config", "OTHER_HIVE"),
                SecurityBaselineService.ResolveHivePath(mount, "OTHER\\HIVE"));
        }

        [Fact]
        public void MapOperationKey_MapsHivesForTheWritePath()
        {
            Assert.Equal(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
                SecurityBaselineService.MapOperationKey(
                    SecurityBaselineService.SoftwareHiveName,
                    @"Microsoft\Windows\CurrentVersion\Policies\System"));
            Assert.Equal(
                @"ControlSet001\Control\Lsa",
                SecurityBaselineService.MapOperationKey(SecurityBaselineService.SystemHiveName, @"ControlSet001\Control\Lsa"));
            Assert.Equal(
                @"Software\Policies\Microsoft\Windows\Control Panel\Desktop",
                SecurityBaselineService.MapOperationKey(
                    SecurityBaselineService.DefaultUserHiveName,
                    @"Software\Policies\Microsoft\Windows\Control Panel\Desktop"));
        }

        [Fact]
        public void MapOperationKey_UnknownHive_Throws()
        {
            Assert.Throws<ArgumentException>(() => SecurityBaselineService.MapOperationKey("HKCR", "Anything"));
        }

        [Fact]
        public void MapOperationHive_MapsHivesForTheWritePath()
        {
            Assert.Equal("HKLM", SecurityBaselineService.MapOperationHive(SecurityBaselineService.SoftwareHiveName));
            Assert.Equal("HKLM", SecurityBaselineService.MapOperationHive(SecurityBaselineService.SystemHiveName));
            Assert.Equal("HKU", SecurityBaselineService.MapOperationHive(SecurityBaselineService.DefaultUserHiveName));
            Assert.Throws<ArgumentException>(() => SecurityBaselineService.MapOperationHive("HKCR"));
        }

        [Fact]
        public void ToWriteValue_ConvertsByKind()
        {
            Assert.Equal(1u, SecurityBaselineService.ToWriteValue(Entry("h", "k", "v", "1")));
            Assert.Equal(255u, SecurityBaselineService.ToWriteValue(Entry("h", "k", "v", "255")));
            Assert.Equal("900", SecurityBaselineService.ToWriteValue(Entry("h", "k", "v", " 900 ", RegistryValueKind.String)));
            Assert.Equal(900ul, SecurityBaselineService.ToWriteValue(Entry("h", "k", "v", "900", RegistryValueKind.QWord)));
            Assert.Throws<FormatException>(() => SecurityBaselineService.ToWriteValue(Entry("h", "k", "v", "not-a-number")));
            Assert.Throws<ArgumentOutOfRangeException>(() => SecurityBaselineService.ToWriteValue(Entry("h", "k", "v", "AA", RegistryValueKind.Binary)));
        }

        [Fact]
        public void BuildApplyOperations_ProducesWritePathOperations()
        {
            var entries = new List<WindowsImageSecurityBaselineEntry>
            {
                Entry(SecurityBaselineService.SoftwareHiveName, @"Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", "1"),
                Entry(SecurityBaselineService.SystemHiveName, @"ControlSet001\Control\Lsa", "LmCompatibilityLevel", "5"),
                Entry(SecurityBaselineService.DefaultUserHiveName, @"Software\Policies\Microsoft\Windows\Control Panel\Desktop", "ScreenSaveTimeOut", "900", RegistryValueKind.String)
            };

            var operations = SecurityBaselineService.BuildApplyOperations(entries);

            Assert.Equal(3, operations.Count);

            var software = operations[0];
            Assert.Equal(RegistryOperationType.Modify, software.Operation);
            Assert.Equal("HKLM", software.Hive);
            Assert.Equal(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", software.Key);
            Assert.Equal("EnableLUA", software.ValueName);
            Assert.Equal(1u, software.Value);
            Assert.Equal(RegistryValueKind.DWord, software.ValueType);

            var system = operations[1];
            Assert.Equal("HKLM", system.Hive);
            Assert.Equal(@"ControlSet001\Control\Lsa", system.Key);
            Assert.Equal("LmCompatibilityLevel", system.ValueName);
            Assert.Equal(5u, system.Value);

            var defaultUser = operations[2];
            Assert.Equal("HKU", defaultUser.Hive);
            Assert.Equal(@"Software\Policies\Microsoft\Windows\Control Panel\Desktop", defaultUser.Key);
            Assert.Equal("ScreenSaveTimeOut", defaultUser.ValueName);
            Assert.Equal("900", defaultUser.Value);
            Assert.Equal(RegistryValueKind.String, defaultUser.ValueType);
        }

        [Fact]
        public void BuildApplyOperations_EmptyInput_ReturnsEmpty()
        {
            Assert.Empty(SecurityBaselineService.BuildApplyOperations(new List<WindowsImageSecurityBaselineEntry>()));
        }

        [Fact]
        public void ToExpectedTypeString_MapsKinds()
        {
            Assert.Equal("RegDword", SecurityBaselineService.ToExpectedTypeString(RegistryValueKind.DWord));
            Assert.Equal("RegSz", SecurityBaselineService.ToExpectedTypeString(RegistryValueKind.String));
        }

        [Fact]
        public void DescribeApplyAction_MentionsCountsAndImage()
        {
            var action = SecurityBaselineService.DescribeApplyAction(6, 16, "Win11 Pro");

            Assert.Contains("6", action);
            Assert.Contains("16", action);
            Assert.Contains("Win11 Pro", action);
        }

        [Fact]
        public void BuildApplyRows_MapsRowStates()
        {
            var written = new[] { Entry("h1", "k1", "v1", "1") };
            var compliant = new[] { Entry("h2", "k2", "v2", "1") };

            var rows = SecurityBaselineService.BuildApplyRows(
                "Win11 Pro",
                written,
                WindowsImageBaselineApplyState.Failed,
                "batch error",
                compliant,
                WindowsImageBaselineApplyState.AlreadyApplied,
                "Already compliant");

            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal("Win11 Pro", r.ImageName));

            Assert.Equal(WindowsImageBaselineApplyState.Failed, rows[0].State);
            Assert.Equal("batch error", rows[0].Detail);
            Assert.Equal("v1", rows[0].ValueName);

            Assert.Equal(WindowsImageBaselineApplyState.AlreadyApplied, rows[1].State);
            Assert.Equal("Already compliant", rows[1].Detail);
        }

        [Fact]
        public void BuildApplyResult_ComputesCountsAndSuccess()
        {
            var rows = new List<WindowsImageSecurityBaselineApplyEntry>
            {
                new WindowsImageSecurityBaselineApplyEntry { State = WindowsImageBaselineApplyState.Applied },
                new WindowsImageSecurityBaselineApplyEntry { State = WindowsImageBaselineApplyState.Applied },
                new WindowsImageSecurityBaselineApplyEntry { State = WindowsImageBaselineApplyState.AlreadyApplied },
                new WindowsImageSecurityBaselineApplyEntry { State = WindowsImageBaselineApplyState.Skipped },
                new WindowsImageSecurityBaselineApplyEntry { State = WindowsImageBaselineApplyState.Failed }
            };

            var result = SecurityBaselineService.BuildApplyResult("Win11 Pro", @"C:\mount", rows, false, "boom");

            Assert.Equal(5, result.TotalCount);
            Assert.Equal(2, result.AppliedCount);
            Assert.Equal(1, result.AlreadyAppliedCount);
            Assert.Equal(1, result.SkippedCount);
            Assert.Equal(1, result.FailedCount);
            Assert.False(result.Success);
            Assert.Equal("boom", result.ErrorMessage);
            Assert.Equal("Win11 Pro", result.ImageName);
        }

        [Fact]
        public void BuildObservation_ProjectsEntryPlusObservation()
        {
            var entry = Entry(SecurityBaselineService.SystemHiveName, @"ControlSet001\Control\Lsa", "RunAsPPL", "1");

            var present = SecurityBaselineService.BuildObservation("Win11 Pro", @"C:\mount", entry, "1", "RegDword");
            Assert.Equal(WindowsImageBaselineComplianceState.Compliant, present.State);
            Assert.Equal("1", present.ObservedValue);
            Assert.Equal("RegDword", present.ObservedValueType);
            Assert.Equal("Win11 Pro", present.ImageName);
            Assert.Equal(entry.ValueType, present.ValueType);

            var missing = SecurityBaselineService.BuildObservation("Win11 Pro", @"C:\mount", entry, null, string.Empty);
            Assert.Equal(WindowsImageBaselineComplianceState.NotPresent, missing.State);
            Assert.Equal(string.Empty, missing.ObservedValue);
        }
    }
}
