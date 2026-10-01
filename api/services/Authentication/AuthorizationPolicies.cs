namespace api.Services.Authentication;

public static class AuthorizationPolicies
{
    /// <summary>Only the master key from configuration. This is also the fallback policy for every endpoint.</summary>
    public const string MasterKeyOnly = "MasterKeyOnly";

    /// <summary>Any valid API key (master or standard). Opt an endpoint in to standard keys with this policy.</summary>
    public const string AnyApiKey = "AnyApiKey";
}
