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
    public class WindowsImageServicesServiceTests
    {
        private static (string Name, object? Data)[] Values(params (string Name, object? Data)[] values)
        {
            return values;
        }

        [Theory]
        [InlineData(0, WindowsImageServiceStartType.Boot)]
        [InlineData(1, WindowsImageServiceStartType.System)]
        [InlineData(2, WindowsImageServiceStartType.Automatic)]
        [InlineData(3, WindowsImageServiceStartType.Manual)]
        [InlineData(4, WindowsImageServiceStartType.Disabled)]
        [InlineData(5, WindowsImageServiceStartType.Unknown)]
        [InlineData(-1, WindowsImageServiceStartType.Unknown)]
        public void ParseStartType_MapsDwordToEnum(int value, WindowsImageServiceStartType expected)
        {
            Assert.Equal(expected, WindowsImageServicesService.ParseStartType(value));
        }

        [Theory]
        [InlineData(WindowsImageServiceStartType.Boot, 0)]
        [InlineData(WindowsImageServiceStartType.System, 1)]
        [InlineData(WindowsImageServiceStartType.Automatic, 2)]
        [InlineData(WindowsImageServiceStartType.Manual, 3)]
        [InlineData(WindowsImageServiceStartType.Disabled, 4)]
        public void ToStartValue_RoundTrips(WindowsImageServiceStartType type, int expected)
        {
            Assert.Equal(expected, WindowsImageServicesService.ToStartValue(type));
        }

        [Fact]
        public void ToStartValue_Unknown_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => WindowsImageServicesService.ToStartValue(WindowsImageServiceStartType.Unknown));
        }

        [Fact]
        public void GetDwordValue_FindsValueCaseInsensitively()
        {
            var values = Values(("Start", (object)2), ("DISPLAYNAME", "Test"));
            Assert.Equal(2, WindowsImageServicesService.GetDwordValue(values, "start"));
        }

        [Fact]
        public void GetDwordValue_Absent_ReturnsNull()
        {
            Assert.Null(WindowsImageServicesService.GetDwordValue(Values(("Start", (object)2)), "Missing"));
        }

        [Theory]
        [InlineData("not-a-number")]
        [InlineData(null)]
        public void GetDwordValue_NonNumericOrNull_ReturnsNull(object? data)
        {
            Assert.Null(WindowsImageServicesService.GetDwordValue(Values(("Start", data)), "Start"));
        }

        [Fact]
        public void GetStringValue_FindsAndCasts()
        {
            var values = Values(("displayname", "DHCP Client"), ("ImagePath", null));
            Assert.Equal("DHCP Client", WindowsImageServicesService.GetStringValue(values, "DisplayName"));
        }

        [Fact]
        public void GetStringValue_Absent_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, WindowsImageServicesService.GetStringValue(Values(("Start", (object)2)), "DisplayName"));
        }

        [Fact]
        public void GetStringValue_NullData_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, WindowsImageServicesService.GetStringValue(Values(("ImagePath", null)), "ImagePath"));
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(0, false)]
        [InlineData(2, false)]
        [InlineData(null, false)]
        public void GetDelayedAutoStart_OnlyOneIsTrue(int? value, bool expected)
        {
            var values = value.HasValue ? Values(("DelayedAutoStart", (object)value.Value)) : Values(("Start", (object)2));
            Assert.Equal(expected, WindowsImageServicesService.GetDelayedAutoStart(values));
        }

        [Fact]
        public void ProjectServiceInfo_MapsAllFields()
        {
            var values = Values(
                ("Start", (object)2),
                ("DisplayName", "DHCP Client"),
                ("ImagePath", @"C:\Windows\System32\svchost.exe"),
                ("Description", "Manages network configuration."),
                ("DelayedAutoStart", (object)1));

            var info = WindowsImageServicesService.ProjectServiceInfo("Win11", @"C:\Mount", "Dhcp", values);

            Assert.Equal("Win11", info.ImageName);
            Assert.Equal(@"C:\Mount", info.MountPath);
            Assert.Equal("Dhcp", info.Name);
            Assert.Equal("DHCP Client", info.DisplayName);
            Assert.Equal(@"C:\Windows\System32\svchost.exe", info.ImagePath);
            Assert.Equal("Manages network configuration.", info.Description);
            Assert.Equal(WindowsImageServiceStartType.Automatic, info.StartType);
            Assert.Equal(2, info.StartValue);
            Assert.True(info.DelayedAutoStart);
        }

        [Fact]
        public void ProjectServiceInfo_MissingStart_DefaultsToUnknownAndMinusOne()
        {
            var info = WindowsImageServicesService.ProjectServiceInfo("Win11", @"C:\Mount", "Test", Values(("DisplayName", "Test Service")));
            Assert.Equal(WindowsImageServiceStartType.Unknown, info.StartType);
            Assert.Equal(-1, info.StartValue);
            Assert.False(info.DelayedAutoStart);
        }

        [Fact]
        public void CollectValues_SortsOrdinalAndSkipsBlankNames()
        {
            var values = Values(
                ("b", (object)2),
                ("A", (object)"x"),
                ("", (object)"skip"),
                ("C", (object)3));

            var collected = WindowsImageServicesService.CollectValues(values);

            Assert.Equal(new[] { "A", "C", "b" }, collected.Keys.ToArray());
            Assert.Equal(3, collected.Count);
        }

        [Fact]
        public void MatchesNameFilter_BlankFilter_MatchesEverything()
        {
            Assert.True(WindowsImageServicesService.MatchesNameFilter("Dhcp", null));
            Assert.True(WindowsImageServicesService.MatchesNameFilter("Dhcp", string.Empty));
            Assert.True(WindowsImageServicesService.MatchesNameFilter("Dhcp", "  "));
        }

        [Fact]
        public void MatchesNameFilter_ExactMatchIsCaseInsensitive()
        {
            Assert.True(WindowsImageServicesService.MatchesNameFilter("Dhcp", "dhcp"));
            Assert.True(WindowsImageServicesService.MatchesNameFilter("dhcp", "DHCP"));
        }

        [Fact]
        public void MatchesNameFilter_NonExactBecomesAnchoredRegex()
        {
            Assert.True(WindowsImageServicesService.MatchesNameFilter("WindowsAgent", "^.*Agent$"));
            Assert.False(WindowsImageServicesService.MatchesNameFilter("AgentSomething", "^.*Agent$"));
            Assert.False(WindowsImageServicesService.MatchesNameFilter("Maples", "agent"));
        }

        [Fact]
        public void MatchesNameFilter_InvalidPattern_MatchesNothing()
        {
            Assert.False(WindowsImageServicesService.MatchesNameFilter("Dhcp", "["));
        }

        [Fact]
        public void MatchesNameFilter_EmptyServiceName_MatchesNothing()
        {
            Assert.False(WindowsImageServicesService.MatchesNameFilter(string.Empty, "Dhcp"));
            Assert.False(WindowsImageServicesService.MatchesNameFilter("  ", "Dhcp"));
        }

        [Fact]
        public void MatchesNameFilter_CatastrophicBacktracking_TimesOutAndMatchesNothing()
        {
            var adversarialName = new string('a', 25) + "X";
            Assert.False(WindowsImageServicesService.MatchesNameFilter(adversarialName, "(a+)+$"));
        }

        [Fact]
        public void ResolveSystemHivePath_CombinesStandardConfigLayout()
        {
            var mountPath = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PSWIT-", Guid.NewGuid().ToString("N"))).FullName;

            try
            {
                Assert.Equal(
                    Path.Combine(mountPath, "Windows", "System32", "config", "SYSTEM"),
                    WindowsImageServicesService.ResolveSystemHivePath(mountPath));
            }
            finally
            {
                Directory.Delete(mountPath);
            }
        }

        [Theory]
        [InlineData("Dhcp")]
        [InlineData("Wuauserv")]
        [InlineData("IntcAzaudAddService")]
        public void IsValidServiceName_AcceptsPlainNames(string name)
        {
            Assert.True(WindowsImageServicesService.IsValidServiceName(name));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("a\\b")]
        [InlineData("a/b")]
        public void IsValidServiceName_RejectsInvalidNames(string name)
        {
            Assert.False(WindowsImageServicesService.IsValidServiceName(name));
        }

        [Fact]
        public void ValidateSetParameters_NothingRequested_Throws()
        {
            Assert.Throws<ArgumentException>(() => WindowsImageServicesService.ValidateSetParameters(null, false));
        }

        [Fact]
        public void ValidateSetParameters_DelayedAutoStartWithoutAutomatic_Throws()
        {
            Assert.Throws<ArgumentException>(() => WindowsImageServicesService.ValidateSetParameters(WindowsImageServiceStartType.Manual, true));
        }

        [Fact]
        public void ValidateSetParameters_ValidCombinations_Pass()
        {
            WindowsImageServicesService.ValidateSetParameters(WindowsImageServiceStartType.Disabled, false);
            WindowsImageServicesService.ValidateSetParameters(null, true);
            WindowsImageServicesService.ValidateSetParameters(WindowsImageServiceStartType.Automatic, true);
        }

        [Fact]
        public void BuildSetOperations_StartOnly_ProducesSingleModifyOperation()
        {
            var operations = WindowsImageServicesService.BuildSetOperations("Dhcp", WindowsImageServiceStartType.Disabled, false);

            var operation = Assert.Single(operations);
            Assert.Equal(RegistryOperationType.Modify, operation.Operation);
            Assert.Equal("HKLM", operation.Hive);
            Assert.Equal(@"ControlSet001\Services\Dhcp", operation.Key);
            Assert.Equal("Start", operation.ValueName);
            Assert.Equal(4, operation.Value);
            Assert.Equal(RegistryValueKind.DWord, operation.ValueType);
        }

        [Fact]
        public void BuildSetOperations_DelayedOnly_ProducesSingleOneDwordOperation()
        {
            var operations = WindowsImageServicesService.BuildSetOperations("Dhcp", null, true);

            var operation = Assert.Single(operations);
            Assert.Equal("DelayedAutoStart", operation.ValueName);
            Assert.Equal(1u, operation.Value);
            Assert.Equal(RegistryValueKind.DWord, operation.ValueType);
        }

        [Fact]
        public void BuildSetOperations_Both_ProducesTwoOperations()
        {
            var operations = WindowsImageServicesService.BuildSetOperations("Dhcp", WindowsImageServiceStartType.Automatic, true);

            Assert.Equal(2, operations.Count);
            Assert.Contains(operations, o => o.ValueName == "Start" && Equals(o.Value, 2));
            Assert.Contains(operations, o => o.ValueName == "DelayedAutoStart" && Equals(o.Value, 1u));
        }

        [Theory]
        [InlineData(WindowsImageServiceStartType.Automatic, true, "Set start type to Automatic and enable delayed auto start")]
        [InlineData(WindowsImageServiceStartType.Disabled, false, "Set start type to Disabled")]
        [InlineData(null, true, "Enable delayed auto start")]
        public void DescribeSetChange_ProducesHumanReadableText(WindowsImageServiceStartType? startType, bool delayed, string expected)
        {
            Assert.Equal(expected, WindowsImageServicesService.DescribeSetChange(startType, delayed));
        }

        [Fact]
        public void BuildSetResult_ProjectsAllFields()
        {
            var result = WindowsImageServicesService.BuildSetResult(
                "Win11", "Dhcp", WindowsImageServiceStartType.Manual, true, false, "boom");

            Assert.Equal("Win11", result.ImageName);
            Assert.Equal("Dhcp", result.ServiceName);
            Assert.Equal("Set start type to Manual and enable delayed auto start", result.Operation);
            Assert.Equal(WindowsImageServiceStartType.Manual, result.RequestedStartType);
            Assert.True(result.SetDelayedAutoStart);
            Assert.False(result.Success);
            Assert.Equal("boom", result.ErrorMessage);
        }
    }
}