# Windows Update Catalog Integration

PSWindowsImageTools provides comprehensive integration with the Microsoft Windows Update Catalog, enabling automated search, download, and installation of Windows updates with enterprise-grade features.

## Overview

The Windows Update Catalog workflow consists of four main cmdlets that work seamlessly together:

- `Search-WindowsUpdateCatalog` - Search Microsoft Update Catalog with advanced filtering
- `Get-WindowsUpdateDownloadUrl` - Extract download URLs from catalog results  
- `Save-WindowsUpdateCatalogResult` - Download update files with resume capability and integrity verification
- `Install-WindowsImageUpdate` - Unified cmdlet for installing updates (supports both file paths and pipeline objects)

## Key Features

### 🔍 **Advanced Search Capabilities**
- Architecture filtering (x86, x64, arm64)
- Product-specific filtering
- Date range filtering (Before/After)
- Result limiting and pagination
- Intelligent parsing of Microsoft's catalog

### 📦 **Robust Download Management**
- Resume interrupted downloads automatically
- Integrity verification with checksums
- Progress reporting with size formatting
- Batch download with statistics
- Error handling and retry logic

### 🛠️ **Flexible Installation Options**
- File-based installation for traditional workflows
- Pipeline-based installation for automation
- DISM API integration for reliability
- Progress tracking and error handling
- Support for both CAB and MSU files

## Windows Update Catalog API Behavior

**Important**: The Windows Update Catalog API returns ALL results without server-side filtering. All filtering (architecture, product, date) is performed client-side after parsing the results. This design ensures maximum compatibility and reliability.

## Complete Workflow Examples

### Basic Update Search and Download

```powershell
# Search for Windows 11 cumulative updates
$updates = Search-WindowsUpdateCatalog -Query "Windows 11 Cumulative" -Architecture x64 -MaxResults 5

# Get download URLs
$downloadInfo = $updates | Get-WindowsUpdateDownloadUrl

# Download files with resume capability
$packages = $downloadInfo | Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates" -Resume -VerifyIntegrity

# Display results
$packages | Format-Table Title, KBNumber, SizeFormatted, LocalFile
```

### Enterprise Deployment Pipeline

```powershell
# 1. Search for latest security updates
$securityUpdates = Search-WindowsUpdateCatalog -Query "Security Update" -Architecture x64 -After (Get-Date).AddDays(-30) |
    Where-Object { $_.Classification -eq "Security Updates" } |
    Sort-Object LastModified -Descending |
    Select-Object -First 10

# 2. Download updates
$packages = $securityUpdates | 
    Get-WindowsUpdateDownloadUrl | 
    Save-WindowsUpdateCatalogResult -DestinationPath "C:\SecurityUpdates" -Resume

# 3. Mount images and install updates
$images = Get-WindowsImageList -ImagePath "install.wim" | Where-Object { $_.ImageName -like "*Enterprise*" }
$mounted = $images | Mount-WindowsImageList -MountPath "C:\Mount" -ReadWrite

# 4. Install updates using pipeline
$mounted | Install-WindowsImageUpdate -UpdatePackages $packages -ContinueOnError

# 5. Save and dismount
$mounted | Dismount-WindowsImageList -Save
```

### Patch Tuesday Automation

```powershell
# Calculate next Patch Tuesday
$nextPatchTuesday = Get-PatchTuesday -Next

# Search for updates released on Patch Tuesday
$patchTuesdayUpdates = Search-WindowsUpdateCatalog -Query "Cumulative" -Architecture x64 |
    Where-Object { $_.LastModified.Date -eq $nextPatchTuesday.Date }

# Download to organized folder structure
$packages = $patchTuesdayUpdates |
    Get-WindowsUpdateDownloadUrl |
    Save-WindowsUpdateCatalogResult -DestinationPath "C:\PatchTuesday\$($nextPatchTuesday.Date.ToString('yyyy-MM'))"

Write-Output "Downloaded $($packages.Count) updates for Patch Tuesday: $($nextPatchTuesday.Date.ToString('MMMM dd, yyyy'))"
```

### Product-Specific Updates

```powershell
# Search for Windows Server 2022 updates
$serverUpdates = Search-WindowsUpdateCatalog -Query "Windows Server 2022" -Architecture x64 -ProductFilter "Windows Server 2022"

# Search for Office updates
$officeUpdates = Search-WindowsUpdateCatalog -Query "Microsoft Office" -ProductFilter "Microsoft Office"

# Search for .NET Framework updates
$dotnetUpdates = Search-WindowsUpdateCatalog -Query ".NET Framework" -ProductFilter ".NET Framework"
```

## Advanced Filtering Techniques

### Date-Based Filtering

```powershell
# Updates from the last 30 days
$recent = Search-WindowsUpdateCatalog -Query "Windows 11" -After (Get-Date).AddDays(-30)

# Updates before a specific date
$older = Search-WindowsUpdateCatalog -Query "Windows 10" -Before (Get-Date "2024-01-01")

# Updates from a specific month
$january = Search-WindowsUpdateCatalog -Query "Cumulative" -After (Get-Date "2024-01-01") -Before (Get-Date "2024-02-01")
```

### Architecture and Product Filtering

```powershell
# ARM64 updates only
$armUpdates = Search-WindowsUpdateCatalog -Query "Windows 11" -Architecture arm64

# Multiple product filtering
$multiProduct = Search-WindowsUpdateCatalog -Query "Security" -ProductFilter @("Windows 11", "Windows Server 2022")

# Exclude specific products using Where-Object
$filtered = Search-WindowsUpdateCatalog -Query "Update" |
    Where-Object { $_.Products -notcontains "Windows 10" }
```

## Download Management

### Resume and Integrity Features

```powershell
# Download with all safety features
$packages = $downloadInfo | Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates" -Resume -VerifyIntegrity

# Monitor download progress
$packages = $downloadInfo | Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates" -Verbose

# Handle download failures gracefully
try {
    $packages = $downloadInfo | Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates" -Resume
    Write-Output "Successfully downloaded $($packages.Count) packages"
} catch {
    Write-Warning "Download failed: $($_.Exception.Message)"
    # Retry logic here
}
```

### Batch Download Statistics

```powershell
# Download multiple update sets
$cumulativeUpdates = Search-WindowsUpdateCatalog -Query "Cumulative" -Architecture x64 -MaxResults 5
$securityUpdates = Search-WindowsUpdateCatalog -Query "Security" -Architecture x64 -MaxResults 5

# Combine and download
$allUpdates = $cumulativeUpdates + $securityUpdates
$packages = $allUpdates | Get-WindowsUpdateDownloadUrl | Save-WindowsUpdateCatalogResult -DestinationPath "C:\AllUpdates"

# Display statistics
Write-Output "Downloaded packages:"
Write-Output "  Cumulative Updates: $($packages | Where-Object { $_.Title -like "*Cumulative*" } | Measure-Object).Count"
Write-Output "  Security Updates: $($packages | Where-Object { $_.Title -like "*Security*" } | Measure-Object).Count"
Write-Output "  Total Size: $((($packages | Measure-Object -Property Size -Sum).Sum / 1GB).ToString('F2')) GB"
```

## Installation Methods

### File-Based Installation (Traditional)

```powershell
# Install from downloaded files
$updateFiles = Get-ChildItem "C:\Updates" -Filter "*.msu"
foreach ($file in $updateFiles) {
    Install-WindowsImageUpdate -UpdatePath $file -ImagePath "C:\Mount\Image1" -ValidateImage -ContinueOnError
}
```

### Pipeline-Based Installation (Modern)

```powershell
# Complete pipeline workflow
$updates = Search-WindowsUpdateCatalog -Query "Windows 11 Cumulative" -Architecture x64 -MaxResults 3 |
    Get-WindowsUpdateDownloadUrl |
    Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates"

$mounted = Get-WindowsImageList -ImagePath "install.wim" | Mount-WindowsImageList -MountPath "C:\Mount" -ReadWrite
$updatedImages = $mounted | Install-WindowsImageUpdate -UpdatePackages $updates -IgnoreCheck
$updatedImages | Dismount-WindowsImageList -Save
```

## Error Handling and Best Practices

### Robust Error Handling

```powershell
try {
    # Search with error handling
    $updates = Search-WindowsUpdateCatalog -Query "Windows 11" -Architecture x64 -MaxResults 10
    
    if ($updates.Count -eq 0) {
        Write-Warning "No updates found matching criteria"
        return
    }
    
    # Download with retry logic
    $packages = $updates | Get-WindowsUpdateDownloadUrl | Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates" -Resume
    
    # Validate downloads
    $failedDownloads = $packages | Where-Object { -not $_.LocalFile.Exists }
    if ($failedDownloads) {
        Write-Warning "$($failedDownloads.Count) downloads failed"
        $failedDownloads | ForEach-Object { Write-Warning "Failed: $($_.Title)" }
    }
    
} catch {
    Write-Error "Update workflow failed: $($_.Exception.Message)"
    # Cleanup logic here
}
```

### Performance Optimization

```powershell
# Use MaxResults to limit large searches
$updates = Search-WindowsUpdateCatalog -Query "Update" -MaxResults 50

# Filter early to reduce processing
$updates = Search-WindowsUpdateCatalog -Query "Windows 11" -Architecture x64 |
    Where-Object { $_.LastModified -gt (Get-Date).AddDays(-7) } |
    Select-Object -First 10

# Batch operations for efficiency
$allUpdates = @()
$allUpdates += Search-WindowsUpdateCatalog -Query "Cumulative" -Architecture x64 -MaxResults 5
$allUpdates += Search-WindowsUpdateCatalog -Query "Security" -Architecture x64 -MaxResults 5
$packages = $allUpdates | Get-WindowsUpdateDownloadUrl | Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates"
```

## Integration with Other Cmdlets

### Windows Release Information

```powershell
# Get latest release info and corresponding updates
$latestRelease = Get-WindowsReleaseInfo -OperatingSystem "Windows 11" -Latest
$updates = Search-WindowsUpdateCatalog -Query $latestRelease.LatestKBArticle -Architecture x64
```

## Troubleshooting

### Common Issues and Solutions

1. **No results returned**: Check query spelling and try broader search terms
2. **Download failures**: Use `-Resume` parameter and check network connectivity
3. **Installation failures**: Verify image is properly mounted and use `-ContinueOnError`
4. **Performance issues**: Use `-MaxResults` to limit large searches

### Debugging Commands

```powershell
# Enable verbose output
$VerbosePreference = "Continue"
$updates = Search-WindowsUpdateCatalog -Query "Windows 11" -Verbose

# Check download integrity
$packages | Where-Object { $_.LocalFile.Exists } | ForEach-Object {
    Write-Output "$($_.Title): $($_.LocalFile.Length) bytes"
}

# Validate mounted images before installation
$mounted | ForEach-Object {
    if ($_.Status -ne "Mounted") {
        Write-Warning "Image $($_.ImageName) is not properly mounted"
    }
}
```

This comprehensive integration enables automated, reliable Windows update management for enterprise deployment scenarios.
