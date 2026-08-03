/*
 * (C) 2023 Radrat Softworks
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 */

using System;
using Nofun.Module.VMGPCaps;
using Nofun.Parser;

namespace Nofun.Settings
{
    public static class GameProfileResolver
    {
        public static GameSetting Resolve(string title, VMGPExecutable executable)
        {
            if (IsHoneyCave2(title))
            {
                return Legacy2DProfile();
            }

            return IsNewGenerationGame(executable) ? NewGenerationProfile() : Legacy2DProfile();
        }

        public static bool IsHoneyCave2(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            string normalized = title.Replace(" ", string.Empty);
            return normalized.Equals("HoneyCave2", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNewGenerationGame(VMGPExecutable executable)
        {
            foreach (var poolItem in executable.PoolItems)
            {
                if (poolItem.poolType == PoolItemType.ImportSymbol &&
                    executable.GetString(poolItem.metaOffset) == "vInit3D")
                {
                    return true;
                }
            }

            return false;
        }

        private static GameSetting Legacy2DProfile() => new()
        {
            screenSizeX = 101,
            screenSizeY = 80,
            fps = 15,
            screenMode = ScreenMode.CustomSize,
            orientation = ScreenOrientation.Potrait,
            deviceModel = SystemDeviceModel.SonyEricssonT310,
            systemVersion = SystemVersion.Version130,
            cpuBackend = CPUBackend.Interpreter,
            enableSoftwareScissor = false
        };

        private static GameSetting NewGenerationProfile() => new()
        {
            screenSizeX = 240,
            screenSizeY = 320,
            fps = 60,
            screenMode = ScreenMode.CustomSize,
            orientation = ScreenOrientation.Potrait,
            deviceModel = SystemDeviceModel.NokiaNgage,
            systemVersion = SystemVersion.Version150,
            cpuBackend = CPUBackend.Interpreter,
            enableSoftwareScissor = false
        };
    }
}
