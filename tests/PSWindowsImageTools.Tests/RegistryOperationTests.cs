using System;
using Microsoft.Win32;
using PSWindowsImageTools.Models;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class RegistryOperationTests
    {
        [Theory]
        [InlineData("HKCU", "HKU")]
        [InlineData("HKEY_CURRENT_USER", "HKU")]
        [InlineData("HKU", "HKU")]
        [InlineData("HKEY_USERS", "HKU")]
        [InlineData("HKLM", "HKLM")]
        [InlineData("HKEY_LOCAL_MACHINE", "HKLM")]
        [InlineData("HKCR", "HKLM\\SOFTWARE\\Classes")]
        [InlineData("HKEY_CLASSES_ROOT", "HKLM\\SOFTWARE\\Classes")]
        public void GetMappedHive_MapsCorrectly(string hive, string expected)
        {
            var operation = new RegistryOperation { Hive = hive };

            Assert.Equal(expected, operation.GetMappedHive());
        }

        [Fact]
        public void GetFullPath_CombinesHiveAndKey()
        {
            var operation = new RegistryOperation { Hive = "HKLM", Key = @"SOFTWARE\Test" };

            Assert.Equal(@"HKLM\SOFTWARE\Test", operation.GetFullPath());
        }

        [Fact]
        public void GetFullPath_ReturnsHiveOnlyWhenKeyEmpty()
        {
            var operation = new RegistryOperation { Hive = "HKLM", Key = string.Empty };

            Assert.Equal("HKLM", operation.GetFullPath());
        }

        [Fact]
        public void SetValue_ConvertsDWordToUInt()
        {
            var operation = new RegistryOperation { ValueType = RegistryValueKind.DWord };

            operation.SetValue(1);

            Assert.Equal(1u, operation.Value);
        }

        [Fact]
        public void SetValue_ConvertsBinaryFromBase64()
        {
            var operation = new RegistryOperation { ValueType = RegistryValueKind.Binary };

            operation.SetValue(Convert.ToBase64String(new byte[] { 0xDE, 0xAD }));

            Assert.IsType<byte[]>(operation.Value);
            Assert.Equal(new byte[] { 0xDE, 0xAD }, (byte[])operation.Value!);
        }

        [Fact]
        public void SetValue_SplitsMultiStringOnNul()
        {
            var operation = new RegistryOperation { ValueType = RegistryValueKind.MultiString };

            operation.SetValue("Alpha\0Beta");

            Assert.Equal(new[] { "Alpha", "Beta" }, (string[])operation.Value!);
        }

        [Fact]
        public void SetValue_ReturnsNullForNullValue()
        {
            var operation = new RegistryOperation { ValueType = RegistryValueKind.String };

            operation.SetValue(null);

            Assert.Null(operation.Value);
        }

        [Fact]
        public void GetFormattedValue_FormatsDWordAsHex()
        {
            var operation = new RegistryOperation
            {
                ValueType = RegistryValueKind.DWord,
                Value = 1u
            };

            Assert.Equal("0x00000001 (1)", operation.GetFormattedValue());
        }

        [Fact]
        public void GetFormattedValue_FormatsQWordAsHex()
        {
            var operation = new RegistryOperation
            {
                ValueType = RegistryValueKind.QWord,
                Value = 255ul
            };

            Assert.Equal("0x00000000000000FF (255)", operation.GetFormattedValue());
        }

        [Fact]
        public void GetFormattedValue_FormatsBinaryAsHexPairs()
        {
            var operation = new RegistryOperation
            {
                ValueType = RegistryValueKind.Binary,
                Value = new byte[] { 0xDE, 0xAD }
            };

            Assert.Equal("DE-AD", operation.GetFormattedValue());
        }

        [Fact]
        public void GetFormattedValue_JoinsMultiString()
        {
            var operation = new RegistryOperation
            {
                ValueType = RegistryValueKind.MultiString,
                Value = new[] { "Alpha", "Beta" }
            };

            Assert.Equal("Alpha, Beta", operation.GetFormattedValue());
        }

        [Fact]
        public void GetFormattedValue_ReturnsNullMarkerForNull()
        {
            var operation = new RegistryOperation();

            Assert.Equal("(null)", operation.GetFormattedValue());
        }

        [Fact]
        public void ToString_ShowsRemoveKeyWithoutValue()
        {
            var operation = new RegistryOperation
            {
                Operation = RegistryOperationType.RemoveKey,
                Hive = "HKLM",
                Key = @"SOFTWARE\DeleteMe"
            };

            Assert.Equal(@"REMOVE_KEY: HKLM\SOFTWARE\DeleteMe", operation.ToString());
        }

        [Fact]
        public void ToString_ShowsDefaultLabelForEmptyValueName()
        {
            var operation = new RegistryOperation
            {
                Operation = RegistryOperationType.Create,
                Hive = "HKLM",
                Key = @"SOFTWARE\Test",
                ValueName = string.Empty,
                Value = "data",
                ValueType = RegistryValueKind.String
            };

            Assert.Equal(@"CREATE: HKLM\SOFTWARE\Test\(Default) = data (String)", operation.ToString());
        }
    }
}
