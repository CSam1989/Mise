using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mise.Modules.StaffIdentity.Domain;
using Mise.Modules.StaffIdentity.Infrastructure.Persistence;

namespace Mise.Modules.StaffIdentity.Infrastructure;

/// <summary>
/// Bootstraps exactly one Manager account so the first real sign-in is possible at all —
/// RegisterStaffCommandHandler's endpoint is Manager-only, so without this nobody could ever
/// reach it. Called directly by Mise.MigrationService (bypassing the Application layer
/// entirely, the same way MigrationService already reaches into ReservationsDbContext
/// directly to run migrations) since seeding is a startup/ops concern, not a business use
/// case — it has no OperationId, no audit attribution (no Manager exists yet to attribute
/// to), and no caller-facing validation contract. Idempotent: safe to run on every
/// Mise.MigrationService start.
/// </summary>
public sealed partial class StaffIdentitySeeder(
    StaffIdentityDbContext dbContext,
    UserManager<StaffIdentityUser> userManager,
    IUserStore<StaffIdentityUser> userStore,
    ILogger<StaffIdentitySeeder> logger)
{
    public async Task<bool> EnsureManagerExistsAsync(
        string username, string password, string fullName, CancellationToken cancellationToken)
    {
        if (await userManager.FindByNameAsync(username) is not null)
        {
            LogSeedManagerAlreadyExists();
            return false;
        }

        if (userStore is UserOnlyStore<StaffIdentityUser, StaffIdentityDbContext, Guid> store)
        {
            store.AutoSaveChanges = false;
        }

        var staffUser = StaffUser.Create(Guid.NewGuid(), fullName, StaffRole.Manager);
        var identityUser = new StaffIdentityUser { Id = staffUser.Id, UserName = username };

        var createResult = await userManager.CreateAsync(identityUser, password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create the seed Manager account: {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
        }

        dbContext.StaffUsers.Add(staffUser);
        await dbContext.SaveChangesAsync(cancellationToken);

        LogSeedManagerCreated(staffUser.Id);
        return true;
    }

    [LoggerMessage(EventId = 65, Level = LogLevel.Information, Message = "Seed Manager account {StaffUserId} created.")]
    private partial void LogSeedManagerCreated(Guid staffUserId);

    [LoggerMessage(EventId = 66, Level = LogLevel.Debug, Message = "Seed Manager account already exists; nothing to do.")]
    private partial void LogSeedManagerAlreadyExists();
}
