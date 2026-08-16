# Spin Spell — Source of Truth

**Status:** Living document — update this as decisions change. Anything not written here is not decided.
**Owner:** Product & design decisions live here. Technical implementation lives with the coding agent.

---

## 1. What Spin Spell Is (One Sentence)

Spin Spell is a real-time, phone-based party game where a group spins up custom truth-or-dare rounds together — friends write the questions, the app picks who's up, and everyone watches live as the drama unfolds on the host's screen.

**The emotional promise:** the fun of a party game with your own friends' inside jokes, without anyone having to remember or invent a dare on the spot.

---

## 2. Positioning & Differentiation

There are a lot of "spin the bottle" / truth-or-dare apps. Here's what makes Spin Spell distinct and why each choice matters:

| Choice                                                     | Why                                                                                                                                                                                                                                                                                                                           |
| ---------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Players write the content, not us**                      | Generic question packe ps get stale after onlay. Friend-written prompts are the actual product — the app is a _delivery mechanism_ for inside jokes, not a content library.                                                                                                                                                   |
| **Host-mediated judgment**                                 | Someone has to decide if a dare "counts." Rather than trusting an honor system, the host (usually the most sober/present person) adjudicates. This also gives the host a real role, not just admin busywork.                                                                                                                  |
| **One shared screen (host) + individual phones (players)** | This is the single biggest UX decision. The host's screen is the "TV" everyone watches together — like a living room game night. Player phones are private input devices (writing questions, joining). This mirrors physical party games (Jackbox-style) rather than everyone staring at their own screen in a group setting. |
| **Rounds, not a fixed question count**                     | The game should feel endless and replayable within one sitting, not like a deck that runs out.                                                                                                                                                                                                                                |

**Tagline direction (pick one later, not locked):**

- "Your friends. Your dares. Your game."
- "The party game that only your group could make."
- "Spin it. Dare it. Own it."

---

## 3. Tone & Personality

This determines every design and copy decision downstream.

- **Playful, a little chaotic, never corporate.** Copy should sound like a fun friend hosting, not an app.
- **PG-13 by default, spicy by choice.** The app itself stays clean — spiciness comes entirely from what players write, not from us. This matters for App Store policy and for broad appeal (works for a casual office game or a wild bachelorette party, same app).
- **Confident, not cutesy.** Avoid overly juvenile mascot-driven design. Think "bold graphic party game" (Jackbox, Codenames, Cards Against Humanity) over "kids app."

---

## 4. Core Game Loop (Refined)

```
CREATE/JOIN ROOM
      ↓
LOBBY (players join, see who's in)
      ↓
QUESTION SUBMISSION (everyone adds truths/dares privately on their phone)
      ↓
──────────── ROUND LOOP (repeats) ────────────
   SPIN → challenger + target selected
      ↓
   CHALLENGER PICKS a question (from a short hand, not the whole pool)
      ↓
   REVEAL → shown big on host screen
      ↓
   TARGET RESPONDS (in person / out loud)
      ↓
   HOST JUDGES → success / skip
      ↓
   POINT + CELEBRATION MOMENT
      ↓
   SCOREBOARD → host taps "Next Round"
──────────────────────────────────────────────
      ↓
GAME END (score threshold, round count, or host ends manually)
      ↓
FINAL RESULTS / WINNER CELEBRATION
```

**Key refinement vs. the original spec:** the challenger picks from a _small hand_ of questions (e.g. 3, dealt randomly from the pool) rather than the full list. This keeps pacing fast and prevents decision paralysis — same trick card games use.

---

## 5. Roles (Confirmed)

- **Host** — creates room, controls pacing, judges challenges, cannot be skipped/removed. Runs on the "big screen" (laptop, TV via casting, or a phone propped up).
- **Player** — joins via code, submits questions privately, becomes challenger/target when selected.
- **Challenger** — temporary role, picks the question for this round only.
- **Target** — temporary role, performs the challenge.

Every player cycles through challenger/target over time — this rotation needs to feel fair (see §9, Fairness).

---

## 6. Visual & Interaction Design Direction

### 6.1 Design Principles

1. **Big, bold, glanceable** — the host screen is often viewed from a few feet away (propped on a table, cast to a TV). Text and key UI must read at a distance: large type, high contrast, minimal fine print.
2. **One primary action per screen** — every screen has one obvious next step. No competing CTAs.
3. **Motion carries meaning, not decoration** — the spin/selection animation, the reveal, and the point celebration are the emotional peaks of the loop. These deserve real animation budget. Everything else (lobby, forms) should be fast and low-motion so it doesn't get in the way.
4. **Color-coded players throughout** — each player gets an assigned color/avatar on join, used consistently everywhere (lobby list, selection, scoreboard) so at-a-glance recognition works even from across the room.

### 6.2 Visual Identity Starting Point

- **Palette:** dark, saturated background (deep purple/navy) as the base "stage," with 4-6 vivid accent colors reserved exclusively for player identity (coral, teal, gold, magenta, lime, sky blue). Dark base makes accent colors and the spin animation pop, and reads well on a TV in a dim room — classic party-game aesthetic (think Jackbox, Kahoot's darker cousin).
- **Typography:** one confident, rounded-but-not-childish display font for headlines/big moments (challenger names, "TRUTH" vs "DARE" labels), paired with a clean, highly legible sans for body/UI text. Avoid script/handwritten fonts — they hurt legibility at distance.
- **Iconography:** simple, thick-stroke icons (not thin/delicate) so they hold up at small sizes on phones and large on a cast screen.
- **Truth vs. Dare visual language:** give these two distinct, memorable colors/shapes from day one (e.g., Truth = cool blue + speech-bubble motif, Dare = hot orange/red + lightning/flame motif) — players should recognize which mode they're in without reading text.

### 6.3 Motion & Micro-interactions

- **The Spin (player selection):** this is the game's signature moment — treat it like a slot machine / wheel, not a static reveal. Worth investing real animation polish here since it repeats every round.
- **Reveal:** question/dare appears with a snap or flip transition, held on screen long enough to be read aloud comfortably.
- **Point Celebration:** short, punchy (1-2 seconds), not a lengthy cutscene — players want to get to the next round, not sit through confetti every time. Confetti/burst animation tied to the scoring player's assigned color.
- **Screen transitions:** fast (200-300ms), consistent direction of travel (forward = right/up) so the app never feels like it's "thinking."

### 6.4 Player Phone vs. Host Screen — Distinct UI Modes

This needs to be explicit in design system terms:

- **Host view:** built for glanceability from a distance, larger touch targets, big typography, minimal text.
- **Player view:** built for quick private input (typing questions, tapping to join) — this can be denser and more form-like since it's held close, one person at a time.

Treat these as two different design modes sharing one visual language, not one responsive layout doing both jobs.

---

## 7. Screen-by-Screen Design Notes

| Screen                       | Device                              | Key design goal                                                                                                                                          |
| ---------------------------- | ----------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Game Selection (Create/Join) | Both                                | Zero friction — 2 buttons max, no signup wall                                                                                                            |
| Join Room                    | Player                              | Name + code entry, instant color/avatar assignment for identity                                                                                          |
| Question Submission          | Player                              | Fast repeated entry (add one, immediately ready for next), truth/dare toggle, gentle nudge toward a minimum without feeling like a form validation error |
| Waiting Room / Lobby         | Host (primary) + Player (secondary) | Show who's joined in real time, host has clear "Start" button that's disabled/greyed with a reason until minimums are met                                |
| Select Players (Spin)        | Host                                | The signature animation moment — full-screen, dramatic                                                                                                   |
| Question Selection           | Player (challenger only)            | Private to the challenger — others wait on a "choosing..." screen, not shown the options                                                                 |
| Challenge/Reveal             | Host                                | Large text, Truth/Dare visual treatment, timer optional (see §9)                                                                                         |
| Host Judgment                | Host                                | Two big clear buttons: Success / Skip — must be fast, no confirmation dialogs slowing pacing                                                             |
| Point Celebration            | Host                                | Short, color-coded to the scorer                                                                                                                         |
| Scoreboard                   | Host                                | Ranked list, clear "Next Round" CTA                                                                                                                      |
| Final Results                | Host                                | Winner spotlight, "Play Again" front and center                                                                                                          |

---

## 8. Content & Question System

- **Player-authored only for MVP** — no built-in question packs at launch. This is a deliberate scope cut: it avoids content moderation liability and keeps the app legally simpler (no need to vet "official" dares for appropriateness), while still delivering the core fun.
- **Truth/Dare toggle at submission**, evenly drawn into the "hand" the challenger sees.
- **Minimum questions per player before start** — exact number is a tuning decision (something like 3-5 each), enough to sustain a full game without running dry.
- **Used questions are marked, not deleted** — keeps history for potential "best of" recap later, avoids reusing the same dare twice in one game.

---

## 9. Fun & Fairness Mechanics (New — Not in Original Spec)

These are gaps in the original technical spec that matter for it to actually feel _fun_ rather than just functional:

1. **Fair rotation, not pure randomness.** Fully random challenger/target selection can (and will) leave someone out for many rounds in a row, which feels bad in a live social setting. Weight selection toward players who've had fewer turns.
2. **No self-targeting, no immediate repeat pairing.** Challenger and target must differ, and ideally the same pair shouldn't repeat back-to-back.
3. **Skip has a cost.** If skipping challenges is completely free, dares lose their teeth. Consider: skip means zero points and the question goes to the "used" pile (can't be swapped for an easier one).
4. **Optional light timer on reveal.** Not required, but a soft 30-60s response timer adds energy without being punishing — should be a host-toggleable setting, not forced.
5. **Host absence handling.** If the host's device disconnects mid-game, the game shouldn't die — needs a graceful reconnect or host-handoff path (a real product decision, flagged here for the technical side to solve).

---

## 10. Scope: MVP vs. Later

**MVP (ship this first):**

- Core loop exactly as in §4
- Player-authored questions only
- Basic fair-rotation selection (§9.1–9.3)
- Host judgment (success/skip)
- Live scoreboard
- One visual theme (the palette/type direction in §6.2)

**Deliberately deferred (do not build yet):**

- Official/curated question packs
- Spicy/NSFW mode toggle with separate content moderation needs
- Custom room themes or player avatar customization beyond auto-assigned color
- Round timer as a forced (non-optional) feature
- Recap/highlight reel at game end
- Spectator mode / streaming overlay
- Multi-language support

---

## 11. Success Signals for "Is This Actually Fun"

Since this is a party game, standard SaaS metrics don't apply. Watch for:

- **Rounds per session** — are groups playing many rounds, or losing steam after 2-3?
- **Return play rate within the same group** — do the same friends start a second game same night?
- **Time-to-first-challenge** — how long from room creation to the first live dare? Long setup kills party momentum.
- **Skip rate** — if skip is used constantly, dares/truths aren't landing or the judgment mechanic needs adjusting.

---

## 12. Open Decisions (Flag for Future You)

- [ ] Final name/tagline
- [ ] Exact minimum question count per player
- [ ] Whether skip should cost points or just forgo them
- [ ] Timer: on by default, off by default, or host choice only
- [ ] Host disconnect/handoff behavior (technical + product decision)
- [ ] End-game condition: fixed rounds, score threshold, or host-ends-anytime (leaning host-ends-anytime for MVP simplicity)
