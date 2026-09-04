using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Thrown when Microsoft's bot-detection layer rejects an automated ISO download request
    /// </summary>
    public class WindowsISODiscoveryRejectedException : Exception
    {
        /// <summary>
        /// Creates the exception with a message explaining the rejection and the manual bypass
        /// </summary>
        public WindowsISODiscoveryRejectedException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Pure request/response logic for Microsoft's public software-download-connector API -- the same
    /// unauthenticated flow the browser download page at https://www.microsoft.com/en-us/software-download/windows11
    /// uses. Kept free of network I/O so it can be unit tested directly. This is an undocumented Microsoft flow
    /// and can change without notice; Save-WindowsISO accepts a plain -Url as a manual bypass if it stops working.
    /// </summary>
    public static class WindowsISODownloadUrlBuilder
    {
        /// <summary>
        /// Fixed organization id Microsoft's anti-abuse session endpoint expects
        /// </summary>
        public const string OrgId = "y6jn8c31";

        /// <summary>
        /// Fixed customer/instance id for Microsoft's ov-df bot-detection (Sentinel) challenge
        /// </summary>
        public const string SentinelInstanceId = "560dc9f3-1aa5-4a2f-b63c-9e18f8d0e175";

        /// <summary>
        /// Fixed profile id for the software-download-connector API
        /// </summary>
        public const string ConnectorProfile = "606624d44113";

        /// <summary>
        /// Referer header value used for the SKU/link lookup calls
        /// </summary>
        public const string RefererUrl = "https://www.microsoft.com/en-us/software-download/windows11";

        /// <summary>
        /// Spoofed non-Windows desktop-browser User-Agent -- Microsoft redirects real Windows/Edge user agents
        /// to the Media Creation Tool instead of returning a direct ISO link
        /// </summary>
        public const string UserAgent = "Mozilla/5.0 (X11; Linux x86_64; rv:109.0) Gecko/20100101 Firefox/117.0";

        private const string SentinelRejectedText = "Sentinel marked this request as rejected.";
        private const string RequestBlockedText = "We are unable to complete your request at this time.";

        /// <summary>
        /// Resolves Microsoft's numeric ProductEditionId for a given edition/architecture combination.
        /// Microsoft's public download page only offers one multi-edition "Windows 11" ISO -- there is no
        /// separate Home/Pro/Enterprise download at this layer.
        /// </summary>
        public static string ResolveProductEditionId(string edition, string architecture)
        {
            if (!string.Equals(edition, "Windows 11", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Unsupported edition '{edition}'. Only \"Windows 11\" is currently supported.", nameof(edition));
            }

            return (architecture ?? string.Empty).ToLowerInvariant() switch
            {
                "x64" => "3321",
                "arm64" => "3324",
                _ => throw new ArgumentException($"Unsupported architecture '{architecture}'. Supported values: x64, arm64.", nameof(architecture))
            };
        }

        /// <summary>
        /// Builds the URL for step 1: registering the session with Microsoft's anti-abuse endpoint
        /// </summary>
        public static string BuildSessionRegistrationUrl(string sessionId)
        {
            return $"https://vlscppe.microsoft.com/tags?org_id={OrgId}&session_id={sessionId}";
        }

        /// <summary>
        /// Builds the URL for step 2a: fetching the ov-df bot-detection challenge script
        /// </summary>
        public static string BuildBotChallengeScriptUrl(string sessionId)
        {
            return $"https://ov-df.microsoft.com/mdt.js?instanceId={SentinelInstanceId}&PageId=si&session_id={sessionId}";
        }

        /// <summary>
        /// Extracts the "w" token and "rticks" value from the ov-df challenge script response
        /// </summary>
        public static (string Token, string Ticks) ExtractBotChallengeTokens(string scriptBody)
        {
            var tokenMatch = Regex.Match(scriptBody ?? string.Empty, "[?&]w=([A-Fa-f0-9]+)");
            var ticksMatch = Regex.Match(scriptBody ?? string.Empty, "rticks=\"?\\+?(\\d+)");

            if (!tokenMatch.Success || !ticksMatch.Success)
            {
                throw new InvalidOperationException(
                    "Could not complete Microsoft's bot-detection challenge (unexpected ov-df response shape). " +
                    "This flow may have changed; use Save-WindowsISO -Url with a manually obtained link instead.");
            }

            return (tokenMatch.Groups[1].Value, ticksMatch.Groups[1].Value);
        }

        /// <summary>
        /// Builds the URL for step 2b: completing the ov-df bot-detection handshake
        /// </summary>
        public static string BuildBotChallengeCompletionUrl(string sessionId, string token, string ticks, long unixTimeMillis)
        {
            return $"https://ov-df.microsoft.com/?session_id={sessionId}&CustomerId={SentinelInstanceId}&PageId=si" +
                   $"&w={token}&mdt={unixTimeMillis}&rticks={ticks}";
        }

        /// <summary>
        /// Builds the URL for step 3: looking up language SKUs for a ProductEditionId
        /// </summary>
        public static string BuildSkuLookupUrl(string productEditionId, string sessionId)
        {
            return "https://www.microsoft.com/software-download-connector/api/getskuinformationbyproductedition" +
                   $"?profile={ConnectorProfile}&ProductEditionId={productEditionId}&SKU=undefined&friendlyFileName=undefined&Locale=en-US&sessionID={sessionId}";
        }

        /// <summary>
        /// Builds the URL for step 4: resolving the download link for a SKU
        /// </summary>
        public static string BuildDownloadLinksUrl(string skuId, string sessionId)
        {
            return "https://www.microsoft.com/software-download-connector/api/GetProductDownloadLinksBySku" +
                   $"?profile={ConnectorProfile}&ProductEditionId=undefined&SKU={skuId}&friendlyFileName=undefined&Locale=en-US&sessionID={sessionId}";
        }

        /// <summary>
        /// Selects the SKU id matching the requested language from a getskuinformationbyproductedition response
        /// </summary>
        public static string SelectSkuId(string skuJson, string language)
        {
            var skus = JObject.Parse(skuJson)["Skus"] as JArray;
            if (skus != null)
            {
                foreach (var sku in skus)
                {
                    if (string.Equals((string?)sku["Language"], language, StringComparison.OrdinalIgnoreCase))
                    {
                        var id = (string?)sku["Id"];
                        if (!string.IsNullOrEmpty(id))
                        {
                            return id!;
                        }
                    }
                }
            }

            throw new InvalidOperationException($"Microsoft did not return a SKU for language '{language}'. Response: {skuJson}");
        }

        /// <summary>
        /// Selects the download URL from a GetProductDownloadLinksBySku response
        /// </summary>
        public static string SelectDownloadUri(string linkJson)
        {
            var options = JObject.Parse(linkJson)["ProductDownloadOptions"] as JArray;
            var uri = options != null && options.Count > 0 ? (string?)options[0]["Uri"] : null;

            if (string.IsNullOrEmpty(uri))
            {
                throw new InvalidOperationException($"Microsoft did not return a download link. Response: {linkJson}");
            }

            return uri!;
        }

        /// <summary>
        /// Throws if a connector API response indicates Microsoft's bot-detection layer rejected the request
        /// </summary>
        public static void ThrowIfRejected(string responseBody)
        {
            if (string.IsNullOrEmpty(responseBody))
            {
                return;
            }

            if (responseBody.Contains(SentinelRejectedText))
            {
                throw new WindowsISODiscoveryRejectedException(
                    "Microsoft rejected the automated ISO download request (Sentinel bot detection). This can " +
                    "happen from datacenter or VPN IP ranges. Obtain the ISO URL manually from " +
                    "https://www.microsoft.com/software-download/windows11 and pass it to Save-WindowsISO -Url instead.");
            }

            if (responseBody.Contains(RequestBlockedText))
            {
                throw new WindowsISODiscoveryRejectedException(
                    "Microsoft blocked the automated ISO download request. Obtain the ISO URL manually from " +
                    "https://www.microsoft.com/software-download/windows11 and pass it to Save-WindowsISO -Url instead.");
            }
        }
    }
}
