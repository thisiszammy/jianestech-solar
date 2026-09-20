using Microsoft.EntityFrameworkCore;

namespace JianeTech.Data.Interfaces
{
    /// <summary>
    /// Dictionary-backed unit of work. Every method is generic over the DbContext so the
    /// same instance can own independent transactions per context once a second one lands.
    /// </summary>
    public interface IUnitOfWork : IDisposable
    {
        TContext Get<TContext>() where TContext : DbContext;
        Task BeginTransactionAsync<TContext>(CancellationToken cancellationToken = default) where TContext : DbContext;
        Task CommitAsync<TContext>(CancellationToken cancellationToken = default) where TContext : DbContext;
        Task RollbackAsync<TContext>(CancellationToken cancellationToken = default) where TContext : DbContext;
        Task SaveChangesAsync<TContext>(CancellationToken cancellationToken = default) where TContext : DbContext;
    }
}
