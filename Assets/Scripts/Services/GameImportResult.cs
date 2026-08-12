/*
 * (C) 2023 Radrat Softworks
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 */

using System;

namespace Nofun.Services
{
    public enum GameImportErrorCode
    {
        None,
        SourceUnavailable,
        PermissionDenied,
        EmptyFile,
        InvalidMpn,
        MissingMultipartPart,
        InvalidInputSet,
        UnsupportedEncryption,
        DecryptionFailed,
        CopyFailed
    }

    public readonly struct GameImportResult
    {
        public bool Succeeded { get; }
        public string ImportedPath { get; }
        public GameImportErrorCode ErrorCode { get; }
        public string Message { get; }
        public Exception Exception { get; }
        public bool WasDecrypted { get; }
        public string SourceSha256 { get; }
        public bool WasMultipart { get; }
        public int MultipartPartCount { get; }
        public string ImportedResourceDirectory { get; }
        public string[] ImportedResourcePaths { get; }

        private GameImportResult(bool succeeded, string importedPath, GameImportErrorCode errorCode,
            string message, Exception exception, bool wasDecrypted, string sourceSha256,
            bool wasMultipart, int multipartPartCount, string importedResourceDirectory,
            string[] importedResourcePaths)
        {
            Succeeded = succeeded;
            ImportedPath = importedPath;
            ErrorCode = errorCode;
            Message = message;
            Exception = exception;
            WasDecrypted = wasDecrypted;
            SourceSha256 = sourceSha256;
            WasMultipart = wasMultipart;
            MultipartPartCount = multipartPartCount;
            ImportedResourceDirectory = importedResourceDirectory;
            ImportedResourcePaths = importedResourcePaths ?? new string[0];
        }

        public static GameImportResult Success(string importedPath, bool wasDecrypted = false, string sourceSha256 = null,
            bool wasMultipart = false, int multipartPartCount = 1,
            string importedResourceDirectory = null, string[] importedResourcePaths = null) =>
            new(true, importedPath, GameImportErrorCode.None, null, null, wasDecrypted, sourceSha256,
                wasMultipart, multipartPartCount, importedResourceDirectory, importedResourcePaths);

        public static GameImportResult Failure(GameImportErrorCode errorCode, string message, Exception exception = null) =>
            new(false, null, errorCode, message, exception, false, null, false, 0, null, null);
    }
}
