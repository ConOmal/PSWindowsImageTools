using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using PSWindowsImageTools.Models;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class BuildRecipeSerializationTests
    {
        private const string SampleRecipeJson = @"{
  ""metadata"": {
    ""name"": ""Corporate Baseline"",
    ""description"": ""Standard enterprise image"",
    ""version"": ""1.2.0"",
    ""author"": ""IT""
  },
  ""imageFilter"": {
    ""enabled"": true,
    ""inclusionExpression"": ""Pro"",
    ""exclusionExpression"": ""Home""
  },
  ""removeAppxPackages"": {
    ""enabled"": true,
    ""patterns"": [ ""Xbox"", ""BingNews"" ]
  },
  ""copyFiles"": {
    ""enabled"": true,
    ""items"": [
      { ""source"": ""C:\\Branding\\logo.png"", ""destination"": ""C:\\Windows\\Branding\\logo.png"", ""overwrite"": false }
    ]
  },
  ""setWallpapers"": {
    ""enabled"": false,
    ""wallpaper"": ""C:\\Branding\\wallpaper.jpg"",
    ""lockScreen"": ""C:\\Branding\\lock.jpg""
  },
  ""registryModifications"": {
    ""enabled"": true,
    ""modifications"": [
      { ""hive"": ""HKLM"", ""key"": ""SOFTWARE\\Policies\\Test"", ""valueName"": ""Enabled"", ""valueData"": 1, ""valueType"": ""DWord"" }
    ]
  }
}";

        [Fact]
        public void Deserialize_PopulatesAllSections()
        {
            var recipe = JsonConvert.DeserializeObject<BuildRecipe>(SampleRecipeJson);

            Assert.NotNull(recipe);
            Assert.Equal("Corporate Baseline", recipe!.Metadata.Name);
            Assert.Equal("1.2.0", recipe.Metadata.Version);
            Assert.True(recipe.ImageFilter.Enabled);
            Assert.Equal("Pro", recipe.ImageFilter.InclusionExpression);
            Assert.Equal("Home", recipe.ImageFilter.ExclusionExpression);
            Assert.True(recipe.RemoveAppxPackages.Enabled);
            Assert.Equal(new[] { "Xbox", "BingNews" }, recipe.RemoveAppxPackages.Patterns);
            Assert.True(recipe.CopyFiles.Enabled);
            Assert.Single(recipe.CopyFiles.Items);
            Assert.False(recipe.CopyFiles.Items[0].Overwrite);
            Assert.False(recipe.SetWallpapers.Enabled);
            Assert.True(recipe.RegistryModifications.Enabled);
            Assert.Single(recipe.RegistryModifications.Modifications);
            Assert.Equal("DWord", recipe.RegistryModifications.Modifications[0].ValueType);
            // Newtonsoft deserializes bare JSON integers as long
            Assert.Equal(1L, Convert.ToInt64(recipe.RegistryModifications.Modifications[0].ValueData));
        }

        [Fact]
        public void Serialize_UsesSnakeCasePropertyNames()
        {
            var recipe = new BuildRecipe
            {
                Metadata = new RecipeMetadata { Name = "Test", Description = "Test recipe" },
                ImageFilter = new ImageFilterSection { Enabled = true, InclusionExpression = "Pro" }
            };

            var json = JsonConvert.SerializeObject(recipe);

            Assert.Contains("\"metadata\"", json);
            Assert.Contains("\"imageFilter\"", json);
            Assert.Contains("\"inclusionExpression\"", json);
            Assert.DoesNotContain("\"Metadata\"", json);
        }

        [Fact]
        public void RoundTrip_PreservesData()
        {
            var original = new BuildRecipe
            {
                Metadata = new RecipeMetadata { Name = "RoundTrip", Author = "QA", Version = "2.0.0" },
                RemoveAppxPackages = new RemoveAppxPackagesSection { Enabled = true, Patterns = new List<string> { "A", "B" } },
                IntegrateDrivers = new IntegrateDriversSection { Enabled = true, Paths = new List<string> { @"C:\Drivers" } },
                IntegrateUpdates = new IntegrateUpdatesSection { Enabled = true, Paths = new List<string> { @"C:\Updates\KB123.msu" } }
            };

            var json = JsonConvert.SerializeObject(original);
            var restored = JsonConvert.DeserializeObject<BuildRecipe>(json);

            Assert.NotNull(restored);
            Assert.Equal(original.Metadata.Name, restored!.Metadata.Name);
            Assert.Equal(original.Metadata.Author, restored.Metadata.Author);
            Assert.Equal(original.Metadata.Version, restored.Metadata.Version);
            Assert.Equal(original.RemoveAppxPackages.Patterns, restored.RemoveAppxPackages.Patterns);
            Assert.True(restored.RemoveAppxPackages.Enabled);
            Assert.Equal(original.IntegrateDrivers.Paths, restored.IntegrateDrivers.Paths);
            Assert.Equal(original.IntegrateUpdates.Paths, restored.IntegrateUpdates.Paths);
        }

        [Fact]
        public void Deserialize_EmptyJson_GivesDefaultsWithDisabledSections()
        {
            var recipe = JsonConvert.DeserializeObject<BuildRecipe>("{}");

            Assert.NotNull(recipe);
            Assert.Equal("1.0.0", recipe!.Metadata.Version);
            Assert.False(recipe.ImageFilter.Enabled);
            Assert.False(recipe.RemoveAppxPackages.Enabled);
            Assert.False(recipe.EnableFeatures.Enabled);
            Assert.False(recipe.IntegrateFeaturesOnDemand.Enabled);
            Assert.Empty(recipe.CopyFiles.Items);
        }
    }
}
