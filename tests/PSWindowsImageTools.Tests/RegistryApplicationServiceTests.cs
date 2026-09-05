using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    /// <summary>
    /// Targeted tests for the refactored RegistryApplicationService surface:
    /// ModuleCallbacks overload (no PSCmdlet), all-failed-on-partial semantics,
    /// and callback routing. All tests avoid real hive mounting.
    /// </summary>
    public class RegistryApplicationServiceTests
    {
        private static MountedWindowsImage CreateImage(string mountId = "mount-1", string? mountPath = null)
        {
            return new MountedWindowsImage
            {
                MountId = mountId,
                ImageName = "Test Image",
                MountPath = mountPath == null ? null : new System.IO.DirectoryInfo(mountPath)
            };
        }

        private static RegistryOperation CreateOperation(string hive = "HKLM", string key = @"SOFTWARE\Test")
        {
            return new RegistryOperation
            {
                Operation = RegistryOperationType.Modify,
                Hive = hive,
                Key = key,
                ValueName = "Value",
                Value = "data",
                ValueType = RegistryValueKind.String
            };
        }

        private static ModuleCallbacks CreateCallbacks(
            out List<string> verbose,
            out List<string> warnings,
            out List<(Exception Exception, string Message)> errors,
            out List<(int Percent, string Activity, string Status)> progress)
        {
            var verboseList = new List<string>();
            var warningsList = new List<string>();
            var errorsList = new List<(Exception, string)>();
            var progressList = new List<(int, string, string)>();

            var callbacks = new ModuleCallbacks
            {
                Verbose = verboseList.Add,
                Warning = warningsList.Add,
                Error = (ex, message) => errorsList.Add((ex, message)),
                Progress = (percent, activity, status) => progressList.Add((percent, activity, status))
            };

            verbose = verboseList;
            warnings = warningsList;
            errors = errorsList;
            progress = progressList;

            return callbacks;
        }

        [Fact]
        public void ApplyOperations_NoImages_ReturnsEmptyResults()
        {
            var service = new RegistryApplicationService();
            var callbacks = CreateCallbacks(out _, out _, out _, out _);

            var results = service.ApplyOperations(Array.Empty<MountedWindowsImage>(), new[] { CreateOperation() }, callbacks);

            Assert.Empty(results);
        }

        [Fact]
        public void ApplyOperations_NullMountPath_MarksAllOperationsFailedAndInvokesError()
        {
            var service = new RegistryApplicationService();
            var callbacks = CreateCallbacks(out _, out _, out var errors, out _);
            var operations = new[] { CreateOperation(), CreateOperation(key: @"SOFTWARE\Other") };

            var results = service.ApplyOperations(new[] { CreateImage(mountPath: null) }, operations, callbacks);

            var result = Assert.Single(results);
            Assert.Equal(2, result.FailedOperations.Count);
            Assert.Empty(result.SuccessfulOperations);
            Assert.Equal(0, result.SuccessCount);
            Assert.Equal(2, result.FailureCount);
            Assert.False(result.IsCompletelySuccessful);
            Assert.Contains(errors, e => e.Message.Contains("Image mount path is null"));
        }

        [Fact]
        public void ApplyOperations_InvokesProgressAndVerboseCallbacks()
        {
            var service = new RegistryApplicationService();
            var callbacks = CreateCallbacks(out var verbose, out _, out _, out var progress);

            service.ApplyOperations(new[] { CreateImage(mountPath: null) }, new[] { CreateOperation() }, callbacks);

            Assert.Contains(verbose, m => m.Contains("Starting to apply 1 registry operations"));
            Assert.Contains(progress, p => p.Percent == 100 && p.Activity == "Applying Registry Operations");
        }

        [Fact]
        public void ApplyOperations_EmptyOperations_StillProcessesImages()
        {
            var service = new RegistryApplicationService();
            var callbacks = CreateCallbacks(out _, out _, out _, out _);

            var results = service.ApplyOperations(new[] { CreateImage(mountPath: null) }, Array.Empty<RegistryOperation>(), callbacks);

            var result = Assert.Single(results);
            Assert.Empty(result.FailedOperations);
            Assert.Empty(result.SuccessfulOperations);
        }

        [Fact]
        public void ApplyOperations_MultipleImages_ProducesOneResultPerImage()
        {
            var service = new RegistryApplicationService();
            var callbacks = CreateCallbacks(out _, out _, out _, out _);

            var results = service.ApplyOperations(
                new[] { CreateImage("mount-1", mountPath: null), CreateImage("mount-2", mountPath: null) },
                new[] { CreateOperation() },
                callbacks);

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal(1, r.FailureCount));
        }
    }
}