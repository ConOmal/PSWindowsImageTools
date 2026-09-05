using System;
using System.Collections.Generic;
using System.Linq;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    /// <summary>
    /// Targeted tests for the refactored RegistryService surface (PSCmdlet-free
    /// live-registry enumeration). All tests are read-only against well-known
    /// HKLM keys that exist on every Windows installation.
    /// </summary>
    public class RegistryServiceTests
    {
        [Fact]
        public void EnumerateUninstallEntries_ReturnsDictionaryWithoutThrowing()
        {
            var entries = RegistryService.EnumerateUninstallEntries();

            Assert.NotNull(entries);
            // Every entry must carry the DisplayName it was keyed on, plus its registry path
            Assert.All(entries.Values, v =>
            {
                Assert.Contains("DisplayName", v.Keys);
                Assert.Contains("RegistryPath", v.Keys);
            });
        }

        [Fact]
        public void OpenKey_EnumerateSubKeys_CurrentVersionHasSubKeys()
        {
            var key = RegistryService.OpenKey(RegistryService.HKEY_LOCAL_MACHINE,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion");
            Assert.NotEqual(IntPtr.Zero, key);

            try
            {
                var subKeys = RegistryService.EnumerateSubKeys(key);
                Assert.NotEmpty(subKeys);
            }
            finally
            {
                RegistryService.CloseKey(key);
            }
        }

        [Fact]
        public void GetStringValue_ProgramFilesDir_ReturnsPath()
        {
            var key = RegistryService.OpenKey(RegistryService.HKEY_LOCAL_MACHINE,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion");
            Assert.NotEqual(IntPtr.Zero, key);

            try
            {
                var programFilesDir = RegistryService.GetStringValue(key, "ProgramFilesDir");
                Assert.False(string.IsNullOrEmpty(programFilesDir));
            }
            finally
            {
                RegistryService.CloseKey(key);
            }
        }

        [Fact]
        public void GetStringValue_MissingValue_ReturnsNull()
        {
            var key = RegistryService.OpenKey(RegistryService.HKEY_LOCAL_MACHINE,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion");
            Assert.NotEqual(IntPtr.Zero, key);

            try
            {
                Assert.Null(RegistryService.GetStringValue(key, "PSWIT_DefinitelyMissingValue"));
            }
            finally
            {
                RegistryService.CloseKey(key);
            }
        }

        [Fact]
        public void GetDWordValue_MissingValue_ReturnsNull()
        {
            var key = RegistryService.OpenKey(RegistryService.HKEY_LOCAL_MACHINE,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion");
            Assert.NotEqual(IntPtr.Zero, key);

            try
            {
                Assert.Null(RegistryService.GetDWordValue(key, "PSWIT_DefinitelyMissingValue"));
            }
            finally
            {
                RegistryService.CloseKey(key);
            }
        }

        [Fact]
        public void OpenKey_NonexistentPath_ReturnsZero()
        {
            var key = RegistryService.OpenKey(RegistryService.HKEY_LOCAL_MACHINE,
                @"SOFTWARE\PSWIT_DefinitelyMissingKey_" + Guid.NewGuid().ToString("N"));
            Assert.Equal(IntPtr.Zero, key);
        }
    }
}