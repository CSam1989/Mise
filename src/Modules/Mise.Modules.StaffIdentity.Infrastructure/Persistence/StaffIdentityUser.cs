using Microsoft.AspNetCore.Identity;

namespace Mise.Modules.StaffIdentity.Infrastructure.Persistence;

/// <summary>
/// Identity's own credential row (AspNetUsers): username, password hash, security stamp.
/// Deliberately holds nothing business-meaningful — <see cref="Domain.StaffUser"/> is the
/// separate 1:1 profile row with FullName/Role/IsActive (StaffIdentityData's doc comment
/// explains why they're two rows, not one). Public, not internal: StaffIdentityDbContext
/// derives from IdentityUserContext&lt;StaffIdentityUser, Guid&gt; and is itself public (the
/// composition root needs it), so the type argument can't be less accessible than that
/// (CS0060) — the type stays otherwise unused outside this project.
/// </summary>
public sealed class StaffIdentityUser : IdentityUser<Guid>;
