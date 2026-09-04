using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PSWindowsImageTools.Services.Native
{
    /// <summary>
    /// Native WIM API declarations based on Microsoft's actual Export-WindowsImage implementation
    /// This is how Microsoft actually implements image export functionality
    /// </summary>
    public static class WimNativeApi
    {
        private const string WimApiDll = "wimgapi.dll";

        #region Constants

        // Access flags
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;

        // Creation disposition
        public const uint OPEN_EXISTING = 3;
        public const uint CREATE_ALWAYS = 2;
        public const uint OPEN_ALWAYS = 4;

        // Compression types
        public const uint WIM_COMPRESS_NONE = 0;
        public const uint WIM_COMPRESS_XPRESS = 1;
        public const uint WIM_COMPRESS_LZX = 2;
        public const uint WIM_COMPRESS_LZMS = 3;

        // Export flags
        public const uint WIM_EXPORT_ALLOW_DUPLICATES = 1;
        public const uint WIM_EXPORT_ONLY_RESOURCES = 2;
        public const uint WIM_EXPORT_ONLY_METADATA = 4;
        public const uint WIM_EXPORT_VERIFY_SOURCE = 8;
        public const uint WIM_EXPORT_VERIFY_DESTINATION = 16;

        // Create flags
        public const uint WIM_FLAG_VERIFY = 0x02000000;
        public const uint WIM_FLAG_INDEX = 0x04000000;
        public const uint WIM_FLAG_NO_DIRACL = 0x08000000;
        public const uint WIM_FLAG_NO_FILEACL = 0x10000000;
        public const uint WIM_FLAG_SHARE_WRITE = 0x20000000;

        #endregion

        #region Structures

        [StructLayout(LayoutKind.Sequential)]
        public struct WimInfo
        {
            public uint WimPath;
            public uint Guid;
            public uint ImageCount;
            public uint CompressionType;
            public ushort PartNumber;
            public ushort TotalParts;
            public uint BootIndex;
            public uint WimAttributes;
            public uint WimFlagsAndAttr;
        }

        // Progress callback delegate
        public delegate uint WimCallback(uint messageId, IntPtr wParam, IntPtr lParam, IntPtr userData);

        #endregion

        #region Core WIM API Functions

        [DllImport(WimApiDll, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr WIMCreateFile(
            [MarshalAs(UnmanagedType.LPWStr)] string wimPath,
            uint desiredAccess,
            uint creationDisposition,
            uint flagsAndAttributes,
            uint compressionType,
            out uint creationResult);

        [DllImport(WimApiDll, SetLastError = true)]
        public static extern bool WIMCloseHandle(IntPtr hObject);

        [DllImport(WimApiDll, SetLastError = true)]
        public static extern IntPtr WIMLoadImage(IntPtr hWim, uint imageIndex);

        [DllImport(WimApiDll, SetLastError = true)]
        public static extern bool WIMExportImage(IntPtr hImage, IntPtr hWim, uint flags);

        [DllImport(WimApiDll, SetLastError = true)]
        public static extern uint WIMRegisterMessageCallback(
            IntPtr hWim, 
            WimCallback messageCallback, 
            IntPtr userData);

        [DllImport(WimApiDll, SetLastError = true)]
        public static extern bool WIMUnregisterMessageCallback(
            IntPtr hWim, 
            WimCallback messageCallback);

        [DllImport(WimApiDll, SetLastError = true)]
        public static extern bool WIMSetTemporaryPath(
            IntPtr hWim, 
            [MarshalAs(UnmanagedType.LPWStr)] string tempPath);

        [DllImport(WimApiDll, SetLastError = true)]
        public static extern bool WIMGetAttributes(
            IntPtr hWim, 
            IntPtr wimInfo, 
            uint wimInfoSize);

        [DllImport(WimApiDll, SetLastError = true)]
        public static extern bool WIMSetBootImage(IntPtr hWim, uint imageIndex);

        [DllImport(WimApiDll, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool WIMSetImageName(
            IntPtr hWim,
            uint imageIndex,
            [MarshalAs(UnmanagedType.LPWStr)] string imageName);

        [DllImport(WimApiDll, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool WIMSetImageDescription(
            IntPtr hWim,
            uint imageIndex,
            [MarshalAs(UnmanagedType.LPWStr)] string imageDescription);

        [DllImport(WimApiDll, SetLastError = true)]
        public static extern bool WIMSetReferenceFile(
            IntPtr hWim,
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            uint flags);

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets the last Win32 error as an HRESULT
        /// </summary>
        public static int GetLastErrorAsHResult()
        {
            var lastError = Marshal.GetLastWin32Error();
            return lastError <= 0 ? lastError : (int)(0x80070000 | (uint)lastError);
        }

        /// <summary>
        /// Converts compression type string to WIM compression constant
        /// </summary>
        public static uint ParseCompressionType(string compressionType)
        {
            return compressionType?.ToLowerInvariant() switch
            {
                "none" => WIM_COMPRESS_NONE,
                "fast" => WIM_COMPRESS_XPRESS,
                "max" => WIM_COMPRESS_LZX,
                "recovery" => WIM_COMPRESS_LZMS,
                _ => WIM_COMPRESS_LZX // Default to max compression
            };
        }

        /// <summary>
        /// Gets standard WIM create flags
        /// </summary>
        public static uint GetWimCreateFlags(bool checkIntegrity, bool wimBoot)
        {
            uint flags = 0;
            
            if (checkIntegrity)
                flags |= WIM_FLAG_VERIFY;
                
            if (wimBoot)
                flags |= 0x2000; // WIMBoot flag
                
            return flags;
        }

        /// <summary>
        /// Gets standard WIM export flags
        /// </summary>
        public static uint GetWimExportFlags()
        {
            return 0; // Standard export, no special flags
        }

        #endregion
    }
}
