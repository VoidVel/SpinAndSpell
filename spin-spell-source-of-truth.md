# Spin Spell — Source of Truth

**Status:** Living document — update this as decisions change. Anything not written here is not decided.
**Owner:** Product & design decisions live here. Technical implementation lives with the coding agent.

---

## 1. What Spin Spell Is (One Sentence)

Spin Spell is a real-time, phone-based party game where a group spins up custom truth-or-dare rounds together — friends write the questions, the app picks who's up, and everyone watches live as the drama unfolds in their hands.

**The emotional promise:** the fun of a party game with your own friends' inside jokes, without anyone having to remember or invent a dare on the spot.

---

## 2. Positioning & Differentiation

There are a lot of "spin the bottle" / truth-or-dare apps. Here's what makes Spin Spell distinct and why each choice matters:

| Choice                                                     | Why                                                                                                                                                                                                                                                                                                                           |
| ---------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Players write the content, not us**                      | Generic question packe ps get stale after onlay. Friend-written prompts are the actual product — the app is a _delivery mechanism_ for inside jokes, not a content library.                                                                                                                                                   |
| **Challenger-judged rounds**                               | Someone has to decide if a dare "counts." The player who picked the card rules on it: they chose the challenge, so they own the verdict. An honor system has no teeth, and a dedicated referee costs the room a player — this costs nothing and reads the same at two people or ten.                                          |
| **Everyone plays, on their own phone**                     | This is the single biggest UX decision, and it was reversed on 2026-09-19. There is no shared screen and no referee seat: everyone connected to a room is a player in it. Needing a third person to sit out and press buttons was the largest barrier to starting a game at all — two people at a table is now a game.        |
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
   REVEAL → shown big on every phone
      ↓
   TARGET RESPONDS (in person / out loud)
      ↓
   CHALLENGER JUDGES → success / skip
      ↓
   POINT + CELEBRATION MOMENT
      ↓
   SCOREBOARD → owner taps "Next Round"
──────────────────────────────────────────────
      ↓
GAME END (score threshold, round count, or owner ends manually)
      ↓
FINAL RESULTS / WINNER CELEBRATION
```

**Key refinement vs. the original spec:** the challenger picks from a _small hand_ of questions (e.g. 3, dealt randomly from the pool) rather than the full list. This keeps pacing fast and prevents decision paralysis — same trick card games use.

---

## 5. Roles (Confirmed)

- **Player** — everybody. Joins via code, writes questions privately, and is dealt into the challenger/target rotation. There is no non-playing role of any kind.
- **Owner** — the player who created the room. Owning it buys the pacing controls (question mode, open writing, start, next round, end game) and **nothing else**: they write, spin, perform and score like everyone else. If they leave the lobby the room hands off to the longest-present player rather than dying.
- **Challenger** — temporary role. Picks the question for this round, then delivers the verdict on it.
- **Target** — temporary role, performs the challenge.

Every player cycles through challenger/target over time — this rotation needs to feel fair (see §9, Fairness).

**Minimum to play: two.** With exactly two people the roles strictly alternate — you perform, then you judge, then you perform.

---

## 6. Visual & Interaction Design Direction

### 6.1 Design Principles

1. **Big, bold, glanceable** — a phone gets passed, propped and read over someone's shoulder, and the big moments are ones the whole room looks up for. Text and key UI must read at a glance: large type, high contrast, minimal fine print.
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

### 6.4 One UI, Two Registers

There is one view, because there is one kind of participant. Within it, screens fall into two registers:

- **Input screens** (lobby, writing, the dealt hand): dense, form-like, held close. One person at a time.
- **Moments** (the spin, the reveal, the celebration, the winner): full-bleed, big type, real animation budget. These used to belong to a shared screen and now play in everyone's hand at once.

The two registers share one visual language and one shell. A screen belongs to the second register because the whole room looks up at that instant, not because of who is holding it.

---

## 7. Screen-by-Screen Design Notes

Everything below is on every player's phone. The "Who sees what" column says only how a screen differs by your part in the round.

| Screen | Who sees what | Key design goal |
| ------ | ------------- | --------------- |
| Game Selection (Start/Join) | Everyone | Zero friction — 2 buttons max, no signup wall. Starting a room asks for your name, exactly as joining one does |
| Join Room | Everyone | Name + code entry, instant color/avatar assignment for identity |
| Waiting Room / Lobby | Owner gets the mode picker and Start | Code + QR on **every** phone so anyone can recruit; Start is disabled with a reason until minimums are met; everyone else is told whose tap they are waiting on |
| Question Submission | Everyone; owner also gets "Let's play" | Fast repeated entry (add one, immediately ready for next), truth/dare toggle, gentle nudge toward a minimum without feeling like a form validation error; live "who's still writing" for the whole room |
| Select Players (Spin) | Everyone | The signature animation moment — full-bleed, dramatic, in every hand at once |
| Question Selection | Challenger only | Private to the challenger — others wait on a "choosing…" screen, not shown the options |
| Challenge/Reveal | Target sees a full-bleed glow; everyone else reads along | Large text, Truth/Dare visual treatment, timer optional (see §9) |
| Judgment | Challenger only | Two big clear buttons: Success / Skip — must be fast, no confirmation dialogs slowing pacing |
| Point Celebration | Everyone | Short, color-coded to the scorer |
| Scoreboard | Owner gets "Next Round" and "End game" | Ranked list with your own row called out; the top-up composer appears when the pot runs low |
| Final Results | Everyone | Winner spotlight, "Play Again" front and center |

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
4. **Optional light timer on reveal.** Not required, but a soft 30-60s response timer adds energy without being punishing — should be an owner-toggleable setting, not forced.
5. **Absence handling.** Nobody's disconnect may end the party. An owner who leaves the lobby hands the room to the longest-present player. A **challenger** who drops mid-reveal would otherwise deadlock the round, since nobody else can deliver the verdict — so the owner may step in and call it, but only while the challenger is actually gone.

---

## 10. Scope: MVP vs. Later

**MVP (ship this first):**

- Core loop exactly as in §4
- Player-authored questions only
- Basic fair-rotation selection (§9.1–9.3)
- Challenger judgment (success/skip)
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
- [ ] Timer: on by default, off by default, or owner choice only
- [x] ~~Host disconnect/handoff behavior~~ — resolved 2026-09-19, see §9.5
- [x] ~~Who judges~~ — resolved 2026-09-19: the challenger, always (§2, §5)
- [ ] End-game condition: fixed rounds, score threshold, or owner-ends-anytime (leaning owner-ends-anytime for MVP simplicity)
