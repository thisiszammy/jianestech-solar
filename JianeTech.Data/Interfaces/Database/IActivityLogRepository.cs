using JianeTech.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Data.Interfaces.Database
{
    public interface IActivityLogRepository
    {
        DbSet<ActivityLog> GetActivityLogs();
        void AddActivityLog(ActivityLog activityLog);
        void UpdateActivityLog(ActivityLog activityLog);
    }
}
