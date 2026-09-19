# Spin Spell

A real-time, phone-based party game. Friends write the truths and dares, the app
picks who's up, and the drama plays out on everyone's phone.

- **Product & design decisions:** [spin-spell-source-of-truth.md](spin-spell-source-of-truth.md)
- **Technical plan:** [spin-spell-development-plan.md](spin-spell-development-plan.md)

## Running it

```bash
docker compose up -d postgres     # the only stateful dependency

cd src/SpinSpell.Server && dotnet run          # API on :8080
cd src/SpinSpell.Web    && npm install && npm run dev   # web on :5173
```

Open the **Network** URL Vite prints (not `localhost`) so phones on the same wifi
can join — that one URL is all anyone needs.

Vite proxies `/api` and `/hub` through to the API, so the browser only ever
talks to port 5173. That is deliberate: otherwise every playtest would need a
Windows firewall exception for 8080 and the client would have to guess the
host's LAN IP. In containers, nginx does the same job.

Or run everything in containers:

```bash
docker compose up --build
```

## Testing

```bash
dotnet test                                       # domain (fast) + room actor (~45s)
node src/SpinSpell.Web/scripts/smoke.mjs          # full game over SignalR
node src/SpinSpell.Web/scripts/smoke.mjs http://192.168.1.101:5173   # ...through the proxy
```

The domain suite is deliberately the bulk of the testing. It runs in about a
second, touches no I/O, and covers the rules that are wrong in ways a playtest
cannot reveal — a fairness bug reads as bad luck, not as a bug.

The room-actor suite is slow because it waits on real phase timers, and it earns
that: it is the regression net for a bug where a live room silently evicted
itself mid-game.

The smoke script drives four clients through a complete game and asserts the
privacy rules (the challenger's hand never reaches anyone else) and the
authority rules (only the challenger judges). Run it against the LAN URL too — the
proxied WebSocket path is not the same code path as localhost.

Neither replaces playing it with real people, which is the point of the
playtest gate in the plan.

## Layout

```
src/SpinSpell.Domain     the game, as a pure function. No dependencies. Ever.
src/SpinSpell.Server     SignalR hub, room actors, Postgres, tokens
src/SpinSpell.Web        React client — one phone view for every player
tests/                   domain suite
```

The one architectural rule: `SpinSpell.Domain` has no project references and no
NuGet packages. Time and randomness are injected, so a whole game is
reproducible from a seed and a command log. Everything else is I/O wrapped
around that.

## Configuration

| Setting | Default | Notes |
| --- | --- | --- |
| `ConnectionStrings__Postgres` | local docker compose | |
| `Auth__Secret` | random per process | Set in deployment, or tokens die on restart |
| `Cors__Origins` | any origin in Development | Comma-separated in production |
| `VITE_API_TARGET` | `http://localhost:8080` | Where the Vite dev proxy forwards to |
| `VITE_API_URL` | same origin (proxied) | Only set if the API is genuinely on another host |

## Deploying

Step-by-step guide: **[DEPLOYMENT.md](DEPLOYMENT.md)** — Neon (Postgres) →
Render (API) → Vercel (SPA), about 30 minutes.

The one thing to know going in: in production the browser talks to Vercel for
the app and **directly to Render** for the API and the WebSocket. Do not proxy
`/hub` through Vercel — its rewrites don't carry WebSocket upgrades, and SignalR
would quietly degrade to long-polling.
