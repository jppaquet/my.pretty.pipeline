namespace Notify.Shared.Validation;

public sealed record ValidationFailure(string Field, string Message);

public sealed record ValidationResult(IReadOnlyList<ValidationFailure> Failures)
{
    public bool IsValid => Failures.Count == 0;
    public static readonly ValidationResult Valid = new(Array.Empty<ValidationFailure>());
}
