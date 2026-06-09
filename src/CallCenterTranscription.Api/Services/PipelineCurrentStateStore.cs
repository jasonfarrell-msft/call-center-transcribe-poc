using CallCenterTranscription.Shared.Events;

namespace CallCenterTranscription.Api.Services;

public sealed class PipelineCurrentStateStore
{
    private readonly object _gate = new();
    private PipelineCurrentStateResponse _currentState;

    public PipelineCurrentStateStore(IScriptedScenarioFeed scriptedScenarioFeed)
    {
        _currentState = scriptedScenarioFeed.GetCurrentState();
    }

    public PipelineCurrentStateResponse GetSnapshot()
    {
        lock (_gate)
        {
            return _currentState;
        }
    }
}
