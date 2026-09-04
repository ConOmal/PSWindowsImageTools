using System;
using Microsoft.Dism;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class ServicingChainServiceTests
    {
        private const string RealSsuPackageName = "Package_for_ServicingStack_9156~31bf3856ad364e35~amd64~~26100.9156.1.0";
        private const string RealLcuPackageName = "Package_for_RollupFix~31bf3856ad364e35~amd64~~26100.9168.1.19";

        [Fact]
        public void ClassifyPackage_RealSsuIdentity_ClassifiedAsVerifiedSSU()
        {
            var result = ServicingChainService.ClassifyPackage(
                RealSsuPackageName, DismPackageFeatureState.Installed, DismReleaseType.SecurityUpdate, new DateTime(2026, 8, 11));

            Assert.NotNull(result);
            Assert.Equal(ServicingPackageRole.ServicingStackUpdate, result!.Role);
            Assert.Equal(ClassificationConfidence.Verified, result.Confidence);
            Assert.Equal(26100, result.Build);
            Assert.Equal(9156, result.Revision);
        }

        [Fact]
        public void ClassifyPackage_RealLcuIdentity_ClassifiedAsVerifiedLCU()
        {
            var result = ServicingChainService.ClassifyPackage(
                RealLcuPackageName, DismPackageFeatureState.Installed, DismReleaseType.SecurityUpdate, new DateTime(2026, 8, 14));

            Assert.NotNull(result);
            Assert.Equal(ServicingPackageRole.CumulativeUpdate, result!.Role);
            Assert.Equal(ClassificationConfidence.Verified, result.Confidence);
            Assert.Equal(26100, result.Build);
            Assert.Equal(9168, result.Revision);
        }

        [Fact]
        public void ClassifyPackage_RemovedState_ReturnsNull()
        {
            var result = ServicingChainService.ClassifyPackage(
                RealLcuPackageName, DismPackageFeatureState.Removed, DismReleaseType.SecurityUpdate, null);

            Assert.Null(result);
        }

        [Fact]
        public void ClassifyPackage_SupersededState_ReturnsNull()
        {
            var result = ServicingChainService.ClassifyPackage(
                RealLcuPackageName, DismPackageFeatureState.Superseded, DismReleaseType.SecurityUpdate, null);

            Assert.Null(result);
        }

        [Fact]
        public void ClassifyPackage_NonUpdateReleaseType_ReturnsNull()
        {
            // A language pack or feature pack should never be classified, even if its name were unusual
            var result = ServicingChainService.ClassifyPackage(
                "Microsoft-Windows-Client-LanguagePack-Package~31bf3856ad364e35~amd64~en-US~10.0.26100.9168",
                DismPackageFeatureState.Installed, DismReleaseType.LanguagePack, null);

            Assert.Null(result);
        }

        [Fact]
        public void ClassifyPackage_UnrecognizedUpdateName_ClassifiedAsOtherHeuristic()
        {
            var result = ServicingChainService.ClassifyPackage(
                "Package_for_KB9999999~31bf3856ad364e35~amd64~~26100.9200.1.0",
                DismPackageFeatureState.Installed, DismReleaseType.Update, null);

            Assert.NotNull(result);
            Assert.Equal(ServicingPackageRole.Other, result!.Role);
            Assert.Equal(ClassificationConfidence.Heuristic, result.Confidence);
        }

        [Fact]
        public void ParseBuildRevision_RealLcuIdentity_ExtractsBuildAndRevision()
        {
            var (build, revision) = ServicingChainService.ParseBuildRevision(RealLcuPackageName);

            Assert.Equal(26100, build);
            Assert.Equal(9168, revision);
        }

        [Fact]
        public void ParseBuildRevision_MalformedName_ReturnsZeros()
        {
            var (build, revision) = ServicingChainService.ParseBuildRevision("not-a-real-package-identity");

            Assert.Equal(0, build);
            Assert.Equal(0, revision);
        }
    }
}
