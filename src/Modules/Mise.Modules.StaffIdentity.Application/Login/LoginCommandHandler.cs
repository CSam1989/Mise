using FluentValidation;
using Microsoft.Extensions.Logging;
using Mise.Modules.StaffIdentity.Application.Ports;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.StaffIdentity.Application.Login;

/// <summary>
/// Deliberately not OperationId-idempotent like CreateReservationCommandHandler — logging in
/// twice with the same credentials is naturally idempotent (two valid sessions, not a
/// duplicated side effect), so there is no replay hazard to guard against. Still takes
/// IAuditWriter (CrossCuttingTests' rule applies to every *CommandHandler by name, not only
/// ones that create a domain resource) and puts it to real use: a "SignedIn" audit entry per
/// successful login, which is genuine FR-09 value, not just a rule-satisfying formality.
/// Failed attempts are deliberately NOT audited here — rate-limiting/lockout on failed
/// attempts is PIN/device-pairing scope (Phase 13), not this endpoint.
/// </summary>
public sealed partial class LoginCommandHandler(
    IStaffIdentityData staffIdentityData,
    IJwtTokenIssuer jwtTokenIssuer,
    IAuditWriter auditWriter,
    IValidator<LoginCommand> validator,
    TimeProvider timeProvider,
    ILogger<LoginCommandHandler> logger)
{
    public async Task<LoginResult> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var credentials = await staffIdentityData.ValidateCredentialsAsync(command.Username, command.Password, cancellationToken);

        if (credentials.Outcome != ValidateCredentialsOutcome.Success)
        {
            LogSignInRejected(credentials.Outcome);
            return LoginResult.Failed(credentials.Outcome == ValidateCredentialsOutcome.Inactive
                ? LoginOutcome.AccountInactive
                : LoginOutcome.InvalidCredentials);
        }

        var issuedToken = jwtTokenIssuer.IssueToken(credentials.StaffUserId, credentials.FullName, credentials.Role);

        await auditWriter.WriteAsync(
            new AuditLogEntry
            {
                Id = Guid.NewGuid(),
                EntityType = "StaffUser",
                EntityId = credentials.StaffUserId,
                Action = "SignedIn",
                PerformedByStaffId = credentials.StaffUserId,
                OccurredAtUtc = timeProvider.GetUtcNow(),
                Details = string.Empty,
            },
            cancellationToken);
        LogSignedIn(credentials.StaffUserId);

        return new LoginResult(
            LoginOutcome.Success, credentials.StaffUserId, credentials.FullName, credentials.Role,
            issuedToken.Token, issuedToken.ExpiresAtUtc);
    }

    [LoggerMessage(EventId = 55, Level = LogLevel.Information, Message = "Staff user {StaffUserId} signed in.")]
    private partial void LogSignedIn(Guid staffUserId);

    [LoggerMessage(EventId = 56, Level = LogLevel.Debug, Message = "Sign-in rejected: {Outcome}.")]
    private partial void LogSignInRejected(ValidateCredentialsOutcome outcome);
}
