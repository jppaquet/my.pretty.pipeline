namespace Notify.Archive.Archive;

public abstract record ArchiveResult
{
    public sealed record Created(string Id) : ArchiveResult;
    public sealed record AlreadyExists(string Id) : ArchiveResult;
}
