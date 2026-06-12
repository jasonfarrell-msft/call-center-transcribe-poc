using System.Net.Http.Json;
using CallCenterTranscription.Shared.Events;
using Microsoft.Extensions.Options;

namespace CallCenterTranscription.Web.Services;

public sealed class PipelineApiClient(HttpClient httpClient, IOptions<BackendApiOptions> options)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly BackendApiOptions _options = options.Value;

    public async Task<ApiFetchResult<SessionCurrentResponse>> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        return await GetFromApiAsync<SessionCurrentResponse>("api/session/current", cancellationToken);
    }

    public async Task<ApiFetchResult<PipelineCurrentStateResponse>> GetCurrentStateAsync(CancellationToken cancellationToken = default)
    {
        return await GetFromApiAsync<PipelineCurrentStateResponse>("api/session/current-state", cancellationToken);
    }

    public async Task<ApiFetchResult<IReadOnlyList<TranscriptEvent>>> GetTranscriptEventsAsync(CancellationToken cancellationToken = default)
    {
        return await GetFromApiAsync<IReadOnlyList<TranscriptEvent>>("api/events/transcript", cancellationToken);
    }

    public async Task<ApiFetchResult<IReadOnlyList<TranslationEvent>>> GetTranslationEventsAsync(CancellationToken cancellationToken = default)
    {
        return await GetFromApiAsync<IReadOnlyList<TranslationEvent>>("api/events/translation", cancellationToken);
    }

    public async Task<ApiFetchResult<SentimentFeedResponse>> GetSentimentFeedAsync(CancellationToken cancellationToken = default)
    {
        return await GetFromApiAsync<SentimentFeedResponse>("api/events/sentiment", cancellationToken);
    }

    public async Task<ApiFetchResult<IReadOnlyList<KnowledgeCardEvent>>> GetKnowledgeCardEventsAsync(CancellationToken cancellationToken = default)
    {
        return await GetFromApiAsync<IReadOnlyList<KnowledgeCardEvent>>("api/events/knowledge-cards", cancellationToken);
    }

    public async Task<ApiFetchResult<MissionControlHealthResponse>> GetMissionControlHealthAsync(CancellationToken cancellationToken = default)
    {
        return await GetFromApiAsync<MissionControlHealthResponse>("api/mission-control/health", cancellationToken);
    }

    private async Task<ApiFetchResult<T>> GetFromApiAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return ApiFetchResult<T>.Failure(
                ApiFetchFailureKind.Configuration,
                $"Backend API is disconnected. Configure {BackendApiOptions.SectionName}:BaseUrl for {path}.");
        }

        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            return ApiFetchResult<T>.Failure(
                ApiFetchFailureKind.Configuration,
                $"Backend API BaseUrl '{_options.BaseUrl}' is invalid.");
        }

        Uri requestUri;
        try
        {
            requestUri = new Uri(baseUri, path);
        }
        catch (UriFormatException ex)
        {
            return ApiFetchResult<T>.Failure(
                ApiFetchFailureKind.Configuration,
                $"Backend API request URI for {path} is invalid: {ex.Message}");
        }

        try
        {
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ApiFetchResult<T>.Failure(
                    ApiFetchFailureKind.Upstream,
                    $"Backend API returned {(int)response.StatusCode} ({response.ReasonPhrase}) for {path}.");
            }

            var payload = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            if (payload is null)
            {
                return ApiFetchResult<T>.Failure(
                    ApiFetchFailureKind.Payload,
                    $"Backend API returned an empty payload for {path}.");
            }

            return ApiFetchResult<T>.Success(payload);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return ApiFetchResult<T>.Failure(
                ApiFetchFailureKind.Connectivity,
                $"Backend API request timed out for {path}.");
        }
        catch (HttpRequestException)
        {
            return ApiFetchResult<T>.Failure(
                ApiFetchFailureKind.Connectivity,
                $"Backend API request failed for {path}.");
        }
        catch (System.Text.Json.JsonException)
        {
            return ApiFetchResult<T>.Failure(
                ApiFetchFailureKind.Payload,
                $"Backend API returned malformed JSON for {path}.");
        }
        catch (NotSupportedException)
        {
            return ApiFetchResult<T>.Failure(
                ApiFetchFailureKind.Payload,
                $"Backend API returned an unsupported payload format for {path}.");
        }
        catch (InvalidOperationException)
        {
            return ApiFetchResult<T>.Failure(
                ApiFetchFailureKind.Payload,
                $"Backend API payload for {path} could not be processed.");
        }
    }
}

public enum ApiFetchFailureKind
{
    Configuration,
    Connectivity,
    Upstream,
    Payload
}

public sealed record ApiFetchResult<T>(bool IsSuccess, T? Value, ApiFetchFailureKind? FailureKind, string? ErrorMessage)
{
    public static ApiFetchResult<T> Success(T value) => new(true, value, null, null);

    public static ApiFetchResult<T> Failure(ApiFetchFailureKind failureKind, string errorMessage) =>
        new(false, default, failureKind, errorMessage);
}
