namespace JianeTech.Services.Interfaces
{
    public interface IClientContextAccessor
    {
        string? IpAddress { get; }
        string? UserAgent { get; }
    }
}
