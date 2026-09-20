using JianeTech.Data.Entities;
using JianeTech.Data.Interfaces;
using JianeTech.Data.Interfaces.Database;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Data.Repositories.Database
{
    public class ActivityLogRepository : BaseRepository<DatabaseContext>, IActivityLogRepository
    {
        public ActivityLogRepository(IUnitOfWork unitOfWork) : base(unitOfWork) { }

        public DbSet<ActivityLog> GetActivityLogs()
            => GetDbSet<ActivityLog>();

        public void AddActivityLog(ActivityLog activityLog)
            => GetDbSet<ActivityLog>().Add(activityLog);

        public void UpdateActivityLog(ActivityLog activityLog)
            => GetDbSet<ActivityLog>().Update(activityLog);
    }
}
