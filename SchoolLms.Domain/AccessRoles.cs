namespace SchoolLms.Domain;

/// <summary>
/// Access role for staff accounts (Boshqaruv → Rollar): a named set of admin-panel
/// permission keys (the same keys as <see cref="AppUser.Permissions"/>).
///
/// <para>
/// Permissions are granted to the ROLE; a staff member is only assigned a role.
/// Each member's <see cref="AppUser.Permissions"/> is kept as a copy of the role's
/// list (rewritten whenever the role or the assignment changes), so the request
/// pipeline — <c>OnTokenValidated</c> claims, <c>AdminPerm</c>, the client's
/// menu — keeps reading one field and needs no join.
/// </para>
/// </summary>
public class AccessRole
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Display name, unique (e.g. "Buxgalter").</summary>
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>Admin-panel permission keys granted to every member.</summary>
    public List<string> Permissions { get; set; } = new();
    public DateTime CreatedAt { get; set; } = AppClock.Now;
}
