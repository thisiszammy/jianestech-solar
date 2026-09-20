using JianeTech.Data.Entities;
using JianeTech.Data.Interfaces;
using JianeTech.Data.Interfaces.Database;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Data.Repositories.Database
{
    public class UserRepository : BaseRepository<DatabaseContext>, IUserRepository
    {
        public UserRepository(IUnitOfWork unitOfWork) : base(unitOfWork) { }

        public DbSet<User> GetUsers()
            => GetDbSet<User>();

        public void AddUser(User user)
            => GetDbSet<User>().Add(user);

        public void UpdateUser(User user)
            => GetDbSet<User>().Update(user);
    }
}
