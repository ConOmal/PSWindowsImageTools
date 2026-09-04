using System;
using System.Collections.Generic;
using System.IO;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Result of changing the edition of a mounted (offline) Windows image via DISM
    /// edition servicing (DISM /Set-Edition).
    /// </summary>
    public class WindowsImageEditionResult
    {
        /// <summary>
        /// Mounted image directory the edition change targeted
        /// </summary>
        public DirectoryInfo ImagePath { get; set; } = null!;

        /// <summary>
        /// Edition read from the image before the change (DISM GetCurrentEdition), empty if it could not be read
        /// </summary>
        public string CurrentEdition { get; set; } = string.Empty;

        /// <summary>
        /// Edition requested for the image ("Professional", "Enterprise", or "ServerEdition" for server SKUs)
        /// </summary>
        public string? RequestedEdition { get; set; }

        /// <summary>
        /// Whether the server SKU path (DISM Set-Edition:ServerEdition) was requested
        /// </summary>
        public bool IsServerEdition { get; set; }

        /// <summary>
        /// Whether a product key was supplied with the request
        /// </summary>
        public bool ProductKeyProvided { get; set; }

        /// <summary>
        /// Masked form of the supplied product key (last group only). Never the full key.
        /// </summary>
        public string ProductKeyMasked { get; set; } = string.Empty;

        /// <summary>
        /// Edition read from the image after the change, or null if the change was declined or failed
        /// </summary>
        public string? AfterEdition { get; set; }

        /// <summary>
        /// Editions DISM reported as available targets for the image (empty if the query failed)
        /// </summary>
        public List<string> AvailableTargetEditions { get; set; } = new List<string>();

        /// <summary>
        /// Whether the DISM set-edition call was actually executed
        /// </summary>
        public bool Applied { get; set; }

        /// <summary>
        /// Whether the operation was skipped because ShouldProcess returned false (-WhatIf or declined -Confirm)
        /// </summary>
        public bool Declined { get; set; }

        /// <summary>
        /// Whether the operation completed without error. False for declines and failures.
        /// </summary>
        public bool IsSuccessful { get; set; }

        /// <summary>
        /// Error message when the operation failed (null on success or decline)
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// When the operation completed
        /// </summary>
        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Wall-clock duration of the operation
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Whether the post-change edition read differs from the pre-change edition (case-insensitive)
        /// </summary>
        public bool EditionChanged =>
            !Declined && !string.IsNullOrEmpty(AfterEdition)
            && !string.Equals(CurrentEdition, AfterEdition, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Human-readable status of the operation
        /// </summary>
        public string Status =>
            ErrorMessage != null ? "failed"
            : Declined ? "declined"
            : EditionChanged ? "changed"
            : Applied ? "unchanged"
            : "no change";

        public override string ToString()
        {
            var target = RequestedEdition ?? "(unspecified)";
            return $"{ImagePath?.Name ?? "(unknown)"}: {CurrentEdition} -> {AfterEdition ?? target} ({Status})";
        }
    }
}