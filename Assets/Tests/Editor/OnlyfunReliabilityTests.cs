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
        public void ImportCopiesGameToPrivateDestination()
        {
            string source = Path.Combine(temporaryDirectory, "picked.mpn");
            string destination = Path.Combine(temporaryDirectory, "__Games", "00000001.mpn");
            byte[] contents = { 1, 2, 3, 4 };
            File.WriteAllBytes(source, contents);

            object result = Import(source, destination);

            Assert.That(Property(result, "Succeeded"), Is.True);
            Assert.That(Property(result, "ImportedPath"), Is.EqualTo(destination));
            Assert.That(File.ReadAllBytes(destination), Is.EqualTo(contents));
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
            Assert.That(Field(profileType, profile, "deviceModel").ToString(), Is.EqualTo("SonyEricssonT310"));
            Assert.That(Field(profileType, profile, "systemVersion").ToString(), Is.EqualTo("Version130"));
            Assert.That(Field(profileType, profile, "cpuBackend").ToString(), Is.EqualTo("Interpreter"));
            Assert.That(Field(profileType, profile, "fps"), Is.EqualTo(15));
        }
    }
}
