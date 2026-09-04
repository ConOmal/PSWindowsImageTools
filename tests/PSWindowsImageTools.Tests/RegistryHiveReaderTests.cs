using System;
using System.IO;
using System.Linq;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class RegistryHiveReaderTests : IDisposable
    {
        private readonly string _tempDirectory;

        public RegistryHiveReaderTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        /// <summary>
        /// The Default user profile's NTUSER.DAT is present on every Windows installation,
        /// is not locked by the OS, and is a valid registry hive.
        /// </summary>
        private static string? GetDefaultUserHivePath()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows) ?? @"C:\Windows", "Users", "Default", "NTUSER.DAT");
            return File.Exists(path) ? path : null;
        }

        [Fact]
        public void GetWindowsVersionInfo_MissingHive_ReturnsEmptyWithoutThrowing()
        {
            using var reader = new RegistryHiveReader();

            var result = reader.GetWindowsVersionInfo(Path.Combine(_tempDirectory, "no-such-hive.dat"));

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void GetInstalledSoftware_MissingHive_ReturnsEmptyWithoutThrowing()
        {
            using var reader = new RegistryHiveReader();

            var result = reader.GetInstalledSoftware(Path.Combine(_tempDirectory, "no-such-hive.dat"));

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void GetWindowsUpdateConfiguration_MissingHive_ReturnsEmptyWithoutThrowing()
        {
            using var reader = new RegistryHiveReader();

            var result = reader.GetWindowsUpdateConfiguration(Path.Combine(_tempDirectory, "no-such-hive.dat"));

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void GetSoftwareHivePath_BuildsExpectedPath()
        {
            var path = RegistryHiveReader.GetSoftwareHivePath(@"C:\Mount\img1");

            Assert.Equal(@"C:\Mount\img1\Windows\System32\config\SOFTWARE", path);
        }

        [Fact]
        public void OpenHive_ParsesRealHive_AndDoesNotHoldFileHandle()
        {
            var hivePath = GetDefaultUserHivePath();
            if (hivePath == null)
            {
                // Skip gracefully on machines without the Default profile hive
                return;
            }

            var copyPath = Path.Combine(_tempDirectory, "NTUSER.DAT");
            File.Copy(hivePath, copyPath);

            using var reader = new RegistryHiveReader();
            var hive = reader.OpenHive(copyPath);
            var key = reader.GetKey(hive, "Software");

            Assert.NotNull(key);
            Assert.True(key!.SubKeys.Count > 0, "Software root should contain subkeys");

            // RegistryHiveOnDemand parses into memory; the file must be deletable immediately
            reader.Dispose();
            File.Delete(copyPath);
            Assert.True(true);
        }

        [Fact]
        public void GetKey_NonExistentPath_ReturnsNull()
        {
            var hivePath = GetDefaultUserHivePath();
            if (hivePath == null)
            {
                return;
            }

            using var reader = new RegistryHiveReader();
            var hive = reader.OpenHive(hivePath);

            var key = reader.GetKey(hive, @"Software\This\Path\Does\Not\Exist\Anywhere");

            Assert.Null(key);
        }

        [Fact]
        public void Callbacks_WarningFiredWhenHiveMissing()
        {
            string? warned = null;
            var callbacks = new ModuleCallbacks { Warning = message => warned = message };
            using var reader = new RegistryHiveReader(callbacks);

            reader.GetWindowsVersionInfo(Path.Combine(_tempDirectory, "no-such-hive.dat"));

            Assert.NotNull(warned);
            Assert.Contains("SOFTWARE hive not found", warned!);
        }

        [Fact]
        public void ModuleCallbacks_FromNullCmdlet_ReturnsSilentInstance()
        {
            Assert.Same(ModuleCallbacks.Silent, ModuleCallbacks.FromCmdlet(null));
        }
    }
}
