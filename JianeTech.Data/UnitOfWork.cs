using JianeTech.Data.Entities;
using JianeTech.Data.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace JianeTech.Data
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly Dictionary<Type, DbContext> _contexts;
        private readonly Dictionary<Type, IDbContextTransaction?> _transactions = new();

        public UnitOfWork(DatabaseContext databaseContext)
        {
            _contexts = new Dictionary<Type, DbContext>
            {
                [typeof(DatabaseContext)] = databaseContext,
            };
        }

        public TContext Get<TContext>() where TContext : DbContext
            => (TContext)_contexts[typeof(TContext)];

        public async Task BeginTransactionAsync<TContext>(CancellationToken cancellationToken = default)
            where TContext : DbContext
        {
            var key = typeof(TContext);

            // Re-entrant by design: a service method that begins, commits, then begins
            // again inside one request must not stack transactions on the same context.
            if (!_transactions.TryGetValue(key, out var existing) || existing is null)
            {
                _transactions[key] = await _contexts[key].Database.BeginTransactionAsync(cancellationToken);
            }
        }

        public async Task CommitAsync<TContext>(CancellationToken cancellationToken = default)
            where TContext : DbContext
        {
            var key = typeof(TContext);

            if (!_transactions.TryGetValue(key, out var transaction) || transaction is null)
            {
                throw new InvalidOperationException(
                    $"No active transaction for {typeof(TContext).Name}. " +
                    $"Call BeginTransactionAsync<{typeof(TContext).Name}>() before CommitAsync<{typeof(TContext).Name}>().");
            }

            await _contexts[key].SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await transaction.DisposeAsync();
            _transactions[key] = null;
        }

        public async Task RollbackAsync<TContext>(CancellationToken cancellationToken = default)
            where TContext : DbContext
        {
            var key = typeof(TContext);

            if (_transactions.TryGetValue(key, out var transaction) && transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                await transaction.DisposeAsync();
                _transactions[key] = null;
            }
        }

        public Task SaveChangesAsync<TContext>(CancellationToken cancellationToken = default)
            where TContext : DbContext
        {
            var key = typeof(TContext);
            return _contexts.TryGetValue(key, out var context)
                ? context.SaveChangesAsync(cancellationToken)
                : Task.CompletedTask;
        }

        public void Dispose()
        {
            foreach (var transaction in _transactions.Values) transaction?.Dispose();
            foreach (var context in _contexts.Values) context.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
