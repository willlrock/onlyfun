/*
 * (C) 2023 Radrat Softworks
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 */

using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace Nofun.Tests
{
    public class OnlyfunReliabilityTests
    {
        private Assembly runtimeAssembly;
        private string temporaryDirectory;

        [SetUp]
        public void SetUp()
        {
            runtimeAssembly = Assembly.Load("Assembly-CSharp");
            temporaryDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(temporaryDirectory, true);
        }

        [TestCase("HoneyCave2")]
        [TestCase("Honey Cave 2")]
        [TestCase("honey cave 2")]
        public void HoneyCave2GetsExactLegacyProfile(string title)
        {
            Type resolverType = RuntimeType("Nofun.Settings.GameProfileResolver");
            object profile = resolverType.GetMethod("Resolve").Invoke(null, new[] { title, null });
            AssertHoneyCaveProfile(profile);
        }

        [Test]
        public void ExternalHoneyCaveFixtureParsesAndResolvesExactProfile()
        {
            string path = Environment.GetEnvironmentVariable("ONLYFUN_TEST_GAME");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Assert.Ignore("Set ONLYFUN_TEST_GAME to a developer-owned Honey Cave 2 .mpn file.");
            }

            Type executableType = RuntimeType("Nofun.Parser.VMGPExecutable");
            Type metadataExtensionType = RuntimeType("Nofun.Parser.VMGPExecutableExtension");
            Type resolverType = RuntimeType("Nofun.Settings.GameProfileResolver");

            using (FileStream stream = File.OpenRead(path))
            {
                object executable = Activator.CreateInstance(executableType, stream);
                try
                {
                    object metadata = metadataExtensionType.GetMethod("GetMetaInfo")
                        .Invoke(null, new[] { executable });
                    Assert.That(metadata, Is.Not.Null);

                    string title = (string)metadata.GetType().GetMethod("Get")
                        .Invoke(metadata, new object[] { "Title" });
                    Assert.That(title, Is.EqualTo("HoneyCave2"));

                    object profile = resolverType.GetMethod("Resolve")
                        .Invoke(null, new[] { title, executable });
                    AssertHoneyCaveProfile(profile);
                }
                finally
                {
                    ((IDisposable)executable).Dispose();
                }
            }
        }

        [Test]
        public void InterpreterReportsOutOfRangeOpcodeAsProbablyEncrypted()
        {
            Type configType = RuntimeType("Nofun.PIP2.ProcessorConfig");
            object config = Activator.CreateInstance(configType);
            configType.GetField("ReadCode").SetValue(config,
                new Func<uint, uint>(_ => 0xD5021BF3));

            Type interpreterType = RuntimeType("Nofun.PIP2.Interpreter.Interpreter");
            object interpreter = Activator.CreateInstance(interpreterType, config);
            uint[] registers = (uint[])interpreterType.BaseType.GetField("registers",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(interpreter);
            registers[32] = 0x1000;

            TargetInvocationException invocation = Assert.Throws<TargetInvocationException>(() =>
                interpreterType.GetMethod("Run").Invoke(interpreter, new object[] { 1 }));

            Assert.That(invocation.InnerException, Is.TypeOf<InvalidProgramException>());
            Assert.That(invocation.InnerException.Message, Does.Contain("0xF3"));
            Assert.That(invocation.InnerException.Message, Does.Contain("0x00001000"));
            Assert.That(invocation.InnerException.Message, Does.Contain("encrypted"));

            TargetInvocationException secondInvocation = Assert.Throws<TargetInvocationException>(() =>
                interpreterType.GetMethod("Run").Invoke(interpreter, new object[] { 1 }));
            Assert.That(secondInvocation.InnerException, Is.TypeOf<InvalidProgramException>(),
                "The interpreter must leave its running state after a failed instruction.");
        }

        [Test]
        public void FileLoggerPreservesVmFailureDetails()
        {
            Type targetType = RuntimeType("Nofun.Util.Unity.FileLogTarget");
            object target = Activator.CreateInstance(targetType, temporaryDirectory);
            Type loggerType = RuntimeType("Nofun.Util.Logging.Logger");
            Type logClassType = RuntimeType("Nofun.Util.Logging.LogClass");
            object loaderClass = Enum.Parse(logClassType, "Loader");
            string failure = new InvalidProgramException(
                "Invalid opcode 0xF3 at PC=0x00001000. The Mophun code section is probably still encrypted.").ToString();

            loggerType.GetMethod("AddTarget").Invoke(null, new[] { target });
            try
            {
                loggerType.GetMethod("Error").Invoke(null,
                    new[] { loaderClass, $"VM initialization or execution failed: {failure}" });
            }
            finally
            {
                loggerType.GetMethod("RemoveTarget").Invoke(null, new[] { target });
            }

            string logPath = (string)targetType.GetProperty("LogPath").GetValue(target);
            string contents = File.ReadAllText(logPath);
            Assert.That(contents, Does.Contain("[Error] [Loader]"));
            Assert.That(contents, Does.Contain("InvalidProgramException"));
            Assert.That(contents, Does.Contain("0xF3"));
            Assert.That(contents, Does.Contain("0x00001000"));
        }

        [Test]
        public void UnicodeMessageBoxMatchesSdkSignatureAndIsRegistered()
        {
            Type moduleType = RuntimeType("Nofun.Module.VMGP.VMGP");
            Type vmStringType = RuntimeType("Nofun.VM.VMString");
            MethodInfo method = moduleType.GetMethod("vMsgBoxU",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            Assert.That(method.ReturnType, Is.EqualTo(typeof(int)));
            ParameterInfo[] parameters = method.GetParameters();
            Assert.That(parameters.Length, Is.EqualTo(3));
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(int)));
            Assert.That(parameters[1].ParameterType, Is.EqualTo(vmStringType));
            Assert.That(parameters[2].ParameterType, Is.EqualTo(vmStringType));

            Type callMapType = RuntimeType("Nofun.VM.VMCallMap");
            object callMap = Activator.CreateInstance(callMapType, new object[] { null });
            object module = System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(moduleType);
            RuntimeType("Nofun.Module.IModule").GetMethod("Register")
                .Invoke(module, new[] { callMap });

            object registrations = callMapType.GetField("callmap",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(callMap);
            bool isRegistered = (bool)registrations.GetType().GetMethod("ContainsKey")
                .Invoke(registrations, new object[] { "vMsgBoxU" });
            Assert.That(isRegistered, Is.True);

            MethodInfo buttonValueConverter = moduleType.GetMethod("ToMophunButtonValue",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(buttonValueConverter.Invoke(null, new object[] { 0 }), Is.EqualTo(1),
                "The UI's right-hand OK/Yes result must map to the Mophun success value.");
            Assert.That(buttonValueConverter.Invoke(null, new object[] { 1 }), Is.EqualTo(0),
                "The UI's left-hand No/Cancel result must map to the Mophun cancel value.");
        }

        [Test]
        public void ImportCopiesGameToPrivateDestination()
        {
            string source = Path.Combine(temporaryDirectory, "picked.mpn");
            string destination = Path.Combine(temporaryDirectory, "__Games", "00000001.mpn");
            byte[] contents = CreateMinimalPlainMpn();
            File.WriteAllBytes(source, contents);

            object result = Import(source, destination);

            Assert.That(Property(result, "Succeeded"), Is.True);
            Assert.That(Property(result, "ImportedPath"), Is.EqualTo(destination));
            Assert.That(File.ReadAllBytes(destination), Is.EqualTo(contents));
        }

        [Test]
        public void ImportRejectsInvalidMpnWithTypedError()
        {
            string source = Path.Combine(temporaryDirectory, "invalid.mpn");
            File.WriteAllBytes(source, new byte[] { 1, 2, 3, 4 });

            object result = Import(source,
                Path.Combine(temporaryDirectory, "__Games", "00000001.mpn"));

            Assert.That(Property(result, "Succeeded"), Is.False);
            Assert.That(Property(result, "ErrorCode").ToString(), Is.EqualTo("InvalidMpn"));
        }

        [Test]
        public void MultipartMpnPartsAreAssembledAndMpcResourcesCopied()
        {
            string firstPart = Path.Combine(temporaryDirectory, "1_2_TestGame.mpn");
            string secondPart = Path.Combine(temporaryDirectory, "2_2_TestGame.mpn");
            string resource = Path.Combine(temporaryDirectory, "TestGame_extrapack.mpc");
            string destination = Path.Combine(temporaryDirectory, "__Games", "00000001.mpn");
            string resourceDirectory = Path.Combine(temporaryDirectory, "__Resources");
            byte[] original = CreateMinimalPlainMpn();

            File.WriteAllBytes(firstPart, Slice(original, 0, 19));
            File.WriteAllBytes(secondPart, Slice(original, 19, original.Length - 19));
            File.WriteAllBytes(resource, new byte[] { 0x4D, 0x50, 0x43, 0x01, 0x02 });

            Type serviceType = RuntimeType("Nofun.Services.GameImportService");
            object service = Activator.CreateInstance(serviceType);
            object result = serviceType.GetMethod("ImportBundle").Invoke(service,
                new object[] { new[] { secondPart, firstPart, resource }, destination, resourceDirectory });

            Assert.That(Property(result, "Succeeded"), Is.True);
            Assert.That(Property(result, "WasMultipart"), Is.True);
            Assert.That(Property(result, "MultipartPartCount"), Is.EqualTo(2));
            Assert.That(File.ReadAllBytes(destination), Is.EqualTo(original));

            string copiedResource = Path.Combine(resourceDirectory, Path.GetFileName(resource));
            Assert.That(File.Exists(copiedResource), Is.True);
            Assert.That(File.ReadAllBytes(copiedResource), Is.EqualTo(File.ReadAllBytes(resource)));
            Assert.That((string[])Property(result, "ImportedResourcePaths"), Is.EqualTo(new[] { copiedResource }));
        }

        [Test]
        public void MultipartMpnReportsMissingPartWithoutWritingOutput()
        {
            string part = Path.Combine(temporaryDirectory, "1_3_Incomplete.mpn");
            string destination = Path.Combine(temporaryDirectory, "__Games", "00000001.mpn");
            File.WriteAllBytes(part, new byte[] { 1, 2, 3 });

            Type serviceType = RuntimeType("Nofun.Services.GameImportService");
            object service = Activator.CreateInstance(serviceType);
            object result = serviceType.GetMethod("ImportBundle").Invoke(service,
                new object[] { new[] { part }, destination, Path.Combine(temporaryDirectory, "__Resources") });

            Assert.That(Property(result, "Succeeded"), Is.False);
            Assert.That(Property(result, "ErrorCode").ToString(), Is.EqualTo("MissingMultipartPart"));
            Assert.That(File.Exists(destination), Is.False);
        }

        [Test]
        public void EncryptedHoneyCaveIsDecryptedAndCachedWithoutChangingSource()
        {
            string source = Environment.GetEnvironmentVariable("ONLYFUN_TEST_GAME");
            string expected = Environment.GetEnvironmentVariable("ONLYFUN_EXPECTED_DECRYPTED");
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source) ||
                string.IsNullOrWhiteSpace(expected) || !File.Exists(expected))
            {
                Assert.Ignore("Set ONLYFUN_TEST_GAME and ONLYFUN_EXPECTED_DECRYPTED for the encrypted fixture test.");
            }

            string destination = Path.Combine(temporaryDirectory, "__Games", "00000001.mpn");
            string cache = Path.Combine(temporaryDirectory, "__MophunCache");
            byte[] original = File.ReadAllBytes(source);
            byte[] expectedBytes = File.ReadAllBytes(expected);

            Type serviceType = RuntimeType("Nofun.Services.GameImportService");
            object service = Activator.CreateInstance(serviceType, new object[] { cache });
            object first = serviceType.GetMethod("Import").Invoke(service, new[] { source, destination });

            Assert.That(Property(first, "Succeeded"), Is.True);
            Assert.That(Property(first, "WasDecrypted"), Is.True);
            Assert.That(File.ReadAllBytes(destination), Is.EqualTo(expectedBytes));
            Assert.That(File.ReadAllBytes(source), Is.EqualTo(original));

            File.Delete(destination);
            object second = serviceType.GetMethod("Import").Invoke(service, new[] { source, destination });
            Assert.That(Property(second, "Succeeded"), Is.True);
            Assert.That(Property(second, "WasDecrypted"), Is.True);
            Assert.That(File.ReadAllBytes(destination), Is.EqualTo(expectedBytes));
            Assert.That(Directory.GetFiles(cache, "*.mpn").Length, Is.EqualTo(1));
        }

        [Test]
        public void ImportRejectsEmptyGameWithTypedError()
        {
            string source = Path.Combine(temporaryDirectory, "empty.mpn");
            File.WriteAllBytes(source, new byte[0]);

            object result = Import(source,
                Path.Combine(temporaryDirectory, "__Games", "00000001.mpn"));

            Assert.That(Property(result, "Succeeded"), Is.False);
            Assert.That(Property(result, "ErrorCode").ToString(), Is.EqualTo("EmptyFile"));
        }

        private object Import(string source, string destination)
        {
            Type serviceType = RuntimeType("Nofun.Services.GameImportService");
            object service = Activator.CreateInstance(serviceType);
            return serviceType.GetMethod("Import").Invoke(service, new[] { source, destination });
        }

        private static byte[] CreateMinimalPlainMpn()
        {
            byte[] bytes = new byte[52];
            bytes[0] = (byte)'V';
            bytes[1] = (byte)'M';
            bytes[2] = (byte)'G';
            bytes[3] = (byte)'P';
            WriteUInt32(bytes, 12, 4);
            WriteUInt32(bytes, 24, 8);
            WriteUInt32(bytes, 40, 0);
            WriteUInt32(bytes, 48, 0);
            return bytes;
        }

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        private static byte[] Slice(byte[] bytes, int offset, int count)
        {
            byte[] result = new byte[count];
            Buffer.BlockCopy(bytes, offset, result, 0, count);
            return result;
        }

        private Type RuntimeType(string name) =>
            runtimeAssembly.GetType(name, true);

        private static object Field(Type type, object instance, string name) =>
            type.GetField(name).GetValue(instance);

        private static object Property(object instance, string name) =>
            instance.GetType().GetProperty(name).GetValue(instance);

        private static void AssertHoneyCaveProfile(object profile)
        {
            Type profileType = profile.GetType();
            Assert.That(Field(profileType, profile, "screenSizeX"), Is.EqualTo(101));
            Assert.That(Field(profileType, profile, "screenSizeY"), Is.EqualTo(80));
            Assert.That(Field(profileType, profile, "orientation").ToString(), Is.EqualTo("Potrait"));
            object deviceModel = Field(profileType, profile, "deviceModel");
            Assert.That(deviceModel, Is.EqualTo(Enum.Parse(deviceModel.GetType(), "SonyEricssonT310")));
            Assert.That(Field(profileType, profile, "systemVersion").ToString(), Is.EqualTo("Version130"));
            Assert.That(Field(profileType, profile, "cpuBackend").ToString(), Is.EqualTo("Interpreter"));
            Assert.That(Field(profileType, profile, "fps"), Is.EqualTo(15));
        }
    }
}
