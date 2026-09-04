using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class RecipeServiceTests : IDisposable
    {
        private readonly string _tempDirectory;

        public RecipeServiceTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        private static List<WindowsImageInfo> SampleImages()
        {
            return new List<WindowsImageInfo>
            {
                new WindowsImageInfo { Index = 1, Name = "Windows 11 Home", Edition = "Home" },
                new WindowsImageInfo { Index = 2, Name = "Windows 11 Pro", Edition = "Professional" },
                new WindowsImageInfo { Index = 3, Name = "Windows 11 Pro for Workstations", Edition = "ProfessionalWorkstation" },
                new WindowsImageInfo { Index = 4, Name = "Windows 11 Enterprise", Edition = "Enterprise" }
            };
        }

        [Fact]
        public void SelectImages_InclusionOnly()
        {
            var recipe = new BuildRecipe
            {
                ImageFilter = new ImageFilterSection { Enabled = true, InclusionExpression = "Pro" }
            };

            var selected = new RecipeService().SelectImages(recipe, SampleImages());

            Assert.Equal(new[] { 2, 3 }, selected.Select(i => i.Index));
        }

        [Fact]
        public void SelectImages_ExclusionOnly()
        {
            var recipe = new BuildRecipe
            {
                ImageFilter = new ImageFilterSection { Enabled = true, ExclusionExpression = "Workstations|Home" }
            };

            var selected = new RecipeService().SelectImages(recipe, SampleImages());

            Assert.Equal(new[] { 2, 4 }, selected.Select(i => i.Index));
        }

        [Fact]
        public void SelectImages_InclusionAndExclusion()
        {
            var recipe = new BuildRecipe
            {
                ImageFilter = new ImageFilterSection
                {
                    Enabled = true,
                    InclusionExpression = "Pro|Enterprise",
                    ExclusionExpression = "Workstations"
                }
            };

            var selected = new RecipeService().SelectImages(recipe, SampleImages());

            Assert.Equal(new[] { 2, 4 }, selected.Select(i => i.Index));
        }

        [Fact]
        public void SelectImages_NoFilter_SelectsAll()
        {
            var recipe = new BuildRecipe();
            var selected = new RecipeService().SelectImages(recipe, SampleImages());

            Assert.Equal(4, selected.Count);
        }

        [Fact]
        public void SelectImages_IsCaseInsensitive()
        {
            var recipe = new BuildRecipe
            {
                ImageFilter = new ImageFilterSection { Enabled = true, InclusionExpression = "ENTERPRISE" }
            };

            var selected = new RecipeService().SelectImages(recipe, SampleImages());

            Assert.Single(selected);
            Assert.Equal("Windows 11 Enterprise", selected[0].Name);
        }

        [Fact]
        public void ValidateRecipe_MissingName_IsReported()
        {
            var recipe = new BuildRecipe(); // empty name + no enabled sections

            var problems = new RecipeService().ValidateRecipe(recipe);

            Assert.Contains(problems, p => p.Contains("metadata.name is required"));
            Assert.Contains(problems, p => p.Contains("No recipe sections are enabled"));
        }

        [Fact]
        public void ValidateRecipe_InvalidRegex_IsReported()
        {
            var recipe = new BuildRecipe
            {
                Metadata = new RecipeMetadata { Name = "Test" },
                EnableFeatures = new EnableFeaturesSection { Enabled = true, Patterns = new List<string> { "[invalid" } }
            };

            var problems = new RecipeService().ValidateRecipe(recipe);

            Assert.Contains(problems, p => p.Contains("enableFeatures.patterns[]"));
        }

        [Fact]
        public void ValidateRecipe_MissingCopySource_IsReported()
        {
            var recipe = new BuildRecipe
            {
                Metadata = new RecipeMetadata { Name = "Test" },
                CopyFiles = new CopyFilesSection
                {
                    Enabled = true,
                    Items = new List<CopyFileItem>
                    {
                        new CopyFileItem { Source = Path.Combine(_tempDirectory, "missing.png"), Destination = "Windows\\Temp\\x.png" }
                    }
                }
            };

            var problems = new RecipeService().ValidateRecipe(recipe);

            Assert.Contains(problems, p => p.Contains("copyFiles source not found"));
        }

        [Fact]
        public void ValidateRecipe_MissingDriverPath_IsReported()
        {
            var recipe = new BuildRecipe
            {
                Metadata = new RecipeMetadata { Name = "Test" },
                IntegrateDrivers = new IntegrateDriversSection { Enabled = true, Paths = new List<string> { Path.Combine(_tempDirectory, "no-drivers") } }
            };

            var problems = new RecipeService().ValidateRecipe(recipe);

            Assert.Contains(problems, p => p.Contains("integrateDrivers path not found"));
        }

        [Fact]
        public void ValidateRecipe_MissingUpdatePath_IsReported()
        {
            var recipe = new BuildRecipe
            {
                Metadata = new RecipeMetadata { Name = "Test" },
                IntegrateUpdates = new IntegrateUpdatesSection { Enabled = true, Paths = new List<string> { Path.Combine(_tempDirectory, "missing.msu") } }
            };

            var problems = new RecipeService().ValidateRecipe(recipe);

            Assert.Contains(problems, p => p.Contains("integrateUpdates path not found"));
        }

        [Fact]
        public void LoadRecipe_MissingFile_Throws()
        {
            Assert.Throws<FileNotFoundException>(() =>
                RecipeService.LoadRecipe(Path.Combine(_tempDirectory, "no-such-recipe.json")));
        }

        [Fact]
        public void SaveAndLoadRecipe_RoundTrips()
        {
            var recipe = new BuildRecipe
            {
                Metadata = new RecipeMetadata { Name = "RoundTrip", Description = "Test", Version = "1.1.0" },
                ImageFilter = new ImageFilterSection { Enabled = true, InclusionExpression = "Pro" },
                RemoveAppxPackages = new RemoveAppxPackagesSection { Enabled = true, Patterns = new List<string> { "Xbox" } },
                RegistryModifications = new RegistryModificationsSection
                {
                    Enabled = true,
                    Modifications = new List<Models.RegistryModification>
                    {
                        new Models.RegistryModification { Hive = "HKLM", Key = @"SOFTWARE\Test", ValueName = "Enabled", ValueData = "1", ValueType = "DWord" }
                    }
                }
            };

            var path = Path.Combine(_tempDirectory, "roundtrip.json");
            RecipeService.SaveRecipe(recipe, path);
            var loaded = RecipeService.LoadRecipe(path);

            Assert.Equal("RoundTrip", loaded.Metadata.Name);
            Assert.Equal("Pro", loaded.ImageFilter.InclusionExpression);
            Assert.Equal(new[] { "Xbox" }, loaded.RemoveAppxPackages.Patterns);
            Assert.Single(loaded.RegistryModifications.Modifications);
        }
    }
}
