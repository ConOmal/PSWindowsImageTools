using System;
using System.Management.Automation;
using System.Net.Http;
using System.Threading;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Resolves official, time-limited direct download links for the Windows 11 ISO from Microsoft's public
    /// consumer software-download-connector API -- the same unauthenticated flow the browser download page
    /// uses. This is an undocumented Microsoft flow and can change without notice; Save-WindowsISO accepts a
    /// plain -Url as a manual bypass if this stops working.
    /// </summary>
    public class WindowsISODownloadService : IDisposable
    {
        private const string ServiceName = "WindowsISODownloadService";

        private readonly HttpClient _httpClient;
        private bool _disposed;

        /// <summary>
        /// Creates the service with a pre-configured HttpClient (custom User-Agent, empty Accept, cert bypass)
        /// </summary>
        public WindowsISODownloadService()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            _httpClient = new HttpClient(handler);
        }

        /// <summary>
        /// Resolves a time-limited direct download URL for the given edition/architecture/language
        /// </summary>
        public WindowsISODownloadInfo GetDownloadInfo(string edition, string architecture, string language, PSCmdlet? cmdlet)
        {
            var productId = WindowsISODownloadUrlBuilder.ResolveProductEditionId(edition, architecture);
            var sessionId = Guid.NewGuid().ToString();

            LoggingService.WriteVerbose(cmdlet, ServiceName, $"Registering session {sessionId} for ProductEditionId {productId}");
            var registrationResponse = Get(WindowsISODownloadUrlBuilder.BuildSessionRegistrationUrl(sessionId), null);
            WindowsISODownloadUrlBuilder.ThrowIfRejected(registrationResponse);

            LoggingService.WriteVerbose(cmdlet, ServiceName, "Completing bot-detection challenge");
            CompleteBotDetectionChallenge(sessionId, cmdlet);

            LoggingService.WriteVerbose(cmdlet, ServiceName, $"Requesting SKU list for language: {language}");
            var skuJson = Get(WindowsISODownloadUrlBuilder.BuildSkuLookupUrl(productId, sessionId), WindowsISODownloadUrlBuilder.RefererUrl);
            WindowsISODownloadUrlBuilder.ThrowIfRejected(skuJson);

            var skuId = WindowsISODownloadUrlBuilder.SelectSkuId(skuJson, language);

            LoggingService.WriteVerbose(cmdlet, ServiceName, $"Requesting download link for SKU {skuId}");
            var linkJson = Get(WindowsISODownloadUrlBuilder.BuildDownloadLinksUrl(skuId, sessionId), WindowsISODownloadUrlBuilder.RefererUrl);
            WindowsISODownloadUrlBuilder.ThrowIfRejected(linkJson);

            var url = WindowsISODownloadUrlBuilder.SelectDownloadUri(linkJson);

            var fileName = System.IO.Path.GetFileName(new Uri(url).LocalPath);
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = $"Win11_{architecture}.iso";
            }

            return new WindowsISODownloadInfo
            {
                Url = new Uri(url),
                FileName = fileName,
                Edition = edition,
                Architecture = architecture,
                Language = language
            };
        }

        private void CompleteBotDetectionChallenge(string sessionId, PSCmdlet? cmdlet)
        {
            var challengeResponse = Get(WindowsISODownloadUrlBuilder.BuildBotChallengeScriptUrl(sessionId), null);
            WindowsISODownloadUrlBuilder.ThrowIfRejected(challengeResponse);

            var (token, ticks) = WindowsISODownloadUrlBuilder.ExtractBotChallengeTokens(challengeResponse);

            Thread.Sleep(200);

            var replyUrl = WindowsISODownloadUrlBuilder.BuildBotChallengeCompletionUrl(
                sessionId, token, ticks, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var replyResponse = Get(replyUrl, null);
            WindowsISODownloadUrlBuilder.ThrowIfRejected(replyResponse);
        }

        private string Get(string url, string? referer)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", WindowsISODownloadUrlBuilder.UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", string.Empty);

            if (!string.IsNullOrEmpty(referer))
            {
                request.Headers.Referrer = new Uri(referer!);
            }

            using var response = _httpClient.SendAsync(request).Result;
            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsStringAsync().Result;
        }

        /// <summary>
        /// Disposes the service
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _httpClient.Dispose();
                GC.SuppressFinalize(this);
            }
        }
    }
}
