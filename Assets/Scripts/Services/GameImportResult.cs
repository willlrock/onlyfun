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
        CopyFailed
    }

    public readonly struct GameImportResult
    {
        public bool Succeeded { get; }
        public string ImportedPath { get; }
        public GameImportErrorCode ErrorCode { get; }
        public string Message { get; }
        public Exception Exception { get; }

        private GameImportResult(bool succeeded, string importedPath, GameImportErrorCode errorCode,
            string message, Exception exception)
        {
            Succeeded = succeeded;
            ImportedPath = importedPath;
            ErrorCode = errorCode;
            Message = message;
            Exception = exception;
        }

        public static GameImportResult Success(string importedPath) =>
            new(true, importedPath, GameImportErrorCode.None, null, null);

        public static GameImportResult Failure(GameImportErrorCode errorCode, string message, Exception exception = null) =>
            new(false, null, errorCode, message, exception);
    }
}
