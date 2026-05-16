using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BO2.Services
{
    internal sealed class GameHistoryPersistenceCoordinator : IDisposable
    {
        private readonly IGameHistoryStore _store;
        private readonly object _sync = new();
        private readonly HashSet<Task> _pendingAppends = [];
        private bool _disposed;

        public GameHistoryPersistenceCoordinator(IGameHistoryStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            _store = store;
        }

        public Task AppendCompletedEntryAsync(GameHistoryEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            Task appendTask;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                appendTask = _store.AppendAsync(entry, CancellationToken.None);
                _pendingAppends.Add(appendTask);
            }

            _ = appendTask.ContinueWith(
                static (completedTask, state) =>
                    ((GameHistoryPersistenceCoordinator)state!).RemovePendingAppend(completedTask),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return appendTask;
        }

        public Task<IReadOnlyList<GameHistorySummary>> LoadSummariesNewestFirstAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            return _store.LoadSummariesNewestFirstAsync(cancellationToken);
        }

        public Task<GameHistoryEntry?> LoadDetailByIdAsync(string id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            return _store.LoadDetailByIdAsync(id, cancellationToken);
        }

        public void Dispose()
        {
            Task[] pendingAppends;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                pendingAppends = [.. _pendingAppends];
            }

            try
            {
                Task.WaitAll(pendingAppends);
            }
            catch (AggregateException)
            {
                // Append failures are observed by the save path that started the task.
            }

            _store.Dispose();
        }

        private void RemovePendingAppend(Task appendTask)
        {
            lock (_sync)
            {
                _pendingAppends.Remove(appendTask);
            }
        }

        private void ThrowIfDisposed()
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
            }
        }
    }
}
