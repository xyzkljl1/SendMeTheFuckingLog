using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;

namespace SendMeTheFuckingLog.Storage;

public static class UploadEndpoint
{
    public static async Task<IResult> HandleAsync(HttpContext context, IAntiforgery antiforgery,
        SubmissionStore store, ILogger<SubmissionStore> logger)
    {
        try
        {
            if (!context.Request.Headers.ContainsKey("X-CSRF-TOKEN"))
                return Results.Json(new { error = "refreshPage" }, statusCode: 400);
            await antiforgery.ValidateRequestAsync(context);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var receipt = await store.ReceiveAsync(context.Request, timeout.Token);
            return Results.Json(receipt, statusCode: 201);
        }
        catch (UploadFailure failure) { return Results.Json(new { error = failure.Code }, statusCode: failure.StatusCode); }
        catch (AntiforgeryValidationException) { return Results.Json(new { error = "refreshPage" }, statusCode: 400); }
        catch (OperationCanceledException) { return Results.Json(new { error = "unknownResult" }, statusCode: 408); }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or BadHttpRequestException)
        {
            return Results.Json(new { error = "invalidSubmission" }, statusCode: 400);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Upload could not be confirmed as saved.");
            return Results.Json(new { error = "storageFailed" }, statusCode: 503);
        }
    }
}
