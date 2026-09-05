using System;
using System.IO;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Validates an Unattend XML configuration file and returns a structured
    /// validation report (per-issue severity, pass, element path, message) with
    /// an overall IsValid. Read-only: no DISM, no image mounting, no file writes.
    /// </summary>
    [Cmdlet(VerbsDiagnostic.Test, "UnattendXMLConfiguration")]
    [OutputType(typeof(UnattendValidationReport))]
    public class TestUnattendXMLConfigurationCmdlet : PSCmdlet
    {
        private const string ComponentName = "Test-UnattendXMLConfiguration";

        /// <summary>
        /// Unattend XML file to validate
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            ValueFromPipeline = true,
            ValueFromPipelineByPropertyName = true,
            HelpMessage = "Unattend XML file to validate")]
        [ValidateNotNull]
        public FileInfo Path { get; set; } = null!;

        /// <summary>
        /// Minimum severity of issues to report. Warning (default) reports
        /// errors and warnings; Error reports errors only. IsValid is always
        /// computed over the complete issue set regardless of this filter.
        /// </summary>
        [Parameter(
            HelpMessage = "Minimum severity of issues to report (Warning = errors and warnings, Error = errors only)")]
        public UnattendValidationSeverity Severity { get; set; } = UnattendValidationSeverity.Warning;

        private UnattendXMLValidationService? _validationService;

        /// <summary>
        /// Processes the cmdlet
        /// </summary>
        protected override void ProcessRecord()
        {
            try
            {
                var operationStartTime = LoggingService.LogOperationStartWithTimestamp(this, ComponentName, "Validate Unattend XML configuration");

                if (!Path.Exists)
                {
                    WriteError(new ErrorRecord(
                        new FileNotFoundException($"File not found: {Path.FullName}"),
                        "FileNotFound",
                        ErrorCategory.ObjectNotFound,
                        Path));
                    return;
                }

                _validationService = new UnattendXMLValidationService(ModuleCallbacks.FromCmdlet(this));
                var report = _validationService.ValidateFile(Path.FullName, Severity);

                LoggingService.WriteVerbose(this, ComponentName,
                    $"Validation report for {Path.FullName}: {report.ErrorCount} error(s), {report.WarningCount} warning(s), IsValid={report.IsValid}");

                LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, "Validate Unattend XML configuration", operationStartTime,
                    $"IsValid={report.IsValid}, {report.ErrorCount} error(s), {report.WarningCount} warning(s)");

                WriteObject(report);
            }
            catch (Exception ex)
            {
                LoggingService.LogOperationFailure(this, ComponentName, ex);

                WriteError(new ErrorRecord(ex, "UnattendXMLValidationError", ErrorCategory.NotSpecified, Path));
            }
        }

        protected override void EndProcessing()
        {
            _validationService = null!;
        }
    }
}
