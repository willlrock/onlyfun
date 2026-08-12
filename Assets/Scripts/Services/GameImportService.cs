/*
 * (C) 2023 Radrat Softworks
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Nofun.Services
{
    public interface IGameImportService
    {
        GameImportResult Import(string sourcePath, string destinationPath);
        GameImportResult ImportBundle(string[] sourcePaths, string destinationPath, string resourceDirectory);
    }

    public sealed class GameImportService : IGameImportService
    {
        private readonly string decryptionCacheDirectory;

        private sealed class MultipartPart
        {
            public string Path;
            public int Number;
            public int Total;
            public string BaseName;
        }

        private sealed class InputSet
        {
            public readonly List<string> MpnPaths = new List<string>();
            public readonly List<string> MpcPaths = new List<string>();
            public bool WasMultipart;
            public int MultipartPartCount;
        }

        public GameImportService() : this(null)
        {
        }

        public GameImportService(string decryptionCacheDirectory)
        {
            this.decryptionCacheDirectory = decryptionCacheDirectory;
        }

        public GameImportResult Import(string sourcePath, string destinationPath)
        {
            return ImportBundle(new[] { sourcePath }, destinationPath, null);
        }

        /// <summary>
        /// Imports one MPN or a complete numbered multipart MPN set. Related MPC
        /// files are copied to a temporary resource directory and finalized by the
        /// game-list controller after it has read the game's title.
        /// </summary>
        public GameImportResult ImportBundle(string[] sourcePaths, string destinationPath, string resourceDirectory)
        {
            InputSet inputSet;
            string inputError;
            GameImportErrorCode inputErrorCode;
            try
            {
                if (!TryBuildInputSet(sourcePaths, out inputSet, out inputErrorCode, out inputError))
                {
                    return GameImportResult.Failure(inputErrorCode, inputError);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                return GameImportResult.Failure(GameImportErrorCode.PermissionDenied,
                    "Onlyfun does not have permission to inspect the selected game set.", ex);
            }
            catch (Exception ex)
            {
                return GameImportResult.Failure(GameImportErrorCode.InvalidInputSet,
                    "Onlyfun could not inspect the selected game set.", ex);
            }

            try
            {
                string destinationParent = Path.GetDirectoryName(destinationPath);
                if (string.IsNullOrEmpty(destinationParent))
                {
                    return GameImportResult.Failure(GameImportErrorCode.CopyFailed,
                        "Onlyfun could not determine where to store the game.");
                }

                byte[] sourceBytes = ReadCombinedMpn(inputSet.MpnPaths);
                if (sourceBytes.Length == 0)
                {
                    return GameImportResult.Failure(GameImportErrorCode.EmptyFile,
                        "The selected game file is empty.");
                }

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

                Directory.CreateDirectory(destinationParent);
                WriteAtomically(destinationPath, normalizedBytes);

                string copiedResourceDirectory = null;
                string[] copiedResourcePaths = new string[0];
                if (inputSet.MpcPaths.Count > 0)
                {
                    if (string.IsNullOrEmpty(resourceDirectory))
                    {
                        return GameImportResult.Failure(GameImportErrorCode.CopyFailed,
                            "Onlyfun could not determine where to store the related MPC resources.");
                    }

                    copiedResourceDirectory = resourceDirectory;
                    Directory.CreateDirectory(copiedResourceDirectory);
                    List<string> copied = new List<string>();
                    foreach (string sourcePath in inputSet.MpcPaths)
                    {
                        string fileName = Path.GetFileName(sourcePath);
                        string targetPath = Path.Combine(copiedResourceDirectory, fileName);
                        WriteAtomicallyFromFile(sourcePath, targetPath);
                        copied.Add(targetPath);
                    }

                    copiedResourcePaths = copied.ToArray();
                }

                return GameImportResult.Success(destinationPath, wasDecrypted, sourceSha256,
                    inputSet.WasMultipart, inputSet.MultipartPartCount,
                    copiedResourceDirectory, copiedResourcePaths);
            }
            catch (UnauthorizedAccessException ex)
            {
                return GameImportResult.Failure(GameImportErrorCode.PermissionDenied,
                    "Onlyfun does not have permission to read the selected file.", ex);
            }
            catch (Exception ex)
            {
                return GameImportResult.Failure(GameImportErrorCode.CopyFailed,
                    "Onlyfun could not import the selected game set.", ex);
            }
        }

        private static bool TryBuildInputSet(string[] sourcePaths, out InputSet inputSet,
            out GameImportErrorCode errorCode, out string error)
        {
            inputSet = new InputSet();
            errorCode = GameImportErrorCode.None;
            error = null;

            if (sourcePaths == null || sourcePaths.Length == 0)
            {
                errorCode = GameImportErrorCode.SourceUnavailable;
                error = "No game files were selected.";
                return false;
            }

            List<string> selectedMpn = new List<string>();
            List<string> selectedMpc = new List<string>();
            HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string sourcePath in sourcePaths)
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    errorCode = GameImportErrorCode.SourceUnavailable;
                    error = "One of the selected files is no longer available.";
                    return false;
                }

                string fullPath = Path.GetFullPath(sourcePath);
                if (!seenPaths.Add(fullPath))
                {
                    continue;
                }

                string extension = Path.GetExtension(fullPath);
                if (string.Equals(extension, ".mpn", StringComparison.OrdinalIgnoreCase))
                {
                    selectedMpn.Add(fullPath);
                }
                else if (string.Equals(extension, ".mpc", StringComparison.OrdinalIgnoreCase))
                {
                    selectedMpc.Add(fullPath);
                }
                else
                {
                    errorCode = GameImportErrorCode.InvalidInputSet;
                    error = "Select a Mophun .mpn game, its numbered .mpn parts, and optional .mpc resources.";
                    return false;
                }
            }

            if (selectedMpn.Count == 0)
            {
                errorCode = GameImportErrorCode.InvalidInputSet;
                error = "Select at least one .mpn game file. Related .mpc files can be selected with it.";
                return false;
            }

            List<MultipartPart> parts = new List<MultipartPart>();
            foreach (string path in selectedMpn)
            {
                MultipartPart part;
                if (TryParseMultipartName(path, out part))
                {
                    parts.Add(part);
                }
            }

            if (parts.Count > 0)
            {
                MultipartPart first = parts[0];
                for (int i = 1; i < parts.Count; i++)
                {
                    if (parts[i].Total != first.Total ||
                        !string.Equals(parts[i].BaseName, first.BaseName, StringComparison.OrdinalIgnoreCase))
                    {
                        errorCode = GameImportErrorCode.InvalidInputSet;
                        error = "The selected numbered MPN parts belong to different multipart games.";
                        return false;
                    }
                }

                // On desktop, selecting one part can still discover its siblings.
                // Android providers often expose only the copied selections, so the
                // multi-file picker remains the portable fallback.
                HashSet<string> selectedPartPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (MultipartPart part in parts)
                {
                    selectedPartPaths.Add(Path.GetFullPath(part.Path));
                }

                foreach (string directory in DistinctDirectories(selectedMpn))
                {
                    string[] candidates = Directory.GetFiles(directory, "*.mpn");
                    foreach (string candidate in candidates)
                    {
                        MultipartPart candidatePart;
                        if (!TryParseMultipartName(candidate, out candidatePart) ||
                            candidatePart.Total != first.Total ||
                            !string.Equals(candidatePart.BaseName, first.BaseName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (selectedPartPaths.Add(Path.GetFullPath(candidate)))
                        {
                            parts.Add(candidatePart);
                        }
                    }
                }

                Dictionary<int, MultipartPart> byNumber = new Dictionary<int, MultipartPart>();
                foreach (MultipartPart part in parts)
                {
                    if (byNumber.ContainsKey(part.Number))
                    {
                        errorCode = GameImportErrorCode.InvalidInputSet;
                        error = "The multipart selection contains a duplicate MPN part.";
                        return false;
                    }

                    byNumber.Add(part.Number, part);
                }

                List<string> ordered = new List<string>();
                List<int> missing = new List<int>();
                for (int number = 1; number <= first.Total; number++)
                {
                    MultipartPart part;
                    if (!byNumber.TryGetValue(number, out part))
                    {
                        missing.Add(number);
                    }
                    else
                    {
                        ordered.Add(part.Path);
                    }
                }

                if (missing.Count > 0)
                {
                    errorCode = GameImportErrorCode.MissingMultipartPart;
                    error = $"This looks like a multipart MPN set, but part(s) {string.Join(", ", missing)} of {first.Total} are missing. Select all files named 1_{first.Total}_... through {first.Total}_{first.Total}_... together.";
                    return false;
                }

                inputSet.MpnPaths.AddRange(ordered);
                inputSet.WasMultipart = first.Total > 1;
                inputSet.MultipartPartCount = first.Total;

                // A locally extracted multipart set commonly keeps its MPC files
                // beside the MPN parts. Include those automatically when possible.
                foreach (string directory in DistinctDirectories(inputSet.MpnPaths))
                {
                    foreach (string candidate in Directory.GetFiles(directory, "*.mpc"))
                    {
                        if (!ContainsPath(selectedMpc, candidate))
                        {
                            selectedMpc.Add(candidate);
                        }
                    }
                }
            }
            else
            {
                if (selectedMpn.Count != 1)
                {
                    errorCode = GameImportErrorCode.InvalidInputSet;
                    error = "Select one Mophun game or all parts of one numbered multipart game.";
                    return false;
                }

                inputSet.MpnPaths.Add(selectedMpn[0]);
                inputSet.WasMultipart = false;
                inputSet.MultipartPartCount = 1;
            }

            foreach (string mpcPath in selectedMpc)
            {
                string fileName = Path.GetFileName(mpcPath);
                if (string.IsNullOrEmpty(fileName))
                {
                    errorCode = GameImportErrorCode.InvalidInputSet;
                    error = "One of the selected MPC resources has no file name.";
                    return false;
                }

                for (int i = 0; i < inputSet.MpcPaths.Count; i++)
                {
                    if (string.Equals(Path.GetFileName(inputSet.MpcPaths[i]), fileName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        errorCode = GameImportErrorCode.InvalidInputSet;
                        error = $"The selected resource set contains duplicate MPC file name '{fileName}'.";
                        return false;
                    }
                }

                inputSet.MpcPaths.Add(mpcPath);
            }

            return true;
        }

        private static IEnumerable<string> DistinctDirectories(IEnumerable<string> paths)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && seen.Add(directory))
                {
                    yield return directory;
                }
            }
        }

        private static bool ContainsPath(List<string> paths, string candidate)
        {
            string fullCandidate = Path.GetFullPath(candidate);
            for (int i = 0; i < paths.Count; i++)
            {
                if (string.Equals(Path.GetFullPath(paths[i]), fullCandidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseMultipartName(string path, out MultipartPart part)
        {
            part = null;
            string fileName = Path.GetFileNameWithoutExtension(path);
            int firstSeparator = fileName.IndexOf('_');
            if (firstSeparator <= 0)
            {
                return false;
            }

            int secondSeparator = fileName.IndexOf('_', firstSeparator + 1);
            if (secondSeparator <= firstSeparator + 1 || secondSeparator == fileName.Length - 1)
            {
                return false;
            }

            int number;
            int total;
            if (!int.TryParse(fileName.Substring(0, firstSeparator), out number) ||
                !int.TryParse(fileName.Substring(firstSeparator + 1, secondSeparator - firstSeparator - 1), out total) ||
                number < 1 || total < number)
            {
                return false;
            }

            part = new MultipartPart
            {
                Path = path,
                Number = number,
                Total = total,
                BaseName = fileName.Substring(secondSeparator + 1)
            };
            return true;
        }

        private static byte[] ReadCombinedMpn(List<string> paths)
        {
            if (paths.Count == 1)
            {
                return File.ReadAllBytes(paths[0]);
            }

            using (MemoryStream combined = new MemoryStream())
            {
                foreach (string path in paths)
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    combined.Write(bytes, 0, bytes.Length);
                }

                return combined.ToArray();
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
                string parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

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

        private static void WriteAtomicallyFromFile(string sourcePath, string destinationPath)
        {
            string temporaryPath = destinationPath + ".importing";
            try
            {
                using (FileStream source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (FileStream destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    source.CopyTo(destination);
                    destination.Flush();
                }

                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }

                File.Move(temporaryPath, destinationPath);
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
