using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BO2.Services
{
    internal interface IGameHistoryStore : IDisposable
    {
        Task AppendAsync(GameHistoryEntry entry, CancellationToken cancellationToken);

        Task<IReadOnlyList<GameHistorySummary>> LoadSummariesNewestFirstAsync(CancellationToken cancellationToken);

        Task<GameHistoryEntry?> LoadDetailByIdAsync(string id, CancellationToken cancellationToken);
    }
}
