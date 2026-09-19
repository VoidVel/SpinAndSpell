using SpinSpell.Domain;
using SpinSpell.Server.Auth;
using SpinSpell.Server.Rooms;

namespace SpinSpell.Server.Endpoints;

public sealed record CreateRoomRequest(
    string Name,
    int MinQuestionsPerPlayer = 4,
    bool TimerEnabled = false);

public sealed record JoinRoomRequest(string Name);

public sealed record JoinedResponse(string Token, Guid PlayerId, Guid RoomId, string Code);

public static class RoomEndpoints
{
    public static void MapRoomEndpoints(this IEndpointRouteBuilder app)
    {
        var rooms = app.MapGroup("/api/rooms");

        // Creating a room is the first thing anyone does, so it stays a plain
        // HTTP call — no socket, no handshake, no waiting.
        rooms.MapPost("/", async (
            CreateRoomRequest request,
            RoomRegistry registry,
            PlayerTokenService tokens,
            CancellationToken ct) =>
        {
            var settings = new RoomSettings(
                MinQuestionsPerPlayer: Math.Clamp(request.MinQuestionsPerPlayer, 1, 10),
                TimerEnabled: request.TimerEnabled);

            var room = await registry.CreateAsync(settings, ct);
            var ownerId = Guid.CreateVersion7();

            // The creator joins their own room as an ordinary player. Owning it
            // buys them the pacing controls and nothing else.
            var result = await room.AskAsync(new JoinRoom(ownerId, request.Name, AsOwner: true), ct);

            if (Rejection(result) is { } rejected)
            {
                return Results.BadRequest(new { code = rejected.Code, message = rejected.Message });
            }

            return Results.Ok(new JoinedResponse(
                tokens.Issue(room.RoomId, room.Code, ownerId), ownerId, room.RoomId, room.Code));
        });

        rooms.MapPost("/{code}/join", async (
            string code,
            JoinRoomRequest request,
            RoomRegistry registry,
            PlayerTokenService tokens,
            CancellationToken ct) =>
        {
            var room = await registry.FindAsync(code, ct);

            if (room is null)
            {
                return Results.NotFound(new { code = "no_such_room", message = "No room with that code." });
            }

            var playerId = Guid.CreateVersion7();
            var result = await room.AskAsync(new JoinRoom(playerId, request.Name, AsOwner: false), ct);

            // The engine owns name uniqueness and capacity, so the answer comes
            // back from the same place it would during play.
            if (Rejection(result) is { } rejected)
            {
                return Results.BadRequest(new { code = rejected.Code, message = rejected.Message });
            }

            return Results.Ok(new JoinedResponse(
                tokens.Issue(room.RoomId, room.Code, playerId), playerId, room.RoomId, room.Code));
        });

        // Does a code exist? Used by the join screen to fail fast before asking
        // for a name — nothing kills party momentum like a two-step error.
        rooms.MapGet("/{code}", async (string code, RoomRegistry registry, CancellationToken ct) =>
        {
            var room = await registry.FindAsync(code, ct);

            return room is null
                ? Results.NotFound(new { code = "no_such_room", message = "No room with that code." })
                : Results.Ok(new
                {
                    code = room.Code,
                    phase = room.Snapshot.Phase.ToString(),
                    players = room.Snapshot.Players.Count,
                    accepting = room.Snapshot.Phase is Phase.Lobby or Phase.Submission
                });
        });
    }

    private static CommandRejected? Rejection(EngineResult result) =>
        result.Events.OfType<CommandRejected>().FirstOrDefault();
}
