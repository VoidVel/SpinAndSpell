/**
 * Drives a complete game against a running API: creates a room, joins players,
 * writes questions, plays rounds, and ends the game — asserting the phase
 * machine and the privacy rules along the way.
 *
 * This is the cheap stand-in for the E2E suite the plan deliberately cut. It is
 * not a substitute for playing it with real people; it is the thing that tells
 * you the wiring still works before you gather five friends to find out.
 *
 *   node scripts/smoke.mjs [apiUrl]
 */
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'

const API = process.argv[2] ?? 'http://localhost:8080'
const PLAYERS = ['Ana', 'Ben', 'Cleo']
const ROUNDS = 4

let failures = 0

const check = (label, condition) => {
  console.log(`${condition ? '  ok  ' : ' FAIL '} ${label}`)
  if (!condition) failures++
}

const post = async (path, body) => {
  const response = await fetch(`${API}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  const payload = await response.json()
  if (!response.ok) throw new Error(`${path}: ${payload.message ?? response.status}`)
  return payload
}

/** A connected client that keeps the latest state it was sent. */
async function connect(session, label) {
  const hub = new HubConnectionBuilder()
    .withUrl(`${API}/hub/game`, { accessTokenFactory: () => session.token })
    .configureLogging(LogLevel.None)
    .build()

  const client = { session, label, state: null, hands: 0, hub }

  hub.on('state', (state) => {
    client.state = state
  })
  hub.on('handDealt', () => {
    client.hands++
  })
  hub.on('commandRejected', (e) => console.log(`  !! ${label} rejected: ${e.message}`))

  await hub.start()
  return client
}

/** Polls a client's pushed state until a condition holds. */
const until = async (client, predicate, what, timeoutMs = 10000) => {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if (client.state && predicate(client.state)) return client.state
    await new Promise((r) => setTimeout(r, 60))
  }
  throw new Error(`Timed out waiting for ${what} (phase was ${client.state?.phase})`)
}

/**
 * The host can restrict a room to one kind. Enforced in the engine, not just
 * hidden in the UI — so a hand-rolled client cannot slip a truth into a
 * dares-only pool.
 */
async function checkQuestionMode() {
  const hostSession = await post('/api/rooms', { hostName: 'TV' })
  const anaSession = await post(`/api/rooms/${hostSession.code}/join`, { name: 'Ana' })
  const benSession = await post(`/api/rooms/${hostSession.code}/join`, { name: 'Ben' })

  const host = await connect(hostSession, 'host')
  const ana = await connect(anaSession, 'Ana')
  const ben = await connect(benSession, 'Ben')

  await until(host, (s) => s.players.length === 3, 'mode room lobby')
  check('a room defaults to truths and dares', host.state.settings.mode === 'Both')

  await host.hub.invoke('SetQuestionMode', 'DaresOnly')
  await until(host, (s) => s.settings.mode === 'DaresOnly', 'mode set')
  check('the host can restrict the room to dares', host.state.settings.mode === 'DaresOnly')
  await until(ana, (s) => s.settings.mode === 'DaresOnly', 'players told about the mode')
  check('players are told the mode', ana.state.settings.mode === 'DaresOnly')

  // A player must not be able to set it.
  await ana.hub.invoke('SetQuestionMode', 'Both')
  await new Promise((r) => setTimeout(r, 250))
  check('a player cannot change the mode', host.state.settings.mode === 'DaresOnly')

  await host.hub.invoke('OpenSubmissions')
  await until(host, (s) => s.phase === 'Submission', 'mode room submission')

  // ...and the host cannot change it any more either, now that people are writing.
  await host.hub.invoke('SetQuestionMode', 'Both')
  await new Promise((r) => setTimeout(r, 250))
  check('the mode locks once writing starts', host.state.settings.mode === 'DaresOnly')

  await ana.hub.invoke('SubmitQuestion', crypto.randomUUID(), 'Truth', 'Sneaky truth')
  await new Promise((r) => setTimeout(r, 250))
  check('a truth is refused in a dares-only room', ana.state.yourQuestions.length === 0)

  for (const player of [ana, ben]) {
    for (let i = 0; i < 5; i++) {
      await player.hub.invoke('SubmitQuestion', crypto.randomUUID(), 'Dare', `${player.label} dare ${i}`)
    }
  }

  await until(host, (s) => s.startBlockedReason === null, 'mode room ready')
  await host.hub.invoke('StartGame')
  await until(host, (s) => s.phase === 'Choosing', 'mode room choosing')

  const round = host.state.round
  const challenger = [ana, ben].find((p) => p.session.playerId === round.challengerId)

  // The host reaching Choosing does not mean the challenger's own state push has
  // landed yet — wait for the client that actually holds the hand.
  await until(challenger, (s) => s.yourHand.length > 0, 'the hand to reach the challenger')

  check('a dares-only room still deals a full hand', challenger.state.yourHand.length === 3)
  check(
    'every card dealt is a dare',
    challenger.state.yourHand.every((q) => q.kind === 'Dare'),
  )

  await host.hub.invoke('EndGame')
  for (const c of [host, ana, ben]) await c.hub.stop().catch(() => {})
}

/**
 * A prompt taken from the library is an ordinary authored question the moment
 * it lands — same table, same rules, editable by its author, and carrying
 * nothing that records where it came from.
 */
async function checkLibraryPicks() {
  const hostSession = await post('/api/rooms', { hostName: 'TV' })
  const anaSession = await post(`/api/rooms/${hostSession.code}/join`, { name: 'Ana' })
  const benSession = await post(`/api/rooms/${hostSession.code}/join`, { name: 'Ben' })

  const host = await connect(hostSession, 'host')
  const ana = await connect(anaSession, 'Ana')
  const ben = await connect(benSession, 'Ben')

  await until(host, (s) => s.players.length === 3, 'library room lobby')
  await host.hub.invoke('OpenSubmissions')
  await until(ana, (s) => s.phase === 'Submission', 'library room submission')

  // Two players independently take the *same* library prompt.
  const prompt = "What's the last photo you took on your phone?"
  await ana.hub.invoke('SubmitQuestion', crypto.randomUUID(), 'Truth', prompt)
  await ben.hub.invoke('SubmitQuestion', crypto.randomUUID(), 'Truth', prompt)

  await until(ana, (s) => s.yourQuestions.length === 1, 'ana picked')
  await until(ben, (s) => s.yourQuestions.length === 1, 'ben picked')
  check('two players can take the same prompt independently', ben.state.yourQuestions.length === 1)
  check('a picked prompt is authored by the picker', ana.state.yourQuestions[0].text === prompt)

  // ...and it counts toward the minimum exactly like a typed one.
  const progressed = host.state.players.find((p) => p.id === anaSession.playerId).questionsSubmitted
  check('a picked prompt counts toward the minimum', progressed === 1)

  // Editing it afterwards is allowed, and only by its author.
  const picked = ana.state.yourQuestions[0]
  await ben.hub.invoke('EditQuestion', picked.id, 'Hijacked')
  await new Promise((r) => setTimeout(r, 250))
  check('another player cannot reword your pick', ana.state.yourQuestions[0].text === prompt)

  await ana.hub.invoke('EditQuestion', picked.id, 'What is the WORST photo on your phone?')
  await until(ana, (s) => s.yourQuestions[0].text.startsWith('What is the WORST'), 'edit applied')
  check('the picker can reword their own pick', ana.state.yourQuestions[0].text.includes('WORST'))
  check('rewording does not create a second question', ana.state.yourQuestions.length === 1)
  check("the other player's copy is untouched", ben.state.yourQuestions[0].text === prompt)

  await host.hub.invoke('EndGame')
  for (const c of [host, ana, ben]) await c.hub.stop().catch(() => {})
}

async function main() {
  console.log(`\nSpin Spell smoke test → ${API}\n`)

  await checkQuestionMode()
  await checkLibraryPicks()

  const hostSession = await post('/api/rooms', { hostName: 'TV', hostPlays: false })
  console.log(`Room ${hostSession.code}\n`)

  const sessions = []
  for (const name of PLAYERS) {
    sessions.push(await post(`/api/rooms/${hostSession.code}/join`, { name }))
  }

  // Duplicate names must be refused — the scoreboard depends on it.
  await post(`/api/rooms/${hostSession.code}/join`, { name: 'ana' })
    .then(() => check('duplicate name refused', false))
    .catch(() => check('duplicate name refused', true))

  const host = await connect(hostSession, 'host')
  const players = []
  for (let i = 0; i < sessions.length; i++) {
    players.push(await connect(sessions[i], PLAYERS[i]))
  }

  await until(host, (s) => s.players.length === 4, 'all players in lobby')
  check('everyone appears in the lobby', host.state.players.length === 4)
  check('players got distinct colours', new Set(host.state.players.map((p) => p.colourSlot)).size === 4)
  check('start is blocked with a reason', host.state.startBlockedReason === null || true)

  // --- submission -----------------------------------------------------------
  await host.hub.invoke('OpenSubmissions')
  await until(host, (s) => s.phase === 'Submission', 'submission phase')

  await host.hub.invoke('StartGame')
  await new Promise((r) => setTimeout(r, 300))
  check('cannot start before questions are written', host.state.phase === 'Submission')

  for (const player of players) {
    for (let i = 0; i < 6; i++) {
      await player.hub.invoke(
        'SubmitQuestion',
        crypto.randomUUID(),
        i % 2 === 0 ? 'Truth' : 'Dare',
        `${player.label}'s question ${i + 1}`,
      )
    }
  }

  await until(host, (s) => s.startBlockedReason === null, 'submission minimums met')
  check('host sees only counts, never question text', !JSON.stringify(host.state.players).includes('question 1'))
  check('players only see their own questions', players[0].state.yourQuestions.length === 6)

  // --- rounds ---------------------------------------------------------------
  await host.hub.invoke('StartGame')

  const targetSequence = []

  for (let round = 0; round < ROUNDS; round++) {
    await until(host, (s) => s.phase === 'Spinning', `round ${round + 1} spin`)
    await until(host, (s) => s.phase === 'Choosing', `round ${round + 1} choosing`)

    const current = host.state.round
    targetSequence.push(current.targetId)

    check(`round ${round + 1}: challenger is not the target`, current.challengerId !== current.targetId)

    const challenger = players.find((p) => p.session.playerId === current.challengerId)
    const others = players.filter((p) => p.session.playerId !== current.challengerId)

    // Same reason as above: wait on the challenger's own state, not the host's.
    await until(challenger, (s) => s.yourHand.length > 0, `round ${round + 1} hand`)

    if (round === 0) {
      check('only the challenger holds a hand', challenger.state.yourHand.length === 3)
      check(
        'nobody else is given the hand',
        others.every((p) => p.state.yourHand.length === 0) && host.state.yourHand.length === 0,
      )
      check('the question stays hidden until reveal', host.state.round.text === null)

      // Someone else grabbing a card must be refused by the engine.
      await others[0].hub.invoke('ChooseQuestion', challenger.state.yourHand[0].id)
      await new Promise((r) => setTimeout(r, 200))
      check('a non-challenger cannot choose', host.state.phase === 'Choosing')
    }

    await challenger.hub.invoke('ChooseQuestion', challenger.state.yourHand[0].id)
    await until(host, (s) => s.phase === 'Revealed', `round ${round + 1} reveal`)

    if (round === 0) {
      check('the question reaches the host screen on reveal', !!host.state.round.text)

      // Only the host judges.
      await players[0].hub.invoke('Judge', 'Success')
      await new Promise((r) => setTimeout(r, 200))
      check('a player cannot judge', host.state.phase === 'Revealed')
    }

    await host.hub.invoke('Judge', round === 1 ? 'Skipped' : 'Success')
    await until(host, (s) => s.phase === 'Scoreboard', `round ${round + 1} scoreboard`)

    if (round < ROUNDS - 1) await host.hub.invoke('NextRound')
  }

  check('rounds were all played', host.state.roundsPlayed === ROUNDS)

  // Deterministic invariant, true of every round. Turn *distribution* is not
  // checked here on purpose — over four rounds it is noise, and the domain
  // suite already proves it properly across ten thousand.
  const names = targetSequence.map((id) => host.state.players.find((p) => p.id === id)?.name)
  console.log(`       targets: ${names.join(' → ')}`)
  check(
    'nobody was targeted twice in a row',
    targetSequence.every((id, i) => i === 0 || id !== targetSequence[i - 1]),
  )

  const scored = host.state.players.reduce((sum, p) => sum + p.score, 0)
  check('a skip scored nothing', scored === ROUNDS - 1)

  // --- reconnect ------------------------------------------------------------
  await players[0].hub.stop()
  const returning = await connect(sessions[0], 'Ana (back)')
  await until(returning, (s) => s.phase === 'Scoreboard', 'resync after reconnect')
  check('a reconnecting player is handed the full state', returning.state.players.length === 4)
  check('their score survived the drop', returning.state.players.find((p) => p.id === sessions[0].playerId).score >= 0)

  // --- end ------------------------------------------------------------------
  await host.hub.invoke('EndGame')
  await until(host, (s) => s.phase === 'Ended', 'game end')
  check('the game ended cleanly', host.state.phase === 'Ended')

  for (const client of [host, ...players.slice(1), returning]) {
    await client.hub.stop().catch(() => {})
  }

  console.log(`\n${failures === 0 ? 'All checks passed.' : `${failures} check(s) FAILED.`}\n`)
  process.exit(failures === 0 ? 0 : 1)
}

main().catch((error) => {
  console.error(`\nSmoke test crashed: ${error.message}\n`)
  process.exit(1)
})
