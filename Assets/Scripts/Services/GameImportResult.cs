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

        private GameImportResult(bool succeeded, string importedPath, GameImportErrorCode errorCode,
            string message, Exception exception, bool wasDecrypted, string sourceSha256)
        {
            Succeeded = succeeded;
            ImportedPath = importedPath;
            ErrorCode = errorCode;
            Message = message;
            Exception = exception;
            WasDecrypted = wasDecrypted;
            SourceSha256 = sourceSha256;
        }

        public static GameImportResult Success(string importedPath, bool wasDecrypted = false, string sourceSha256 = null) =>
            new(true, importedPath, GameImportErrorCode.None, null, null, wasDecrypted, sourceSha256);

        public static GameImportResult Failure(GameImportErrorCode errorCode, string message, Exception exception = null) =>
            new(false, null, errorCode, message, exception, false, null);
    }
}
