namespace BucketSync.Auth;

/// <summary>WebAuthn relying-party config. Origin is the exact string the client puts in clientDataJSON.</summary>
public sealed record PasskeyOptions(string RpId, string Origin)
{
    public static readonly PasskeyOptions LocalDev = new("localhost", "http://localhost");
}
