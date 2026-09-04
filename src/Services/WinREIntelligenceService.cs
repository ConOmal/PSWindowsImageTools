using System;
using System.IO;
using System.Text;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Pure file-based intelligence on the embedded WinRE image (Windows\System32\Recovery\Winre.wim)
    /// inside a mounted Windows image. Reads the WIM's own 208-byte header and (optionally) its XML
    /// metadata block — never DISM, never WIMGAPI, so it works even where DISM servicing is broken.
    /// </summary>
    public class WinREIntelligenceService
    {
        private const string ServiceName = "WinREIntelligenceService";

        /// <summary>
        /// Minimum length of a WIM header (format-version WIMs declare a 208-byte header)
        /// </summary>
        internal const int WimHeaderLength = 208;

        /// <summary>
        /// Cap on how many bytes of WIM XML metadata are read for the -Detailed display-name extraction
        /// </summary>
        internal const int MaxXmlMetadataBytes = 1024 * 1024;

        private readonly ModuleCallbacks _callbacks;

        public WinREIntelligenceService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Inspects the embedded WinRE image under a mounted Windows image directory
        /// </summary>
        /// <param name="mountPath">Path to the mounted Windows image directory</param>
        /// <param name="detailed">Whether to also read the WIM XML metadata for the first image's display name</param>
        public WinREIntelligenceReport Inspect(string mountPath, bool detailed)
        {
            if (string.IsNullOrEmpty(mountPath))
            {
                throw new ArgumentException("Mount path is required", nameof(mountPath));
            }

            if (!Directory.Exists(mountPath))
            {
                throw new DirectoryNotFoundException($"Mount path does not exist: {mountPath}");
            }

            var report = new WinREIntelligenceReport { ImagePath = mountPath };
            var winREPath = ResolveWinREPath(mountPath);
            report.WinREPath = winREPath;

            if (!File.Exists(winREPath))
            {
                _callbacks.Verbose?.Invoke($"No embedded WinRE image found at {winREPath}");
                return report;
            }

            report.WinREPresent = true;

            try
            {
                var fileInfo = new FileInfo(winREPath);
                report.SizeBytes = fileInfo.Length;
                report.SizeMB = BytesToMB(fileInfo.Length);
                report.LastModifiedUtc = fileInfo.LastWriteTimeUtc;
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to read WinRE file metadata for {winREPath}: {ex.Message}");
            }

            var header = TryReadWimHeader(winREPath);
            if (header != null)
            {
                report.WimHeaderParsed = true;
                report.WimHeader = header;
                report.WimVersion = header.VersionText;
                report.ImageCount = header.ImageCount;
                report.CompressionType = header.CompressionTypeName;
            }

            if (detailed && header != null && header.MetadataOffset > 0)
            {
                report.XmlImageDisplayName = TryReadXmlImageDisplayName(winREPath, header.MetadataOffset, header.TotalBytes);
            }

            _callbacks.Verbose?.Invoke($"WinRE intelligence complete for {mountPath}: {report}");
            return report;
        }

        /// <summary>
        /// Resolves the full path to the embedded WinRE image under a mounted image. Pure.
        /// </summary>
        internal static string ResolveWinREPath(string mountPath)
        {
            return Path.Combine(mountPath, WinREImageService.EmbeddedWinREPath);
        }

        /// <summary>
        /// Converts a byte count to megabytes, rounded to 2 decimals. Pure.
        /// </summary>
        internal static double BytesToMB(long bytes)
        {
            return Math.Round(bytes / 1048576.0, 2);
        }

        /// <summary>
        /// Parses the fixed 208-byte WIM file header from a raw byte buffer. Returns null when the
        /// buffer is too short or does not carry the MSWIM signature. Never throws. Pure.
        /// </summary>
        internal static WimHeaderInfo? TryParseWimHeader(byte[] bytes)
        {
            if (bytes == null || bytes.Length < WimHeaderLength)
            {
                return null;
            }

            if (bytes[0] != (byte)'M' || bytes[1] != (byte)'S' || bytes[2] != (byte)'W' ||
                bytes[3] != (byte)'I' || bytes[4] != (byte)'M' || bytes[5] != 0 || bytes[6] != 0 || bytes[7] != 0)
            {
                return null;
            }

            var header = new WimHeaderInfo { IsValid = true };

            using (var reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                reader.ReadBytes(8);         // signature
                header.HeaderSize = reader.ReadUInt32();
                header.Version = reader.ReadUInt32();
                header.Flags = reader.ReadUInt32();
                header.CompressionType = reader.ReadUInt32();
                reader.ReadBytes(7);         // reserved
                header.WimGuid = new Guid(reader.ReadBytes(16));
                header.PartNumber = reader.ReadUInt16();
                header.NumberOfParts = reader.ReadUInt16();
                header.ImageCount = reader.ReadInt64();
                header.BootIndex = reader.ReadInt64();
                reader.ReadInt64();          // boot metadata offset (unused)
                header.MetadataOffset = reader.ReadInt64();
                header.TotalBytes = reader.ReadInt64();
            }

            header.VersionMajor = (int)(header.Version >> 16);
            header.VersionMinor = (int)(header.Version & 0xFFFF);
            header.VersionText = $"{header.VersionMajor}.{header.VersionMinor}";
            header.CompressionTypeName = MapCompressionType(header.CompressionType);

            return header;
        }

        /// <summary>
        /// Maps a WIM compression-type value to its friendly name. Pure.
        /// </summary>
        internal static string MapCompressionType(uint type)
        {
            switch (type)
            {
                case 1:
                    return "LZX";
                case 2:
                    return "XPRESS";
                case 3:
                    return "LZMS";
                default:
                    return $"Unknown ({type})";
            }
        }

        /// <summary>
        /// Extracts the first image's &lt;DISPLAYNAME&gt; from raw WIM XML metadata bytes (UTF-16LE or
        /// UTF-8, BOM-detected). Returns null when no element is found. Never throws. Pure.
        /// </summary>
        internal static string? TryExtractXmlImageDisplayName(byte[] xmlBytes)
        {
            if (xmlBytes == null || xmlBytes.Length == 0)
            {
                return null;
            }

            var text = DecodeXmlBytes(xmlBytes);
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            return ExtractFirstElementText(text!, "DISPLAYNAME");
        }

        /// <summary>
        /// Extracts the trimmed, entity-unescaped inner text of the first &lt;elementName&gt; element.
        /// Pure.
        /// </summary>
        internal static string? ExtractFirstElementText(string xml, string elementName)
        {
            if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(elementName))
            {
                return null;
            }

            var openTag = "<" + elementName + ">";
            var closeTag = "</" + elementName + ">";

            var start = xml.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return null;
            }

            start += openTag.Length;

            var end = xml.IndexOf(closeTag, start, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                return null;
            }

            var value = xml.Substring(start, end - start).Trim();
            return value.Length == 0 ? null : UnescapeXml(value);
        }

        /// <summary>
        /// Unescapes the five basic XML entities. Pure.
        /// </summary>
        internal static string UnescapeXml(string value)
        {
            return value
                .Replace("&amp;", "&")
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&quot;", "\"")
                .Replace("&apos;", "'");
        }

        private static string? DecodeXmlBytes(byte[] bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }

            return Encoding.UTF8.GetString(bytes);
        }

        private WimHeaderInfo? TryReadWimHeader(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                var buffer = new byte[WimHeaderLength];
                var read = stream.Read(buffer, 0, WimHeaderLength);
                if (read < WimHeaderLength)
                {
                    Array.Resize(ref buffer, read);
                }

                return TryParseWimHeader(buffer);
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to read WIM header of {path}: {ex.Message}");
                return null;
            }
        }

        private string? TryReadXmlImageDisplayName(string path, long metadataOffset, long totalBytes)
        {
            try
            {
                long bytesToRead = MaxXmlMetadataBytes;
                if (totalBytes > metadataOffset)
                {
                    bytesToRead = Math.Min(bytesToRead, totalBytes - metadataOffset);
                }

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                stream.Seek(metadataOffset, SeekOrigin.Begin);
                var buffer = new byte[(int)bytesToRead];
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read > 0 && read < buffer.Length)
                {
                    Array.Resize(ref buffer, read);
                }

                var displayName = TryExtractXmlImageDisplayName(buffer);
                if (displayName == null)
                {
                    _callbacks.Verbose?.Invoke($"No <DISPLAYNAME> found in WIM XML metadata of {path}");
                }

                return displayName;
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to read WIM XML metadata of {path}: {ex.Message}");
                return null;
            }
        }
    }
}