using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Service for parsing .reg files and managing registry operations
    /// </summary>
    public class RegistryOperationService
    {
        private const string ServiceName = "RegistryOperationService";
        private readonly ModuleCallbacks _callbacks;

        /// <summary>
        /// Creates the service with explicit callbacks
        /// </summary>
        public RegistryOperationService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Parses .reg files and returns registry operations
        /// </summary>
        public List<RegistryOperation> ParseRegFiles(FileInfo[] regFiles, PSCmdlet cmdlet)
        {
            return ParseRegFiles(regFiles, ModuleCallbacks.FromCmdlet(cmdlet));
        }

        /// <summary>
        /// Parses .reg files and returns registry operations
        /// </summary>
        public List<RegistryOperation> ParseRegFiles(FileInfo[] regFiles, ModuleCallbacks callbacks)
        {
            var operations = new List<RegistryOperation>();
            var totalFiles = regFiles.Length;

            callbacks.Verbose?.Invoke($"Starting to parse {totalFiles} .reg files");

            for (int i = 0; i < regFiles.Length; i++)
            {
                var regFile = regFiles[i];
                var progress = (int)((double)(i + 1) / totalFiles * 100);

                callbacks.Progress?.Invoke(progress, "Parsing Registry Files", $"[{i + 1} of {totalFiles}] - {regFile.Name}: Parsing {regFile.FullName} ({progress}%)");

                try
                {
                    var fileOperations = ParseSingleRegFile(regFile, callbacks);
                    operations.AddRange(fileOperations);

                    callbacks.Verbose?.Invoke($"[{i + 1} of {totalFiles}] - Parsed {fileOperations.Count} operations from {regFile.Name}");
                }
                catch (Exception ex)
                {
                    callbacks.Warning?.Invoke($"[{i + 1} of {totalFiles}] - Failed to parse {regFile.Name}: {ex.Message}");
                }
            }

            callbacks.Verbose?.Invoke($"Parsing completed. Found {operations.Count} total registry operations across {totalFiles} files");

            return operations;
        }

        /// <summary>
        /// Parses a single .reg file
        /// </summary>
        private List<RegistryOperation> ParseSingleRegFile(FileInfo regFile, ModuleCallbacks callbacks)
        {
            var operations = new List<RegistryOperation>();

            if (!regFile.Exists)
            {
                throw new FileNotFoundException($"Registry file not found: {regFile.FullName}");
            }

            callbacks.Verbose?.Invoke($"Parsing registry file: {regFile.FullName}");

            var lines = File.ReadAllLines(regFile.FullName, Encoding.UTF8);
            string? currentKey = null;
            int lineNumber = 0;

            foreach (var rawLine in lines)
            {
                lineNumber++;
                var line = rawLine.Trim();

                // Skip empty lines and comments
                if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("Windows Registry Editor"))
                    continue;

                try
                {
                    // Check if this is a key line
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        currentKey = line.Substring(1, line.Length - 2);
                        
                        // Check for key deletion (starts with -)
                        if (currentKey.StartsWith("-"))
                        {
                            var keyToDelete = currentKey.Substring(1);
                            var deleteOperation = CreateKeyDeleteOperation(keyToDelete, rawLine, lineNumber);
                            if (deleteOperation != null)
                            {
                                operations.Add(deleteOperation);
                            }
                            currentKey = null; // Don't process values for deleted keys
                        }
                        continue;
                    }

                    // Process value lines
                    if (!string.IsNullOrEmpty(currentKey) && line.Contains("="))
                    {
                        var operation = ParseValueLine(currentKey!, line, rawLine, lineNumber);
                        if (operation != null)
                        {
                            operations.Add(operation);
                        }
                    }
                }
                catch (Exception ex)
                {
                    callbacks.Warning?.Invoke($"Error parsing line {lineNumber} in {regFile.Name}: {ex.Message}");
                }
            }

            return operations;
        }

        /// <summary>
        /// Creates a key deletion operation
        /// </summary>
        private RegistryOperation? CreateKeyDeleteOperation(string keyPath, string originalLine, int lineNumber)
        {
            var parts = SplitRegistryPath(keyPath);
            if (parts == null) return null;

            return new RegistryOperation
            {
                Operation = RegistryOperationType.RemoveKey,
                Hive = parts.Value.hive,
                Key = parts.Value.key,
                ValueName = string.Empty,
                Value = null,
                ValueType = RegistryValueKind.Unknown,
                OriginalLine = originalLine,
                LineNumber = lineNumber
            };
        }

        /// <summary>
        /// Parses a value line from a .reg file
        /// </summary>
        private RegistryOperation? ParseValueLine(string currentKey, string line, string originalLine, int lineNumber)
        {
            var parts = SplitRegistryPath(currentKey);
            if (parts == null) return null;

            // Split the line into name and value
            var equalIndex = line.IndexOf('=');
            if (equalIndex == -1) return null;

            var valueName = line.Substring(0, equalIndex).Trim().Trim('"');
            var valueData = line.Substring(equalIndex + 1).Trim();

            // Handle default value
            if (valueName == "@")
            {
                valueName = string.Empty;
            }

            // Check for value deletion
            if (valueData == "-")
            {
                return new RegistryOperation
                {
                    Operation = RegistryOperationType.Remove,
                    Hive = parts.Value.hive,
                    Key = parts.Value.key,
                    ValueName = valueName,
                    Value = null,
                    ValueType = RegistryValueKind.Unknown,
                    OriginalLine = originalLine,
                    LineNumber = lineNumber
                };
            }

            // Parse the value and determine type
            var (value, valueType) = ParseRegistryValue(valueData);

            return new RegistryOperation
            {
                Operation = RegistryOperationType.Create, // Will be changed to Modify if key exists
                Hive = parts.Value.hive,
                Key = parts.Value.key,
                ValueName = valueName,
                Value = value,
                ValueType = valueType,
                OriginalLine = originalLine,
                LineNumber = lineNumber
            };
        }

        /// <summary>
        /// Splits a registry path into hive and key components
        /// </summary>
        private (string hive, string key)? SplitRegistryPath(string path)
        {
            var parts = path.Split(new char[] { '\\' }, 2);
            if (parts.Length < 1) return null;

            var hive = parts[0];
            var key = parts.Length > 1 ? parts[1] : string.Empty;

            return (hive, key);
        }

        /// <summary>
        /// Parses registry value data and determines the type
        /// </summary>
        private (object? value, RegistryValueKind type) ParseRegistryValue(string valueData)
        {
            // String value (quoted)
            if (valueData.StartsWith("\"") && valueData.EndsWith("\""))
            {
                var stringValue = valueData.Substring(1, valueData.Length - 2);
                return (stringValue, RegistryValueKind.String);
            }

            // DWORD value
            if (valueData.StartsWith("dword:", StringComparison.OrdinalIgnoreCase))
            {
                var hexValue = valueData.Substring(6);
                if (uint.TryParse(hexValue, System.Globalization.NumberStyles.HexNumber, null, out var dwordValue))
                {
                    return (dwordValue, RegistryValueKind.DWord);
                }
            }

            // QWORD value
            if (valueData.StartsWith("qword:", StringComparison.OrdinalIgnoreCase))
            {
                var hexValue = valueData.Substring(6);
                if (ulong.TryParse(hexValue, System.Globalization.NumberStyles.HexNumber, null, out var qwordValue))
                {
                    return (qwordValue, RegistryValueKind.QWord);
                }
            }

            // Binary value (hex)
            if (valueData.StartsWith("hex:", StringComparison.OrdinalIgnoreCase))
            {
                var hexData = valueData.Substring(4).Replace(",", "").Replace(" ", "").Replace("\\", "");
                try
                {
                    var bytes = ConvertHexStringToBytes(hexData);
                    return (bytes, RegistryValueKind.Binary);
                }
                catch
                {
                    // Fall back to string if hex parsing fails
                }
            }

            // Multi-string value
            if (valueData.StartsWith("hex(7):", StringComparison.OrdinalIgnoreCase))
            {
                var hexData = valueData.Substring(7).Replace(",", "").Replace(" ", "").Replace("\\", "");
                try
                {
                    var bytes = ConvertHexStringToBytes(hexData);
                    var text = Encoding.Unicode.GetString(bytes);
                    var strings = text.Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                    return (strings, RegistryValueKind.MultiString);
                }
                catch
                {
                    // Fall back to string if parsing fails
                }
            }

            // Expandable string value
            if (valueData.StartsWith("hex(2):", StringComparison.OrdinalIgnoreCase))
            {
                var hexData = valueData.Substring(7).Replace(",", "").Replace(" ", "").Replace("\\", "");
                try
                {
                    var bytes = ConvertHexStringToBytes(hexData);
                    var text = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                    return (text, RegistryValueKind.ExpandString);
                }
                catch
                {
                    // Fall back to string if parsing fails
                }
            }

            // Default to string
            return (valueData, RegistryValueKind.String);
        }

        /// <summary>
        /// Converts hex string to byte array (compatible with older .NET versions)
        /// </summary>
        private byte[] ConvertHexStringToBytes(string hexString)
        {
            if (hexString.Length % 2 != 0)
                throw new ArgumentException("Hex string must have even length");

            var bytes = new byte[hexString.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
            }
            return bytes;
        }
    }
}
