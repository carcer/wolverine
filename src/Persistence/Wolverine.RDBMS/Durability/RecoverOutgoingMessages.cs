using Microsoft.Extensions.Logging;
using Weasel.Core;
using Wolverine.Logging;
using Wolverine.Persistence.Durability;
using Wolverine.Runtime;
using Wolverine.Transports;
using Wolverine.Transports.Sending;

namespace Wolverine.RDBMS.Durability;

[Obsolete("Goes away with DurabilityAgent rewrite")]
internal class RecoverOutgoingMessages : IDurabilityAction
{
    private readonly CancellationToken _cancellation;
    private readonly ILogger _logger;
    private readonly IWolverineRuntime _runtime;

    public RecoverOutgoingMessages(IWolverineRuntime runtime, ILogger logger)
    {
        _runtime = runtime;
        _logger = logger;
        _cancellation = runtime.Cancellation;
    }

    public string Description => "Recover persisted outgoing messages";

    public async Task ExecuteAsync(IMessageDatabase database, IDurabilityAgent agent,
        IDurableStorageSession session)
    {
        var hasLock = await session.TryGetGlobalLockAsync(TransportConstants.OutgoingMessageLockId);
        if (!hasLock)
        {
            return;
        }

        try
        {
            var destinations = await database.FindAllDestinationsAsync();

            var count = 0;
            foreach (var destination in destinations)
            {
                try
                {
                    var sendingAgent = _runtime.Endpoints.GetOrBuildSendingAgent(destination);

                    if (sendingAgent.Latched)
                    {
                        break;
                    }

                    var found = await recoverFromAsync(sendingAgent, database, session, database.Durability);

                    count += found;
                }
                catch (UnknownTransportException e)
                {
                    _logger.LogError(e, "Could not resolve a channel for {Destination}. Deleting outgoing messages",
                        destination);

                    await session.BeginAsync();

                    await DeleteByDestinationAsync(session, destination, database);
                    await session.CommitAsync();
                    break;
                }
            }

            var wasMaxedOut = count >= database.Durability.RecoveryBatchSize;

            if (wasMaxedOut)
            {
                agent.RescheduleOutgoingRecovery();
            }
        }
        finally
        {
            await session.ReleaseGlobalLockAsync(TransportConstants.OutgoingMessageLockId);
        }
    }

    internal Task DeleteByDestinationAsync(IDurableStorageSession session, Uri? destination,
        IMessageDatabase wolverineDatabase)
    {
        if (session.Transaction == null)
        {
            throw new InvalidOperationException("No current transaction");
        }

        return session.Transaction
            .CreateCommand(
                $"delete from {wolverineDatabase.SchemaName}.{DatabaseConstants.OutgoingTable} where owner_id = :owner and destination = @destination")
            .With("destination", destination!.ToString())
            .With("owner", TransportConstants.AnyNode)
            .ExecuteNonQueryAsync(_cancellation);
    }


    private async Task<int> recoverFromAsync(ISendingAgent sendingAgent, IMessageDatabase storage,
        IDurableStorageSession session,
        DurabilitySettings durabilitySettings)
    {
#pragma warning disable CS8600
        Envelope[] filtered;
        IReadOnlyList<Envelope> outgoing;
#pragma warning restore CS8600

        try
        {
            await session.BeginAsync();

            outgoing = await storage.Outbox.LoadOutgoingAsync(sendingAgent.Destination);

            var expiredMessages = outgoing.Where(x => x.IsExpired()).ToArray();
            _logger.DiscardedExpired(expiredMessages);


            await storage.Outbox.DeleteOutgoingAsync(expiredMessages.ToArray());
            filtered = outgoing.Where(x => !expiredMessages.Contains(x)).ToArray();

            // Might easily try to do this in the time between starting
            // and having the data fetched. Was able to make that happen in
            // (contrived) testing
            if (sendingAgent.Latched || !filtered.Any())
            {
                await session.RollbackAsync();
                return 0;
            }

            await storage.ReassignOutgoingAsync(durabilitySettings.AssignedNodeNumber, filtered);

            await session.CommitAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while trying to recover persisted, outgoing envelopes to {Uri}",
                sendingAgent.Destination);
            await session.RollbackAsync();
            throw;
        }

        _logger.RecoveredOutgoing(filtered);

        foreach (var envelope in filtered)
        {
            try
            {
                await sendingAgent.EnqueueOutgoingAsync(envelope);
            }
            catch (Exception? e)
            {
                _logger.LogError(e, "Unable to enqueue {Envelope} for sending", envelope);
            }
        }

        return outgoing.Count();
    }
}