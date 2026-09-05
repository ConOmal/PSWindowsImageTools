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
    /// <summary>
    /// Targeted tests for the refactored NativeRegistryService surface:
    /// the ModuleCallbacks core overloads (no PSCmdlet) and their callback
    /// routing on failure paths that require no admin rights or real hive mounting.
    /// </summary>
    public class NativeRegistryServiceCallbacksTests
    {
        private static PSWindowsImageTools.Services.RegistryModification CreateModification(
            string hive = "HKLM",
            string key = @"SOFTWARE\Test",
            string operation = "Set")
        {
            return new PSWindowsImageTools.Services.RegistryModification
            {
                HiveName = hive,
                KeyPath = key,
                ValueName = "Value",
                ValueData = "data",
                ValueType = "String",
                Operation = operation
            };
        }

        private static ModuleCallbacks CreateCallbacks(
            out List<string> verbose,
            out List<string> warnings,
            out List<(Exception Exception, string Message)> errors)
        {
            var verboseList = new List<string>();
            var warningsList = new List<string>();
            var errorsList = new List<(Exception, string)>();

            var callbacks = new ModuleCallbacks
            {
                Verbose = verboseList.Add,
                Warning = warningsList.Add,
                Error = (ex, message) => errorsList.Add((ex, message))
            };

            verbose = verboseList;
            warnings = warningsList;
            errors = errorsList;

            return callbacks;
        }

        private static string GetNonexistentMountPath()
        {
            return Path.Combine(Path.GetTempPath(), "PSWIT-nonexistent-" + Guid.NewGuid().ToString("N"));
        }

        [Fact]
        public void ModifyOfflineRegistry_NoModifications_ReturnsFalseAndWarns()
        {
            using var service = new NativeRegistryService();
            var callbacks = CreateCallbacks(out _, out var warnings, out _);

            var success = service.ModifyOfflineRegistry(GetNonexistentMountPath(), new List<PSWindowsImageTools.Services.RegistryModification>(), callbacks);

            Assert.False(success);
            Assert.Contains(warnings, w => w.Contains("No registry modifications"));
        }

        [Fact]
        public void ModifyOfflineRegistry_UnconvertibleModifications_ReturnsFalseAndWarns()
        {
            using var service = new NativeRegistryService();
            var callbacks = CreateCallbacks(out _, out var warnings, out _);

            var success = service.ModifyOfflineRegistry(
                GetNonexistentMountPath(),
                new List<PSWindowsImageTools.Services.RegistryModification> { CreateModification(hive: "BOGUS") },
                callbacks);

            Assert.False(success);
            Assert.Contains(warnings, w => w.Contains("could be converted"));
        }

        [Fact]
        public void ModifyOfflineRegistry_NonexistentMountPath_ReturnsFalseWithWarnings()
        {
            using var service = new NativeRegistryService();
            var callbacks = CreateCallbacks(out var verbose, out var warnings, out _);

            var success = service.ModifyOfflineRegistry(
                GetNonexistentMountPath(),
                new List<PSWindowsImageTools.Services.RegistryModification> { CreateModification() },
                callbacks);

            Assert.False(success);
            Assert.NotEmpty(verbose);
            Assert.NotEmpty(warnings);
        }

        [Fact]
        public void ApplyRegistryOperations_EmptyOperations_ReturnsTrue()
        {
            using var service = new NativeRegistryService();
            var callbacks = CreateCallbacks(out _, out _, out _);

            var success = service.ApplyRegistryOperations(GetNonexistentMountPath(), Array.Empty<RegistryOperation>(), callbacks);

            Assert.True(success);
        }

        [Fact]
        public void ApplyRegistryOperations_NonexistentMountPath_ReturnsFalseWithWarnings()
        {
            using var service = new NativeRegistryService();
            var callbacks = CreateCallbacks(out var verbose, out var warnings, out _);

            var operation = new RegistryOperation
            {
                Operation = RegistryOperationType.Modify,
                Hive = "HKLM",
                Key = @"SOFTWARE\Test",
                ValueName = "Value",
                Value = "data",
                ValueType = RegistryValueKind.String
            };

            var success = service.ApplyRegistryOperations(GetNonexistentMountPath(), new[] { operation }, callbacks);

            Assert.False(success);
            Assert.Contains(verbose, v => v.Contains("Applying 1 registry operations"));
            Assert.Contains(warnings, w => w.Contains("Failed to apply operation"));
        }

        [Fact]
        public void BackupRegistryHives_NoHiveFiles_ReturnsTrueAndCreatesBackupDir()
        {
            using var service = new NativeRegistryService();
            var callbacks = CreateCallbacks(out var verbose, out _, out _);
            var backupPath = Path.Combine(Path.GetTempPath(), "PSWIT-backup-" + Guid.NewGuid().ToString("N"));

            try
            {
                var success = service.BackupRegistryHives(GetNonexistentMountPath(), backupPath, callbacks);

                // No hive files exist under the nonexistent mount path, so nothing is copied
                Assert.True(success);
                Assert.True(Directory.Exists(backupPath));
                Assert.Contains(verbose, v => v.Contains("Registry hive backup completed successfully"));
            }
            finally
            {
                if (Directory.Exists(backupPath))
                {
                    Directory.Delete(backupPath, true);
                }
            }
        }
    }
}