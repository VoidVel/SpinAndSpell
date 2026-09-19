# Spin Spell — Development Plan

**Status:** Living document. Companion to `spin-spell-source-of-truth.md` (product & design).
**Optimising for:** getting a real group playing this as fast as possible, without building something we'd have to throw away when it works.

---

## 0. The Governing Trade-off

The MVP's job is to answer one question: **is this fun with real friends?** Everything in this plan is weighed against time-to-that-answer.

So the rule is: **be rigorous exactly where being wrong is expensive and invisible, and be cheap everywhere else.**

- **Rigorous:** the game engine's selection and scoring rules. These are subtly wrong in ways a demo won't reveal — a fairness bug looks like bad luck, not a bug, and you'll never catch it by playing three rounds.
- **Cheap:** persistence, infrastructure, observability, deployment. All of it is replaceable in an afternoon once there's a reason to replace it.

Concretely, cut from the first pass: Testcontainers, Playwright, NetArchTest, property-based testing, OpenTelemetry, load testing, rate limiting, MessagePack, event sourcing. Each is listed in §9 with the trigger that would bring it back.

**Target: ~7–9 focused build-days to a playtest-ready build.**

---

## 1. Stack

| Layer | Choice | Why |
| --- | --- | --- |
| **Backend** | .NET 10 (LTS), ASP.NET Core Minimal APIs | Long-lived stateful processes and good concurrency primitives (`Channel<T>`). Plan originally said .NET 9; 10 is the installed LTS, so we target that. |
| **Real-time** | **SignalR** | The whole game is a live multi-device session. Groups, reconnect, and transport fallback for free. |
| **Database** | **PostgreSQL 16** | One `jsonb` column per room. That's the entire schema (§4). |
| **ORM** | EF Core 10 + Npgsql | Migrations. Two tables — it barely earns its keep, but it's one less thing to hand-roll. |
| **Frontend** | React 19 + TypeScript + Vite | Fast dev loop, no SSR needed behind a room code. |
| **Routing** | React Router v7 | ~12 screens across two device modes. |
| **Client state** | Zustand + one SignalR connection singleton | The client is a projection of server-pushed state. Nothing heavier is warranted. |
| **Styling** | Tailwind v4 + CSS custom properties for player colours | §6.4 needs two design modes sharing one token set. |
| **Animation** | Motion (`motion/react`) | §6.3's spin/reveal/celebration *are* the product. This is not the place to economise. |
| **Testing** | xUnit — heavily on the engine, lightly elsewhere | See §8. |
| **Deploy** | Render (or Railway/Fly) + managed Postgres | See §10, including the cold-start problem. |

---

## 2. The Two Things Worth Doing Properly

Everything else in this plan is disposable. These two aren't, because retrofitting either means a rewrite.

### 2.1 The game engine is a pure function

```
(GameState, Command, Seed) -> (GameState', Event[])
```

No `DateTime.Now`, no `Random` instance, no database, no SignalR inside `SpinSpell.Domain`. Time and randomness are injected.

This is the one piece of discipline that pays for itself immediately: the fairness rules (§9 of the source of truth) become testable without a server, a database, or a browser. You can simulate 10,000 rounds in a unit test and assert that nobody gets left out — which is the failure mode that will actually ruin a game night, and the one you cannot catch by playing.

### 2.2 One room = one actor

All commands for a room serialise through a single-consumer `Channel<T>`. Room state lives in memory, owned by exactly one `RoomActor`.

```
Client ──SignalR──▶ Hub ──▶ Channel<Command> (per room)
                                    │
                                    ▼  single consumer loop
                              GameEngine.Apply(state, cmd)
                                    │
                        ┌───────────┴───────────┐
                        ▼                       ▼
                 broadcast events        save state jsonb
                 (SignalR groups)        (on phase change)
```

A party game's input is genuinely racy — the challenger taps *Success* while someone else is still submitting, two clients ack a spin at once, someone rejoins mid-round. Serialising per room removes the entire class of problem instead of defending each path with locks. Volume per room is trivially low, so there's no cost.

Rooms idle-evict after 30 minutes and rehydrate from Postgres on demand.

**One hazard in this design, found the hard way during Phase 3.** The actor waits on its mailbox *with a deadline*, so a phase timeout and a player's tap arrive through the same path and cannot race. But that means the loop has to distinguish "the phase deadline elapsed" from "nobody said anything for 30 minutes" — and `CancelAfter` can fire a hair *early*. Re-deriving the reason from a fresh clock read after waking made a live room take the eviction branch mid-game. Nothing rehydrated it, because every client was sitting there waiting to be told what happened next, so the party simply stopped. Decide why you are waiting *before* you wait. Regression test: `RoomActorTests.Phase_timers_keep_firing_across_many_rounds_and_never_evict_a_live_room`.

### 2.3 Server decides, client renders

The spin result is chosen server-side the moment the owner taps, then pushed with an `animationMs` budget — every phone animates *toward a known outcome*. Phases advance on a **server-side timer**, never on a client "animation finished" callback. A backgrounded phone or a closed laptop must not be able to wedge the game.

---

## 3. Solution Structure

Two backend projects, not four. The only boundary that matters is Domain ← everything else; the Application/Infrastructure/Api split was ceremony at this size.

```
SpinSpell/
├── docker-compose.yml               # postgres + api + web, one command
├── src/
│   ├── SpinSpell.Domain/            # ⭐ zero dependencies. The game.
│   │   ├── GameState.cs             #   immutable records
│   │   ├── Commands.cs / Events.cs
│   │   ├── GameEngine.cs            #   Apply(state, command) -> Result
│   │   └── Selection.cs             #   fair rotation + hand dealing (§9.1-9.2)
│   │
│   ├── SpinSpell.Server/            # ASP.NET Core host — depends on Domain
│   │   ├── Hubs/GameHub.cs          #   thin: authenticate -> enqueue
│   │   ├── Rooms/RoomActor.cs       #   mailbox loop, timers, eviction
│   │   ├── Rooms/RoomRegistry.cs    #   code -> actor, rehydration
│   │   ├── Persistence/             #   DbContext + save/load
│   │   ├── Projections.cs           #   GameState -> per-role view model
│   │   └── Auth/                    #   room-scoped JWTs
│   │
│   └── SpinSpell.Web/               # React + Vite
│       ├── src/realtime/            #   connection, typed events, reconnect
│       ├── src/store/               #   Zustand, fed by server events
│       ├── src/design/              #   tokens, player colours, primitives
│       └── src/features/            #   lobby | submission | round | results
└── tests/
    └── SpinSpell.Domain.Tests/      # fast, no I/O. Nearly all the tests.
```

**The one rule:** `SpinSpell.Domain` has no project references and no NuGet packages beyond the BCL. Not enforced by tooling — just don't. It's a single directory and it's obvious in review.

---

## 4. Data Model

The room is the aggregate. Nothing queries across rooms during gameplay, so nothing needs to be normalised.

```sql
CREATE TABLE rooms (
    id          uuid PRIMARY KEY,
    code        varchar(6) NOT NULL,   -- no 0/O/1/I in the alphabet
    status      text NOT NULL,         -- active | ended
    state       jsonb NOT NULL,        -- the whole serialised GameState
    created_at  timestamptz NOT NULL,
    updated_at  timestamptz NOT NULL
);
CREATE UNIQUE INDEX ix_rooms_code_active ON rooms(code) WHERE status = 'active';

-- Written once, at game end. Covers every §11 fun-signal.
CREATE TABLE game_results (
    room_id              uuid PRIMARY KEY REFERENCES rooms(id) ON DELETE CASCADE,
    player_count         integer NOT NULL,
    rounds_played        integer NOT NULL,
    skips                integer NOT NULL,
    questions_submitted  integer NOT NULL,
    seconds_to_first_challenge integer,
    duration_seconds     integer NOT NULL,
    ended_at             timestamptz NOT NULL
);
```

Players, questions, and rounds all live inside `state`. Saving happens on phase change, not per event — a room does maybe 10 writes per game.

`game_results` is the whole analytics story: rounds per session, skip rate, and time-to-first-challenge are three trivial queries over it. No event log, no batched writer, no replay path.

**Retention:** nightly job deletes rooms 7 days after they end. Player-written dares are exactly the content not worth accumulating. `game_results` survives — it's anonymous counts.

---

## 5. Real-Time Contract

Single hub at `/hub/game`. The hub is dumb: authenticate → validate shape → enqueue → return. No game logic in it.

**Client → Server:** `JoinRoom(code, name)`, `Rejoin(token)`, `SubmitQuestion(kind, text)`, `DeleteQuestion(id)`, `StartGame()`, `Spin()`, `ChooseQuestion(id)`, `Judge(success|skip)`, `NextRound()`, `EndGame()`, `TransferOwner(playerId)`.

**Server → Client:**

| Event | Audience |
| --- | --- |
| `StateSync` — full role-appropriate snapshot | connection, on connect **and every reconnect** |
| `PlayerJoined` / `PlayerLeft` / `PlayerReconnected` | room |
| `SubmissionProgress` — counts only, never text | room |
| `PhaseChanged` — `{ phase, deadlineUtc? }` | room |
| `SpinResolved` — `{ challengerId, targetId, animationMs }` | room |
| `HandDealt` — the 3-card hand | **challenger's connection only** |
| `QuestionRevealed` — `{ kind, text, deadlineUtc? }` | room |
| `RoundJudged` / `ScoresUpdated` / `GameEnded` | room |
| `CommandRejected` — `{ code, message }`, typed | connection |

Clients never patch from a stale base — any doubt, re-`StateSync`. At this state size it's cheaper than reconciliation logic.

**Privacy note:** the challenger's hand goes to a *connection*, not a group. This is correctness, not polish — leaking the hand to anyone else's phone spoils the reveal (§7 of the source of truth).

**Auth:** no signup wall. On join the server issues a short-lived JWT of `{ roomId, playerId, role }` into `sessionStorage`. It's a capability token for one room — the only authority the game needs.

---

## 6. Engine Rules

```
Lobby → Submission → Spinning → Choosing → Revealed → Judging → Celebration → Scoreboard
                          ↑                                                       │
                          └───────────────────────────────────────────────────────┘
                                                                                  ↓
                                                                                Ended
```

Phases carry an optional server deadline. Timers enqueue a `PhaseTimeout` into the same mailbox, so a timeout is just another serialised command — no special concurrency path.

**Fair rotation (§9.1–9.2):**
```
weight(player as target) = 1 / (1 + turnsAsTarget)
excluded: the challenger, the previous round's target, disconnected players
pair rule: (challenger, target) ≠ the immediately previous pair
```
Weighted-random rather than round-robin — self-correcting toward fairness while the spin stays genuinely unpredictable, which is the entire point of the animation.

**The target is picked first, then the challenger.** This ordering is load-bearing and was found by the fairness suite during Phase 3, not by reasoning. Picking the challenger first — the intuitive reading of the spin animation — silently cancels the catch-up rule: a player who is behind on turns wins the *challenger* seat on their low count, which then disqualifies them from the *target* seat, so the person being left out stays left out. A latecomer got exactly an even share instead of a catch-up share. Being the target is what "getting a turn" means socially; the challenger is a side role. Pick the seat that matters first.

**Hand dealing:** 3 questions, balanced truth/dare where the pool allows, excluding ones authored by the target. Unchosen cards return to the pool; the chosen one is burned.

**Skip:** zero points, question burned. Harsher of the two options in §12; it's one line to soften and the skip-rate metric will say whether to.

**Pool exhaustion:** deal what's left below 3; at zero, the scoreboard prompts a top-up round rather than ending abruptly.

---

## 7. Delivery Phases

Five phases. The first four get to a playtest; polish comes after the loop is proven.

**Status: Phases 0–3 built and passing.** 57 domain tests green, and a smoke
script drives four SignalR clients through a complete game end to end. The next
thing that happens is the playtest gate, not more code.

### Phase 0 — Foundations · ~1 day ✅
Solution scaffold, `docker compose up` (Postgres + API + Vite), initial migration, design tokens and the §6.2 player palette, both layout shells with placeholder content.
**Exit:** clone → one command → both shells load on a laptop and a phone over LAN.

### Phase 1 — Rooms & Presence · ~2 days ✅
Room creation, 6-char code, join flow, colour assignment, SignalR lifecycle, `StateSync`, live lobby, owner Start button with a disabled-reason.
**Exit:** two phones make a game; four all appear live.

### Phase 2 — Submission · ~1 day ✅
Truth/dare toggle, fast repeated entry, per-player minimum, room-wide progress counts, start gating.
**Exit:** a group can fill the pool — owner included — and the owner can start.

### Phase 3 — The Round Loop ⭐ · ~3 days ✅
`SpinSpell.Domain` phase machine, fair rotation, hand dealing, private hand delivery, server-authoritative spin, reveal, judgment, scoring, scoreboard, end-game, final results.
**This is where the test investment goes** — see §8. Everything else in the plan is cheap; this isn't.
**Exit:** a complete game, playable start to finish, with plain visuals.

### 🎯 PLAYTEST GATE — stop and play it with real people
Four to six friends, one evening, before writing a line of animation code. What you're looking for is in §11 of the source of truth: do they keep going past round three, do they start a second game, does skip get abused. If the loop isn't fun, animation won't save it — and if pacing is wrong, you'd rather find out before building the celebration sequence around it.

### Phase 4 — Motion & Polish · ~3 days
The three peak moments from §6.3: the spin (slot-machine/wheel), the reveal (snap/flip), the celebration (1–2s, tinted to the scorer's colour). 200–300ms transitions. Truth vs. Dare visual language.
**Not garnish** — the source of truth calls the spin the product's signature. This phase is why Motion is in the stack.

### Phase 5 — Ship It · ~1–2 days
Reconnect (token rejoin → `StateSync`), owner-disconnect grace with a visible countdown then auto-handoff, room eviction, input length caps, keep-warm (§10), retention job, deploy.

---

## 8. Testing

Deliberately lopsided.

| Where | How much | Why |
| --- | --- | --- |
| **`SpinSpell.Domain`** | Thorough. Every phase transition, illegal-command rejection, hand dealing, pool exhaustion — plus a **10,000-round fairness simulation** asserting turn distribution stays even and no pair repeats back-to-back. | Fast (no I/O), and this is the code where being wrong is invisible. A fairness bug reads as bad luck for months. |
| **Server** | A handful of smoke tests over the actor: command ordering under burst, eviction and rehydration. | Enough to catch a broken mailbox. |
| **Frontend** | None automated for now. | The screens are thin projections of pushed state; they change shape every playtest. Automated UI tests here would be rewritten faster than they'd catch anything. |
| **Manual** | Every phase exit, on real phones at real distance from a real laptop. | §6.1 legibility genuinely cannot be unit-tested. |

---

## 9. Deferred — and What Brings It Back

| Cut | Bring it back when |
| --- | --- |
| Playwright E2E | The loop stops changing shape every playtest |
| Testcontainers integration tests | There's persistence logic more complex than one jsonb round-trip |
| Property-based tests (FsCheck) | The engine's rules outgrow example-based tests |
| NetArchTest | More than two people commit to the repo |
| OpenTelemetry / structured tracing | You're debugging a problem logs can't reach |
| Rate limiting | The app is public and someone abuses submission |
| Load testing | Real concurrency approaches double digits of simultaneous rooms |
| Event log / event sourcing | You want the deferred recap reel (§10 of the source of truth), which needs round history |
| Redis backplane + room affinity | More than one API instance is needed |
| MessagePack over the hub | Payload size becomes a mobile-network problem |

**None of these are precluded.** Because state is in memory behind an actor and the engine is pure, each is a swap rather than a rewrite. That's what the architecture in §2 buys — it's what lets the rest of the plan be this cheap.

---

## 10. Deploy — and the Cold-Start Problem

**This is a real conflict, not a nit.** A free-tier instance sleeps after ~15 minutes idle. State survives (that's what the `jsonb` snapshot is for), but *whoever started the room waits 30–60 seconds staring at nothing* while the container wakes. Time-to-first-challenge is one of the four fun-signals in §11, and party momentum dies during that wait — this is the single worst first impression the product can make.

Three fixes, cheapest first:

1. **Keep-warm pinger** — an external cron (GitHub Actions, cron-job.org) hitting `/health` every 10 minutes. Free, ~5 minutes to set up, and it fully solves the sleep problem in practice. **Do this in Phase 0**, not Phase 5 — the moment there's a deployed URL, it should already be warm.
2. **Paid tier** — ~$7/month removes the problem by definition. If you're demoing to real groups on any kind of schedule, this is the honest answer and it's cheaper than the engineering time spent working around it.
3. **Cover the gap in UI** — if a cold start is ever possible, the room-creation screen shows a branded "warming up the stage…" state rather than a spinner. Cheap insurance, and it turns dead time into tone (§3).

Do (1) immediately and (3) as part of Phase 1's create-room screen. Move to (2) before the first playtest with people whose evening you don't want to waste.

---

## 11. Defaults We'll Build (from §12 of the Source of Truth)

Nothing here blocks a start; each has a default so work never stalls on a product call.

| Open decision | Default | Cost to change |
| --- | --- | --- |
| Minimum questions per player | **4** | Config value |
| Skip cost | **Zero points + question burned** | One engine rule |
| Timer | **Owner-toggleable, off by default** | Already in room settings |
| End-game condition | **Owner ends anytime** (per the §12 lean) | Thresholds are additive later |
| Who judges | **The challenger, always** | They picked the card; reads the same at two players as at ten |
| Owner disconnect | **60s grace with visible countdown, then handoff to longest-connected player** | Phase 5 — confirm before then |
| Challenger disconnect mid-reveal | **Owner may call the round while they are gone** | Unblocks a deadlock; never overrules a challenger who is present |
| Final name/tagline | `SpinSpell` internally, not blocking | Rises after deploy — decide before launch |

---

## 12. Next Steps

1. Confirm the defaults in §11.
2. Phase 0 — scaffold, compose file, tokens, **and the keep-warm cron**.
3. Write the `SpinSpell.Domain` phase machine and its fairness tests before any SignalR or EF code exists. If the engine is right, the rest is plumbing.
