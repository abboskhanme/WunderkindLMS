using System.Net.Http.Json;

namespace SchoolLms.Application.Services;

/// <summary>The answer of the last attempt: status, raw body, and the model that gave it.</summary>
public sealed record GeminiRestResponse(int StatusCode, string Body, string Model)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}

/// <summary>
/// One <c>generateContent</c> call shared by the Gemini clients, with retries and a fallback model.
/// A new model answers 503 "high demand" for minutes at a time (seen 2026-10-28 on gemini-3.8-flash:
/// every director message failed). So a 429/500/503/504 is retried with a short pause, and when the
/// primary model still fails — or answers 404 because Google retired it — the fallback model is asked.
/// Config: <c>Gemini:Model</c> and <c>Gemini:FallbackModel</c> (env <c>Gemini__FallbackModel</c>).
/// </summary>
public static class GeminiRest
{
    /// <summary>The previous stable Flash — a different capacity pool from the default model.</summary>
    public const string DefaultFallbackModel = "gemini-3.6-flash";

    /// <summary>Pauses between attempts on one model: 3 attempts in all.</summary>
    public static readonly TimeSpan[] DefaultRetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)];

    /// <summary>Primary first, then the fallback; blanks and a fallback equal to the primary dropped.</summary>
    public static IReadOnlyList<string> Models(IConfiguration config)
    {
        var primary = config["Gemini:Model"];
        if (string.IsNullOrWhiteSpace(primary)) primary = GeminiLessonAnalysisAi.DefaultModel;
        var fallback = config["Gemini:FallbackModel"];
        if (string.IsNullOrWhiteSpace(fallback)) fallback = DefaultFallbackModel;
        return new[] { primary.Trim(), fallback.Trim() }.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Our status for an attempt cut off by the per-attempt timeout (no answer from Google).</summary>
    public const int TimeoutStatus = 408;

    /// <summary>Overload or a passing server fault — worth asking again.</summary>
    public static bool IsTransient(int statusCode) => statusCode is 429 or 500 or 503 or 504;

    /// <summary>
    /// Posts <paramref name="payload"/> to each model in turn. A transient status is retried after each of
    /// <paramref name="retryDelays"/>; a transient or 404 answer that survives moves on to the next model;
    /// anything else (400, 403 …) is returned at once — another model would not fix a bad request or key.
    /// <paramref name="attemptTimeout"/> caps one attempt: a model that hangs (seen 2026-10-29 — the HR call
    /// sat until the client's 90 s timeout and never reached the fallback) answers <see cref="TimeoutStatus"/>
    /// and the next model is asked at once; a hang is not retried on the same model.
    /// Network exceptions propagate: the callers already turn them into an error text.
    /// </summary>
    public static async Task<GeminiRestResponse> GenerateAsync(
        HttpClient http, string apiKey, IReadOnlyList<string> models, object payload,
        CancellationToken ct = default, IReadOnlyList<TimeSpan>? retryDelays = null, TimeSpan? attemptTimeout = null)
    {
        var delays = retryDelays ?? DefaultRetryDelays;
        GeminiRestResponse? last = null;
        foreach (var model in models)
        {
            for (var attempt = 0; ; attempt++)
            {
                last = await SendOnceAsync(http, apiKey, model, payload, attemptTimeout, ct);
                if (last.IsSuccess) return last;
                if (last.StatusCode == TimeoutStatus || !IsTransient(last.StatusCode) || attempt >= delays.Count) break;
                await Task.Delay(delays[attempt], ct);
            }
            if (!IsTransient(last.StatusCode) && last.StatusCode is not (404 or TimeoutStatus)) return last;
        }
        return last ?? throw new ArgumentException("No Gemini model to call.", nameof(models));
    }

    private static async Task<GeminiRestResponse> SendOnceAsync(
        HttpClient http, string apiKey, string model, object payload, TimeSpan? timeout, CancellationToken ct)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        request.Headers.Add("x-goog-api-key", apiKey.Trim());
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is { } t) cts.CancelAfter(t);
        try
        {
            using var response = await http.SendAsync(request, cts.Token);
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            return new GeminiRestResponse((int)response.StatusCode, body, model);
        }
        catch (OperationCanceledException) when (timeout is not null && cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new GeminiRestResponse(TimeoutStatus, $"no answer in {timeout.Value.TotalSeconds:0} s", model);
        }
    }
}
