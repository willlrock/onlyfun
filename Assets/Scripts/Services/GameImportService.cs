/*
 * (C) 2023 Radrat Softworks
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 */

using System;
using System.IO;

namespace Nofun.Services
{
    public interface IGameImportService
    {
        GameImportResult Import(string sourcePath, string destinationPath);
    }

    public sealed class GameImportService : IGameImportService
    {
        public GameImportResult Import(string sourcePath, string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return GameImportResult.Failure(GameImportErrorCode.SourceUnavailable,
                    "The selected game is no longer available.");
            }

            try
            {
                var sourceInfo = new FileInfo(sourcePath);
                if (sourceInfo.Length == 0)
                {
                    return GameImportResult.Failure(GameImportErrorCode.EmptyFile,
                        "The selected game file is empty.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                string temporaryPath = destinationPath + ".importing";

                try
                {
                    using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        source.CopyTo(destination);
                        destination.Flush();
                    }

                    if (File.Exists(destinationPath))
                    {
                        File.Delete(destinationPath);
                    }

                    File.Move(temporaryPath, destinationPath);
                    return GameImportResult.Success(destinationPath);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                return GameImportResult.Failure(GameImportErrorCode.PermissionDenied,
                    "Onlyfun does not have permission to read the selected file.", ex);
            }
            catch (Exception ex)
            {
                return GameImportResult.Failure(GameImportErrorCode.CopyFailed,
                    "Onlyfun could not copy the selected game into its library.", ex);
            }
        }
    }
}
