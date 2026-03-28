using System;

namespace ResourceRouter.Core.Abstractions;

public interface IAppLogger
{
    void LogInfo(string message);

    void LogWarning(string message);

    void LogError(string message, Exception? exception = null);
}