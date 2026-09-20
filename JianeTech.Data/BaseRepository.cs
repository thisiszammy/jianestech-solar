using JianeTech.Data.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Data
{
    /// <summary>
    /// Repositories resolve their DbContext through the unit of work rather than taking
    /// one directly, so every repository in a request shares the same change tracker and
    /// the same open transaction.
    /// </summary>
    public abstract class BaseRepository<TContext> where TContext : DbContext
    {
        protected IUnitOfWork UnitOfWork { get; }

        protected DbContext Context => UnitOfWork.Get<TContext>();

        protected BaseRepository(IUnitOfWork unitOfWork)
        {
            UnitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        protected virtual DbSet<TEntity> GetDbSet<TEntity>() where TEntity : class
            => Context.Set<TEntity>();

        protected virtual void SetEntityState(object entity, EntityState entityState)
            => Context.Entry(entity).State = entityState;
    }
}
