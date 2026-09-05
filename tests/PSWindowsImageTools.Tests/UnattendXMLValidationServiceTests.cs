using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class UnattendXMLValidationServiceTests : IDisposable
    {
        private const string UnattendNamespace = "urn:schemas-microsoft-com:unattend";

        private const string ShellSetupComponent =
            "<component name=\"Microsoft-Windows-Shell-Setup\" processorArchitecture=\"amd64\" " +
            "publicKeyToken=\"31bf3856ad364e35\" language=\"neutral\" versionScope=\"nonSxS\">{0}</component>";

        private const string DeploymentComponent =
            "<component name=\"Microsoft-Windows-Deployment\" processorArchitecture=\"amd64\"/>";

        private readonly string _tempDirectory;

        public UnattendXMLValidationServiceTests()
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

        #region temp-file fixtures (public ValidateFile path)

        [Fact]
        public void ValidateFile_ValidUnattend_IsValidWithNoIssues()
        {
            var filePath = WriteUnattendFile(ValidUnattend());
            var service = new UnattendXMLValidationService();

            var report = service.ValidateFile(filePath);

            Assert.True(report.IsValid);
            Assert.Empty(report.Issues);
            Assert.Equal(0, report.ErrorCount);
            Assert.Equal(0, report.WarningCount);
            Assert.Equal(filePath, report.FilePath);
        }

        [Fact]
        public void ValidateFile_MalformedXml_SingleNotWellFormedError_Invalid()
        {
            var filePath = WriteUnattendFile("<unattend xmlns=\"" + UnattendNamespace + "\"><settings pass='specialize'></unattend>");
            var service = new UnattendXMLValidationService();

            var report = service.ValidateFile(filePath);

            Assert.False(report.IsValid);
            var issue = Assert.Single(report.Issues);
            Assert.Equal(UnattendValidationSeverity.Error, issue.Severity);
            Assert.Equal("XML-NotWellFormed", issue.RuleId);
            Assert.Equal("/", issue.ElementPath);
        }

        [Fact]
        public void ValidateFile_MissingFile_ThrowsInvalidOperationException()
        {
            var service = new UnattendXMLValidationService();
            var missing = Path.Combine(_tempDirectory, "does-not-exist.xml");

            Assert.Throws<InvalidOperationException>(() => service.ValidateFile(missing));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ValidateFile_BlankPath_ThrowsArgumentException(string? filePath)
        {
            var service = new UnattendXMLValidationService();

            Assert.ThrowsAny<ArgumentException>(() => service.ValidateFile(filePath!));
        }

        [Fact]
        public void ValidateFile_EmptyFile_SingleNotWellFormedError()
        {
            var filePath = WriteUnattendFile(string.Empty);
            var service = new UnattendXMLValidationService();

            var report = service.ValidateFile(filePath);

            Assert.False(report.IsValid);
            Assert.Equal("XML-NotWellFormed", Assert.Single(report.Issues).RuleId);
        }

        #endregion

        #region R1-R3: root structure

        [Fact]
        public void AnalyzeDocument_EmptyDocument_RootNotUnattendError()
        {
            var issues = UnattendXMLValidationService.AnalyzeDocument(new XmlDocument());

            var issue = Assert.Single(issues);
            Assert.Equal("XML-RootNotUnattend", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Error, issue.Severity);
            Assert.Equal("/", issue.ElementPath);
        }

        [Fact]
        public void AnalyzeDocument_WrongRootElement_RootNotUnattendError()
        {
            var issues = AnalyzeXml("<settings pass=\"specialize\"/>");

            var issue = Assert.Single(issues);
            Assert.Equal("XML-RootNotUnattend", issue.RuleId);
        }

        [Fact]
        public void AnalyzeDocument_WrongNamespace_WrongNamespaceError()
        {
            var issues = AnalyzeXml(
                "<unattend xmlns=\"http://wrong.example.com/ns\">" +
                "<settings pass=\"specialize\">" + DeploymentComponent + "</settings></unattend>");

            var issue = Assert.Single(issues);
            Assert.Equal("XML-WrongNamespace", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Error, issue.Severity);
        }

        [Fact]
        public void AnalyzeDocument_MissingNamespace_WrongNamespaceError()
        {
            var issues = AnalyzeXml(
                "<unattend><settings pass=\"specialize\">" + DeploymentComponent + "</settings></unattend>");

            var issue = Assert.Single(issues);
            Assert.Equal("XML-WrongNamespace", issue.RuleId);
        }

        #endregion

        #region R4-R8: settings passes and structure

        [Fact]
        public void AnalyzeDocument_MissingPassAttribute_PassMissingError()
        {
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings>" + DeploymentComponent + "</settings></unattend>");

            var issue = Assert.Single(issues);
            Assert.Equal("Pass-Missing", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Error, issue.Severity);
            Assert.Equal("/unattend/settings", issue.ElementPath);
        }

        [Fact]
        public void AnalyzeDocument_UnknownPass_PassUnknownError()
        {
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"bogusPass\">" + DeploymentComponent + "</settings></unattend>");

            var issue = Assert.Single(issues);
            Assert.Equal("Pass-Unknown", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Error, issue.Severity);
            Assert.Equal("/unattend/settings[@pass='bogusPass']", issue.ElementPath);
        }

        [Theory]
        [InlineData("auditSystem")]
        [InlineData("auditUser")]
        public void AnalyzeDocument_AuditPass_PassUnknownWarning(string pass)
        {
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"" + pass + "\">" + DeploymentComponent + "</settings></unattend>");

            var issue = Assert.Single(issues);
            Assert.Equal("Pass-Unknown", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Warning, issue.Severity);
        }

        [Theory]
        [InlineData("windowsPE")]
        [InlineData("offlineServicing")]
        [InlineData("generalize")]
        [InlineData("specialize")]
        [InlineData("oobeSystem")]
        public void AnalyzeDocument_ValidPass_NoIssues(string pass)
        {
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"" + pass + "\">" + DeploymentComponent + "</settings>" +
                "</unattend>");

            Assert.Empty(issues);
        }

        [Fact]
        public void AnalyzeDocument_ValidPassCaseInsensitive_NoIssues()
        {
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"Specialize\">" + DeploymentComponent + "</settings>" +
                "</unattend>");

            Assert.Empty(issues);
        }

        [Fact]
        public void AnalyzeDocument_SettingsChildNotComponent_SettingsInvalidChildError()
        {
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"oobeSystem\"><OOBE/></settings></unattend>");

            var issue = Assert.Single(issues);
            Assert.Equal("Settings-InvalidChild", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Error, issue.Severity);
            Assert.Equal("oobeSystem", issue.Pass);
            Assert.Equal("/unattend/settings[@pass='oobeSystem']/OOBE", issue.ElementPath);
        }

        [Fact]
        public void AnalyzeDocument_ComponentDirectlyUnderRoot_RootInvalidChildError()
        {
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<component name=\"Microsoft-Windows-Shell-Setup\" processorArchitecture=\"amd64\"/></unattend>");

            var issue = Assert.Single(issues);
            Assert.Equal("Root-InvalidChild", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Error, issue.Severity);
            Assert.Empty(issue.Pass);
        }

        [Fact]
        public void AnalyzeDocument_EmptySettings_SettingsEmptyWarning()
        {
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"specialize\"/></unattend>");

            var issue = Assert.Single(issues);
            Assert.Equal("Settings-Empty", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Warning, issue.Severity);
        }

        #endregion

        #region R9-R13: components

        [Fact]
        public void AnalyzeDocument_ComponentWithoutName_MissingNameError()
        {
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"specialize\">" +
                "<component processorArchitecture=\"amd64\"/></settings></unattend>");

            var issue = Assert.Single(issues);
            Assert.Equal("Component-MissingName", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Error, issue.Severity);
        }

        [Fact]
        public void AnalyzeDocument_DuplicateComponentSameArch_DuplicateError()
        {
            var component = "<component name=\"Microsoft-Windows-Shell-Setup\" processorArchitecture=\"amd64\"/>";
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"specialize\">" + component + component + "</settings></unattend>");

            var issue = Assert.Single(issues);
            Assert.Equal("Component-Duplicate", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Error, issue.Severity);
            Assert.Equal("specialize", issue.Pass);
        }

        [Fact]
        public void AnalyzeDocument_DuplicateComponentInDifferentPasses_NoDuplicateIssue()
        {
            var component = "<component name=\"Microsoft-Windows-Shell-Setup\" processorArchitecture=\"amd64\"/>";
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"specialize\">" + component + "</settings>" +
                "<settings pass=\"oobeSystem\">" + component + "</settings>" +
                "</unattend>");

            Assert.DoesNotContain(issues, i => i.RuleId == "Component-Duplicate");
            Assert.Empty(issues);
        }

        [Fact]
        public void AnalyzeDocument_SameNameDifferentArchitectures_NoDuplicateIssue()
        {
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"specialize\">" +
                "<component name=\"Microsoft-Windows-Shell-Setup\" processorArchitecture=\"amd64\"/>" +
                "<component name=\"Microsoft-Windows-Shell-Setup\" processorArchitecture=\"x86\"/>" +
                "</settings></unattend>");

            Assert.DoesNotContain(issues, i => i.RuleId == "Component-Duplicate");
            Assert.Empty(issues);
        }

        [Theory]
        [InlineData("Vendor-Tool")]
        [InlineData("Microsoft Windows Shell Setup")]
        public void AnalyzeDocument_UnknownComponentName_UnknownNameWarning(string name)
        {
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"specialize\">" +
                "<component name=\"" + name + "\" processorArchitecture=\"amd64\"/></settings></unattend>");

            var issue = Assert.Single(issues);
            Assert.Equal("Component-UnknownName", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Warning, issue.Severity);
        }

        [Fact]
        public void AnalyzeDocument_ComponentWithoutArchitecture_MissingArchitectureWarning()
        {
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"specialize\">" +
                "<component name=\"Microsoft-Windows-Shell-Setup\"/></settings></unattend>");

            var issue = Assert.Single(issues);
            Assert.Equal("Component-MissingArchitecture", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Warning, issue.Severity);
        }

        [Fact]
        public void AnalyzeDocument_UnknownArchitecture_UnknownArchitectureWarning()
        {
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"specialize\">" +
                "<component name=\"Microsoft-Windows-Shell-Setup\" processorArchitecture=\"sparc\"/></settings></unattend>");

            var issue = Assert.Single(issues);
            Assert.Equal("Component-UnknownArchitecture", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Warning, issue.Severity);
        }

        [Fact]
        public void AnalyzeDocument_Arm64Architecture_NoArchitectureWarning()
        {
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"specialize\">" +
                "<component name=\"Microsoft-Windows-Shell-Setup\" processorArchitecture=\"arm64\"/></settings></unattend>");

            Assert.Empty(issues);
        }

        #endregion

        #region R14-R19: run commands

        private static string RunCommand(
            int? order = null,
            string? command = "cmd /c exit",
            string commandElementName = "RunSynchronousCommand")
        {
            var orderXml = order.HasValue ? "<Order>" + order.Value + "</Order>" : string.Empty;
            var commandXml = command == null ? string.Empty : "<Command>" + command + "</Command>";
            return "<" + commandElementName + ">" + orderXml + commandXml + "</" + commandElementName + ">";
        }

        private static string RunDocument(string commandName, string children, string pass = "specialize")
        {
            return
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"" + pass + "\">" +
                "<component name=\"Microsoft-Windows-Deployment\" processorArchitecture=\"amd64\">" +
                "<" + commandName + ">" + children + "</" + commandName + ">" +
                "</component></settings></unattend>";
        }

        [Fact]
        public void AnalyzeDocument_DuplicateRunOrders_DuplicateOrderError()
        {
            var issues = AnalyzeXml(RunDocument("RunSynchronous", RunCommand(1) + RunCommand(2) + RunCommand(2)));

            var issue = Assert.Single(issues);
            Assert.Equal("Run-DuplicateOrder", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Error, issue.Severity);
            Assert.Equal("specialize", issue.Pass);
        }

        [Fact]
        public void AnalyzeDocument_RunCommandWithoutOrder_MissingOrderError()
        {
            var issues = AnalyzeXml(RunDocument("RunSynchronous", RunCommand()));

            var issue = Assert.Single(issues);
            Assert.Equal("Run-MissingOrder", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Error, issue.Severity);
        }

        [Theory]
        [InlineData("not-a-number")]
        [InlineData("0")]
        [InlineData("-3")]
        public void AnalyzeDocument_InvalidRunOrder_InvalidOrderError(string order)
        {
            var issues = AnalyzeXml(RunDocument("RunSynchronous",
                "<RunSynchronousCommand><Order>" + order + "</Order><Command>cmd /c exit</Command></RunSynchronousCommand>"));

            var issue = Assert.Single(issues);
            Assert.Equal("Run-InvalidOrder", issue.RuleId);
        }

        [Fact]
        public void AnalyzeDocument_RunCommandWithoutCommand_MissingCommandError()
        {
            var issues = AnalyzeXml(RunDocument("RunSynchronous", RunCommand(1, command: null)));

            var issue = Assert.Single(issues);
            Assert.Equal("Run-MissingCommand", issue.RuleId);
        }

        [Fact]
        public void AnalyzeDocument_UnknownChildInRunSection_UnknownCommandElementWarning()
        {
            var issues = AnalyzeXml(RunDocument("RunSynchronous", "<NotACommand/>"));

            var issue = Assert.Single(issues);
            Assert.Equal("Run-UnknownCommandElement", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Warning, issue.Severity);
        }

        [Fact]
        public void AnalyzeDocument_RunAsynchronousInWindowsPE_InvalidPassWarning()
        {
            var issues = AnalyzeXml(RunDocument("RunAsynchronous", RunCommand(1, commandElementName: "RunAsynchronousCommand"), pass: "windowsPE"));

            var issue = Assert.Single(issues);
            Assert.Equal("Run-InvalidPass", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Warning, issue.Severity);
            Assert.Equal("windowsPE", issue.Pass);
        }

        [Fact]
        public void AnalyzeDocument_RunAsynchronousInSpecialize_NoInvalidPassWarning()
        {
            var issues = AnalyzeXml(RunDocument("RunAsynchronous", RunCommand(1, commandElementName: "RunAsynchronousCommand"), pass: "specialize"));

            Assert.Empty(issues);
        }

        [Fact]
        public void AnalyzeDocument_UniqueRunOrders_NoIssues()
        {
            var issues = AnalyzeXml(RunDocument("RunSynchronous", RunCommand(1) + RunCommand(2) + RunCommand(3)));

            Assert.Empty(issues);
        }

        [Fact]
        public void AnalyzeDocument_SameOrderAcrossSeparateRunSections_NoDuplicateIssue()
        {
            var section = "<RunSynchronous>" + RunCommand(1) + "</RunSynchronous>";
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"specialize\">" +
                "<component name=\"Microsoft-Windows-Deployment\" processorArchitecture=\"amd64\">" +
                section + section +
                "</component></settings></unattend>");

            Assert.Empty(issues);
        }

        #endregion

        #region R20-R21: known settings mistakes

        [Fact]
        public void AnalyzeDocument_CopyProfileOutsideSpecialize_WrongPassError()
        {
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"oobeSystem\">" +
                string.Format(ShellSetupComponent, "<CopyProfile>true</CopyProfile>") +
                "</settings></unattend>");

            var issue = Assert.Single(issues);
            Assert.Equal("Setting-WrongPass", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Error, issue.Severity);
            Assert.Equal("oobeSystem", issue.Pass);
            Assert.Equal(
                "/unattend/settings[@pass='oobeSystem']/component[@name='Microsoft-Windows-Shell-Setup']/CopyProfile",
                issue.ElementPath);
        }

        [Fact]
        public void AnalyzeDocument_CopyProfileInSpecialize_NoIssue()
        {
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"specialize\">" +
                string.Format(ShellSetupComponent, "<CopyProfile>true</CopyProfile>") +
                "</settings></unattend>");

            Assert.Empty(issues);
        }

        [Theory]
        [InlineData("SkipMachineOOBE")]
        [InlineData("SkipUserOOBE")]
        public void AnalyzeDocument_DeprecatedSetting_DeprecatedWarning(string elementName)
        {
            var issues = AnalyzeXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"oobeSystem\">" +
                string.Format(ShellSetupComponent, "<" + elementName + ">true</" + elementName + ">") +
                "</settings></unattend>");

            var issue = Assert.Single(issues);
            Assert.Equal("Setting-Deprecated", issue.RuleId);
            Assert.Equal(UnattendValidationSeverity.Warning, issue.Severity);
        }

        #endregion

        #region element path building

        [Fact]
        public void BuildElementPath_IndexedSiblingElements_IncludesOneBasedIndex()
        {
            var document = LoadXml(
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"specialize\">" +
                "<component name=\"Microsoft-Windows-Shell-Setup\" processorArchitecture=\"amd64\">" +
                "<Run/><Run/></component>" +
                "</settings></unattend>");

            var secondRun = document.SelectNodes("//*[local-name()='Run']")![1];

            Assert.Equal(
                "/unattend/settings[@pass='specialize']/component[@name='Microsoft-Windows-Shell-Setup']/Run[2]",
                UnattendXMLValidationService.BuildElementPath(secondRun));
        }

        [Fact]
        public void BuildElementPath_SettingsWithoutPass_BareSettingsPath()
        {
            var document = LoadXml("<unattend><settings><component name=\"X\"/></settings></unattend>");

            var component = document.SelectNodes("//*[local-name()='component']")![0];

            Assert.Equal("/unattend/settings/component[@name='X']", UnattendXMLValidationService.BuildElementPath(component));
        }

        [Fact]
        public void BuildElementPath_NullNode_ReturnsRootSlash()
        {
            Assert.Equal("/", UnattendXMLValidationService.BuildElementPath(null));
        }

        #endregion

        #region report building + severity filter

        [Fact]
        public void BuildReport_MixedIssues_IsValidFromUnfilteredSet_IssuesFilteredBySeverity()
        {
            var issues = new List<UnattendValidationIssue>
            {
                new UnattendValidationIssue { Severity = UnattendValidationSeverity.Warning, RuleId = "Component-UnknownName" },
                new UnattendValidationIssue { Severity = UnattendValidationSeverity.Error, RuleId = "Pass-Missing" }
            };

            var allReport = UnattendXMLValidationService.BuildReport("test.xml", issues, UnattendValidationSeverity.Warning);
            var errorsOnly = UnattendXMLValidationService.BuildReport("test.xml", issues, UnattendValidationSeverity.Error);

            Assert.False(allReport.IsValid);
            Assert.False(errorsOnly.IsValid);
            Assert.Equal(2, allReport.Issues.Count);
            Assert.Single(errorsOnly.Issues);
            Assert.Equal("Pass-Missing", errorsOnly.Issues[0].RuleId);
            Assert.Equal(1, allReport.ErrorCount);
            Assert.Equal(1, allReport.WarningCount);
            Assert.Equal(1, errorsOnly.ErrorCount);
            Assert.Equal(0, errorsOnly.WarningCount);
        }

        [Fact]
        public void BuildReport_OnlyWarnings_IsValidTrue()
        {
            var issues = new List<UnattendValidationIssue>
            {
                new UnattendValidationIssue { Severity = UnattendValidationSeverity.Warning, RuleId = "Settings-Empty" }
            };

            var report = UnattendXMLValidationService.BuildReport("test.xml", issues, UnattendValidationSeverity.Warning);

            Assert.True(report.IsValid);
            Assert.Single(report.Issues);
        }

        [Fact]
        public void BuildReport_NullIssues_ProducesEmptyValidReport()
        {
            var report = UnattendXMLValidationService.BuildReport("test.xml", null!, UnattendValidationSeverity.Warning);

            Assert.True(report.IsValid);
            Assert.Empty(report.Issues);
        }

        [Fact]
        public void IssueToString_ExpectedFormat()
        {
            var issue = new UnattendValidationIssue
            {
                Severity = UnattendValidationSeverity.Error,
                Pass = "specialize",
                ElementPath = "/unattend/settings",
                Message = "broken"
            };

            Assert.Equal("[Error] specialize: broken (/unattend/settings)", issue.ToString());
        }

        [Fact]
        public void ReportToString_ExpectedFormat()
        {
            var report = UnattendXMLValidationService.BuildReport("C:\\media\\unattend.xml",
                new List<UnattendValidationIssue>
                {
                    new UnattendValidationIssue { Severity = UnattendValidationSeverity.Error }
                },
                UnattendValidationSeverity.Warning);

            Assert.Equal("C:\\media\\unattend.xml: IsValid=False (1 errors, 0 warnings)", report.ToString());
        }

        [Fact]
        public void AnalyzeDocument_MultiIssueDocument_RulesRunInDocumentedOrder()
        {
            var xml =
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"bogusPass\">" +
                "<component processorArchitecture=\"sparc\">" +
                "<RunSynchronous><RunSynchronousCommand><Command>cmd</Command></RunSynchronousCommand></RunSynchronous>" +
                "</component>" +
                "</settings>" +
                "<component name=\"Vendor\"/>" +
                "</unattend>";

            var issues = AnalyzeXml(xml);

            Assert.Equal(
                new[]
                {
                    "Root-InvalidChild",
                    "Pass-Unknown",
                    "Component-MissingName",
                    "Component-UnknownArchitecture",
                    "Component-UnknownName",
                    "Component-MissingArchitecture",
                    "Run-MissingOrder"
                },
                issues.Select(i => i.RuleId).ToArray());
            Assert.False(issues.First().Severity == UnattendValidationSeverity.Warning);
        }

        #endregion

        #region service constants

        [Fact]
        public void ServiceConstants_UnattendNamespaceAndValidPasses_ExpectedValues()
        {
            Assert.Equal("urn:schemas-microsoft-com:unattend", UnattendXMLValidationService.UnattendNamespace);
            Assert.Equal(
                new[] { "windowsPE", "offlineServicing", "generalize", "specialize", "oobeSystem" },
                UnattendXMLValidationService.ValidPasses);
        }

        #endregion

        #region helpers

        private static string ValidUnattend()
        {
            var specializeComponent = string.Format(ShellSetupComponent, "<CopyProfile>true</CopyProfile>");
            var oobeComponent = string.Format(ShellSetupComponent, "<OOBE><HideEULAPage>true</HideEULAPage></OOBE>");

            return
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<unattend xmlns=\"" + UnattendNamespace + "\">" +
                "<settings pass=\"specialize\">" + specializeComponent + "</settings>" +
                "<settings pass=\"oobeSystem\">" + oobeComponent + "</settings>" +
                "</unattend>";
        }

        private static List<UnattendValidationIssue> AnalyzeXml(string xml)
        {
            return UnattendXMLValidationService.AnalyzeDocument(LoadXml(xml));
        }

        private static XmlDocument LoadXml(string xml)
        {
            var document = new XmlDocument();
            document.LoadXml(xml);
            return document;
        }

        private string WriteUnattendFile(string xml)
        {
            var filePath = Path.Combine(_tempDirectory, "unattend-" + Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(filePath, xml);
            return filePath;
        }

        #endregion
    }
}
