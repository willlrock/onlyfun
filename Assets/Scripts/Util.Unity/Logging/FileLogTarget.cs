/*
 * (C) 2023 Radrat Softworks
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 */

using System;
using System.IO;
using Nofun.Util.Logging;
using UnityEngine;

namespace Nofun.Util.Unity
{
    public sealed class FileLogTarget : ILogTarget
    {
        private const long MaximumLogSize = 1024 * 1024;
        private readonly object writeLock = new();
        private readonly string logPath;

        public FileLogTarget(string persistentDataPath)
        {
            logPath = Path.Combine(persistentDataPath, "onlyfun.log");
            TryRotate();
        }

        public string Name => "Onlyfun file";
        public string LogPath => logPath;

        public void Log(object sender, LogEventArgs args)
        {
            lock (writeLock)
            {
                try
                {
                    RotateIfNeeded();
                    File.AppendAllText(logPath,
                        $"{args.time:O} [{args.logLevel}] [{args.logClass}] {args.message}{Environment.NewLine}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Onlyfun could not write its diagnostic log: {ex}");
                }
            }
        }

        private void TryRotate()
        {
            try
            {
                RotateIfNeeded();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Onlyfun could not rotate its diagnostic log: {ex}");
            }
        }

        private void RotateIfNeeded()
        {
            if (!File.Exists(logPath) || new FileInfo(logPath).Length < MaximumLogSize)
            {
                return;
            }

            string previousPath = logPath + ".1";
            if (File.Exists(previousPath))
            {
                File.Delete(previousPath);
            }

            File.Move(logPath, previousPath);
        }
    }
}
