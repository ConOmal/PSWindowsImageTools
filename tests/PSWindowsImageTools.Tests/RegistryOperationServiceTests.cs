using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class RegistryOperationServiceTests : IDisposable
    {
        private readonly string _tempDirectory;

        public RegistryOperationServiceTests()
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

        private FileInfo WriteRegFile(string content, string fileName = "test.reg")
        {
            var path = Path.Combine(_tempDirectory, fileName);
            File.WriteAllText(path, content);
            return new FileInfo(path);
        }

        [Fact]
        public void ParseRegFiles_ParsesAllValueTypes()
        {
            var regFile = WriteRegFile(@"Windows Registry Editor Version 5.00

; Remove a key
[-HKEY_LOCAL_MACHINE\SOFTWARE\Test\DeleteMe]

[HKEY_LOCAL_MACHINE\SOFTWARE\Test\Settings]
""StringValue""=""Hello World""
""DWordValue""=dword:00000001
""QWordValue""=qword:000000000000000a
""BinaryValue""=hex:de,ad,be,ef
""ExpandValue""=hex(2):25,00,50,00,41,00,54,00,48,00,25,00,00,00
@=""DefaultValue""
""DeletedValue""=-
");

            var operations = new RegistryOperationService().ParseRegFiles(new[] { regFile }, ModuleCallbacks.Silent);

            Assert.Equal(8, operations.Count);

            var byName = operations
                .Where(op => op.Operation != RegistryOperationType.RemoveKey)
                .ToDictionary(op => op.ValueName.Length == 0 ? "(Default)" : op.ValueName);

            var removeKey = operations.Single(op => op.Operation == RegistryOperationType.RemoveKey);
            Assert.Equal("HKEY_LOCAL_MACHINE", removeKey.Hive);
            Assert.Equal(@"SOFTWARE\Test\DeleteMe", removeKey.Key);
            Assert.Equal(RegistryValueKind.Unknown, removeKey.ValueType);

            Assert.Equal("Hello World", (string)byName["StringValue"].Value!);
            Assert.Equal(RegistryValueKind.String, byName["StringValue"].ValueType);
            Assert.Equal(RegistryOperationType.Create, byName["StringValue"].Operation);

            Assert.Equal(1u, byName["DWordValue"].Value);
            Assert.Equal(RegistryValueKind.DWord, byName["DWordValue"].ValueType);

            Assert.Equal(10ul, byName["QWordValue"].Value);
            Assert.Equal(RegistryValueKind.QWord, byName["QWordValue"].ValueType);

            Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, (byte[])byName["BinaryValue"].Value!);
            Assert.Equal(RegistryValueKind.Binary, byName["BinaryValue"].ValueType);

            Assert.Equal("%PATH%", (string)byName["ExpandValue"].Value!);
            Assert.Equal(RegistryValueKind.ExpandString, byName["ExpandValue"].ValueType);

            Assert.Equal("DefaultValue", (string)byName["(Default)"].Value!);
            Assert.Equal(string.Empty, byName["(Default)"].ValueName);

            var removed = byName["DeletedValue"];
            Assert.Equal(RegistryOperationType.Remove, removed.Operation);
            Assert.Null(removed.Value);
        }

        [Fact]
        public void ParseRegFiles_IgnoresCommentsAndHeader()
        {
            var regFile = WriteRegFile(@"Windows Registry Editor Version 5.00

; This is a comment
[HKEY_LOCAL_MACHINE\SOFTWARE\Test]
""One""=""1""
");

            var operations = new RegistryOperationService().ParseRegFiles(new[] { regFile }, ModuleCallbacks.Silent);

            Assert.Single(operations);
            Assert.Equal("One", operations[0].ValueName);
        }

        [Fact]
        public void ParseRegFiles_AggregatesMultipleFiles()
        {
            var file1 = WriteRegFile(@"[HKEY_LOCAL_MACHINE\SOFTWARE\Test1]
""A""=""1""
", "one.reg");
            var file2 = WriteRegFile(@"[HKEY_LOCAL_MACHINE\SOFTWARE\Test2]
""B""=""2""
", "two.reg");

            var operations = new RegistryOperationService().ParseRegFiles(new[] { file1, file2 }, ModuleCallbacks.Silent);

            Assert.Equal(2, operations.Count);
            Assert.Equal(2, operations.Count(op => op.Operation == RegistryOperationType.Create));
        }

        [Fact]
        public void ParseRegFiles_RecordsOriginalLineAndNumber()
        {
            var regFile = WriteRegFile(@"[HKEY_LOCAL_MACHINE\SOFTWARE\Test]
""Line1""=""first""
""Line2""=""second""
");

            var operations = new RegistryOperationService().ParseRegFiles(new[] { regFile }, ModuleCallbacks.Silent);

            Assert.Equal(2, operations.Count);
            Assert.Equal(@"""Line1""=""first""", operations[0].OriginalLine);
            Assert.True(operations[0].LineNumber > 0);
            Assert.True(operations[1].LineNumber == operations[0].LineNumber + 1);
        }

        [Fact]
        public void ParseRegFiles_MissingFileDoesNotThrow()
        {
            var missing = new FileInfo(Path.Combine(_tempDirectory, "does-not-exist.reg"));

            var operations = new RegistryOperationService().ParseRegFiles(new[] { missing }, ModuleCallbacks.Silent);

            Assert.Empty(operations);
        }
    }
}
