using CallCenterTranscription.Shared.Events;
using CallCenterTranscription.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CallCenterTranscription.Web.Pages;

public class MissionControlModel : PageModel
{
    private readonly ILogger<MissionControlModel> _logger;
    private readonly PipelineApiClient _pipelineApiClient;

    public MissionControlModel(ILogger<MissionControlModel> logger, PipelineApiClient pipelineApiClient)
    {
        _logger = logger;
        _pipelineApiClient = pipelineApiClient;
    }

    public MissionControlHealthResponse MissionControlHealth { get; private set; } = new();
    public string? MissionControlWarning { get; private set; }

    public async Task OnGetAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        var result = await _pipelineApiClient.GetMissionControlHealthAsync(cancellationToken);

        if (result.IsSuccess && result.Value is not null)
        {
            MissionControlHealth = result.Value;
        }
        else
        {
            MissionControlWarning = "Backend API is unavailable for mission control feed.";
            _logger.LogWarning(
                "Backend API mission control feed unavailable. Detail: {Detail}.",
                result.ErrorMessage ?? "none");
        }
    }

    public static string ToDisplayLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        return string.Join(' ',
            value.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(token =>
                {
                    if (token.Length == 0)
                    {
                        return token;
                    }

                    return char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
                }));
    }
}
