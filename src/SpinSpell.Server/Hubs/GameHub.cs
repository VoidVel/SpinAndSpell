using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SpinSpell.Domain;
using SpinSpell.Server.Auth;
using SpinSpell.Server.Rooms;

namespace SpinSpell.Server.Hubs;

/// <summary>
/// Deliberately dumb. Authenticate, shape-check, enqueue, return. There is no
/// game logic in here and there must never be — every rule lives in the engine,
/// where it can be tested without a socket.
///
/// Note that ActorId always comes from the connection's own claims. A client
/// cannot act as anyone but itself, because it never gets to say who it is.
/// </summary>
[Authorize]
public sealed class GameHub(RoomRegistry rooms, IClock clock, ILogger<GameHub> logger) : Hub
{
    public static string GroupFor(string code) => $"room:{code.ToUpperInvariant()}";

    public override async Task OnConnectedAsync()
    {
        var code = Context.User!.RoomCode();
        var playerId = Context.User!.PlayerId();
        var room = await rooms.FindAsync(code, Context.ConnectionAborted);

        if (room is null)
        {
            throw new HubException("That room has closed.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(code));
        room.Tell(new SetConnected(playerId, true));

        // Every connect and every reconnect starts from a full snapshot — the
        // client never patches from a base it cannot verify.
        await Clients.Caller.SendAsync("state", Projections.For(room.Snapshot, playerId, clock.Now));

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            var code = Context.User!.RoomCode();
            var playerId = Context.User!.PlayerId();
            var room = await rooms.FindAsync(code);

            room?.Tell(new SetConnected(playerId, false));
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(code));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Disconnect cleanup failed.");
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ------------------------------------------------------------- commands

    public Task SubmitQuestion(Guid questionId, string kind, string text) =>
        Send(new SubmitQuestion(questionId, ParseKind(kind), text));

    public Task DeleteQuestion(Guid questionId) => Send(new DeleteQuestion(questionId));

    public Task EditQuestion(Guid questionId, string text) => Send(new EditQuestion(questionId, text));

    public Task SetQuestionMode(string mode) => Send(new SetQuestionMode(ParseMode(mode)));

    public Task OpenSubmissions() => Send(new OpenSubmissions());

    public Task StartGame() => Send(new StartGame());

    public Task ChooseQuestion(Guid questionId) => Send(new ChooseQuestion(questionId));

    public Task Judge(string outcome) => Send(new Judge(ParseOutcome(outcome)));

    public Task NextRound() => Send(new NextRound());

    public Task TransferOwner(Guid newOwnerId) => Send(new TransferOwner(newOwnerId));

    public Task EndGame() => Send(new EndGame());

    public Task LeaveRoom() => Send(new LeaveRoom(Context.User!.PlayerId()));

    /// <summary>Client-driven resync. Cheap, and the answer to any client-side doubt.</summary>
    public async Task Resync()
    {
        var room = await rooms.FindAsync(Context.User!.RoomCode(), Context.ConnectionAborted)
            ?? throw new HubException("That room has closed.");

        await Clients.Caller.SendAsync("state",
            Projections.For(room.Snapshot, Context.User!.PlayerId(), clock.Now));
    }

    // -------------------------------------------------------------- plumbing

    private async Task Send(Command command)
    {
        var room = await rooms.FindAsync(Context.User!.RoomCode(), Context.ConnectionAborted)
            ?? throw new HubException("That room has closed.");

        if (!room.Tell(command with { ActorId = Context.User!.PlayerId() }))
        {
            await Clients.Caller.SendAsync("commandRejected",
                new { code = "busy", message = "That didn't go through — try again." });
        }
    }

    private static QuestionKind ParseKind(string kind) =>
        Enum.TryParse<QuestionKind>(kind, ignoreCase: true, out var parsed)
            ? parsed
            : throw new HubException("Questions are either a truth or a dare.");

    private static QuestionMode ParseMode(string mode) =>
        Enum.TryParse<QuestionMode>(mode, ignoreCase: true, out var parsed)
            ? parsed
            : throw new HubException("A room is truths, dares, or both.");

    private static RoundOutcome ParseOutcome(string outcome) =>
        Enum.TryParse<RoundOutcome>(outcome, ignoreCase: true, out var parsed)
            ? parsed
            : throw new HubException("A round is either a success or a skip.");
}
