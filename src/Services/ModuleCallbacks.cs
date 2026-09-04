using System;
using System.Management.Automation;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Callbacks used by services to communicate with the host (PowerShell cmdlet or test harness).
    /// Services should depend on this instead of PSCmdlet so they remain decoupled from PowerShell
    /// and unit-testable.
    /// </summary>
    public sealed class ModuleCallbacks
    {
        /// <summary>
        /// Shared silent instance that discards all output
        /// </summary>
        public static readonly ModuleCallbacks Silent = new ModuleCallbacks();

        /// <summary>
        /// Writes a verbose message (optional)
        /// </summary>
        public Action<string>? Verbose { get; set; }

        /// <summary>
        /// Writes a warning message (optional)
        /// </summary>
        public Action<string>? Warning { get; set; }

        /// <summary>
        /// Writes an error message with the associated exception (optional)
        /// </summary>
        public Action<Exception, string>? Error { get; set; }

        /// <summary>
        /// Writes a progress record: percent complete (0-100 or -1), activity, status (optional)
        /// </summary>
        public Action<int, string, string>? Progress { get; set; }

        /// <summary>
        /// Creates callbacks that route to a PowerShell cmdlet
        /// </summary>
        /// <param name="cmdlet">Cmdlet instance, or null for silent callbacks</param>
        public static ModuleCallbacks FromCmdlet(PSCmdlet? cmdlet)
        {
            if (cmdlet == null)
            {
                return Silent;
            }

            return new ModuleCallbacks
            {
                Verbose = message => LoggingService.WriteVerbose(cmdlet, message),
                Warning = message => LoggingService.WriteWarning(cmdlet, message),
                Error = (exception, message) => LoggingService.WriteError(cmdlet, message, exception)
            };
        }
    }
}
