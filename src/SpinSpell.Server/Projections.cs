using SpinSpell.Domain;

namespace SpinSpell.Server;

public sealed record PlayerView(
    Guid Id,
    string Name,
    int ColourSlot,
    bool IsOwner,
    int Score,
    bool Connected,
    int QuestionsSubmitted,
    bool MeetsMinimum);

public sealed record QuestionView(Guid Id, string Kind, string Text);

public sealed record RoundView(
    int Index,
    Guid ChallengerId,
    Guid TargetId,
    string? Kind,
    string? Text,
    string? Outcome);

public sealed record SettingsView(
    int MinQuestionsPerPlayer,
    bool TimerEnabled,
    int RevealTimerSeconds,
    int HandSize,
    string Mode);

/// <summary>
/// What one connection is allowed to know. Built per player, never shared —
/// the challenger's hand and everyone's draft questions are private, and the
/// only reliable way to keep them private is to never put them in a payload
/// addressed to anyone else.
/// </summary>
public sealed record RoomView(
    string Code,
    string Phase,
    DateTimeOffset? Deadline,
    DateTimeOffset ServerTime,
    SettingsView Settings,
    IReadOnlyList<PlayerView> Players,
    RoundView? Round,
    Guid YouId,
    bool YouAreOwner,
    bool YouAreChallenger,
    bool YouAreTarget,
    IReadOnlyList<QuestionView> YourQuestions,
    IReadOnlyList<QuestionView> YourHand,
    int QuestionsInPool,
    int RoundsPlayed,
    string? StartBlockedReason);

public static class Projections
{
    public static RoomView For(GameState state, Guid viewerId, DateTimeOffset now)
    {
        var round = state.CurrentRound;
        var isChallenger = round?.ChallengerId == viewerId;
        var revealed = state.Phase is Phase.Revealed or Phase.Judging or Phase.Celebration or Phase.Scoreboard;

        // The chosen question's text is only in the payload once it has actually
        // been revealed to the room. Before that, nobody but the challenger has
        // seen the hand it came from.
        var chosen = revealed && round?.ChosenQuestionId is { } id ? state.QuestionById(id) : null;

        return new RoomView(
            Code: state.Code,
            Phase: state.Phase.ToString(),
            Deadline: state.PhaseDeadline,
            ServerTime: now,
            Settings: new SettingsView(
                state.Settings.MinQuestionsPerPlayer,
                state.Settings.TimerEnabled,
                state.Settings.RevealTimerSeconds,
                state.Settings.HandSize,
                state.Settings.Mode.ToString()),
            Players: [.. state.Players.Select(p => ToView(state, p))],
            Round: round is null
                ? null
                : new RoundView(
                    round.Index,
                    round.ChallengerId,
                    round.TargetId,
                    chosen?.Kind.ToString(),
                    chosen?.Text,
                    round.Outcome?.ToString()),
            YouId: viewerId,
            YouAreOwner: state.PlayerById(viewerId)?.IsOwner ?? false,
            YouAreChallenger: isChallenger,
            YouAreTarget: round?.TargetId == viewerId,
            YourQuestions: [.. state.Questions.Where(q => q.AuthorId == viewerId).Select(ToView)],
            YourHand: isChallenger && state.Phase == Phase.Choosing
                ? [.. round!.Hand.Select(state.QuestionById).OfType<Question>().Select(ToView)]
                : [],
            QuestionsInPool: state.AvailableQuestionCount,
            RoundsPlayed: state.RoundsPlayed,
            StartBlockedReason: StartBlockedReason(state));
    }

    private static PlayerView ToView(GameState state, Player p)
    {
        var submitted = state.QuestionsBy(p.Id);

        return new PlayerView(
            p.Id, p.Name, p.ColourSlot, p.IsOwner, p.Score, p.Connected,
            QuestionsSubmitted: submitted,
            MeetsMinimum: submitted >= state.Settings.MinQuestionsPerPlayer);
    }

    private static QuestionView ToView(Question q) => new(q.Id, q.Kind.ToString(), q.Text);

    /// <summary>
    /// §7 of the source of truth: the Start button is disabled *with a reason*,
    /// never just greyed out. This is that reason, in the app's voice.
    /// </summary>
    public static string? StartBlockedReason(GameState state)
    {
        if (state.Phase is not (Phase.Lobby or Phase.Submission))
        {
            return null;
        }

        var connected = state.ConnectedPlayers.Count();

        if (connected < Limits.MinPlayersToStart)
        {
            var needed = Limits.MinPlayersToStart - connected;
            return needed == 1 ? "One more player and you're good to go." : $"{needed} more players needed.";
        }

        if (state.Phase == Phase.Lobby)
        {
            return null;
        }

        var waiting = state.PlayersBelowMinimum.Select(p => p.Name).ToList();

        return waiting.Count switch
        {
            0 => null,
            1 => $"Still waiting on {waiting[0]}'s questions.",
            2 => $"Still waiting on {waiting[0]} and {waiting[1]}.",
            _ => $"Still waiting on {waiting.Count} players' questions."
        };
    }
}
