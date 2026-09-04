using System;
using System.IO;
using Microsoft.Dism;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    /// <summary>
    /// Manual smoke tests for the DISM host bootstrap. These exercise real servicing
    /// against the local Windows image and require an elevated host. Gated behind
    /// PSWIT_DISM_E2E=1 so regular CI/test runs never execute them.
    /// DISM API constraint: initialize/shutdown exactly once per process; mount dirs
    /// must be unique per run because DISM cannot remount into a used directory.
    /// </summary>
    public class DismHostSmokeTests
    {
        private const string RealWim = @"C:\Win11Pro25H2\sources\install.wim";

        [Fact]
        public void OpenOfflineSession_ServicesRealImage_FromPlainHost()
        {
            if (Environment.GetEnvironmentVariable("PSWIT_DISM_E2E") != "1")
            {
                return;
            }

            Assert.True(File.Exists(RealWim), $"Real WIM not found: {RealWim}");

            var mountRoot = Path.Combine(Path.GetTempPath(), "PSWIT-SMOKE");
            Directory.CreateDirectory(mountRoot);
            var mountPath = Path.Combine(mountRoot, "m1-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var scratch = Path.Combine(mountRoot, "scratch-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(scratch);

            DismApi.Initialize(DismLogLevel.LogErrorsWarnings, null, scratch);
            try
            {
                DismApi.MountImage(RealWim, mountPath, 1, true, DismMountImageOptions.None);
                try
                {
                    using var session = DismApi.OpenOfflineSession(mountPath);
                    var packages = DismApi.GetPackages(session);
                    Assert.True(packages.Count > 0, $"Expected servicing to return packages, got {packages.Count}");
                }
                finally
                {
                    DismApi.UnmountImage(mountPath, false);
                }
            }
            finally
            {
                DismApi.Shutdown();
            }
        }
    }
}
