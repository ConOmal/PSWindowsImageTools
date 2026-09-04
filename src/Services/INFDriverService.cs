using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.RegularExpressions;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Service for INF driver file operations including parsing and scanning
    /// </summary>
    public class INFDriverService
    {
        private const string ServiceName = "INFDriverService";
        private readonly ModuleCallbacks _callbacks;

        /// <summary>
        /// Creates the service with explicit callbacks
        /// </summary>
        public INFDriverService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Scans directories for INF files
        /// </summary>
        /// <param name="directories">Directories to scan</param>
        /// <param name="recurse">Whether to scan recursively</param>
        /// <param name="parseINF">Whether to parse INF files for metadata</param>
        /// <param name="cmdlet">Cmdlet for progress reporting</param>
        /// <returns>List of INF driver information objects</returns>
        public List<INFDriverInfo> ScanForINFDrivers(
            DirectoryInfo[] directories,
            bool recurse,
            bool parseINF,
            PSCmdlet cmdlet)
        {
            return ScanForINFDrivers(directories, recurse, parseINF, ModuleCallbacks.FromCmdlet(cmdlet));
        }

        /// <summary>
        /// Scans directories for INF files using callbacks
        /// </summary>
        /// <param name="directories">Directories to scan</param>
        /// <param name="recurse">Whether to scan recursively</param>
        /// <param name="parseINF">Whether to parse INF files for metadata</param>
        /// <param name="callbacks">Callbacks for logging and progress</param>
        /// <returns>List of INF driver information objects</returns>
        public List<INFDriverInfo> ScanForINFDrivers(
            DirectoryInfo[] directories,
            bool recurse,
            bool parseINF,
            ModuleCallbacks callbacks)
        {
            var allDrivers = new List<INFDriverInfo>();
            var totalDirectories = directories.Length;

            callbacks.Verbose?.Invoke($"Starting INF driver scan of {totalDirectories} directories (Recurse: {recurse}, Parse: {parseINF})");

            for (int i = 0; i < directories.Length; i++)
            {
                var directory = directories[i];
                var progress = (int)((double)(i + 1) / totalDirectories * 100);

                callbacks.Progress?.Invoke(progress, "Scanning for INF Drivers",
                    $"[{i + 1} of {totalDirectories}] - {directory.Name}: Scanning {directory.FullName} ({progress}%)");

                try
                {
                    var driversInDirectory = ScanSingleDirectory(directory, recurse, parseINF, callbacks);
                    allDrivers.AddRange(driversInDirectory);

                    callbacks.Verbose?.Invoke($"[{i + 1} of {totalDirectories}] - Found {driversInDirectory.Count} INF files in {directory.FullName}");
                }
                catch (Exception ex)
                {
                    callbacks.Warning?.Invoke($"[{i + 1} of {totalDirectories}] - Failed to scan directory {directory.FullName}: {ex.Message}");
                }
            }

            callbacks.Verbose?.Invoke($"Scan completed. Found {allDrivers.Count} total INF files across {totalDirectories} directories");

            return allDrivers;
        }

        /// <summary>
        /// Scans a single directory for INF files
        /// </summary>
        private List<INFDriverInfo> ScanSingleDirectory(
            DirectoryInfo directory,
            bool recurse,
            bool parseINF,
            ModuleCallbacks callbacks)
        {
            var drivers = new List<INFDriverInfo>();

            if (!directory.Exists)
            {
                callbacks.Warning?.Invoke($"Directory does not exist: {directory.FullName}");
                return drivers;
            }

            var searchOption = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var infFiles = directory.GetFiles("*.inf", searchOption);

            callbacks.Verbose?.Invoke($"Found {infFiles.Length} INF files in {directory.FullName} (recursive: {recurse})");

            for (int i = 0; i < infFiles.Length; i++)
            {
                var infFile = infFiles[i];

                try
                {
                    var driverInfo = new INFDriverInfo
                    {
                        INFFile = infFile
                    };

                    if (parseINF)
                    {
                        try
                        {
                            driverInfo.ParsedInfo = ParseINFFile(infFile, callbacks);
                        }
                        catch (Exception parseEx)
                        {
                            callbacks.Warning?.Invoke($"Failed to parse INF file {infFile.FullName}: {parseEx.Message}");
                            // Still add the driver info even if parsing failed
                            driverInfo.ParsedInfo = null;
                        }
                    }

                    drivers.Add(driverInfo);
                }
                catch (Exception ex)
                {
                    callbacks.Warning?.Invoke($"Failed to process INF file {infFile.FullName}: {ex.Message}");
                }
            }

            return drivers;
        }

        /// <summary>
        /// Parses an INF file to extract driver metadata
        /// </summary>
        /// <param name="infFile">INF file to parse</param>
        /// <param name="cmdlet">Cmdlet for logging</param>
        /// <returns>Parsed INF driver information</returns>
        public INFDriverParseResult ParseINFFile(FileInfo infFile, PSCmdlet cmdlet)
        {
            return ParseINFFile(infFile, ModuleCallbacks.FromCmdlet(cmdlet));
        }

        /// <summary>
        /// Parses an INF file to extract driver metadata using callbacks
        /// </summary>
        /// <param name="infFile">INF file to parse</param>
        /// <param name="callbacks">Callbacks for logging</param>
        /// <returns>Parsed INF driver information</returns>
        public INFDriverParseResult ParseINFFile(FileInfo infFile, ModuleCallbacks callbacks)
        {
            var result = new INFDriverParseResult();

            try
            {
                callbacks.Verbose?.Invoke($"Parsing INF file: {infFile.FullName}");

                // Validate file exists and is readable
                if (!infFile.Exists)
                {
                    throw new FileNotFoundException($"INF file not found: {infFile.FullName}");
                }

                var lines = File.ReadAllLines(infFile.FullName);
                var sections = ParseINFSections(lines);

                // Parse Version section
                if (sections.ContainsKey("Version"))
                {
                    ParseVersionSection(sections["Version"], result);
                }

                // Parse Strings section for localized strings
                var strings = new Dictionary<string, string>();
                if (sections.ContainsKey("Strings"))
                {
                    strings = ParseStringsSection(sections["Strings"]);
                }

                // Resolve string references
                ResolveStringReferences(result, strings);

                // Parse Manufacturer section for hardware IDs
                if (sections.ContainsKey("Manufacturer"))
                {
                    ParseManufacturerSection(sections["Manufacturer"], sections, result);
                }

                // Check for catalog file
                try
                {
                    CheckForCatalogFile(infFile, result);
                }
                catch (Exception catalogEx)
                {
                    callbacks.Verbose?.Invoke($"Could not check catalog file for {infFile.Name}: {catalogEx.Message}");
                    result.IsSigned = false;
                }

                callbacks.Verbose?.Invoke($"Successfully parsed INF: {result.DriverName} v{result.Version}");
            }
            catch (Exception ex)
            {
                result.ParseErrors.Add($"Failed to parse INF file: {ex.Message}");
                callbacks.Warning?.Invoke($"Error parsing INF file {infFile.FullName}: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Parses INF file into sections
        /// </summary>
        private Dictionary<string, List<string>> ParseINFSections(string[] lines)
        {
            var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string currentSection = string.Empty;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // Skip empty lines and comments
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith(";"))
                    continue;

                // Check for section header
                if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                {
                    currentSection = trimmedLine.Substring(1, trimmedLine.Length - 2);
                    if (!sections.ContainsKey(currentSection))
                    {
                        sections[currentSection] = new List<string>();
                    }
                }
                else if (!string.IsNullOrEmpty(currentSection))
                {
                    sections[currentSection].Add(trimmedLine);
                }
            }

            return sections;
        }

        /// <summary>
        /// Parses the Version section of an INF file
        /// </summary>
        private void ParseVersionSection(List<string> versionLines, INFDriverParseResult result)
        {
            foreach (var line in versionLines)
            {
                var parts = line.Split(new char[] { '=' }, 2);
                if (parts.Length != 2) continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                switch (key.ToLowerInvariant())
                {
                    case "driverver":
                        ParseDriverVersion(value, result);
                        break;
                    case "provider":
                        result.Provider = value.Trim('"', '%');
                        break;
                    case "class":
                        result.Class = value.Trim('"', '%');
                        break;
                    case "classguid":
                        result.ClassGuid = value.Trim(new char[] { '"', '{', '}' });
                        break;
                    case "catalogfile":
                        // Store the cleaned filename temporarily for later processing
                        result.AdditionalProperties["_CatalogFileName"] = CleanCatalogFileName(value);
                        break;
                    default:
                        // Store additional properties
                        result.AdditionalProperties[key] = value;
                        break;
                }
            }
        }

        /// <summary>
        /// Parses driver version information using FormatUtilityService
        /// </summary>
        private void ParseDriverVersion(string driverVer, INFDriverParseResult result)
        {
            // DriverVer format: MM/DD/YYYY,Version
            var parts = driverVer.Split(',');
            if (parts.Length >= 1)
            {
                // Use FormatUtilityService for robust date parsing
                result.DriverDate = FormatUtilityService.ParseDate(parts[0].Trim());
            }
            if (parts.Length >= 2)
            {
                var versionString = parts[1].Trim();

                // Use FormatUtilityService for robust version parsing
                result.Version = FormatUtilityService.ParseVersion(versionString);
            }
        }

        /// <summary>
        /// Parses the Strings section for localized strings
        /// </summary>
        private Dictionary<string, string> ParseStringsSection(List<string> stringLines)
        {
            var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in stringLines)
            {
                var parts = line.Split(new char[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim().Trim('"');
                    strings[key] = value;
                }
            }

            return strings;
        }

        /// <summary>
        /// Resolves string references in the parsed result
        /// </summary>
        private void ResolveStringReferences(INFDriverParseResult result, Dictionary<string, string> strings)
        {
            result.Provider = ResolveStringReference(result.Provider, strings);
            result.Class = ResolveStringReference(result.Class, strings);
            result.DriverName = ResolveStringReference(result.DriverName, strings);
        }

        /// <summary>
        /// Resolves a single string reference
        /// </summary>
        private string ResolveStringReference(string value, Dictionary<string, string> strings)
        {
            if (string.IsNullOrEmpty(value)) return value;

            // Check for string reference format %StringKey%
            var match = Regex.Match(value, @"%([^%]+)%");
            if (match.Success)
            {
                var key = match.Groups[1].Value;
                if (strings.ContainsKey(key))
                {
                    return strings[key];
                }
            }

            return value;
        }

        /// <summary>
        /// Parses the Manufacturer section for hardware IDs and architectures
        /// </summary>
        private void ParseManufacturerSection(List<string> manufacturerLines,
            Dictionary<string, List<string>> sections, INFDriverParseResult result)
        {
            var allHardwareIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allCompatibleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allArchitectures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in manufacturerLines)
            {
                var parts = line.Split(new char[] { '=' }, 2);
                if (parts.Length != 2) continue;

                var manufacturerName = parts[0].Trim().Trim(new char[] { '"', '%' });
                var deviceSections = parts[1].Trim();

                // Set driver name from first manufacturer if not already set
                if (string.IsNullOrEmpty(result.DriverName))
                {
                    result.DriverName = manufacturerName;
                }

                // Parse device sections (e.g., "Intel,NTamd64,NTx86")
                var sectionNames = deviceSections.Split(',');
                var baseSectionName = sectionNames[0].Trim(); // First part is the base name

                for (int i = 0; i < sectionNames.Length; i++)
                {
                    var sectionName = sectionNames[i].Trim();

                    string actualSectionName;
                    if (i == 0)
                    {
                        // First section is the base name (e.g., "Intel")
                        actualSectionName = sectionName;
                    }
                    else
                    {
                        // Subsequent sections are architecture-specific (e.g., "Intel.NTamd64")
                        actualSectionName = $"{baseSectionName}.{sectionName}";
                    }

                    // Extract architecture from section name
                    ExtractArchitectureFromSectionName(sectionName, allArchitectures);

                    // Parse the actual device section for hardware IDs
                    if (sections.ContainsKey(actualSectionName))
                    {
                        ParseDeviceSection(sections[actualSectionName], allHardwareIds, allCompatibleIds);
                    }
                }
            }

            // Convert sets to lists
            result.HardwareIds.AddRange(allHardwareIds);
            result.CompatibleIds.AddRange(allCompatibleIds);
            result.SupportedArchitectures.AddRange(allArchitectures);
        }

        /// <summary>
        /// Extracts architecture information from section names
        /// </summary>
        private void ExtractArchitectureFromSectionName(string sectionName, HashSet<string> architectures)
        {
            var lowerSection = sectionName.ToLowerInvariant();

            // Common architecture patterns in INF files
            if (lowerSection.Contains("ntamd64") || lowerSection.Contains("nt.amd64"))
            {
                architectures.Add("AMD64");
            }
            else if (lowerSection.Contains("ntx86") || lowerSection.Contains("nt.x86"))
            {
                architectures.Add("x86");
            }
            else if (lowerSection.Contains("ntarm64") || lowerSection.Contains("nt.arm64"))
            {
                architectures.Add("ARM64");
            }
            else if (lowerSection.Contains("ntarm") || lowerSection.Contains("nt.arm"))
            {
                architectures.Add("ARM");
            }
            else if (lowerSection.Contains("ntia64") || lowerSection.Contains("nt.ia64"))
            {
                architectures.Add("IA64");
            }
            else if (lowerSection.EndsWith(".nt") || (!lowerSection.Contains("nt") && !lowerSection.Contains(".")))
            {
                // Generic NT or no architecture specified usually means x86/AMD64
                architectures.Add("x86");
                architectures.Add("AMD64");
            }
        }

        /// <summary>
        /// Parses a device section for hardware IDs and compatible IDs
        /// </summary>
        private void ParseDeviceSection(List<string> deviceLines, HashSet<string> hardwareIds, HashSet<string> compatibleIds)
        {
            foreach (var line in deviceLines)
            {
                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(";"))
                    continue;

                // Device lines format: %DeviceDesc%=InstallSection,HardwareID[,CompatibleID1,CompatibleID2...]
                var parts = line.Split(new char[] { '=' }, 2);
                if (parts.Length != 2) continue;

                var rightSide = parts[1].Trim();
                var idParts = rightSide.Split(',');

                if (idParts.Length < 2) continue;

                // First part after = is the install section, skip it
                // Second part is the primary hardware ID
                var primaryHardwareId = CleanHardwareId(idParts[1]);
                if (IsValidHardwareId(primaryHardwareId))
                {
                    hardwareIds.Add(primaryHardwareId);
                }

                // Remaining parts are compatible IDs
                for (int i = 2; i < idParts.Length; i++)
                {
                    var compatibleId = CleanHardwareId(idParts[i]);
                    if (IsValidHardwareId(compatibleId))
                    {
                        compatibleIds.Add(compatibleId);
                    }
                }
            }
        }

        /// <summary>
        /// Cleans a hardware ID by removing comments and extra whitespace
        /// </summary>
        private string CleanHardwareId(string rawId)
        {
            if (string.IsNullOrWhiteSpace(rawId)) return string.Empty;

            // Remove leading/trailing whitespace and quotes
            var cleaned = rawId.Trim().Trim('"');

            // Remove comments (everything after semicolon)
            var semicolonIndex = cleaned.IndexOf(';');
            if (semicolonIndex >= 0)
            {
                cleaned = cleaned.Substring(0, semicolonIndex);
            }

            // Final trim to remove any trailing whitespace before the comment
            return cleaned.Trim();
        }

        /// <summary>
        /// Cleans a catalog file name by removing comments and extra whitespace
        /// </summary>
        private string CleanCatalogFileName(string rawFileName)
        {
            if (string.IsNullOrWhiteSpace(rawFileName)) return string.Empty;

            // Remove leading/trailing whitespace and quotes
            var cleaned = rawFileName.Trim().Trim('"');

            // Remove comments (everything after semicolon)
            var semicolonIndex = cleaned.IndexOf(';');
            if (semicolonIndex >= 0)
            {
                cleaned = cleaned.Substring(0, semicolonIndex);
            }

            // Final trim to remove any trailing whitespace before the comment
            return cleaned.Trim();
        }

        /// <summary>
        /// Validates if a string looks like a valid hardware ID
        /// </summary>
        private bool IsValidHardwareId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;

            // Hardware IDs typically contain backslashes and follow specific patterns
            // Common patterns: PCI\, USB\, ACPI\, HID\, etc.
            return id.Contains("\\") &&
                   (id.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) ||
                    id.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) ||
                    id.StartsWith("ACPI\\", StringComparison.OrdinalIgnoreCase) ||
                    id.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase) ||
                    id.StartsWith("HDAUDIO\\", StringComparison.OrdinalIgnoreCase) ||
                    id.StartsWith("SCSI\\", StringComparison.OrdinalIgnoreCase) ||
                    id.StartsWith("IDE\\", StringComparison.OrdinalIgnoreCase) ||
                    id.StartsWith("1394\\", StringComparison.OrdinalIgnoreCase) ||
                    id.StartsWith("PCMCIA\\", StringComparison.OrdinalIgnoreCase) ||
                    id.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase) ||
                    id.StartsWith("V1394\\", StringComparison.OrdinalIgnoreCase) ||
                    id.Contains("VEN_") || id.Contains("DEV_") || id.Contains("SUBSYS_"));
        }

        /// <summary>
        /// Checks for the presence of a catalog file and creates FileInfo object
        /// </summary>
        private void CheckForCatalogFile(FileInfo infFile, INFDriverParseResult result)
        {
            // Check if we have a catalog filename stored during parsing
            if (result.AdditionalProperties.TryGetValue("_CatalogFileName", out var catalogFileName) &&
                !string.IsNullOrEmpty(catalogFileName) &&
                infFile.Directory != null)
            {
                try
                {
                    var catalogPath = Path.Combine(infFile.Directory.FullName, catalogFileName);
                    var catalogFileInfo = new FileInfo(catalogPath);

                    result.IsSigned = catalogFileInfo.Exists;

                    // Set the CatalogFile as FileInfo object
                    result.CatalogFile = catalogFileInfo;

                    // Remove the temporary property
                    result.AdditionalProperties.Remove("_CatalogFileName");
                }
                catch (Exception)
                {
                    // If we can't construct the catalog path, assume not signed
                    result.IsSigned = false;
                    result.CatalogFile = null;
                    result.AdditionalProperties.Remove("_CatalogFileName");
                }
            }
            else
            {
                result.IsSigned = false;
                result.CatalogFile = null;
            }
        }
    }
}
