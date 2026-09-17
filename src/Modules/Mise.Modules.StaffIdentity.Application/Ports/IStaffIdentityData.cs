using Mise.Modules.StaffIdentity.Domain;

namespace Mise.Modules.StaffIdentity.Application.Ports;

/// <summary>
/// Slice-specific gateway (CLAUDE.md "The testable seam") — not a generic repository. Wraps
/// both the module's own StaffUser profile table and the ASP.NET Core Identity credential
/// store (AspNetUsers etc.), because both need to change together: a registered staff member
/// with credentials but no profile row (or vice versa) is not a valid state.
/// </summary>
public interface IStaffIdentityData
{
    /// <summary>
    /// Atomic with the OperationId idempotency check, same contract as
    /// IReservationsData.CreateReservationAsync — replaying the same
    /// <paramref name="operationId"/> returns the existing staff user id and writes nothing
    /// new. <see cref="RegisterStaffOutcome.UsernameTaken"/> is an expected outcome, not an
    /// exception — the handler turns it into the same field-scoped 400 shape validation
    /// failures use.
    /// </summary>
    Task<RegisterStaffResult> RegisterStaffAsync(
        StaffUser staffUser, string username, string password, Guid operationId, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies credentials via the Identity password hasher (ASP.NET Core Identity's
    /// UserManager.CheckPasswordAsync, which transparently rehashes on a successful verify
    /// against an older hash format — docs/plan.md's "rehash-on-login path" that ships
    /// regardless of which hasher is chosen). Never throws for wrong credentials — that is
    /// an expected outcome the caller maps to 401, not a bug.
    /// </summary>
    Task<ValidateCredentialsResult> ValidateCredentialsAsync(
        string username, string password, CancellationToken cancellationToken);
}

public enum RegisterStaffOutcome
{
    Created,
    AlreadyProcessed,
    UsernameTaken,
}

public sealed record RegisterStaffResult(RegisterStaffOutcome Outcome, Guid StaffUserId);

public enum ValidateCredentialsOutcome
{
    Success,
    NotFound,
    InvalidPassword,
    Inactive,
}

public sealed record ValidateCredentialsResult(
    ValidateCredentialsOutcome Outcome, Guid StaffUserId, string FullName, StaffRole Role);
