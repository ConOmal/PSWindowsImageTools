using System;
using System.Diagnostics;
using System.Management.Automation;
using System.Threading;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Service for monitoring long-running processes with progress reporting
    /// </summary>
    public class ProcessMonitoringService
    {
        private const string ServiceName = "ProcessMonitoringService";

        /// <summary>
        /// Executes a process with enhanced monitoring and progress reporting
        /// </summary>
        /// <param name="fileName">Executable file name</param>
        /// <param name="arguments">Command line arguments</param>
        /// <param name="workingDirectory">Working directory for the process</param>
        /// <param name="timeoutMinutes">Timeout in minutes (0 for no timeout)</param>
        /// <param name="progressTitle">Title for progress reporting</param>
        /// <param name="progressDescription">Description for progress reporting</param>
        /// <param name="cmdlet">Cmdlet for progress and logging</param>
        /// <returns>Process exit code</returns>
        public int ExecuteProcessWithMonitoring(
            string fileName,
            string arguments,
            string? workingDirectory = null,
            int timeoutMinutes = 30,
            string progressTitle = "Executing Process",
            string progressDescription = "Running external process",
            PSCmdlet? cmdlet = null)
        {
            var fullCommandLine = $"{fileName} {arguments}";
            
            LoggingService.WriteVerbose(cmdlet, ServiceName, $"Executing command: {fullCommandLine}");
            
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, $"Working directory: {workingDirectory}");
            }

            var processInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                processInfo.WorkingDirectory = workingDirectory;
            }

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                throw new InvalidOperationException($"Failed to start process: {fileName}");
            }

            var processId = process.Id;
            var startTime = DateTime.UtcNow;
            
            LoggingService.WriteVerbose(cmdlet, ServiceName, 
                $"Process started with PID {processId} at {startTime:yyyy-MM-dd HH:mm:ss} UTC");

            // Start monitoring in a separate thread
            var monitoringThread = new Thread(() => MonitorProcess(process, processId, startTime, timeoutMinutes,
                progressTitle, progressDescription, fullCommandLine, cmdlet))
            {
                IsBackground = true
            };
            monitoringThread.Start();

            // Wait for process completion or timeout
            var timeoutMs = timeoutMinutes > 0 ? timeoutMinutes * 60 * 1000 : -1;
            var processCompleted = process.WaitForExit(timeoutMs);

            if (processCompleted)
            {
                // Process completed normally
                var exitCode = process.ExitCode;
                var duration = DateTime.UtcNow - startTime;

                LoggingService.WriteVerbose(cmdlet, ServiceName,
                    $"Process {processId} completed with exit code {exitCode} after {duration.TotalMinutes:F1} minutes");

                // Read any remaining output
                var standardOutput = process.StandardOutput.ReadToEnd();
                var standardError = process.StandardError.ReadToEnd();

                if (!string.IsNullOrEmpty(standardOutput))
                {
                    LoggingService.WriteVerbose(cmdlet, ServiceName,
                        $"Process output: {standardOutput.Trim()}");
                }

                if (!string.IsNullOrEmpty(standardError))
                {
                    if (exitCode == 0)
                    {
                        LoggingService.WriteVerbose(cmdlet, ServiceName,
                            $"Process stderr: {standardError.Trim()}");
                    }
                    else
                    {
                        LoggingService.WriteWarning(cmdlet, ServiceName,
                            $"Process error output: {standardError.Trim()}");
                    }
                }

                return exitCode;
            }
            else
            {
                // Timeout occurred
                LoggingService.WriteWarning(cmdlet, ServiceName,
                    $"Process {processId} timed out after {timeoutMinutes} minutes, attempting to terminate");

                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(5000); // Wait up to 5 seconds for graceful termination
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.WriteWarning(cmdlet, ServiceName,
                        $"Failed to terminate process {processId}: {ex.Message}");
                }

                throw new TimeoutException($"Process timed out after {timeoutMinutes} minutes: {fullCommandLine}");
            }
        }

        /// <summary>
        /// Monitors a running process and provides periodic progress updates
        /// </summary>
        private void MonitorProcess(
            Process process,
            int processId,
            DateTime startTime,
            int timeoutMinutes,
            string progressTitle,
            string progressDescription,
            string commandLine,
            PSCmdlet? cmdlet)
        {
            var updateIntervalSeconds = 10; // Update every 10 seconds
            var lastUpdateTime = DateTime.UtcNow;

            while (!process.HasExited)
            {
                var currentTime = DateTime.UtcNow;
                var elapsed = currentTime - startTime;
                var elapsedMinutes = elapsed.TotalMinutes;

                // Calculate progress percentage (if timeout is set)
                var progressPercentage = timeoutMinutes > 0
                    ? Math.Min((int)(elapsedMinutes / timeoutMinutes * 100), 99)
                    : (int)(elapsedMinutes % 100); // Cycle 0-99 if no timeout

                // Update progress every interval
                if ((currentTime - lastUpdateTime).TotalSeconds >= updateIntervalSeconds)
                {
                    var statusMessage = timeoutMinutes > 0
                        ? $"PID {processId} running for {elapsed.TotalMinutes:F1} minutes (timeout: {timeoutMinutes} min)"
                        : $"PID {processId} running for {elapsed.TotalMinutes:F1} minutes";

                    SafeWriteProgress(cmdlet, progressTitle,
                        progressDescription,
                        statusMessage,
                        progressPercentage);

                    SafeWriteVerbose(cmdlet, ServiceName,
                        $"Process {processId} status: Running for {elapsed.TotalMinutes:F1} minutes");

                    lastUpdateTime = currentTime;
                }

                // Check if we should show command line periodically (every 60 seconds)
                if (elapsed.TotalSeconds % 60 < updateIntervalSeconds)
                {
                    SafeWriteVerbose(cmdlet, ServiceName,
                        $"Command line: {commandLine}");
                }

                // Wait before next check
                Thread.Sleep(TimeSpan.FromSeconds(updateIntervalSeconds));
            }

            // Process has exited, get final status
            var finalElapsed = DateTime.UtcNow - startTime;
            var exitCode = process.ExitCode;

            SafeWriteProgress(cmdlet, progressTitle,
                "Process completed",
                $"PID {processId} finished after {finalElapsed.TotalMinutes:F1} minutes with exit code {exitCode}",
                100);
        }

        /// <summary>
        /// Writes progress from the monitoring thread. Cmdlet write APIs are thread-affine:
        /// once the pipeline has stopped, calls from a worker thread throw
        /// PSInvalidOperationException, which would crash the host if left unhandled.
        /// </summary>
        private static void SafeWriteProgress(PSCmdlet? cmdlet, string activity, string description, string status, int percent)
        {
            if (cmdlet == null) return;
            try
            {
                LoggingService.WriteProgress(cmdlet, activity, description, status, percent);
            }
            catch (InvalidOperationException)
            {
                // Pipeline no longer accepting writes; monitoring output is moot.
            }
        }

        /// <summary>
        /// Thread-safe counterpart to LoggingService.WriteVerbose for calls made
        /// outside the cmdlet's pipeline thread. See SafeWriteProgress.
        /// </summary>
        private static void SafeWriteVerbose(PSCmdlet? cmdlet, string component, string message)
        {
            if (cmdlet == null) return;
            try
            {
                LoggingService.WriteVerbose(cmdlet, component, message);
            }
            catch (InvalidOperationException)
            {
                // Pipeline no longer accepting writes; monitoring output is moot.
            }
        }

        /// <summary>
        /// Thread-safe counterpart to LoggingService.WriteWarning for calls made
        /// outside the cmdlet's pipeline thread. See SafeWriteProgress.
        /// </summary>
        private static void SafeWriteWarning(PSCmdlet? cmdlet, string component, string message)
        {
            if (cmdlet == null) return;
            try
            {
                LoggingService.WriteWarning(cmdlet, component, message);
            }
            catch (InvalidOperationException)
            {
                // Pipeline no longer accepting writes; monitoring output is moot.
            }
        }



        /// <summary>
        /// Gets information about a running process
        /// </summary>
        /// <param name="processId">Process ID to query</param>
        /// <param name="cmdlet">Cmdlet for logging</param>
        /// <returns>Process information or null if not found</returns>
        public ProcessInfo? GetProcessInfo(int processId, PSCmdlet? cmdlet = null)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                
                return new ProcessInfo
                {
                    ProcessId = processId,
                    ProcessName = process.ProcessName,
                    StartTime = process.StartTime,
                    HasExited = process.HasExited,
                    ExitCode = process.HasExited ? (int?)process.ExitCode : null,
                    WorkingSet = process.WorkingSet64,
                    TotalProcessorTime = process.TotalProcessorTime
                };
            }
            catch (ArgumentException)
            {
                // Process not found
                SafeWriteVerbose(cmdlet, ServiceName,
                    $"Process {processId} not found or has exited");
                return null;
            }
            catch (Exception ex)
            {
                SafeWriteWarning(cmdlet, ServiceName,
                    $"Failed to get process info for PID {processId}: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Information about a running process
    /// </summary>
    public class ProcessInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public bool HasExited { get; set; }
        public int? ExitCode { get; set; }
        public long WorkingSet { get; set; }
        public TimeSpan TotalProcessorTime { get; set; }
        
        public TimeSpan RunningTime => HasExited ? TimeSpan.Zero : DateTime.Now - StartTime;
        
        public string WorkingSetFormatted
        {
            get
            {
                const long MB = 1024 * 1024;
                const long GB = MB * 1024;
                
                if (WorkingSet >= GB)
                    return $"{WorkingSet / (double)GB:F1} GB";
                if (WorkingSet >= MB)
                    return $"{WorkingSet / (double)MB:F0} MB";
                
                return $"{WorkingSet / 1024:F0} KB";
            }
        }
    }
}
