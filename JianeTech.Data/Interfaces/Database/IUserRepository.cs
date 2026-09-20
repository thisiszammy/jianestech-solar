using JianeTech.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Data.Interfaces.Database
{
    public interface IUserRepository
    {
        DbSet<User> GetUsers();
        void AddUser(User user);
        void UpdateUser(User user);
    }
}
