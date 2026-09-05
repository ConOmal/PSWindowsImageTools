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
    public class WindowsImageOobeServiceTests
    {
        private static (string Name, object? Data)[] Values(params (string Name, object? Data)[] values)
        {
            return values;
        }

        private static WindowsImageOobeChange Change(string valueName, int? value)
        {
            return new WindowsImageOobeChange { ValueName = valueName, Value = value };
        }

        [Fact]
        public void GetDefaultSettings_HasSevenUniqueDocumentedEntries()
        {
            var settings = WindowsImageOobeService.GetDefaultSettings();

            Assert.Equal(7, settings.Count);
            Assert.All(settings, s => Assert.False(string.IsNullOrWhiteSpace(s.Description)));
            Assert.All(settings, s => Assert.Equal(s.SettingName, s.ValueName));
            Assert.Equal(settings.Count, settings.Select(s => s.ValueName).Distinct(StringComparer.OrdinalIgnoreCase).Count());

            var names = settings.Select(s => s.ValueName).ToList();
            Assert.Contains("SkipMachineOOBE", names);
            Assert.Contains("SkipUserOOBE", names);
            Assert.Contains("SkipPrivacyExperience", names);
            Assert.Contains("ProtectYourPC", names);
        }

        [Fact]
        public void GetDwordValue_FindsValueCaseInsensitively()
        {
            var values = Values(("skipprivacyexperience", (object)1), ("ProtectYourPC", (object)2));
            Assert.Equal(1, WindowsImageOobeService.GetDwordValue(values, "SkipPrivacyExperience"));
        }

        [Fact]
        public void GetDwordValue_Absent_ReturnsNull()
        {
            Assert.Null(WindowsImageOobeService.GetDwordValue(Values(("SkipPrivacyExperience", (object)1)), "ProtectYourPC"));
        }

        [Theory]
        [InlineData("not-a-number")]
        [InlineData(null)]
        public void GetDwordValue_NonNumericOrNull_ReturnsNull(object? data)
        {
            Assert.Null(WindowsImageOobeService.GetDwordValue(Values(("ProtectYourPC", data)), "ProtectYourPC"));
        }

        [Fact]
        public void ProjectSetting_SetValue_MapsAllFields()
        {
            var definition = WindowsImageOobeService.GetDefaultSettings().First(s => s.ValueName == "SkipPrivacyExperience");

            var setting = WindowsImageOobeService.ProjectSetting("Win11", @"C:\Mount", definition, 1);

            Assert.Equal("Win11", setting.ImageName);
            Assert.Equal(@"C:\Mount", setting.MountPath);
            Assert.Equal("SkipPrivacyExperience", setting.SettingName);
            Assert.Equal("SkipPrivacyExperience", setting.ValueName);
            Assert.Equal(definition.Description, setting.Description);
            Assert.True(setting.IsSet);
            Assert.Equal(1, setting.Value);
            Assert.Equal("Set: 1", setting.State);
        }

        [Fact]
        public void ProjectSetting_ZeroValue_ReportsSetZero()
        {
            var definition = WindowsImageOobeService.GetDefaultSettings().First(s => s.ValueName == "ProtectYourPC");

            var setting = WindowsImageOobeService.ProjectSetting("Win11", @"C:\Mount", definition, 0);

            Assert.True(setting.IsSet);
            Assert.Equal(0, setting.Value);
            Assert.Equal("Set: 0", setting.State);
        }

        [Fact]
        public void ProjectSetting_Unset_ReportsNotSet()
        {
            var definition = WindowsImageOobeService.GetDefaultSettings().First(s => s.ValueName == "BypassNRO");

            var setting = WindowsImageOobeService.ProjectSetting("Win11", @"C:\Mount", definition, null);

            Assert.False(setting.IsSet);
            Assert.Null(setting.Value);
            Assert.Equal("Not set", setting.State);
        }

        [Fact]
        public void ResolveSoftwareHivePath_MapsToConfigFolder()
        {
            var mountPath = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + Guid.NewGuid().ToString("N"));

            var hivePath = WindowsImageOobeService.ResolveSoftwareHivePath(mountPath);

            Assert.Equal(Path.Combine(mountPath, "Windows", "System32", "config", "SOFTWARE"), hivePath);
        }

        [Theory]
        [InlineData("SkipPrivacyExperience", true)]
        [InlineData("skipprivacyexperience", true)]
        [InlineData(" BypassNRO ", true)]
        [InlineData("NotARealValue", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsValidValueName_MatchesCatalogOnly(string? valueName, bool expected)
        {
            Assert.Equal(expected, WindowsImageOobeService.IsValidValueName(valueName));
        }

        [Theory]
        [InlineData(WindowsImageOobeProtectYourPc.Recommended, 1)]
        [InlineData(WindowsImageOobeProtectYourPc.ImportantOnly, 2)]
        [InlineData(WindowsImageOobeProtectYourPc.NotInProgram, 3)]
        public void ToProtectYourPcValue_MapsEnumToDword(WindowsImageOobeProtectYourPc mode, int expected)
        {
            Assert.Equal(expected, WindowsImageOobeService.ToProtectYourPcValue(mode));
        }

        [Fact]
        public void ValidateChanges_EmptyList_Throws()
        {
            Assert.Throws<ArgumentException>(() => WindowsImageOobeService.ValidateChanges(null));
            Assert.Throws<ArgumentException>(() => WindowsImageOobeService.ValidateChanges(new List<WindowsImageOobeChange>()));
        }

        [Fact]
        public void ValidateChanges_UnknownValueName_Throws()
        {
            var changes = new List<WindowsImageOobeChange> { Change("NotARealValue", 1) };

            Assert.Throws<ArgumentException>(() => WindowsImageOobeService.ValidateChanges(changes));
        }

        [Fact]
        public void ValidateChanges_WrittenAndRemoved_Throws()
        {
            var changes = new List<WindowsImageOobeChange>
            {
                Change("BypassNRO", 1),
                Change("bypassnro", null)
            };

            Assert.Throws<ArgumentException>(() => WindowsImageOobeService.ValidateChanges(changes));
        }

        [Fact]
        public void ValidateChanges_DuplicateWrite_Throws()
        {
            var changes = new List<WindowsImageOobeChange>
            {
                Change("SkipUserOOBE", 1),
                Change("SkipUserOOBE", 0)
            };

            Assert.Throws<ArgumentException>(() => WindowsImageOobeService.ValidateChanges(changes));
        }

        [Fact]
        public void ValidateChanges_ValidMixedList_Passes()
        {
            var changes = new List<WindowsImageOobeChange>
            {
                Change("SkipPrivacyExperience", 1),
                Change("ProtectYourPC", 2),
                Change("BypassNRO", null)
            };

            WindowsImageOobeService.ValidateChanges(changes);
        }

        [Fact]
        public void BuildSetOperations_WriteMapsToOobeOperationKey()
        {
            var operations = WindowsImageOobeService.BuildSetOperations(new List<WindowsImageOobeChange>
            {
                Change("SkipPrivacyExperience", 1)
            });

            var operation = Assert.Single(operations);
            Assert.Equal(RegistryOperationType.Modify, operation.Operation);
            Assert.Equal("HKLM", operation.Hive);
            Assert.Equal(@"SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE", operation.Key);
            Assert.Equal("SkipPrivacyExperience", operation.ValueName);
            Assert.Equal(RegistryValueKind.DWord, operation.ValueType);
            Assert.Equal(1u, Assert.IsType<uint>(operation.Value));
        }

        [Fact]
        public void BuildSetOperations_WriteZero_WritesDwordZero()
        {
            var operations = WindowsImageOobeService.BuildSetOperations(new List<WindowsImageOobeChange>
            {
                Change("SkipUserOOBE", 0)
            });

            var operation = Assert.Single(operations);
            Assert.Equal(RegistryOperationType.Modify, operation.Operation);
            Assert.Equal(0u, Assert.IsType<uint>(operation.Value));
        }

        [Fact]
        public void BuildSetOperations_Removal_IsRemoveOperation()
        {
            var operations = WindowsImageOobeService.BuildSetOperations(new List<WindowsImageOobeChange>
            {
                Change("BypassNRO", null)
            });

            var operation = Assert.Single(operations);
            Assert.Equal(RegistryOperationType.Remove, operation.Operation);
            Assert.Equal(@"SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE", operation.Key);
            Assert.Equal("BypassNRO", operation.ValueName);
            Assert.Null(operation.Value);
            Assert.Equal(RegistryValueKind.Unknown, operation.ValueType);
        }

        [Fact]
        public void BuildSetOperations_WritesBeforeRemovals_InCatalogOrder()
        {
            var operations = WindowsImageOobeService.BuildSetOperations(new List<WindowsImageOobeChange>
            {
                Change("BypassNRO", null),
                Change("HideOnlineAccountScreens", 1),
                Change("ProtectYourPC", 3),
                Change("HideWirelessSetupInOOBE", null),
                Change("SkipPrivacyExperience", 1)
            });

            Assert.Equal(
                new[]
                {
                    "SkipPrivacyExperience",
                    "ProtectYourPC",
                    "HideOnlineAccountScreens",
                    "BypassNRO",
                    "HideWirelessSetupInOOBE"
                },
                operations.Select(o => o.ValueName).ToArray());
            Assert.Equal(3, operations.Count(o => o.Operation == RegistryOperationType.Modify));
            Assert.Equal(2, operations.Count(o => o.Operation == RegistryOperationType.Remove));
            Assert.All(operations, o => Assert.Equal(@"SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE", o.Key));
        }

        [Fact]
        public void BuildSetOperations_NullOrEmpty_ReturnsEmpty()
        {
            Assert.Empty(WindowsImageOobeService.BuildSetOperations(null!));
            Assert.Empty(WindowsImageOobeService.BuildSetOperations(new List<WindowsImageOobeChange>()));
        }

        [Fact]
        public void DescribeSetChange_SingleWrite()
        {
            var description = WindowsImageOobeService.DescribeSetChange(new List<WindowsImageOobeChange>
            {
                Change("SkipPrivacyExperience", 1)
            });

            Assert.Equal("Write SkipPrivacyExperience=1", description);
        }

        [Fact]
        public void DescribeSetChange_MixedWritesAndRemovals_InCatalogOrder()
        {
            var description = WindowsImageOobeService.DescribeSetChange(new List<WindowsImageOobeChange>
            {
                Change("BypassNRO", null),
                Change("SkipMachineOOBE", 0),
                Change("ProtectYourPC", 2),
                Change("SkipPrivacyExperience", 1)
            });

            Assert.Equal(
                "Write SkipMachineOOBE=0, Write SkipPrivacyExperience=1, Write ProtectYourPC=2, Remove BypassNRO",
                description);
        }

        [Fact]
        public void DescribeSetChange_Empty_FallsBack()
        {
            Assert.Equal("No OOBE changes", WindowsImageOobeService.DescribeSetChange(null!));
            Assert.Equal("No OOBE changes", WindowsImageOobeService.DescribeSetChange(new List<WindowsImageOobeChange>()));
        }

        [Fact]
        public void BuildSetResult_MapsFields()
        {
            var result = WindowsImageOobeService.BuildSetResult("Win11", "Write SkipPrivacyExperience=1", true, null);

            Assert.Equal("Win11", result.ImageName);
            Assert.Equal("Write SkipPrivacyExperience=1", result.Operation);
            Assert.True(result.Success);
            Assert.Null(result.ErrorMessage);
            Assert.Contains("Win11", result.ToString());
        }

        [Fact]
        public void BuildSetResult_Failure_KeepsErrorMessage()
        {
            var result = WindowsImageOobeService.BuildSetResult("Win11", "Write SkipPrivacyExperience=1", false, "boom");

            Assert.False(result.Success);
            Assert.Equal("boom", result.ErrorMessage);
            Assert.Contains("boom", result.ToString());
        }
    }
}
