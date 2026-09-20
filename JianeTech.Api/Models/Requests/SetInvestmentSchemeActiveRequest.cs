namespace JianeTech.Api.Models.Requests;

/// <summary>
/// The investment scheme register's on/off switch. Idempotent by design: the caller states
/// the state it wants rather than an action to perform, so a double-click cannot toggle
/// twice.
/// </summary>
public class SetInvestmentSchemeActiveRequest
{
    public bool IsActive { get; set; }
}
