using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class ScheduledTasksServiceTests
    {
        private static (string Name, object? Data)[] Values(params (string Name, object? Data)[] values)
        {
            return values;
        }

        [Theory]
        [InlineData(1, WindowsImageScheduledTaskState.Disabled)]
        [InlineData(2, WindowsImageScheduledTaskState.Queued)]
        [InlineData(3, WindowsImageScheduledTaskState.Ready)]
        [InlineData(4, WindowsImageScheduledTaskState.Running)]
        [InlineData(0, WindowsImageScheduledTaskState.Unknown)]
        [InlineData(5, WindowsImageScheduledTaskState.Unknown)]
        [InlineData(-1, WindowsImageScheduledTaskState.Unknown)]
        [InlineData(99, WindowsImageScheduledTaskState.Unknown)]
        public void ParseTaskState_MapsDwordToEnum(int value, WindowsImageScheduledTaskState expected)
        {
            Assert.Equal(expected, ScheduledTasksService.ParseTaskState(value));
        }

        [Fact]
        public void JoinTreePath_RootChild_GetsLeadingBackslash()
        {
            Assert.Equal("\\Microsoft", ScheduledTasksService.JoinTreePath(string.Empty, "Microsoft"));
        }

        [Fact]
        public void JoinTreePath_Nested_AppendsSegment()
        {
            Assert.Equal("\\Microsoft\\Windows", ScheduledTasksService.JoinTreePath("\\Microsoft", "Windows"));
        }

        [Fact]
        public void MatchesPathFilter_BlankFilter_MatchesEverything()
        {
            Assert.True(ScheduledTasksService.MatchesPathFilter("\\Microsoft\\Windows\\Defrag\\ScheduledDefrag", null));
            Assert.True(ScheduledTasksService.MatchesPathFilter("\\Microsoft\\Windows\\Defrag\\ScheduledDefrag", string.Empty));
            Assert.True(ScheduledTasksService.MatchesPathFilter("\\CustomTask", "  "));
        }

        [Fact]
        public void MatchesPathFilter_ExactMatchIsCaseInsensitive()
        {
            Assert.True(ScheduledTasksService.MatchesPathFilter("\\Microsoft\\Windows\\Defrag\\ScheduledDefrag", "\\microsoft\\windows\\defrag\\scheduleddefrag"));
            Assert.True(ScheduledTasksService.MatchesPathFilter("\\Task", "\\Task"));
        }

        [Fact]
        public void MatchesPathFilter_NonExactBecomesAnchoredRegex()
        {
            Assert.True(ScheduledTasksService.MatchesPathFilter("\\Microsoft\\Windows\\UpdateOrchestrator\\Reboot", "^\\\\Microsoft\\\\Windows\\\\UpdateOrchestrator\\\\.*$"));
            Assert.False(ScheduledTasksService.MatchesPathFilter("\\Other\\Reboot", "^\\\\Microsoft\\\\"));
            Assert.False(ScheduledTasksService.MatchesPathFilter("\\Microsoft\\X\\Defrag", "ScheduledDefrag"));
        }

        [Fact]
        public void MatchesPathFilter_InvalidPattern_MatchesNothing()
        {
            Assert.False(ScheduledTasksService.MatchesPathFilter("\\Task", "["));
        }

        [Fact]
        public void MatchesPathFilter_CatastrophicBacktracking_TimesOutAndMatchesNothing()
        {
            var adversarialPath = "\\" + new string('a', 25) + "X";
            Assert.False(ScheduledTasksService.MatchesPathFilter(adversarialPath, "(a+)+$"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void MatchesPathFilter_EmptyTaskPath_MatchesNothing(string? taskPath)
        {
            Assert.False(ScheduledTasksService.MatchesPathFilter(taskPath, null));
        }

        [Fact]
        public void FilterTreeTasks_BlankFilter_SortsByPathAndKeepsGuids()
        {
            var tasks = new List<(string TaskPath, string TaskGuid)>
            {
                ("\\B\\Task", "guid-2"),
                ("\\A\\Task", "guid-1"),
                ("\\a\\Task2", "guid-3")
            };

            var filtered = ScheduledTasksService.FilterTreeTasks(tasks, null);

            Assert.Equal(new[] { "\\A\\Task", "\\a\\Task2", "\\B\\Task" }, filtered.Select(t => t.TaskPath).ToArray());
            Assert.Equal("guid-1", filtered[0].TaskGuid);
            Assert.Equal("guid-3", filtered[1].TaskGuid);
            Assert.Equal("guid-2", filtered[2].TaskGuid);
        }

        [Fact]
        public void FilterTreeTasks_PathFilter_NarrowsResult()
        {
            var tasks = new List<(string TaskPath, string TaskGuid)>
            {
                ("\\Microsoft\\Windows\\Defrag\\ScheduledDefrag", "guid-defrag"),
                ("\\Microsoft\\Windows\\UpdateOrchestrator\\Reboot", "guid-reboot"),
                ("\\Custom\\Agent", "guid-agent")
            };

            var filtered = ScheduledTasksService.FilterTreeTasks(tasks, "\\Microsoft\\Windows\\Defrag\\ScheduledDefrag");

            var task = Assert.Single(filtered);
            Assert.Equal("guid-defrag", task.TaskGuid);
        }

        [Fact]
        public void FilterTreeTasks_NullList_ReturnsEmpty()
        {
            var filtered = ScheduledTasksService.FilterTreeTasks(null!, null);
            Assert.Empty(filtered);
        }

        [Fact]
        public void GetDwordValue_FindsValueCaseInsensitively()
        {
            var values = Values(("STATE", (object)3), ("Other", "x"));
            Assert.Equal(3, ScheduledTasksService.GetDwordValue(values, "state"));
        }

        [Fact]
        public void GetDwordValue_Absent_ReturnsNull()
        {
            Assert.Null(ScheduledTasksService.GetDwordValue(Values(("State", (object)3)), "Missing"));
        }

        [Theory]
        [InlineData("not-a-number")]
        [InlineData(null)]
        public void GetDwordValue_NonNumericOrNull_ReturnsNull(object? data)
        {
            Assert.Null(ScheduledTasksService.GetDwordValue(Values(("State", data)), "State"));
        }

        [Fact]
        public void GetDwordValue_ParsesDecimalStringFromHiveReader()
        {
            // The Registry package delivers DWORD data as a plain decimal string
            Assert.Equal(3, ScheduledTasksService.GetDwordValue(Values(("State", "3")), "State"));
        }

        [Fact]
        public void GetStringValue_FindsAndCasts()
        {
            var values = Values(("uri", "\\Microsoft\\Windows\\Defrag\\ScheduledDefrag"), ("State", "3"));
            Assert.Equal("\\Microsoft\\Windows\\Defrag\\ScheduledDefrag", ScheduledTasksService.GetStringValue(values, "Uri"));
        }

        [Fact]
        public void GetStringValue_Absent_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, ScheduledTasksService.GetStringValue(Values(("State", "3")), "Uri"));
        }

        [Fact]
        public void GetStringValue_NullData_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, ScheduledTasksService.GetStringValue(Values(("Uri", null)), "Uri"));
        }

        [Fact]
        public void CollectValues_SortsOrdinalAndSkipsBlankNames()
        {
            var values = Values(
                ("b", (object)2),
                ("A", (object)"x"),
                ("", (object)"skip"),
                ("C", (object)3));

            var collected = ScheduledTasksService.CollectValues(values)!;

            Assert.Equal(new[] { "A", "C", "b" }, collected.Keys.ToArray());
            Assert.Equal(3, collected.Count);
        }

        [Fact]
        public void BuildTaskInfo_WithEntry_MapsAllFields()
        {
            var values = Values(
                ("State", "3"),
                ("Uri", "\\Microsoft\\Windows\\Defrag\\ScheduledDefrag"),
                ("Hash", "0123456789abcdef"));

            var info = ScheduledTasksService.BuildTaskInfo(
                "Win11", @"C:\Mount", "\\Microsoft\\Windows\\Defrag\\ScheduledDefrag", "{11111111-2222-3333-4444-555555555555}",
                hasTasksEntry: true, values: values, detailed: true);

            Assert.Equal("Win11", info.ImageName);
            Assert.Equal(@"C:\Mount", info.MountPath);
            Assert.Equal("\\Microsoft\\Windows\\Defrag\\ScheduledDefrag", info.TaskPath);
            Assert.Equal("{11111111-2222-3333-4444-555555555555}", info.TaskGuid);
            Assert.Equal(WindowsImageScheduledTaskState.Ready, info.State);
            Assert.Equal(3, info.StateValue);
            Assert.Equal("\\Microsoft\\Windows\\Defrag\\ScheduledDefrag", info.Uri);
            Assert.True(info.HasTasksEntry);
            Assert.NotNull(info.RegistryValues);
            Assert.True(info.RegistryValues!.ContainsKey("State"));
        }

        [Fact]
        public void BuildTaskInfo_NotDetailed_RegistryValuesStayNull()
        {
            var info = ScheduledTasksService.BuildTaskInfo(
                "Win11", @"C:\Mount", "\\Task", "guid-1",
                hasTasksEntry: true, values: Values(("State", "4")), detailed: false);

            Assert.Equal(WindowsImageScheduledTaskState.Running, info.State);
            Assert.Equal(4, info.StateValue);
            Assert.Null(info.RegistryValues);
        }

        [Fact]
        public void BuildTaskInfo_StateAbsent_IsUnknownWithMinusOne()
        {
            var info = ScheduledTasksService.BuildTaskInfo(
                "Win11", @"C:\Mount", "\\Task", "guid-1",
                hasTasksEntry: true, values: Values(("Hash", "abc")), detailed: false);

            Assert.Equal(WindowsImageScheduledTaskState.Unknown, info.State);
            Assert.Equal(-1, info.StateValue);
        }

        [Fact]
        public void BuildTaskInfo_StateZero_IsUnknownButRawValueIsKept()
        {
            var info = ScheduledTasksService.BuildTaskInfo(
                "Win11", @"C:\Mount", "\\Task", "guid-1",
                hasTasksEntry: true, values: Values(("State", "0")), detailed: false);

            Assert.Equal(WindowsImageScheduledTaskState.Unknown, info.State);
            Assert.Equal(0, info.StateValue);
        }

        [Fact]
        public void BuildTaskInfo_NonNumericState_IsUnknownWithMinusOne()
        {
            var info = ScheduledTasksService.BuildTaskInfo(
                "Win11", @"C:\Mount", "\\Task", "guid-1",
                hasTasksEntry: true, values: Values(("State", "dword:00000003")), detailed: false);

            Assert.Equal(WindowsImageScheduledTaskState.Unknown, info.State);
            Assert.Equal(-1, info.StateValue);
        }

        [Fact]
        public void BuildTaskInfo_NoTasksEntry_IsUnknownWithoutValuesOrUri()
        {
            var info = ScheduledTasksService.BuildTaskInfo(
                "Win11", @"C:\Mount", "\\OrphanTask", "guid-orphan",
                hasTasksEntry: false, values: null, detailed: true);

            Assert.Equal(WindowsImageScheduledTaskState.Unknown, info.State);
            Assert.Equal(-1, info.StateValue);
            Assert.Equal(string.Empty, info.Uri);
            Assert.False(info.HasTasksEntry);
            Assert.Null(info.RegistryValues);
        }

        [Fact]
        public void ResolveSoftwareHivePath_CombinesStandardConfigLayout()
        {
            var mountPath = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PSWIT-", Guid.NewGuid().ToString("N"))).FullName;

            try
            {
                Assert.Equal(
                    Path.Combine(mountPath, "Windows", "System32", "config", "SOFTWARE"),
                    ScheduledTasksService.ResolveSoftwareHivePath(mountPath));
            }
            finally
            {
                Directory.Delete(mountPath);
            }
        }

        [Fact]
        public void GetScheduledTasks_MissingHive_ReturnsEmptyWithoutThrowing()
        {
            string? verbose = null;
            var callbacks = new ModuleCallbacks { Verbose = message => verbose = message };
            var service = new ScheduledTasksService(callbacks);
            var missingMount = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + Guid.NewGuid().ToString("N"));

            using var reader = new RegistryHiveReader(callbacks);
            var tasks = service.GetScheduledTasks(reader, "Win11", missingMount);

            Assert.Empty(tasks);
            Assert.NotNull(verbose);
            Assert.Contains("SOFTWARE hive not found", verbose!);
        }

        [Fact]
        public void ToString_IncludesPathStateAndImage()
        {
            var info = new WindowsImageScheduledTaskInfo
            {
                ImageName = "Win11",
                TaskPath = "\\Custom\\Agent",
                State = WindowsImageScheduledTaskState.Ready
            };

            Assert.Equal("\\Custom\\Agent (Ready) on Win11", info.ToString());
        }
    }
}
