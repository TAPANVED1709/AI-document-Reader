using Microsoft.AspNetCore.Http.Features;

namespace AI.DocumentReader.Api.Services;

public sealed class UploadTooLargeException() : ArgumentException("This report exceeds the 20 MB upload limit.");

public sealed class UploadSizeMiddleware(RequestDelegate next)
{
    // Multipart framing allowance, not an increase to the per-file limit.
    public const long RequestLimit = 21 * 1024 * 1024;
    private const long RejectedBodyDrainLimit = 32 * 1024 * 1024;
    public async Task InvokeAsync(HttpContext context)
    {
        // Batch requests retain their existing aggregate limit; each file is still validated.
        var upload = HttpMethods.IsPost(context.Request.Method) &&
            new[] { "/api/reports/upload", "/api/reports/self-upload", "/api/lab-ingestion/reports" }
                .Contains(context.Request.Path.Value, StringComparer.OrdinalIgnoreCase);
        if (!upload) { await next(context); return; }
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (context.Request.ContentLength > RequestLimit)
        {
            await Reject(context);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            // Some clients send the whole body before reading an early response.
            // Discard modest oversize bodies without buffering, parsing or storing them,
            // so those clients can receive the 413 instead of stalling on their send.
            // This is bounded transport cleanup, not a larger accepted upload limit.
            if (context.Request.ContentLength <= RejectedBodyDrainLimit && feature is { IsReadOnly: false })
            {
                feature.MaxRequestBodySize = RejectedBodyDrainLimit;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                try { await context.Request.Body.CopyToAsync(Stream.Null, timeout.Token); }
                catch (OperationCanceledException) { }
                catch (IOException) { }
            }
            return;
        }
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = RequestLimit;
        try { await next(context); }
        catch (BadHttpRequestException ex) when (ex.StatusCode == 413 && !context.Response.HasStarted) { await Reject(context); }
    }
    private static Task Reject(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return context.Response.WriteAsJsonAsync(new { error = "This report exceeds the 20 MB upload limit." });
    }
}
