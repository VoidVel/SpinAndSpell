using Microsoft.AspNetCore.SignalR;
using SpinSpell.Domain;
using SpinSpell.Server.Auth;
using SpinSpell.Server.Rooms;

namespace SpinSpell.Server.Hubs;

/// <summary>Maps a SignalR connection to its player, so Clients.User(id) works.</summary>
public sealed class PlayerIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst(PlayerClaims.PlayerId)?.Value;
}

/// <summary>
/// Turns engine events into SignalR pushes.
///
/// Two things go out per batch: the events themselves, which drive animation and
/// tone, and a fresh per-player state projection, which is the truth. Sending a
/// whole view to each player is a handful of small payloads at this state size —
/// far cheaper than the client-side reconciliation logic it replaces.
/// </summary>
public sealed class HubBroadcaster(
    IHubContext<GameHub> hub,
    IClock clock,
    ILogger<HubBroadcaster> logger) : IRoomBroadcaster
{
    public async Task PublishAsync(GameState state, IReadOnlyList<GameEvent> events, CancellationToken ct)
    {
        try
        {
            foreach (var evt in events)
            {
                await DispatchAsync(state, evt, ct);
            }

            var now = clock.Now;

            foreach (var player in state.Players)
            {
                await hub.Clients.User(player.Id.ToString())
                    .SendAsync("state", Projections.For(state, player.Id, now), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Room shutting down.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Broadcast failed for room {Code}.", state.Code);
        }
    }

    private Task DispatchAsync(GameState state, GameEvent evt, CancellationToken ct)
    {
        var name = EventName(evt);
        var audience = evt.Audience;

        // A player-addressed event goes to that player's connections and nowhere
        // else. This is how HandDealt stays private — the challenger's hand is
        // never placed in a payload anyone else receives.
        IClientProxy target = audience.Kind switch
        {
            AudienceKind.Player when audience.PlayerId is { } id => hub.Clients.User(id.ToString()),
            _ => hub.Clients.Group(GameHub.GroupFor(state.Code))
        };

        return target.SendAsync(name, evt, ct);
    }

    /// <summary>SpinResolved -> "spinResolved", matching the client's handler names.</summary>
    private static string EventName(GameEvent evt)
    {
        var name = evt.GetType().Name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
