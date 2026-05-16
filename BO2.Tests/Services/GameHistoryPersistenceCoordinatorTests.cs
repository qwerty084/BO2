using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameHistoryPersistenceCoordinatorTests
    {
        [Fact]
        public async Task Dispose_WhenCompletedGameAppendIsPending_ReturnsBeforeAppendAndDisposesStoreAfterAppend()
        {
            var store = new BlockingGameHistoryStore();
            var coordinator = new GameHistoryPersistenceCoordinator(store);
            GameHistoryEntry entry = CreateEntry();

            Task appendTask = coordinator.AppendCompletedEntryAsync(entry);
            await store.AppendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task disposeTask = Task.Run(coordinator.Dispose);
            Task firstCompletedTask = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromMilliseconds(100)));

            if (firstCompletedTask != disposeTask)
            {
                store.CompleteAppend();
                await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            Assert.Same(disposeTask, firstCompletedTask);
            Assert.False(store.AppendCancellationToken.CanBeCanceled);
            Assert.False(store.Disposed);
            Assert.Throws<ObjectDisposedException>(() =>
            {
                _ = coordinator.AppendCompletedEntryAsync(CreateEntry());
            });

            store.CompleteAppend();

            await appendTask.WaitAsync(TimeSpan.FromSeconds(5));
            await store.DisposeCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(store.AppendCompletedBeforeDispose);
            Assert.True(store.Disposed);
        }

        private static GameHistoryEntry CreateEntry()
        {
            DateTimeOffset startedAt = new(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);
            return new GameHistoryEntry
            {
                Id = "pending-save",
                StartedAt = startedAt,
                EndedAt = startedAt.AddMinutes(62),
                MapIdentity = new GameHistoryMapIdentity("zm_transit", "town", "zm_transit_gump_town", "Town"),
                FinalRound = 12,
                FinalStats = new GameHistoryStats
                {
                    Points = 12345,
                    Kills = 98,
                    Downs = 2,
                    Revives = 4,
                    Headshots = 55
                },
                GameDuration = TimeSpan.FromMinutes(62)
            };
        }

        private sealed class BlockingGameHistoryStore : IGameHistoryStore
        {
            private readonly TaskCompletionSource _allowAppend = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource AppendStarted { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource DisposeCompleted { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public CancellationToken AppendCancellationToken { get; private set; }

            public bool AppendCompletedBeforeDispose { get; private set; }

            public bool Disposed { get; private set; }

            public async Task AppendAsync(GameHistoryEntry entry, CancellationToken cancellationToken)
            {
                AppendCancellationToken = cancellationToken;
                AppendStarted.SetResult();

                await _allowAppend.Task;

                AppendCompletedBeforeDispose = !Disposed;
            }

            public Task<IReadOnlyList<GameHistorySummary>> LoadSummariesNewestFirstAsync(
                CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<GameHistorySummary>>([]);
            }

            public Task<GameHistoryEntry?> LoadDetailByIdAsync(string id, CancellationToken cancellationToken)
            {
                return Task.FromResult<GameHistoryEntry?>(null);
            }

            public void CompleteAppend()
            {
                _allowAppend.TrySetResult();
            }

            public void Dispose()
            {
                Disposed = true;
                DisposeCompleted.SetResult();
            }
        }
    }
}
