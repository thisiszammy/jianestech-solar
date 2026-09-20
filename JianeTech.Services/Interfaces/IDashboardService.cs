using JianeTech.Services.DTOs;

namespace JianeTech.Services.Interfaces
{
    public interface IDashboardService
    {
        Task<DashboardSnapshotDTO> GetDashboardSnapshotDTO(CancellationToken cancellationToken);
    }
}
