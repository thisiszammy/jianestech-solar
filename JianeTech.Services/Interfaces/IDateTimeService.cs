namespace JianeTech.Services.Interfaces
{
    /// <summary>
    /// The clock, behind an interface so time is substitutable in tests. Local time is
    /// the house rule — see the "no DateTime.UtcNow" entry in CLAUDE.md.
    /// </summary>
    public interface IDateTimeService
    {
        DateTime Now();

        DateTime Today();
    }
}
