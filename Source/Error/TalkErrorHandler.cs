using System;
using System.Threading.Tasks;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Error;

public static class AIErrorHandler
{
    private static bool _quotaWarningShown;

    public static async Task<T> HandleWithRetry<T>(Func<Task<T>> operation, Action<Exception> onFailure = null)
    {
        // Add overall timeout protection to prevent infinite hanging
        const int overallTimeoutSeconds = 180; // 3 minutes maximum for entire operation
        
        try
        {
            // Check if game is still running
            if (Current.Game == null)
            {
                Logger.Warning("Game instance not found, cancelling operation");
                return default;
            }
            
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(overallTimeoutSeconds));
            var operationTask = operation();
            
            Task completedTask = null;
            try
            {
                completedTask = await Task.WhenAny(operationTask, timeoutTask);
            }
            catch (Exception awaitEx)
            {
                Logger.Error($"Task.WhenAny exception: {awaitEx.Message}");
                throw;
            }
            
            if (completedTask == timeoutTask)
            {
                var timeoutEx = new TimeoutException($"Operation exceeded overall timeout of {overallTimeoutSeconds}s");
                HandleFinalFailure(timeoutEx);
                onFailure?.Invoke(timeoutEx);
                return default;
            }
            
            try
            {
                return await operationTask;
            }
            catch (Exception taskEx)
            {
                // Re-throw to be caught by outer catch
                throw;
            }
        }
        catch (Exception ex)
        {
            var settings = Settings.Get();
            if (!CanRetryGeneration(settings))
            {
                HandleFinalFailure(ex);
                onFailure?.Invoke(ex);
                return default;
            }

            // Prepare for retry
            var nextModel = settings.GetCurrentModel();
            if (!settings.UseSimpleConfig)
            {
                ShowRetryMessage(ex, nextModel);
            }

            try
            {
                // Check if game is still running before retry
                if (Current.Game == null)
                {
                    Logger.Warning("Game instance not found during retry, cancelling operation");
                    return default;
                }
                
                var retryTimeoutTask = Task.Delay(TimeSpan.FromSeconds(overallTimeoutSeconds));
                var retryTask = operation();
                
                Task completedRetryTask = null;
                try
                {
                    completedRetryTask = await Task.WhenAny(retryTask, retryTimeoutTask);
                }
                catch (Exception awaitEx)
                {
                    Logger.Error($"Retry Task.WhenAny exception: {awaitEx.Message}");
                    HandleFinalFailure(awaitEx);
                    onFailure?.Invoke(awaitEx);
                    return default;
                }
                
                if (completedRetryTask == retryTimeoutTask)
                {
                    var timeoutEx = new TimeoutException($"Retry operation exceeded overall timeout of {overallTimeoutSeconds}s");
                    HandleFinalFailure(timeoutEx);
                    onFailure?.Invoke(timeoutEx);
                    return default;
                }
                
                try
                {
                    return await retryTask;
                }
                catch (Exception taskEx)
                {
                    throw;
                }
            }
            catch (Exception retryEx)
            {
                Logger.Warning($"Retry failed: {retryEx.Message}\n{retryEx.StackTrace}");
                HandleFinalFailure(ex); // Show the original error logic
                onFailure?.Invoke(retryEx);
                return default;
            }
        }
    }

    private static bool CanRetryGeneration(RimTalkSettings settings)
    {
        if (settings.UseSimpleConfig)
        {
            if (settings.IsUsingFallbackModel) return false;
            settings.IsUsingFallbackModel = true;
            return true;
        }

        if (!settings.UseCloudProviders) return false;
        int originalIndex = settings.CurrentCloudConfigIndex;
        settings.TryNextConfig();
        return settings.CurrentCloudConfigIndex != originalIndex;

    }

    private static void HandleFinalFailure(Exception ex)
    {
        if (ex is QuotaExceededException)
        {
            ShowQuotaWarning(ex);
        }
        else
        {
            ShowGenerationWarning(ex);
        }
    }

    public static void ResetQuotaWarning()
    {
        _quotaWarningShown = false;
    }

    private static void ShowQuotaWarning(Exception ex)
    {
        if (!_quotaWarningShown)
        {
            _quotaWarningShown = true;
            string message = "RimTalk.TalkService.QuotaExceeded".Translate();
            Messages.Message(message, MessageTypeDefOf.NeutralEvent, false);
            Logger.Warning(ex.Message);
        }
    }

    private static void ShowGenerationWarning(Exception ex)
    {
        Logger.Warning(ex.StackTrace);
        string message = $"{"RimTalk.TalkService.GenerationFailed".Translate()}: {ex.Message}";
        Messages.Message(message, MessageTypeDefOf.NeutralEvent, false);
    }

    private static void ShowRetryMessage(Exception ex, string nextModel)
    {
        string messageKey = ex is QuotaExceededException ? "RimTalk.TalkService.QuotaReached" : "RimTalk.TalkService.APIError";
        string message = $"{messageKey.Translate()}. {"RimTalk.TalkService.TryingNextAPI".Translate(nextModel)}";
        Messages.Message(message, MessageTypeDefOf.NeutralEvent, false);
    }
}