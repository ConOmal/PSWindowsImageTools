using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Dism;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Changes the edition of a mounted (offline) Windows image via DISM edition servicing
    /// (the API equivalent of `DISM /Image:&lt;path&gt; /Set-Edition:&lt;edition&gt; [/ProductKey:&lt;key&gt;]`
    /// and `/Set-Edition:ServerEdition` for server SKUs).
    ///
    /// The Microsoft.Dism 3.3.12 wrapper exposes the full edition surface
    /// (GetCurrentEdition / GetTargetEditions / SetEdition / SetEditionAndProductKey), so this
    /// service wraps those calls directly. All decision logic (edition-name validation, product-key
    /// validation/masking, argument selection, result mapping) lives in <c>internal static</c> methods
    /// so it can be unit-tested without a DISM session or real image.
    ///
    /// The caller owns the DISM API lifecycle: initialize once (e.g. via
    /// <c>WindowsImageService.Initialize()</c>) before calling, and shut down after. This service never
    /// calls DismApi.Initialize/Shutdown itself.
    /// </summary>
    public class WindowsImageEditionService
    {
        private const string ServiceName = "WindowsImageEditionService";

        /// <summary>
        /// Edition id used on the DISM API for the server edition-change path
        /// (`DISM /Image:... /Set-Edition:ServerEdition`). The product key then selects the concrete server SKU.
        /// </summary>
        public const string ServerEditionId = "ServerEdition";

        private readonly ModuleCallbacks _callbacks;

        /// <summary>
        /// Creates the service with explicit callbacks
        /// </summary>
        public WindowsImageEditionService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Reads the current edition of a mounted image. Read-only, thin DISM call.
        /// Precondition: the DISM API is initialized by the caller.
        /// </summary>
        /// <param name="imagePath">Mount directory of the offline image</param>
        /// <returns>The current edition name (e.g. "Professional"), or empty string</returns>
        public string GetCurrentEdition(string imagePath)
        {
            _callbacks.Verbose?.Invoke($"Reading current edition from mounted image at {imagePath}");

            using var session = DismApi.OpenOfflineSession(imagePath);
            return DismApi.GetCurrentEdition(session) ?? string.Empty;
        }

        /// <summary>
        /// Changes the edition of a mounted image. Thin DISM path: the only non-unit-tested surface.
        /// Precondition: the DISM API is initialized by the caller; -ShouldProcess is the cmdlet's job.
        /// </summary>
        /// <param name="imagePath">Mount directory of the offline image</param>
        /// <param name="edition">Target edition name (e.g. "Professional"). Ignored when serverEdition is true.</param>
        /// <param name="productKey">Optional product key. Not allowed with serverEdition.</param>
        /// <param name="serverEdition">True to use the server SKU path (Set-Edition:ServerEdition)</param>
        /// <param name="progressCallback">Optional percent/status progress callback</param>
        /// <returns>A WindowsImageEditionResult; never throws for anticipated failures (they are mapped into the result)</returns>
        public WindowsImageEditionResult SetImageEdition(
            string imagePath,
            string? edition,
            string? productKey,
            bool serverEdition,
            Action<int, string>? progressCallback = null)
        {
            ValidateEditionParameters(edition, productKey, serverEdition);
            var editionId = ResolveEditionId(edition, serverEdition);
            var startedAt = DateTime.UtcNow;

            try
            {
                var dismCall = DescribeSetEditionCall(editionId, productKey, serverEdition);
                _callbacks.Verbose?.Invoke($"Setting edition on mounted image at {imagePath}: {dismCall}");

                using var session = DismApi.OpenOfflineSession(imagePath);

                var currentEdition = DismApi.GetCurrentEdition(session) ?? string.Empty;

                IReadOnlyList<string>? targetEditions = null;
                try
                {
                    targetEditions = DismApi.GetTargetEditions(session).ToList();
                }
                catch (Exception targetEx)
                {
                    _callbacks.Warning?.Invoke($"Could not enumerate available target editions for {imagePath}: {targetEx.Message}");
                }

                if (EditionsMatch(currentEdition, editionId))
                {
                    _callbacks.Warning?.Invoke($"Image at {imagePath} is already edition '{editionId}'; nothing to change.");
                    return BuildResult(
                        new DirectoryInfo(imagePath), editionId, serverEdition, productKey,
                        currentEdition, currentEdition, applied: false, declined: false, isSuccessful: true,
                        errorMessage: null, targetEditions, DateTime.UtcNow, DateTime.UtcNow - startedAt);
                }

                if (!IsEditionSupported(editionId, targetEditions))
                {
                    var available = targetEditions != null ? string.Join(", ", targetEditions) : "(unknown)";
                    _callbacks.Warning?.Invoke(
                        $"The requested edition '{editionId}' was not found among the image's usual target editions ({available}). " +
                        "DISM may still accept the change with a valid product key.");
                }

                if (serverEdition)
                {
                    DismApi.SetEdition(session, ServerEditionId, WrapDismProgress(progressCallback));
                }
                else if (!string.IsNullOrWhiteSpace(productKey))
                {
                    DismApi.SetEditionAndProductKey(session, editionId, productKey!, WrapDismProgress(progressCallback));
                }
                else
                {
                    DismApi.SetEdition(session, editionId, WrapDismProgress(progressCallback));
                }

                string? afterEdition = null;
                try
                {
                    afterEdition = DismApi.GetCurrentEdition(session);
                }
                catch (Exception readEx)
                {
                    _callbacks.Warning?.Invoke($"Edition was changed but the post-change edition read failed: {readEx.Message}");
                }

                _callbacks.Verbose?.Invoke($"Edition changed from '{currentEdition}' to '{afterEdition ?? editionId}'");
                return BuildResult(
                    new DirectoryInfo(imagePath), editionId, serverEdition, productKey,
                    currentEdition, afterEdition, applied: true, declined: false, isSuccessful: true,
                    errorMessage: null, targetEditions, DateTime.UtcNow, DateTime.UtcNow - startedAt);
            }
            catch (Exception ex)
            {
                var message = $"Failed to set edition '{editionId}' on image at {imagePath}: {ex.Message}";
                _callbacks.Error?.Invoke(ex, message);
                return BuildResult(
                    new DirectoryInfo(imagePath), editionId, serverEdition, productKey,
                    string.Empty, null, applied: false, declined: false, isSuccessful: false,
                    errorMessage: message, null, DateTime.UtcNow, DateTime.UtcNow - startedAt);
            }
        }

        /// <summary>
        /// Validates the edition/product-key/server-edition combination. Pure.
        /// </summary>
        /// <exception cref="ArgumentException">When the combination is invalid</exception>
        internal static void ValidateEditionParameters(string? edition, string? productKey, bool serverEdition)
        {
            if (serverEdition)
            {
                if (!string.IsNullOrWhiteSpace(edition))
                {
                    throw new ArgumentException("-Edition is mutually exclusive with -ServerEdition.", nameof(edition));
                }

                if (!string.IsNullOrWhiteSpace(productKey))
                {
                    throw new ArgumentException("-ProductKey is mutually exclusive with -ServerEdition.", nameof(productKey));
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(edition))
            {
                throw new ArgumentException("Specify -Edition <name> (e.g. 'Professional'), or use -ServerEdition for the server SKU path.", nameof(edition));
            }

            if (!string.IsNullOrWhiteSpace(productKey) && !IsValidProductKeyFormat(productKey!))
            {
                throw new ArgumentException(
                    "ProductKey is not a valid product key format (expected XXXXX-XXXXX-XXXXX-XXXXX-XXXXX or 25 characters).",
                    nameof(productKey));
            }
        }

        /// <summary>
        /// Resolves the DISM edition id. Server requests always map to "ServerEdition"; client requests
        /// are normalized edition names. Pure.
        /// </summary>
        internal static string ResolveEditionId(string? edition, bool serverEdition)
        {
            return serverEdition ? ServerEditionId : NormalizeEditionName(edition);
        }

        /// <summary>
        /// Trims an edition name and rejects blank/unsafe values. Pure.
        /// </summary>
        /// <exception cref="ArgumentException">When the name is null/blank or contains path separators</exception>
        internal static string NormalizeEditionName(string? edition)
        {
            if (string.IsNullOrWhiteSpace(edition))
            {
                throw new ArgumentException("An edition name is required. Use -Edition <name> (e.g. 'Professional'), or -ServerEdition for server SKUs.", nameof(edition));
            }

            var normalized = edition!.Trim();
            if (normalized.IndexOfAny(new[] { '\\', '/' }) >= 0)
            {
                throw new ArgumentException($"Edition name '{normalized}' must not contain path separators.", nameof(edition));
            }

            return normalized;
        }

        /// <summary>
        /// Validates a product key format: five dash-separated groups of five alphanumeric characters,
        /// or a flat 25-character alphanumeric key. Pure.
        /// </summary>
        internal static bool IsValidProductKeyFormat(string productKey)
        {
            if (string.IsNullOrWhiteSpace(productKey))
            {
                return false;
            }

            var trimmed = productKey.Trim();

            if (trimmed.Contains("-"))
            {
                var groups = trimmed.Split('-');
                if (groups.Length != 5)
                {
                    return false;
                }

                foreach (var group in groups)
                {
                    if (group.Length != 5 || !IsAlphanumeric(group))
                    {
                        return false;
                    }
                }

                return true;
            }

            return trimmed.Length == 25 && IsAlphanumeric(trimmed);
        }

        /// <summary>
        /// Masks a product key for display, keeping only the last five characters. Pure.
        /// </summary>
        internal static string MaskProductKey(string? productKey)
        {
            if (string.IsNullOrWhiteSpace(productKey))
            {
                return string.Empty;
            }

            var compact = productKey!.Trim().Replace("-", string.Empty);
            if (compact.Length == 0)
            {
                return string.Empty;
            }

            var tail = compact.Length <= 5 ? compact : compact.Substring(compact.Length - 5);
            return "XXXXX-XXXXX-XXXXX-XXXXX-" + tail;
        }

        /// <summary>
        /// True when the current edition already equals the requested edition (case-insensitive). Pure.
        /// </summary>
        internal static bool EditionsMatch(string currentEdition, string? requestedEdition)
        {
            return !string.IsNullOrEmpty(currentEdition)
                && !string.IsNullOrWhiteSpace(requestedEdition)
                && string.Equals(currentEdition.Trim(), requestedEdition!.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when the requested edition appears in the image's reported target edition list.
        /// A null/empty target list is treated as "supported" (unknown target set must not warn). Pure.
        /// </summary>
        internal static bool IsEditionSupported(string editionId, IEnumerable<string>? targetEditions)
        {
            if (targetEditions == null)
            {
                return true;
            }

            foreach (var target in targetEditions)
            {
                if (string.Equals(target.Trim(), editionId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Describes which DISM call the caller will issue for a given request. Mirrors the branching
        /// the thin path executes, kept here so the argument-building logic is unit-testable. Pure.
        /// </summary>
        internal static string DescribeSetEditionCall(string editionId, string? productKey, bool serverEdition)
        {
            if (serverEdition)
            {
                return $"DismSetEdition('{ServerEditionId}')";
            }

            if (!string.IsNullOrWhiteSpace(productKey))
            {
                return $"DismSetEditionAndProductKey('{editionId}', productKeyMasked='{MaskProductKey(productKey)}')";
            }

            return $"DismSetEdition('{editionId}')";
        }

        /// <summary>
        /// Maps operation inputs and outcomes into a WindowsImageEditionResult. Pure.
        /// </summary>
        internal static WindowsImageEditionResult BuildResult(
            DirectoryInfo imagePath,
            string? requestedEdition,
            bool serverEdition,
            string? productKey,
            string currentEdition,
            string? afterEdition,
            bool applied,
            bool declined,
            bool isSuccessful,
            string? errorMessage,
            IReadOnlyList<string>? availableTargetEditions,
            DateTime completedAt,
            TimeSpan duration)
        {
            return new WindowsImageEditionResult
            {
                ImagePath = imagePath,
                CurrentEdition = currentEdition ?? string.Empty,
                RequestedEdition = requestedEdition,
                IsServerEdition = serverEdition,
                ProductKeyProvided = !string.IsNullOrWhiteSpace(productKey),
                ProductKeyMasked = MaskProductKey(productKey),
                AfterEdition = afterEdition,
                AvailableTargetEditions = availableTargetEditions?.ToList() ?? new List<string>(),
                Applied = applied,
                Declined = declined,
                IsSuccessful = isSuccessful,
                ErrorMessage = errorMessage,
                CompletedAt = completedAt,
                Duration = duration
            };
        }

        /// <summary>
        /// Wraps a percent/status callback into a Microsoft.Dism progress callback that never throws
        /// </summary>
        private static DismProgressCallback? WrapDismProgress(Action<int, string>? progressCallback)
        {
            if (progressCallback == null)
            {
                return null;
            }

            return progress =>
            {
                try
                {
                    progressCallback(progress.Current, $"{progress.Current}%");
                }
                catch
                {
                    // Never throw from the native callback thread
                }
            };
        }

        private static bool IsAlphanumeric(string value)
        {
            foreach (var ch in value)
            {
                if (!char.IsLetterOrDigit(ch))
                {
                    return false;
                }
            }

            return true;
        }
    }
}