using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Executes BuildRecipe JSON files against Windows images: selects matching images, mounts them
    /// read-write, applies enabled sections in a deterministic order, and saves the image.
    ///
    /// Section application order:
    /// 1. removeAppxPackages   - DISM provisioned AppX removal by regex patterns (matched on DisplayName + PackageName)
    /// 2. copyFiles            - file copy into the mounted image
    /// 3. setWallpapers        - wallpaper/lockscreen configuration
    /// 4. enableFeatures       - Windows feature enablement by regex patterns (matched on FeatureName)
    /// 5. integrateDrivers     - DISM driver integration from directories
    /// 6. integrateUpdates     - package (.cab/.msu) integration from files
    /// 7. integrateFeaturesOnDemand - DISM capability (Features on Demand) addition by capability NAME
    /// 8. registryModifications - offline registry value writes
    /// </summary>
    public class RecipeService
    {
        private const string ServiceName = "RecipeService";
        private readonly ModuleCallbacks _callbacks;

        public RecipeService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Loads a recipe from a JSON file
        /// </summary>
        /// <param name="recipePath">Path to the recipe JSON file</param>
        /// <returns>Deserialized recipe</returns>
        public static BuildRecipe LoadRecipe(string recipePath)
        {
            if (!File.Exists(recipePath))
            {
                throw new FileNotFoundException($"Recipe file not found: {recipePath}");
            }

            var json = File.ReadAllText(recipePath);
            var recipe = JsonConvert.DeserializeObject<BuildRecipe>(json);

            return recipe ?? throw new InvalidOperationException($"Recipe file is empty or invalid: {recipePath}");
        }

        /// <summary>
        /// Saves a recipe to a JSON file
        /// </summary>
        /// <param name="recipe">Recipe to save</param>
        /// <param name="recipePath">Destination path</param>
        public static void SaveRecipe(BuildRecipe recipe, string recipePath)
        {
            var json = JsonConvert.SerializeObject(recipe, Formatting.Indented);
            File.WriteAllText(recipePath, json);
        }

        /// <summary>
        /// Validates the recipe structure and referenced paths
        /// </summary>
        /// <param name="recipe">Recipe to validate</param>
        /// <param name="imagePath">Optional image path to validate against</param>
        /// <returns>List of validation problems (empty when valid)</returns>
        public List<string> ValidateRecipe(BuildRecipe recipe, string? imagePath = null)
        {
            var problems = new List<string>();

            if (recipe == null)
            {
                problems.Add("Recipe is null");
                return problems;
            }

            if (string.IsNullOrWhiteSpace(recipe.Metadata.Name))
            {
                problems.Add("metadata.name is required");
            }

            if (!recipe.ImageFilter.Enabled && !recipe.RemoveAppxPackages.Enabled && !recipe.CopyFiles.Enabled &&
                !recipe.SetWallpapers.Enabled && !recipe.EnableFeatures.Enabled && !recipe.IntegrateDrivers.Enabled &&
                !recipe.IntegrateUpdates.Enabled && !recipe.IntegrateFeaturesOnDemand.Enabled &&
                !recipe.RegistryModifications.Enabled)
            {
                problems.Add("No recipe sections are enabled");
            }

            if (!string.IsNullOrWhiteSpace(recipe.ImageFilter.InclusionExpression))
            {
                if (!IsValidRegex(recipe.ImageFilter.InclusionExpression, problems, "imageFilter.inclusionExpression"))
                {
                    // recorded
                }
            }

            if (!string.IsNullOrWhiteSpace(recipe.ImageFilter.ExclusionExpression))
            {
                IsValidRegex(recipe.ImageFilter.ExclusionExpression, problems, "imageFilter.exclusionExpression");
            }

            foreach (var pattern in recipe.EnableFeatures.Patterns)
            {
                IsValidRegex(pattern, problems, "enableFeatures.patterns[]");
            }

            foreach (var pattern in recipe.RemoveAppxPackages.Patterns)
            {
                IsValidRegex(pattern, problems, "removeAppxPackages.patterns[]");
            }

            if (recipe.CopyFiles.Enabled)
            {
                foreach (var item in recipe.CopyFiles.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Source))
                    {
                        problems.Add("copyFiles.items[].source is required");
                    }
                    else if (!File.Exists(item.Source))
                    {
                        problems.Add($"copyFiles source not found: {item.Source}");
                    }

                    if (string.IsNullOrWhiteSpace(item.Destination))
                    {
                        problems.Add("copyFiles.items[].destination is required");
                    }
                }
            }

            if (recipe.SetWallpapers.Enabled)
            {
                if (string.IsNullOrWhiteSpace(recipe.SetWallpapers.Wallpaper) && string.IsNullOrWhiteSpace(recipe.SetWallpapers.LockScreen))
                {
                    problems.Add("setWallpapers requires 'wallpaper' and/or 'lockScreen'");
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(recipe.SetWallpapers.Wallpaper) && !File.Exists(recipe.SetWallpapers.Wallpaper))
                    {
                        problems.Add($"setWallpapers.wallpaper not found: {recipe.SetWallpapers.Wallpaper}");
                    }

                    if (!string.IsNullOrWhiteSpace(recipe.SetWallpapers.LockScreen) && !File.Exists(recipe.SetWallpapers.LockScreen))
                    {
                        problems.Add($"setWallpapers.lockScreen not found: {recipe.SetWallpapers.LockScreen}");
                    }
                }
            }

            foreach (var path in recipe.IntegrateDrivers.Paths)
            {
                if (!Directory.Exists(path))
                {
                    problems.Add($"integrateDrivers path not found: {path}");
                }
            }

            foreach (var path in recipe.IntegrateUpdates.Paths)
            {
                if (!File.Exists(path))
                {
                    problems.Add($"integrateUpdates path not found: {path}");
                }
            }

            foreach (var name in recipe.IntegrateFeaturesOnDemand.Paths)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    problems.Add("integrateFeaturesOnDemand contains an empty capability name");
                }
            }

            return problems;
        }

        /// <summary>
        /// Selects images matching the recipe's image filter expressions
        /// </summary>
        /// <param name="recipe">Recipe with image filter</param>
        /// <param name="images">Available images</param>
        /// <returns>Selected images</returns>
        public List<WindowsImageInfo> SelectImages(BuildRecipe recipe, List<WindowsImageInfo> images)
        {
            var selected = images;

            if (!string.IsNullOrWhiteSpace(recipe.ImageFilter.InclusionExpression))
            {
                var inclusion = new Regex(recipe.ImageFilter.InclusionExpression, RegexOptions.IgnoreCase);
                selected = selected.Where(i => inclusion.IsMatch(i.Name)).ToList();
            }

            if (!string.IsNullOrWhiteSpace(recipe.ImageFilter.ExclusionExpression))
            {
                var exclusion = new Regex(recipe.ImageFilter.ExclusionExpression, RegexOptions.IgnoreCase);
                selected = selected.Where(i => !exclusion.IsMatch(i.Name)).ToList();
            }

            _callbacks.Verbose?.Invoke($"Image filter selected {selected.Count} of {images.Count} images");
            return selected;
        }

        /// <summary>
        /// Applies the recipe to a single image: mounts read-write, applies enabled sections, saves
        /// </summary>
        /// <param name="recipe">Recipe to apply</param>
        /// <param name="image">Image to apply the recipe to</param>
        /// <param name="mountPath">Directory to mount the image in</param>
        /// <param name="imageService">Unified image service for DISM operations</param>
        /// <param name="cmdlet">Optional cmdlet for legacy service paths (registry/wallpaper)</param>
        /// <returns>Execution result</returns>
        public RecipeImageExecutionResult ExecuteForImage(BuildRecipe recipe, WindowsImageInfo image, string mountPath, IWindowsImageService imageService, System.Management.Automation.PSCmdlet? cmdlet = null)
        {
            var startTime = DateTime.UtcNow;
            var result = new RecipeImageExecutionResult
            {
                RecipeName = recipe.Metadata.Name,
                ImageName = image.Name,
                ImageIndex = image.Index,
                ImagePath = image.SourcePath,
                MountPath = mountPath
            };

            try
            {
                _callbacks.Verbose?.Invoke($"[{image.Index}] Mounting image read-write for recipe execution: {mountPath}");

                // Create the mount directory up-front; DISM requires it to exist
                if (!Directory.Exists(mountPath))
                {
                    Directory.CreateDirectory(mountPath);
                }

                // Mount read-write (throws on failure)
                imageService.MountImage(image.SourcePath, mountPath, (uint)image.Index, readOnly: false);

                // Apply enabled sections in deterministic order
                if (recipe.RemoveAppxPackages.Enabled)
                {
                    ApplyRemoveAppxPackages(recipe.RemoveAppxPackages, mountPath, image, result, imageService);
                }

                if (recipe.CopyFiles.Enabled)
                {
                    ApplyCopyFiles(recipe.CopyFiles, mountPath, image, result);
                }

                if (recipe.SetWallpapers.Enabled)
                {
                    ApplySetWallpapers(recipe.SetWallpapers, mountPath, image, result, cmdlet);
                }

                if (recipe.EnableFeatures.Enabled)
                {
                    ApplyEnableFeatures(recipe.EnableFeatures, mountPath, image, result, imageService);
                }

                if (recipe.IntegrateDrivers.Enabled)
                {
                    ApplyIntegrateDrivers(recipe.IntegrateDrivers, mountPath, image, result, imageService);
                }

                if (recipe.IntegrateUpdates.Enabled)
                {
                    ApplyIntegrateUpdates(recipe.IntegrateUpdates, mountPath, image, result, imageService);
                }

                if (recipe.IntegrateFeaturesOnDemand.Enabled)
                {
                    ApplyIntegrateFeaturesOnDemand(recipe.IntegrateFeaturesOnDemand, mountPath, image, result, imageService);
                }

                if (recipe.RegistryModifications.Enabled)
                {
                    ApplyRegistryModifications(recipe.RegistryModifications, mountPath, image, result, cmdlet);
                }

                // Unmount and save (throws on failure)
                _callbacks.Verbose?.Invoke($"[{image.Index}] Unmounting and saving image");
                imageService.UnmountImage(mountPath, commitChanges: true);
                CleanupMountDirectory(mountPath);

                result.Success = result.Sections.All(s => s.FailureCount == 0);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _callbacks.Error?.Invoke(ex, $"Recipe execution failed on [{image.Index}] {image.Name}: {ex.Message}");

                // Best-effort unmount discard so we don't leave the image mounted
                TryUnmountDiscard(mountPath, imageService);
            }

            result.Duration = DateTime.UtcNow - startTime;
            return result;
        }

        private void ApplyRemoveAppxPackages(RemoveAppxPackagesSection section, string mountPath, WindowsImageInfo image, RecipeImageExecutionResult result, IWindowsImageService imageService)
        {
            var sectionResult = BeginSection("removeAppxPackages", result);
            var patterns = section.Patterns
                .Select(p => new Regex(p, RegexOptions.IgnoreCase))
                .ToList();

            try
            {
                var packages = imageService.GetProvisionedAppxPackages(mountPath);

                foreach (var package in packages)
                {
                    var displayName = package.DisplayName ?? string.Empty;
                    var packageName = package.PackageName ?? string.Empty;
                    var matched = patterns.Any(p => p.IsMatch(displayName) || p.IsMatch(packageName));

                    sectionResult.ItemsProcessed++;

                    if (!matched)
                    {
                        continue;
                    }

                    try
                    {
                        imageService.RemoveProvisionedAppxPackage(mountPath, packageName);
                        sectionResult.SuccessCount++;
                        _callbacks.Verbose?.Invoke($"[{image.Index}] Removed AppX package: {displayName}");
                    }
                    catch (Exception ex)
                    {
                        sectionResult.FailureCount++;
                        sectionResult.Errors.Add($"AppX '{displayName}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                sectionResult.Errors.Add($"Failed to enumerate AppX packages: {ex.Message}");
            }
        }

        private void ApplyCopyFiles(CopyFilesSection section, string mountPath, WindowsImageInfo image, RecipeImageExecutionResult result)
        {
            var sectionResult = BeginSection("copyFiles", result);

            foreach (var item in section.Items)
            {
                sectionResult.ItemsProcessed++;

                try
                {
                    var destinationPath = Path.Combine(mountPath, item.Destination.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar));
                    var destinationDirectory = Path.GetDirectoryName(destinationPath);

                    if (!string.IsNullOrEmpty(destinationDirectory) && !Directory.Exists(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    File.Copy(item.Source, destinationPath, item.Overwrite);
                    sectionResult.SuccessCount++;
                    _callbacks.Verbose?.Invoke($"[{image.Index}] Copied {item.Source} -> {destinationPath}");
                }
                catch (Exception ex)
                {
                    sectionResult.FailureCount++;
                    sectionResult.Errors.Add($"Copy '{item.Source}' -> '{item.Destination}': {ex.Message}");
                }
            }
        }

        private void ApplySetWallpapers(SetWallpapersSection section, string mountPath, WindowsImageInfo image, RecipeImageExecutionResult result, System.Management.Automation.PSCmdlet? cmdlet)
        {
            var sectionResult = BeginSection("setWallpapers", result);
            sectionResult.ItemsProcessed = 1;

            try
            {
                var wallpaperSource = string.IsNullOrWhiteSpace(section.Wallpaper) ? null : new FileInfo(section.Wallpaper);
                var lockscreenSource = string.IsNullOrWhiteSpace(section.LockScreen) ? null : new FileInfo(section.LockScreen);

                if (wallpaperSource == null && lockscreenSource == null)
                {
                    sectionResult.FailureCount++;
                    sectionResult.Errors.Add("No wallpaper or lockScreen source provided");
                    return;
                }

                var scratchDirectory = Path.Combine(mountPath, "Windows", "Temp");
                var configuration = new WallpaperConfiguration(
                    new DirectoryInfo(mountPath),
                    wallpaperSource ?? lockscreenSource!,
                    lockscreenSource);

                configuration.ImageScratchDirectory = new DirectoryInfo(Directory.Exists(scratchDirectory)
                    ? scratchDirectory
                    : Path.GetTempPath());

                using var wallpaperService = new WallpaperConfigurationService();
                var configurationResult = wallpaperService.ConfigureWallpaper(configuration, cmdlet);

                if (configurationResult.Success)
                {
                    sectionResult.SuccessCount++;
                    foreach (var warning in configurationResult.Warnings)
                    {
                        _callbacks.Warning?.Invoke($"[{image.Index}] Wallpaper: {warning}");
                    }
                }
                else
                {
                    sectionResult.FailureCount++;
                    sectionResult.Errors.Add(configurationResult.ErrorMessage ?? "Wallpaper configuration failed");
                }
            }
            catch (Exception ex)
            {
                sectionResult.FailureCount++;
                sectionResult.Errors.Add($"Wallpaper configuration: {ex.Message}");
            }
        }

        private void ApplyEnableFeatures(EnableFeaturesSection section, string mountPath, WindowsImageInfo image, RecipeImageExecutionResult result, IWindowsImageService imageService)
        {
            var sectionResult = BeginSection("enableFeatures", result);
            var patterns = section.Patterns
                .Select(p => new Regex(p, RegexOptions.IgnoreCase))
                .ToList();

            try
            {
                var features = imageService.GetFeatures(mountPath);

                foreach (var feature in features)
                {
                    var featureName = feature.FeatureName ?? string.Empty;
                    var matched = patterns.Any(p => p.IsMatch(featureName));

                    sectionResult.ItemsProcessed++;

                    if (!matched)
                    {
                        continue;
                    }

                    try
                    {
                        imageService.EnableFeature(mountPath, featureName, enableAll: true);
                        sectionResult.SuccessCount++;
                        _callbacks.Verbose?.Invoke($"[{image.Index}] Enabled feature: {featureName}");
                    }
                    catch (Exception ex)
                    {
                        sectionResult.FailureCount++;
                        sectionResult.Errors.Add($"Feature '{featureName}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                sectionResult.Errors.Add($"Failed to enumerate features: {ex.Message}");
            }
        }

        private void ApplyIntegrateDrivers(IntegrateDriversSection section, string mountPath, WindowsImageInfo image, RecipeImageExecutionResult result, IWindowsImageService imageService)
        {
            var sectionResult = BeginSection("integrateDrivers", result);

            foreach (var path in section.Paths)
            {
                sectionResult.ItemsProcessed++;

                try
                {
                    imageService.AddDriversFromDirectory(mountPath, path, forceUnsigned: false, recursive: true);
                    sectionResult.SuccessCount++;
                    _callbacks.Verbose?.Invoke($"[{image.Index}] Integrated drivers from: {path}");
                }
                catch (Exception ex)
                {
                    sectionResult.FailureCount++;
                    sectionResult.Errors.Add($"Drivers from '{path}': {ex.Message}");
                }
            }
        }

        private void ApplyIntegrateUpdates(IntegrateUpdatesSection section, string mountPath, WindowsImageInfo image, RecipeImageExecutionResult result, IWindowsImageService imageService)
        {
            var sectionResult = BeginSection("integrateUpdates", result);

            foreach (var path in section.Paths)
            {
                sectionResult.ItemsProcessed++;

                try
                {
                    imageService.AddPackage(mountPath, path);
                    sectionResult.SuccessCount++;
                    _callbacks.Verbose?.Invoke($"[{image.Index}] Integrated update: {path}");
                }
                catch (Exception ex)
                {
                    sectionResult.FailureCount++;
                    sectionResult.Errors.Add($"Update '{path}': {ex.Message}");
                }
            }
        }

        private void ApplyIntegrateFeaturesOnDemand(IntegrateFeaturesOnDemandSection section, string mountPath, WindowsImageInfo image, RecipeImageExecutionResult result, IWindowsImageService imageService)
        {
            var sectionResult = BeginSection("integrateFeaturesOnDemand", result);

            foreach (var capabilityName in section.Paths)
            {
                sectionResult.ItemsProcessed++;

                try
                {
                    imageService.AddCapability(mountPath, capabilityName);
                    sectionResult.SuccessCount++;
                    _callbacks.Verbose?.Invoke($"[{image.Index}] Added capability: {capabilityName}");
                }
                catch (Exception ex)
                {
                    sectionResult.FailureCount++;
                    sectionResult.Errors.Add($"Capability '{capabilityName}': {ex.Message}");
                }
            }
        }

        private void ApplyRegistryModifications(RegistryModificationsSection section, string mountPath, WindowsImageInfo image, RecipeImageExecutionResult result, System.Management.Automation.PSCmdlet? cmdlet)
        {
            var sectionResult = BeginSection("registryModifications", result);

            try
            {
                // Build a temporary .reg file from the modifications and reuse the proven
                // .reg application path (parse + hive-mounted writes)
                var regContent = BuildRegFileContent(section.Modifications, sectionResult);
                if (regContent.Count <= 2)
                {
                    return;
                }

                var tempRegPath = Path.Combine(Path.GetTempPath(), $"PSWIT-Recipe-{Guid.NewGuid():N}.reg");
                try
                {
                    File.WriteAllLines(tempRegPath, regContent);

                    var operationService = new RegistryOperationService();
                    var operations = operationService.ParseRegFiles(new[] { new FileInfo(tempRegPath) }, cmdlet!);
                    sectionResult.ItemsProcessed += operations.Count;

                    if (operations.Count == 0)
                    {
                        sectionResult.Errors.Add("Registry modifications produced no applicable operations");
                        return;
                    }

                    // RegistryApplicationService requires a MountedWindowsImage shell; cmdlet is
                    // only used for logging (null-guarded internally)
                    var shell = new MountedWindowsImage
                    {
                        MountId = Guid.NewGuid().ToString(),
                        SourceImagePath = image.SourcePath,
                        ImageIndex = image.Index,
                        ImageName = image.Name,
                        MountPath = new DirectoryInfo(mountPath),
                        Status = MountStatus.Mounted,
                        IsReadOnly = false
                    };

                    var applicationService = new RegistryApplicationService();
                    applicationService.CleanupAllNativeServices();
                    var applicationResults = applicationService.ApplyOperations(new[] { shell }, operations.ToArray(), cmdlet!);

                    foreach (var applicationResult in applicationResults)
                    {
                        sectionResult.SuccessCount += applicationResult.SuccessCount;
                        sectionResult.FailureCount += applicationResult.FailureCount;
                        foreach (var error in applicationResult.ErrorMessages)
                        {
                            sectionResult.Errors.Add($"{error.Key}: {error.Value}");
                        }
                    }
                }
                finally
                {
                    if (File.Exists(tempRegPath))
                    {
                        File.Delete(tempRegPath);
                    }
                }
            }
            catch (Exception ex)
            {
                sectionResult.Errors.Add($"Registry modifications: {ex.Message}");
            }
        }

        private static List<string> BuildRegFileContent(List<Models.RegistryModification> modifications, RecipeSectionResult sectionResult)
        {
            var lines = new List<string> { "Windows Registry Editor Version 5.00", string.Empty };

            // Group modifications by full key path so each key header appears once
            var grouped = modifications
                .Where(m => !string.IsNullOrWhiteSpace(m.Hive) && !string.IsNullOrWhiteSpace(m.Key))
                .GroupBy(m => $@"{m.Hive}\{m.Key}", StringComparer.OrdinalIgnoreCase);

            foreach (var group in grouped)
            {
                lines.Add($"[{group.Key}]");

                foreach (var modification in group)
                {
                    var valueName = string.IsNullOrEmpty(modification.ValueName) ? "@" : $"\"{modification.ValueName}\"";
                    var valueData = FormatRegValue(modification, sectionResult);

                    if (valueData != null)
                    {
                        lines.Add($"{valueName}={valueData}");
                    }
                    else
                    {
                        sectionResult.FailureCount++;
                        sectionResult.Errors.Add($"Unsupported value type '{modification.ValueType}' for '{modification.ValueName}'");
                    }
                }

                lines.Add(string.Empty);
            }

            return lines;
        }

        private static string? FormatRegValue(Models.RegistryModification modification, RecipeSectionResult sectionResult)
        {
            var raw = modification.ValueData?.ToString() ?? string.Empty;

            switch (modification.ValueType?.Trim() ?? "String")
            {
                case "String":
                    return $"\"{raw}\"";
                case "DWord":
                    return uint.TryParse(raw, out var dword) ? $"dword:{dword:x8}" : null;
                case "QWord":
                    return ulong.TryParse(raw, out var qword) ? $"qword:{qword:x16}" : null;
                case "ExpandString":
                    return $"hex(2):{ToHexUtf16(raw, multi: false)}";
                case "Binary":
                    return $"hex:{raw.Replace(" ", "").Replace(",", "").Replace("-", "")}";
                case "MultiString":
                    return $"hex(7):{ToHexUtf16(raw, multi: true)}";
                default:
                    return null;
            }
        }

        private static string ToHexUtf16(string text, bool multi)
        {
            // REG_EXPAND_SZ/REG_SZ-in-hex: data + single NUL terminator
            // REG_MULTI_SZ: items separated by NUL, terminated by an extra NUL
            var data = multi ? text + "\0\0" : text + "\0";
            var bytes = System.Text.Encoding.Unicode.GetBytes(data);
            return string.Join(",", bytes.Select(b => b.ToString("x2")));
        }

        private void TryUnmountDiscard(string mountPath, IWindowsImageService imageService)
        {
            try
            {
                if (Directory.Exists(mountPath))
                {
                    imageService.UnmountImage(mountPath, commitChanges: false);
                    CleanupMountDirectory(mountPath);
                    _callbacks.Warning?.Invoke($"Image discarded after failed recipe execution: {mountPath}");
                }
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to discard image after failed recipe execution: {ex.Message}");
            }
        }

        private void CleanupMountDirectory(string mountPath)
        {
            try
            {
                if (Directory.Exists(mountPath))
                {
                    Directory.Delete(mountPath, true);
                }
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to clean up mount directory {mountPath}: {ex.Message}");
            }
        }

        private static RecipeSectionResult BeginSection(string name, RecipeImageExecutionResult result)
        {
            var created = new RecipeSectionResult { SectionName = name, Enabled = true };
            result.Sections.Add(created);
            return created;
        }

        private static bool IsValidRegex(string pattern, List<string> problems, string field)
        {
            try
            {
                _ = new Regex(pattern);
                return true;
            }
            catch (ArgumentException ex)
            {
                problems.Add($"{field} is not a valid regex: {ex.Message}");
                return false;
            }
        }
    }
}
