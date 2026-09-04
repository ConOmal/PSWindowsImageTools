using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Queries and changes Windows Reserved Storage state in a mounted image via dism.exe.
    /// Microsoft.Dism.dll (3.3.12) exposes no reserved-storage API, so the DISM CLI is used,
    /// mirroring ComponentStoreService.Cleanup. Argument building, output parsing, state
    /// mapping and error-text extraction are pure/internal-static and unit-tested; the
    /// Process.Start invocation is the only thin, non-unit-tested surface.
    /// </summary>
    public class ReservedStorageService
    {
        private const string ServiceName = "ReservedStorageService";
        private const string UnicodeBom = "\uFEFF";
        private readonly ModuleCallbacks _callbacks;

        /// <summary>
        /// Creates the service with explicit callbacks
        /// </summary>
        public ReservedStorageService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Builds the dism.exe argument string for querying reserved-storage state. Pure.
        /// </summary>
        internal static string BuildGetReservedStorageStateArguments(string imagePath)
        {
            return $"/Image:\"{imagePath}\" /Get-ReservedStorageState";
        }

        /// <summary>
        /// Builds the dism.exe argument string for enabling/disabling reserved storage. Pure.
        /// </summary>
        internal static string BuildSetReservedStorageStateArguments(string imagePath, bool enable)
        {
            var state = enable ? "Enabled" : "Disabled";
            return $"/Image:\"{imagePath}\" /Set-ReservedStorageState:{state}";
        }

        /// <summary>
        /// Parses the DISM "Reserved Storage is: Enabled/Disabled" state from command output.
        /// Pure. Returns null when the line is missing or the value is unrecognized.
        /// </summary>
        internal static ReservedStorageState? ParseReservedStorageState(string? output)
        {
            if (output is null || string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            const string marker = "Reserved Storage is";

            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim().TrimStart(UnicodeBom.ToCharArray());

                var markerIndex = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex < 0)
                {
                    continue;
                }

                var colonIndex = line.IndexOf(':', markerIndex);
                if (colonIndex < 0)
                {
                    continue;
                }

                var value = line.Substring(colonIndex + 1).Trim();
                if (string.Equals(value, "Enabled", StringComparison.OrdinalIgnoreCase))
                {
                    return ReservedStorageState.Enabled;
                }

                if (string.Equals(value, "Disabled", StringComparison.OrdinalIgnoreCase))
                {
                    return ReservedStorageState.Disabled;
                }
            }

            return null;
        }

        /// <summary>
        /// Parses a reserved-storage size from command output (defensive: current DISM
        /// /Get-ReservedStorageState output reports state only). Looking for a "size"-named
        /// line with a numeric token and a KB/MB/GB/bytes unit. Returns null when absent.
        /// Pure.
        /// </summary>
        internal static long? ParseReservedStorageSizeBytes(string? output)
        {
            if (output is null || string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim();

                if (line.IndexOf("size", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (TryParseSize(line, out var bytes))
                {
                    return bytes;
                }
            }

            return null;
        }

        /// <summary>
        /// Extracts a human-readable DISM error from command output. Pure.
        /// </summary>
        internal static string ExtractErrorMessage(string? output, int exitCode)
        {
            if (output is null || string.IsNullOrWhiteSpace(output))
            {
                return $"dism.exe exited with code {exitCode}";
            }

            var lines = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToArray();

            if (lines.Length == 0)
            {
                return $"dism.exe exited with code {exitCode}";
            }

            var errorLine = lines.LastOrDefault(l => l.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0);
            return $"{errorLine ?? lines[lines.Length - 1]} (exit code {exitCode})";
        }

        /// <summary>
        /// Queries the reserved-storage state of a mounted image via dism.exe
        /// </summary>
        /// <param name="imagePath">Path to the mounted image directory</param>
        /// <param name="cmdlet">Cmdlet for logging, or null for silent output</param>
        /// <exception cref="InvalidOperationException">DISM failed or returned an unparseable state</exception>
        public WindowsImageReservedStorage GetState(string imagePath, PSCmdlet? cmdlet = null)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                throw new ArgumentException("Image path cannot be null or empty.", nameof(imagePath));
            }

            if (!Directory.Exists(imagePath))
            {
                throw new DirectoryNotFoundException($"Mounted image directory not found: {imagePath}");
            }

            var args = BuildGetReservedStorageStateArguments(imagePath);
            _callbacks.Verbose?.Invoke($"Querying reserved storage state: dism.exe {args}");

            var startTime = LoggingService.LogOperationStartWithTimestamp(cmdlet, ServiceName, "Reserved Storage state query", imagePath);
            var (exitCode, output) = RunDism(args, cmdlet);
            LoggingService.LogOperationCompleteWithTimestamp(cmdlet, ServiceName, "Reserved Storage state query", startTime, $"exit code {exitCode}");

            var state = ParseReservedStorageState(output);
            if (exitCode != 0 || state == null)
            {
                var message = ExtractErrorMessage(output, exitCode);
                var exception = new InvalidOperationException($"Failed to query reserved storage state for {imagePath}: {message}");
                _callbacks.Error?.Invoke(exception, message);
                throw exception;
            }

            var result = new WindowsImageReservedStorage
            {
                ImagePath = imagePath,
                State = state.Value,
                SizeBytes = ParseReservedStorageSizeBytes(output)
            };

            _callbacks.Verbose?.Invoke($"Reserved storage state for {imagePath}: {result}");
            return result;
        }

        /// <summary>
        /// Enables or disables reserved storage in a mounted image via dism.exe. Always
        /// returns a result object; DISM-reported failures are reflected via Success/ErrorMessage.
        /// </summary>
        /// <param name="imagePath">Path to the mounted image directory</param>
        /// <param name="enable">True to enable, false to disable reserved storage</param>
        /// <param name="cmdlet">Cmdlet for logging, or null for silent output</param>
        public ReservedStorageOperationResult SetState(string imagePath, bool enable, PSCmdlet? cmdlet = null)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                throw new ArgumentException("Image path cannot be null or empty.", nameof(imagePath));
            }

            if (!Directory.Exists(imagePath))
            {
                throw new DirectoryNotFoundException($"Mounted image directory not found: {imagePath}");
            }

            var args = BuildSetReservedStorageStateArguments(imagePath, enable);
            var operation = enable ? "EnableReservedStorage" : "DisableReservedStorage";
            var requested = enable ? ReservedStorageState.Enabled : ReservedStorageState.Disabled;

            _callbacks.Verbose?.Invoke($"Setting reserved storage state to {requested}: dism.exe {args}");

            var startTime = LoggingService.LogOperationStartWithTimestamp(cmdlet, ServiceName, operation, imagePath);
            var (exitCode, output) = RunDism(args, cmdlet);
            LoggingService.LogOperationCompleteWithTimestamp(cmdlet, ServiceName, operation, startTime, $"exit code {exitCode}");

            var success = exitCode == 0;
            if (!success)
            {
                var message = ExtractErrorMessage(output, exitCode);
                _callbacks.Warning?.Invoke($"Failed to {(enable ? "enable" : "disable")} reserved storage for {imagePath}: {message}");
            }

            return new ReservedStorageOperationResult
            {
                ImagePath = imagePath,
                Operation = operation,
                RequestedState = requested,
                Success = success,
                ExitCode = exitCode,
                ErrorMessage = success ? null : ExtractErrorMessage(output, exitCode)
            };
        }

        /// <summary>
        /// Parses a single output line for a size value (bytes with optional KB/MB/GB unit).
        /// Pure. Scans numeric tokens from the end of the segment after the first colon.
        /// </summary>
        private static bool TryParseSize(string line, out long bytes)
        {
            bytes = 0;

            var separator = line.IndexOf(':');
            var segment = separator >= 0 ? line.Substring(separator + 1) : line;
            var tokens = segment
                .Split(new[] { ' ', '\t', '(', ')', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .ToArray();

            for (var i = tokens.Length - 1; i >= 0; i--)
            {
                var normalized = tokens[i].Split(':')[0].Trim();
                if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    var unit = i + 1 < tokens.Length ? tokens[i + 1].ToUpperInvariant() : string.Empty;
                    long multiplier;
                    if (unit.StartsWith("GB", StringComparison.Ordinal))
                    {
                        multiplier = 1024L * 1024L * 1024L;
                    }
                    else if (unit.StartsWith("MB", StringComparison.Ordinal))
                    {
                        multiplier = 1024L * 1024L;
                    }
                    else if (unit.StartsWith("KB", StringComparison.Ordinal))
                    {
                        multiplier = 1024L;
                    }
                    else if (unit.StartsWith("BYTES", StringComparison.Ordinal) || unit.StartsWith("B", StringComparison.Ordinal))
                    {
                        multiplier = 1L;
                    }
                    else
                    {
                        multiplier = 1L;
                    }

                    bytes = (long)(value * multiplier);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Runs dism.exe with the given arguments and captures combined stdout+stderr
        /// </summary>
        private (int ExitCode, string Output) RunDism(string arguments, PSCmdlet? cmdlet)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "dism.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start dism.exe");
            }

            var startTime = DateTime.UtcNow;
            _callbacks.Verbose?.Invoke($"dism.exe {arguments} started (PID {process.Id})");

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(180000))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch
                {
                    // Best effort termination
                }

                throw new TimeoutException($"dism.exe timed out after 3 minutes: {arguments}");
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                _callbacks.Verbose?.Invoke($"dism.exe output: {output.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                _callbacks.Warning?.Invoke($"dism.exe error output: {error.Trim()}");
            }

            _callbacks.Verbose?.Invoke($"dism.exe exited with code {process.ExitCode} after {(DateTime.UtcNow - startTime).TotalSeconds:F1}s");

            var combined = output;
            if (!string.IsNullOrWhiteSpace(error))
            {
                combined = combined.TrimEnd() + Environment.NewLine + error;
            }

            return (process.ExitCode, combined);
        }
    }
}