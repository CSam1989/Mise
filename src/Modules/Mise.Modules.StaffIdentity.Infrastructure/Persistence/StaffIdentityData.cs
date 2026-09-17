using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mise.Modules.StaffIdentity.Application.Ports;
using Mise.Modules.StaffIdentity.Domain;
using Mise.SharedKernel.Persistence;

namespace Mise.Modules.StaffIdentity.Infrastructure.Persistence;

/// <summary>
/// Wraps two things that must change together: the module's own staff_user profile row and
/// ASP.NET Core Identity's AspNetUsers credential row for the same Id — a staff account with
/// one but not the other is not a valid state. AutoSaveChanges=false on the Identity store
/// (set only for the duration of RegisterStaffAsync, on a per-request-scoped store instance)
/// is what makes the Identity user, the profile row, and the ProcessedOperation all commit —
/// or fail to commit — in the exact same SaveChangesAsync call, the same atomicity guarantee
/// ReservationsData.CreateReservationAsync gives its own OperationId check.
/// </summary>
internal sealed partial class StaffIdentityData(
    StaffIdentityDbContext dbContext,
    UserManager<StaffIdentityUser> userManager,
    IUserStore<StaffIdentityUser> userStore,
    TimeProvider timeProvider,
    ILogger<StaffIdentityData> logger) : IStaffIdentityData
{
    public async Task<RegisterStaffResult> RegisterStaffAsync(
        StaffUser staffUser, string username, string password, Guid operationId, CancellationToken cancellationToken)
    {
        var existingResourceId = await dbContext.ProcessedOperations
            .AsNoTracking()
            .Where(p => p.OperationId == operationId)
            .Select(p => (Guid?)p.ResourceId)
            .SingleOrDefaultAsync(cancellationToken);

        if (existingResourceId is { } resourceId)
        {
            LogIdempotencyCheckHit(operationId, resourceId);
            return new RegisterStaffResult(RegisterStaffOutcome.AlreadyProcessed, resourceId);
        }

        if (userStore is UserOnlyStore<StaffIdentityUser, StaffIdentityDbContext, Guid> store)
        {
            store.AutoSaveChanges = false;
        }

        var identityUser = new StaffIdentityUser { Id = staffUser.Id, UserName = username };
        var createResult = await userManager.CreateAsync(identityUser, password);

        if (!createResult.Succeeded)
        {
            if (createResult.Errors.Any(e => e.Code == "DuplicateUserName"))
            {
                LogUsernameTaken();
                return new RegisterStaffResult(RegisterStaffOutcome.UsernameTaken, Guid.Empty);
            }

            // Any other Identity failure means the password/username policy configured in
            // AddStaffIdentityPersistence disagrees with RegisterStaffCommandValidator — a
            // configuration bug, not an outcome the caller is meant to branch on.
            throw new InvalidOperationException(
                $"Failed to create staff Identity user: {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
        }

        dbContext.StaffUsers.Add(staffUser);
        dbContext.ProcessedOperations.Add(new ProcessedOperation
        {
            OperationId = operationId,
            ResourceType = "StaffUser",
            ResourceId = staffUser.Id,
            ProcessedAtUtc = timeProvider.GetUtcNow(),
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterStaffResult(RegisterStaffOutcome.Created, staffUser.Id);
    }

    public async Task<ValidateCredentialsResult> ValidateCredentialsAsync(
        string username, string password, CancellationToken cancellationToken)
    {
        var identityUser = await userManager.FindByNameAsync(username);
        if (identityUser is null)
        {
            LogCredentialsRejected(ValidateCredentialsOutcome.NotFound);
            return new ValidateCredentialsResult(ValidateCredentialsOutcome.NotFound, Guid.Empty, string.Empty, default);
        }

        // CheckPasswordAsync transparently rehashes the stored hash when it verifies against
        // an older-format one (docs/plan.md's rehash-on-login path) — nothing else to do here
        // to get that guarantee; it's the framework's own UserManager behavior.
        var passwordValid = await userManager.CheckPasswordAsync(identityUser, password);
        if (!passwordValid)
        {
            LogCredentialsRejected(ValidateCredentialsOutcome.InvalidPassword);
            return new ValidateCredentialsResult(ValidateCredentialsOutcome.InvalidPassword, Guid.Empty, string.Empty, default);
        }

        var staffUser = await dbContext.StaffUsers
            .AsNoTracking()
            .SingleAsync(s => s.Id == identityUser.Id, cancellationToken);

        if (!staffUser.IsActive)
        {
            LogCredentialsRejected(ValidateCredentialsOutcome.Inactive);
            return new ValidateCredentialsResult(ValidateCredentialsOutcome.Inactive, Guid.Empty, string.Empty, default);
        }

        LogCredentialsAccepted(staffUser.Id);
        return new ValidateCredentialsResult(ValidateCredentialsOutcome.Success, staffUser.Id, staffUser.FullName, staffUser.Role);
    }

    [LoggerMessage(EventId = 60, Level = LogLevel.Debug,
        Message = "OperationId {OperationId} already recorded in shared.processed_operation, pointing at resource {ResourceId}.")]
    private partial void LogIdempotencyCheckHit(Guid operationId, Guid resourceId);

    [LoggerMessage(EventId = 61, Level = LogLevel.Debug,
        Message = "Staff registration rejected: username already taken.")]
    private partial void LogUsernameTaken();

    [LoggerMessage(EventId = 62, Level = LogLevel.Debug, Message = "Credentials rejected: {Outcome}.")]
    private partial void LogCredentialsRejected(ValidateCredentialsOutcome outcome);

    [LoggerMessage(EventId = 63, Level = LogLevel.Debug, Message = "Credentials accepted for staff user {StaffUserId}.")]
    private partial void LogCredentialsAccepted(Guid staffUserId);
}
