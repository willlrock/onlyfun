/*
 * (C) 2023 Radrat Softworks
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 */

using System;
using System.IO;
using System.Security.Cryptography;

namespace Nofun.Services
{
    public interface IGameImportService
    {
        GameImportResult Import(string sourcePath, string destinationPath);
    }

    public sealed class GameImportService : IGameImportService
    {
        private readonly string decryptionCacheDirectory;

        public GameImportService() : this(null)
        {
        }

        public GameImportService(string decryptionCacheDirectory)
        {
            this.decryptionCacheDirectory = decryptionCacheDirectory;
        }

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

                byte[] sourceBytes = File.ReadAllBytes(sourcePath);
                string sourceSha256 = ComputeSha256(sourceBytes);
                byte[] normalizedBytes;
                bool wasDecrypted;

                string cachePath = GetCachePath(sourceSha256);
                if (cachePath != null && File.Exists(cachePath))
                {
                    byte[] cachedBytes = File.ReadAllBytes(cachePath);
                    string cacheError;
                    if (MophunDecryptor.TryValidatePlain(cachedBytes, out cacheError))
                    {
                        normalizedBytes = cachedBytes;
                        wasDecrypted = true;
                    }
                    else
                    {
                        TryDelete(cachePath);
                        normalizedBytes = null;
                        wasDecrypted = false;
                    }
                }
                else
                {
                    MophunNormalizationResult normalization = MophunDecryptor.Normalize(sourceBytes);
                    if (!normalization.Succeeded)
                    {
                        GameImportErrorCode errorCode = normalization.Status == MophunNormalizationStatus.InvalidMpn
                            ? GameImportErrorCode.InvalidMpn
                            : normalization.Status == MophunNormalizationStatus.UnsupportedEncryption
                                ? GameImportErrorCode.UnsupportedEncryption
                                : GameImportErrorCode.DecryptionFailed;
                        return GameImportResult.Failure(errorCode, normalization.Message);
                    }

                    normalizedBytes = normalization.Bytes;
                    wasDecrypted = normalization.WasDecrypted;
                    if (wasDecrypted && cachePath != null)
                    {
                        WriteAtomically(cachePath, normalizedBytes);
                    }
                }

                string destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (string.IsNullOrEmpty(destinationDirectory))
                {
                    return GameImportResult.Failure(GameImportErrorCode.CopyFailed,
                        "Onlyfun could not determine where to store the game.");
                }

                Directory.CreateDirectory(destinationDirectory);
                string temporaryPath = destinationPath + ".importing";

                try
                {
                    using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        destination.Write(normalizedBytes, 0, normalizedBytes.Length);
                        destination.Flush();
                    }

                    if (File.Exists(destinationPath))
                    {
                        File.Delete(destinationPath);
                    }

                    File.Move(temporaryPath, destinationPath);
                    return GameImportResult.Success(destinationPath, wasDecrypted, sourceSha256);
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

        private string GetCachePath(string sourceSha256)
        {
            if (string.IsNullOrWhiteSpace(decryptionCacheDirectory))
            {
                return null;
            }

            Directory.CreateDirectory(decryptionCacheDirectory);
            return Path.Combine(decryptionCacheDirectory, sourceSha256 + ".mpn");
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static void WriteAtomically(string path, byte[] bytes)
        {
            string temporaryPath = path + ".importing";
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temporaryPath, path);
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // A stale cache can be rebuilt on the next import.
            }
        }
    }
}
