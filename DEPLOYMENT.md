# Deploying Spin Spell

**Neon** (Postgres) → **Render** (.NET API + SignalR) → **Vercel** (React SPA).

Roughly 30 minutes end to end. Do it in that order — each step needs a value from the one before.

| Piece | Where | Why |
| --- | --- | --- |
| Postgres | **Neon** free | No expiry. Render's own free Postgres self-deletes after 90 days — don't use it. |
| API + SignalR | **Render** free | Docker, and it carries WebSockets, which SignalR needs. Sleeps after ~15 min idle. |
| React SPA | **Vercel** free | Static, instant, no cold start. |

---

## The one architectural thing to understand first

In development, Vite proxies `/api` and `/hub` to the API, so the browser only ever talks to one origin. **On Vercel that stops being true**, and you must not try to recreate it:

> **Do not proxy `/hub` through Vercel.** Vercel's rewrites do not carry WebSocket upgrades. SignalR would silently fall back to long-polling — or just fail — and the whole game runs over that socket.

So the browser talks to Vercel for the app and **directly to Render** for the API and the socket. That is a cross-origin setup, which means two settings have to agree:

- `VITE_API_URL` on Vercel → your Render URL
- `Cors__Origins` on Render → your Vercel URL

Get those two wrong and nothing connects. They're the only fiddly part of this.

---

## Step 0 — Get the code on GitHub

The project isn't a git repo yet, and both Render and Vercel deploy from one.

```bash
cd c:/Users/user/OneDrive/Desktop/SpinAndSpell

git init
git add .
git commit -m "Spin Spell"
```

Create an empty repo on GitHub (no README, no .gitignore — you have one), then:

```bash
git remote add origin https://github.com/<you>/spinspell.git
git branch -M main
git push -u origin main
```

Check `node_modules/`, `bin/` and `obj/` are **not** in the push — `.gitignore` covers them.

---

## Step 1 — Neon (Postgres)

1. Sign up at **neon.tech** → **Create project**. Name it `spinspell`, pick the region nearest your players.
2. On the dashboard, open **Connection string**.
3. **Switch it to "Pooled connection"** — the host will contain `-pooler`. EF Core opens a connection pool and the free tier's direct-connection limit is low.
4. Copy it. It looks like:

```
postgresql://spinspell_owner:npg_XXXX@ep-cool-name-123456-pooler.eu-central-1.aws.neon.tech/neondb?sslmode=require
```

### Convert it — Npgsql will not accept that URL

This trips everyone. Npgsql wants key–value, not a `postgresql://` URI. Rewrite it:

```
Host=ep-cool-name-123456-pooler.eu-central-1.aws.neon.tech;Database=neondb;Username=spinspell_owner;Password=npg_XXXX;SSL Mode=Require
```

Mapping: `Host` is everything between `@` and `/`. `Database` is after the last `/`, minus the `?...`. `Username` and `Password` are the two halves before the `@`. Drop `?sslmode=require` and use `SSL Mode=Require` instead.

Keep this string — it's `ConnectionStrings__Postgres` in the next step.

> If you later hit a certificate error, append `;Trust Server Certificate=true`. Neon's certs are valid, so you shouldn't need it.

---

## Step 2 — Render (API + SignalR)

1. **render.com** → **New** → **Web Service** → connect your GitHub repo.
2. Settings:

| Field | Value |
| --- | --- |
| Language / Runtime | **Docker** |
| Dockerfile Path | `src/SpinSpell.Server/Dockerfile` |
| Docker Build Context Directory | `.` |
| Instance Type | Free |

The context must be the repo root — the Dockerfile copies both `SpinSpell.Domain` and `SpinSpell.Server`, so building from inside the server folder fails.

3. **Environment variables** — add these three:

| Key | Value |
| --- | --- |
| `ConnectionStrings__Postgres` | the converted Neon string from Step 1 |
| `Auth__Secret` | a random string, **at least 32 characters** |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

Generate a secret:

```bash
openssl rand -base64 48
```

> **`Auth__Secret` is not optional in production.** Without it the server generates a random signing key at startup, so every player's token is invalidated the moment the instance restarts — and on a free tier that sleeps, it restarts constantly. Everyone gets kicked mid-game.

You don't need to set `PORT`; Render injects it and the app binds to it.

4. **Create Web Service**. First build takes 5–10 minutes. Migrations run automatically on startup — no manual step.

5. When it's live, note the URL: `https://spinspell-api.onrender.com`. Check it:

```bash
curl https://spinspell-api.onrender.com/health
# {"status":"ok","liveRooms":0,"time":"..."}
```

Leave `Cors__Origins` unset for now — you need the Vercel URL first.

---

## Step 3 — Vercel (frontend)

1. **vercel.com** → **Add New** → **Project** → import the same repo.
2. Settings:

| Field | Value |
| --- | --- |
| Root Directory | `src/SpinSpell.Web` |
| Framework Preset | Vite (auto-detected) |

`vercel.json` already sets the build command, output directory, and the SPA rewrite that stops `/room` 404-ing on refresh.

3. **Environment variable** — one, and it matters:

| Key | Value |
| --- | --- |
| `VITE_API_URL` | `https://spinspell-api.onrender.com` |

No trailing slash. This is a build-time variable, so **changing it later requires a redeploy**, not just a restart.

4. **Deploy**. Note the URL: `https://spinspell.vercel.app`.

---

## Step 4 — Close the CORS loop

Back on **Render** → your service → **Environment** → add:

| Key | Value |
| --- | --- |
| `Cors__Origins` | `https://spinspell.vercel.app` |

Exact scheme, exact host, **no trailing slash**. Comma-separate if you want to keep a preview domain working.

Render redeploys. Once it's live, open your Vercel URL and host a game — the lobby should appear with a room code and a QR.

---

## Step 5 — Keep it warm

A sleeping instance costs the host 30–60 seconds staring at nothing, and time-to-first-challenge is one of the four fun-signals in the source of truth (§11). The repo already has the cron.

**GitHub repo** → **Settings** → **Secrets and variables** → **Actions** → **Variables** → **New repository variable**:

| Name | Value |
| --- | --- |
| `KEEP_WARM_URL` | `https://spinspell-api.onrender.com/health` |

`.github/workflows/keep-warm.yml` pings it every 10 minutes. Trigger it once manually from the **Actions** tab to confirm it's green.

> Honest version: this mostly works, but GitHub's scheduled runners drift under load and can skip a firing. If you're demoing to real people on a schedule, **Render's $7/month Starter tier removes the problem outright** and is cheaper than the evening you'd waste on a cold start. Neon also scales to zero after 5 minutes, adding ~500ms to the first query — harmless next to a container wake, but it's why the very first room creation feels slower than the second.

---

## Verify the deployment

Run the real smoke test against production. It drives four SignalR clients through a full game.

```bash
cd src/SpinSpell.Web
node scripts/smoke.mjs https://spinspell-api.onrender.com
```

Point it at the **API** origin, not the Vercel one — it talks to the hub directly.

Expect `All checks passed.` The first run may take a minute while the instance wakes.

Then the part that actually matters: **open the Vercel URL on a laptop, scan the QR with two phones, and play a round.** Specifically confirm the browser console shows a WebSocket connection to the Render host and not repeated long-polling requests — that's the one failure this setup can produce that still looks like it's working.

---

## When something's wrong

| Symptom | Cause |
| --- | --- |
| `Failed to fetch` / "Can't reach the game right now" | `VITE_API_URL` wrong or missing. It's build-time — redeploy Vercel after changing it. |
| Room creates, then stuck on "Connecting…" | `Cors__Origins` doesn't exactly match the Vercel origin. Check scheme and trailing slash. |
| Everyone dropped after a few minutes | `Auth__Secret` unset — tokens die with the process. |
| `Format of the initialization string does not conform` | Neon URL not converted to Npgsql key–value form (Step 1). |
| Refreshing `/room` gives 404 | `vercel.json` not picked up — check Root Directory is `src/SpinSpell.Web`. |
| Connects, but the game lags and phases arrive late | WebSocket blocked, falling back to long-polling. Something is proxying `/hub` — don't route it through Vercel. |
| First load takes a minute | Cold start. Expected on free tier; see Step 5. |

---

## Costs

Free, at these limits: Neon 0.5 GB, Render 750 instance-hours/month, Vercel 100 GB bandwidth. A party game's data is tiny — rooms purge 7 days after they end, and `game_results` is anonymous counts.

The first thing worth paying for is Render Starter at $7/month, purely to delete the cold start.
