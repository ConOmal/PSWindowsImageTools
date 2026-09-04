using System.Collections.Generic;
using Microsoft.Win32;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class NativeRegistryServiceTests
    {
        private static PSWindowsImageTools.Services.RegistryModification CreateModification(
            string hive = "HKLM",
            string key = @"SOFTWARE\Test",
            string valueName = "Value",
            string valueData = "data",
            string valueType = "String",
            string operation = "Set")
        {
            return new PSWindowsImageTools.Services.RegistryModification
            {
                HiveName = hive,
                KeyPath = key,
                ValueName = valueName,
                ValueData = valueData,
                ValueType = valueType,
                Operation = operation
            };
        }

        [Fact]
        public void ConvertToRegistryOperations_SetDefaultsToModify()
        {
            var result = NativeRegistryService.ConvertToRegistryOperations(
                new List<PSWindowsImageTools.Services.RegistryModification> { CreateModification() });

            var operation = Assert.Single(result);
            Assert.Equal(RegistryOperationType.Modify, operation.Operation);
            Assert.Equal("HKLM", operation.Hive);
            Assert.Equal(@"SOFTWARE\Test", operation.Key);
            Assert.Equal("Value", operation.ValueName);
            Assert.Equal("data", operation.Value);
            Assert.Equal(RegistryValueKind.String, operation.ValueType);
        }

        [Fact]
        public void ConvertToRegistryOperations_NormalizesHiveNames()
        {
            var result = NativeRegistryService.ConvertToRegistryOperations(
                new List<PSWindowsImageTools.Services.RegistryModification>
                {
                    CreateModification(hive: "HKEY_LOCAL_MACHINE"),
                    CreateModification(hive: "HKCU", key: @"Software\Test"),
                    CreateModification(hive: "HKEY_USERS", key: @"Software\Test"),
                    CreateModification(hive: "HKCR", key: @"Test")
                });

            Assert.Equal(4, result.Count);
            Assert.Equal("HKLM", result[0].Hive);
            Assert.Equal("HKCU", result[1].Hive);
            Assert.Equal("HKU", result[2].Hive);
            Assert.Equal("HKCR", result[3].Hive);
        }

        [Theory]
        [InlineData("Set", RegistryOperationType.Modify)]
        [InlineData("Modify", RegistryOperationType.Modify)]
        [InlineData("Create", RegistryOperationType.Create)]
        [InlineData("Delete", RegistryOperationType.Remove)]
        [InlineData("Remove", RegistryOperationType.Remove)]
        [InlineData("DeleteKey", RegistryOperationType.RemoveKey)]
        [InlineData("RemoveKey", RegistryOperationType.RemoveKey)]
        public void ConvertToRegistryOperations_MapsOperations(string operation, RegistryOperationType expected)
        {
            var result = NativeRegistryService.ConvertToRegistryOperations(
                new List<PSWindowsImageTools.Services.RegistryModification> { CreateModification(operation: operation) });

            Assert.Equal(expected, Assert.Single(result).Operation);
        }

        [Theory]
        [InlineData("DWord", RegistryValueKind.DWord, "1")]
        [InlineData("QWord", RegistryValueKind.QWord, "1")]
        [InlineData("ExpandString", RegistryValueKind.ExpandString, "data")]
        [InlineData("Binary", RegistryValueKind.Binary, "DE")]
        [InlineData("MultiString", RegistryValueKind.MultiString, "data")]
        [InlineData("", RegistryValueKind.String, "data")]
        public void ConvertToRegistryOperations_MapsValueTypes(string valueType, RegistryValueKind expected, string valueData)
        {
            var result = NativeRegistryService.ConvertToRegistryOperations(
                new List<PSWindowsImageTools.Services.RegistryModification> { CreateModification(valueType: valueType, valueData: valueData) });

            Assert.Equal(expected, Assert.Single(result).ValueType);
        }

        [Fact]
        public void ConvertToRegistryOperations_ConvertsDWordData()
        {
            var result = NativeRegistryService.ConvertToRegistryOperations(
                new List<PSWindowsImageTools.Services.RegistryModification> { CreateModification(valueType: "DWord", valueData: "128") });

            Assert.Equal(128u, Assert.Single(result).Value);
        }

        [Fact]
        public void ConvertToRegistryOperations_ConvertsHexDWordData()
        {
            var result = NativeRegistryService.ConvertToRegistryOperations(
                new List<PSWindowsImageTools.Services.RegistryModification> { CreateModification(valueType: "DWord", valueData: "0x100") });

            Assert.Equal(256u, Assert.Single(result).Value);
        }

        [Fact]
        public void ConvertToRegistryOperations_ConvertsQWordData()
        {
            var result = NativeRegistryService.ConvertToRegistryOperations(
                new List<PSWindowsImageTools.Services.RegistryModification> { CreateModification(valueType: "QWord", valueData: "5000000000") });

            Assert.Equal(5000000000ul, Assert.Single(result).Value);
        }

        [Fact]
        public void ConvertToRegistryOperations_ConvertsBinaryHexData()
        {
            var result = NativeRegistryService.ConvertToRegistryOperations(
                new List<PSWindowsImageTools.Services.RegistryModification> { CreateModification(valueType: "Binary", valueData: "DE,AD BE-EF") });

            Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, (byte[])Assert.Single(result).Value!);
        }

        [Fact]
        public void ConvertToRegistryOperations_ConvertsMultiStringData()
        {
            var result = NativeRegistryService.ConvertToRegistryOperations(
                new List<PSWindowsImageTools.Services.RegistryModification> { CreateModification(valueType: "MultiString", valueData: "Alpha\0Beta") });

            Assert.Equal(new[] { "Alpha", "Beta" }, (string[])Assert.Single(result).Value!);
        }

        [Fact]
        public void ConvertToRegistryOperations_ListsEmptyForNullInput()
        {
            var result = NativeRegistryService.ConvertToRegistryOperations(null!);

            Assert.Empty(result);
        }

        [Fact]
        public void ConvertToRegistryOperations_SkipsUnknownHive()
        {
            var result = NativeRegistryService.ConvertToRegistryOperations(
                new List<PSWindowsImageTools.Services.RegistryModification> { CreateModification(hive: "BOGUS") });

            Assert.Empty(result);
        }

        [Fact]
        public void ConvertToRegistryOperations_SkipsMissingKeyPath()
        {
            var result = NativeRegistryService.ConvertToRegistryOperations(
                new List<PSWindowsImageTools.Services.RegistryModification> { CreateModification(key: "") });

            Assert.Empty(result);
        }

        [Fact]
        public void ConvertToRegistryOperations_SkipsUnknownOperation()
        {
            var result = NativeRegistryService.ConvertToRegistryOperations(
                new List<PSWindowsImageTools.Services.RegistryModification> { CreateModification(operation: "Explode") });

            Assert.Empty(result);
        }

        [Fact]
        public void ConvertToRegistryOperations_SkipsUnknownValueType()
        {
            var result = NativeRegistryService.ConvertToRegistryOperations(
                new List<PSWindowsImageTools.Services.RegistryModification> { CreateModification(valueType: "Bogus") });

            Assert.Empty(result);
        }

        [Fact]
        public void ConvertToRegistryOperations_SkipsMalformedValueData()
        {
            var result = NativeRegistryService.ConvertToRegistryOperations(
                new List<PSWindowsImageTools.Services.RegistryModification>
                {
                    CreateModification(valueType: "DWord", valueData: "not-a-number"),
                    CreateModification(valueType: "Binary", valueData: "ZZ")
                });

            Assert.Empty(result);
        }

        [Fact]
        public void ConvertToRegistryOperations_RemoveOpIgnoresValueType()
        {
            var result = NativeRegistryService.ConvertToRegistryOperations(
                new List<PSWindowsImageTools.Services.RegistryModification>
                {
                    CreateModification(operation: "Delete", valueType: "Bogus")
                });

            var operation = Assert.Single(result);
            Assert.Equal(RegistryOperationType.Remove, operation.Operation);
            Assert.Null(operation.Value);
        }

        [Fact]
        public void ConvertToRegistryOperations_RemoveKeyIgnoresValueName()
        {
            var result = NativeRegistryService.ConvertToRegistryOperations(
                new List<PSWindowsImageTools.Services.RegistryModification>
                {
                    CreateModification(operation: "RemoveKey", valueName: "Value")
                });

            var operation = Assert.Single(result);
            Assert.Equal(RegistryOperationType.RemoveKey, operation.Operation);
            Assert.Equal(string.Empty, operation.ValueName);
            Assert.Null(operation.Value);
        }
    }
}