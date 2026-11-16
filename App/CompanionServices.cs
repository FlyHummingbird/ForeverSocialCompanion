using ForeverSocial.Companion;

// Test seams are constructor-only; production builds expose no command-line credential overrides.
internal sealed class CompanionServices
{
    public Func<Uri, SyncClient> CreateClient { get; init; } = uri => new SyncClient(uri);
    public Func<Uri, Export, string?> LoadCredential { get; init; } =
        (uri, export) => Credentials.Load(uri.AbsoluteUri, export.Client + ":" + export.Actor);
    public Action<Uri, Export, string> SaveCredential { get; init; } =
        (uri, export, token) => Credentials.Save(uri.AbsoluteUri, export.Client + ":" + export.Actor, token);
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;
    public int PollIntervalMilliseconds { get; init; } = 30000;
}
