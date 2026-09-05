using System.IO;
using System.Management.Automation;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Locates and services boot.wim (the WinPE-based Setup/PE image on Windows installation
    /// media) — a thin convenience layer over the module's generic WIM/driver/component-store
    /// services, since boot.wim is serviced through exactly the same mechanisms as any other WIM.
    /// </summary>
    public class BootImageService
    {
        private const string ServiceName = "BootImageService";
        private readonly ModuleCallbacks _callbacks;

        public BootImageService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Locates boot.wim under an extracted media root and reports the images it contains.
        /// Returns null if no boot.wim is present — a normal outcome for some media layouts, not
        /// an error.
        /// </summary>
        public BootImageInfo? Locate(DirectoryInfo mediaRoot, IWindowsImageService? imageService = null)
        {
            var media = WindowsInstallationMedia.FromRoot(mediaRoot);

            if (media.BootWim == null)
            {
                _callbacks.Verbose?.Invoke($"No boot.wim found under {mediaRoot.FullName}");
                return null;
            }

            var info = new BootImageInfo
            {
                Path = media.BootWim,
                SourceMediaRoot = mediaRoot.FullName
            };

            if (imageService != null)
            {
                try
                {
                    info.Images = imageService.GetImageInfo(media.BootWim.FullName);
                }
                catch (System.Exception ex)
                {
                    _callbacks.Warning?.Invoke($"Failed to read boot.wim image info: {ex.Message}");
                }
            }

            return info;
        }

        /// <summary>
        /// Injects drivers into a mounted boot.wim
        /// </summary>
        public void AddDriver(MountedWindowsImage mountedImage, IWindowsImageService imageService, DirectoryInfo driverDirectory, bool forceUnsigned)
        {
            if (mountedImage.MountPath == null)
            {
                throw new System.InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            _callbacks.Verbose?.Invoke($"Adding drivers from {driverDirectory.FullName} to boot image {mountedImage.ImageName}");
            imageService.AddDriversFromDirectory(mountedImage.MountPath.FullName, driverDirectory.FullName, forceUnsigned);
        }

        /// <summary>
        /// Runs component cleanup against a mounted boot.wim. ResetBase is intentionally never
        /// offered here — a boot/PE image has no update history to reset, so the option would be
        /// meaningless, not merely unsupported.
        /// </summary>
        public ComponentStoreCleanupResult Optimize(MountedWindowsImage mountedImage, IWindowsImageService imageService, PSCmdlet cmdlet)
        {
            return new ComponentStoreService(_callbacks).Cleanup(mountedImage, imageService, resetBase: false, cmdlet);
        }
    }
}
