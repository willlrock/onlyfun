/*
 * (C) 2023 Radrat Softworks
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 */

using System.IO;
using Nofun.Module.VMGPCaps;
using Nofun.Services;
using Nofun.Settings;
using NUnit.Framework;

namespace Nofun.Tests
{
    public class OnlyfunReliabilityTests
    {
        private string temporaryDirectory;

        [SetUp]
        public void SetUp()
        {
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
            GameSetting profile = GameProfileResolver.Resolve(title, null);

            Assert.That(profile.screenSizeX, Is.EqualTo(101));
            Assert.That(profile.screenSizeY, Is.EqualTo(80));
            Assert.That(profile.orientation, Is.EqualTo(ScreenOrientation.Potrait));
            Assert.That(profile.deviceModel, Is.EqualTo(SystemDeviceModel.SonyEricssonT310));
            Assert.That(profile.systemVersion, Is.EqualTo(SystemVersion.Version130));
            Assert.That(profile.cpuBackend, Is.EqualTo(CPUBackend.Interpreter));
            Assert.That(profile.fps, Is.EqualTo(15));
        }

        [Test]
        public void ImportCopiesGameToPrivateDestination()
        {
            string source = Path.Combine(temporaryDirectory, "picked.mpn");
            string destination = Path.Combine(temporaryDirectory, "__Games", "00000001.mpn");
            byte[] contents = { 1, 2, 3, 4 };
            File.WriteAllBytes(source, contents);

            GameImportResult result = new GameImportService().Import(source, destination);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.ImportedPath, Is.EqualTo(destination));
            Assert.That(File.ReadAllBytes(destination), Is.EqualTo(contents));
        }

        [Test]
        public void ImportRejectsEmptyGameWithTypedError()
        {
            string source = Path.Combine(temporaryDirectory, "empty.mpn");
            File.WriteAllBytes(source, new byte[0]);

            GameImportResult result = new GameImportService().Import(source,
                Path.Combine(temporaryDirectory, "__Games", "00000001.mpn"));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(GameImportErrorCode.EmptyFile));
        }
    }
}
