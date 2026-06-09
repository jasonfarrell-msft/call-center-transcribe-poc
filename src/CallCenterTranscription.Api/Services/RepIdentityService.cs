using Azure.Communication;
using Azure.Communication.Identity;

namespace CallCenterTranscription.Api.Services;

/// <summary>
/// Issues short-lived, VoIP-scoped ACS user access tokens for the rep's browser Calling SDK.
///
/// Auth: <see cref="CommunicationIdentityClient"/> is built with DefaultAzureCredential (managed
/// identity on ACA) against the ACS endpoint — NO connection strings. The API's managed identity
/// holds a role on the ACS resource that covers identity/token issuance.
///
/// Identity model (closes the restart split-brain): the browser persists its own userId and
/// passes it back on every token refresh. ACS persists identities, so refreshing a token for an
/// EXISTING identity works across API restarts/revisions. Only when the browser has no userId yet
/// (first ever load) do we create a new identity and hand its id back for the browser to store.
/// There is deliberately NO server-side identity cache.
/// </summary>
public sealed class RepIdentityService
{
    private static readonly CommunicationTokenScope[] VoipScope = [CommunicationTokenScope.VoIP];

    private readonly CommunicationIdentityClient _identityClient;

    public RepIdentityService(CommunicationIdentityClient identityClient)
    {
        _identityClient = identityClient;
    }

    public sealed record RepToken(string UserId, string Token, DateTimeOffset ExpiresOnUtc);

    /// <summary>
    /// Issues a VoIP token. When <paramref name="userId"/> is a non-empty existing ACS identity,
    /// a token is minted for it (refresh path). Otherwise a fresh identity is created and returned
    /// alongside its first token so the browser can persist the id.
    /// </summary>
    public async Task<RepToken> IssueTokenAsync(string? userId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var user = new CommunicationUserIdentifier(userId);
            var token = await _identityClient
                .GetTokenAsync(user, VoipScope, cancellationToken)
                .ConfigureAwait(false);
            return new RepToken(userId, token.Value.Token, token.Value.ExpiresOn);
        }

        var created = await _identityClient
            .CreateUserAndTokenAsync(VoipScope, cancellationToken)
            .ConfigureAwait(false);
        return new RepToken(created.Value.User.Id, created.Value.AccessToken.Token, created.Value.AccessToken.ExpiresOn);
    }
}
