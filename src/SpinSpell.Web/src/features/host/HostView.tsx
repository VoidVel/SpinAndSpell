import { AnimatePresence, motion } from 'motion/react'
import { QRCodeSVG } from 'qrcode.react'
import type { PlayerView, QuestionMode, RoomView } from '../../realtime/types'
import {
  Button,
  ChallengeCard,
  Eyebrow,
  KindBadge,
  PlayerChip,
  RoomCode,
  StageShell,
} from '../../design/primitives'
import { colourOf } from '../../design/colours'
import { MODE_LABELS, kindStyle, kindsFor } from '../../design/kinds'
import { Motif, Star } from '../../design/Motifs'
import { Waiting } from '../../design/Loader'
import { leaveToStart, useSend } from '../../store/gameStore'
import { Spin } from '../round/Spin'
import { Celebration } from '../round/Celebration'
import { Countdown } from '../shared/Countdown'

/**
 * How long the reel has left, taken from the server's own deadline rather than
 * from the SpinResolved event. That way a host who reloads mid-spin picks the
 * animation up part-way and still lands exactly when the server advances the
 * phase, instead of staring at a blank stage because the one-shot event is gone.
 */
function remainingSpinMs(room: RoomView): number {
  if (!room.deadline) return 3200
  const left = new Date(room.deadline).getTime() - new Date(room.serverTime).getTime()
  return Math.max(400, left)
}

export function HostView({ room }: { room: RoomView }) {
  const send = useSend()

  const playing = room.players.filter((p) => room.settings.hostPlays || !p.isHost)
  const byId = (id?: string | null) => room.players.find((p) => p.id === id)

  return (
    <StageShell>
      <AnimatePresence mode="wait">
        <motion.div
          key={room.phase}
          initial={{ opacity: 0, y: 16 }}
          animate={{ opacity: 1, y: 0 }}
          exit={{ opacity: 0, y: -16 }}
          // §6.3: 200-300ms, forward = up, so the app never feels like it is thinking.
          transition={{ duration: 0.25, ease: [0.2, 0.9, 0.3, 1] }}
          className="flex w-full flex-col items-center gap-8"
        >
          {room.phase === 'Lobby' && (
            <Lobby
              room={room}
              onOpen={() => send('OpenSubmissions')}
              onMode={(m) => send('SetQuestionMode', m)}
            />
          )}

          {room.phase === 'Submission' && (
            <Writing room={room} playing={playing} onStart={() => send('StartGame')} />
          )}

          {room.phase === 'Spinning' && room.round && (
            <Spin
              key={room.round.index}
              players={playing}
              challengerId={room.round.challengerId}
              targetId={room.round.targetId}
              animationMs={remainingSpinMs(room)}
            />
          )}

          {room.phase === 'Choosing' && (
            <Waiting
              big
              title={`${byId(room.round?.challengerId)?.name ?? 'Someone'} is choosing…`}
              subtitle="Picking your fate from three cards."
            />
          )}

          {(room.phase === 'Revealed' || room.phase === 'Judging') && (
            <Challenge room={room} target={byId(room.round?.targetId)} send={send} />
          )}

          {room.phase === 'Celebration' && byId(room.round?.targetId) && (
            <Celebration player={byId(room.round!.targetId)!} outcome={room.round?.outcome ?? null} />
          )}

          {room.phase === 'Scoreboard' && (
            <Scoreboard
              room={room}
              playing={playing}
              onNext={() => send('NextRound')}
              onEnd={() => send('EndGame')}
            />
          )}

          {room.phase === 'Ended' && <Results playing={playing} />}
        </motion.div>
      </AnimatePresence>
    </StageShell>
  )
}

/**
 * The mode picker. It lives in the lobby because the engine locks the setting
 * the moment submissions open — flipping a room to dares-only after everyone
 * wrote truths would strand the pool.
 */
function ModePicker({
  mode,
  onMode,
}: {
  mode: QuestionMode
  onMode: (mode: QuestionMode) => void
}) {
  const options: QuestionMode[] = ['Both', 'TruthsOnly', 'DaresOnly']

  return (
    <div className="flex flex-col items-center gap-2">
      <Eyebrow>Tonight we play</Eyebrow>

      <div className="flex gap-2 rounded-2xl border-2 border-edge bg-stage-sunken p-2">
        {options.map((option) => {
          const on = option === mode
          const kinds = kindsFor(option)
          // A single-kind mode takes that kind's colour; "both" stays gold,
          // because gold is the app's own accent rather than a challenge type.
          const accent = kinds.length === 1 ? kindStyle(kinds[0]) : null

          return (
            <button
              key={option}
              onClick={() => onMode(option)}
              className="flex min-h-14 items-center gap-2 rounded-xl px-5 font-display text-lg font-extrabold transition active:scale-[0.97]"
              style={{
                background: on
                  ? (accent?.deep ?? 'var(--color-gold-deep)')
                  : 'transparent',
                color: on ? (accent?.colour ?? 'var(--color-gold)') : 'var(--color-ink-dim)',
                boxShadow: on ? `0 0 26px ${accent?.glow ?? 'var(--color-gold-glow)'}` : 'none',
              }}
            >
              {kinds.map((k) => (
                <Motif key={k} kind={k} className="h-5 w-5" />
              ))}
              {MODE_LABELS[option]}
            </button>
          )
        })}
      </div>
    </div>
  )
}

function Lobby({
  room,
  onOpen,
  onMode,
}: {
  room: RoomView
  onOpen: () => void
  onMode: (mode: QuestionMode) => void
}) {
  const joined = room.players.filter((p) => !p.isHost)
  // Scanning beats typing a code and a name. Time-to-first-challenge is one of
  // the four fun-signals in §11, and this is the cheapest minute to win.
  const joinUrl = `${window.location.origin}/?join=${room.code}`
  const emptySeats = Math.max(0, 4 - joined.length)

  return (
    <>
      <div className="flex flex-col items-center gap-5 sm:flex-row sm:gap-10">
        <div className="flex flex-col items-center gap-3">
          <Eyebrow>Join at this code</Eyebrow>
          <RoomCode code={room.code} />
        </div>

        <div className="flex flex-col items-center gap-2">
          <div className="rounded-2xl bg-ink p-3">
            <QRCodeSVG value={joinUrl} size={132} bgColor="#f4f0ff" fgColor="#140b2e" level="M" />
          </div>
          <Eyebrow>or scan</Eyebrow>
        </div>
      </div>

      <div className="flex min-h-32 flex-wrap items-center justify-center gap-5">
        <AnimatePresence mode="popLayout">
          {joined.map((p) => (
            <motion.div
              key={p.id}
              layout
              initial={{ scale: 0, opacity: 0, y: 20 }}
              animate={{ scale: 1, opacity: 1, y: 0 }}
              exit={{ scale: 0, opacity: 0 }}
              transition={{ type: 'spring', stiffness: 400, damping: 22 }}
              className="flex flex-col items-center gap-2"
            >
              <PlayerChip player={p} size="lg" />
              <span className="stage-subtitle" style={{ color: colourOf(p) }}>
                {p.name}
              </span>
            </motion.div>
          ))}
        </AnimatePresence>

        {/* Empty seats read as "room for more" — far warmer than a bare line of text. */}
        {Array.from({ length: emptySeats }, (_, i) => (
          <motion.div
            key={`seat-${i}`}
            layout
            className="flex flex-col items-center gap-2"
            animate={{ opacity: [0.15, 0.32, 0.15] }}
            transition={{ duration: 2.4, repeat: Infinity, delay: i * 0.25 }}
          >
            <span className="h-20 w-20 rounded-full border-4 border-dashed border-ink-dim" />
            <span className="stage-subtitle text-ink-dim">…</span>
          </motion.div>
        ))}
      </div>

      <ModePicker mode={room.settings.mode} onMode={onMode} />

      <div className="flex flex-col items-center gap-3">
        <Button size="xl" onClick={onOpen} disabled={!!room.startBlockedReason}>
          Start writing questions
        </Button>
        {/* §7: disabled with a reason, never just greyed out. */}
        {room.startBlockedReason && (
          <span className="text-lg text-ink-dim">{room.startBlockedReason}</span>
        )}
      </div>
    </>
  )
}

function Writing({
  room,
  playing,
  onStart,
}: {
  room: RoomView
  playing: PlayerView[]
  onStart: () => void
}) {
  const target = room.settings.minQuestionsPerPlayer
  const pot = playing.reduce((sum, p) => sum + p.questionsSubmitted, 0)

  return (
    <>
      <div className="flex flex-col items-center gap-3">
        <h1 className="stage-title">
          {room.settings.mode === 'TruthsOnly'
            ? 'Write your truths'
            : room.settings.mode === 'DaresOnly'
              ? 'Write your dares'
              : 'Write your truths & dares'}
        </h1>
        <p className="stage-subtitle text-ink-dim">
          {target} each, on your phone. Make them personal.
        </p>
      </div>

      <div className="grid w-full max-w-3xl gap-3">
        {playing.map((p) => (
          <div key={p.id} className="flex items-center gap-4">
            <PlayerChip player={p} />
            <span
              className="w-32 truncate text-left text-xl font-bold"
              style={{ color: colourOf(p) }}
            >
              {p.name}
            </span>

            <div className="relative h-5 flex-1 overflow-hidden rounded-full bg-stage-sunken ring-1 ring-edge">
              <motion.div
                className="h-full rounded-full"
                style={{
                  background: p.meetsMinimum ? 'var(--color-gold)' : colourOf(p),
                  boxShadow: p.meetsMinimum ? '0 0 20px var(--color-gold-glow)' : 'none',
                }}
                animate={{ width: `${Math.min(100, (p.questionsSubmitted / target) * 100)}%` }}
                transition={{ type: 'spring', stiffness: 220, damping: 26 }}
              />
            </div>

            <span className="w-14 text-right">
              {p.meetsMinimum ? (
                <motion.span
                  initial={{ scale: 0, rotate: -30 }}
                  animate={{ scale: 1, rotate: 0 }}
                  transition={{ type: 'spring', stiffness: 500, damping: 14 }}
                  className="inline-block text-2xl text-gold"
                >
                  ✓
                </motion.span>
              ) : (
                <span className="text-lg tabular-nums text-ink-dim">
                  {p.questionsSubmitted}/{target}
                </span>
              )}
            </span>
          </div>
        ))}
      </div>

      <div className="flex flex-col items-center gap-3">
        <span className="score-figure text-2xl">{pot} in the pot</span>

        <Button size="xl" onClick={onStart} disabled={!!room.startBlockedReason}>
          Let's play
        </Button>
        {room.startBlockedReason && (
          <span className="text-lg text-ink-dim">{room.startBlockedReason}</span>
        )}
      </div>
    </>
  )
}

function Challenge({
  room,
  target,
  send,
}: {
  room: RoomView
  target?: PlayerView
  send: (method: string, ...args: unknown[]) => void
}) {
  const round = room.round
  if (!round?.text || !round.kind || !target) return null

  return (
    <>
      <div className="flex flex-col items-center gap-4">
        <KindBadge kind={round.kind} large />
        <span className="stage-subtitle" style={{ color: colourOf(target) }}>
          {target.name}, you're up
        </span>
      </div>

      {/* The emotional peak of the loop (§6.3) — so it gets the whole stage. */}
      <motion.div
        initial={{ rotateX: -70, opacity: 0, scale: 0.9 }}
        animate={{ rotateX: 0, opacity: 1, scale: 1 }}
        transition={{ type: 'spring', stiffness: 180, damping: 20 }}
        className="w-full max-w-5xl"
        style={{ perspective: 1200 }}
      >
        <ChallengeCard kind={round.kind} className="px-8 py-12">
          <p className="font-display text-4xl leading-tight font-extrabold sm:text-6xl">
            {round.text}
          </p>
        </ChallengeCard>
      </motion.div>

      {room.deadline && <Countdown deadline={room.deadline} serverTime={room.serverTime} />}

      {/* §7: two big buttons, no confirmation dialog — pacing beats safety here. */}
      <div className="flex w-full max-w-2xl gap-4">
        <Button variant="quiet" size="xl" full onClick={() => send('Judge', 'Skipped')}>
          Skip
        </Button>
        <Button size="xl" full onClick={() => send('Judge', 'Success')}>
          Nailed it
        </Button>
      </div>
    </>
  )
}

function Scoreboard({
  room,
  playing,
  onNext,
  onEnd,
}: {
  room: RoomView
  playing: PlayerView[]
  onNext: () => void
  onEnd: () => void
}) {
  const ranked = [...playing].sort((a, b) => b.score - a.score)
  const leading = ranked[0]?.score ?? 0

  return (
    <>
      <div className="flex flex-col items-center gap-2">
        <Eyebrow>After {room.roundsPlayed} rounds</Eyebrow>
        <h1 className="stage-title">Scores</h1>
      </div>

      <div className="flex w-full max-w-2xl flex-col gap-3">
        {ranked.map((p, i) => {
          const top = p.score === leading && leading > 0
          return (
            <motion.div
              key={p.id}
              layout
              transition={{ type: 'spring', stiffness: 300, damping: 26 }}
              className="flex items-center gap-4 rounded-2xl px-5 py-3"
              style={{
                background: top ? 'var(--color-gold-deep)' : 'var(--color-stage-raised)',
                boxShadow: top ? '0 0 40px var(--color-gold-glow)' : 'none',
              }}
            >
              <span className="w-8 text-2xl font-bold tabular-nums text-ink-dim">{i + 1}</span>
              <PlayerChip player={p} crowned={top && i === 0} />
              <span className="flex-1 text-left text-2xl font-bold" style={{ color: colourOf(p) }}>
                {p.name}
              </span>
              <span className="score-figure text-3xl">{p.score}</span>
            </motion.div>
          )
        })}
      </div>

      <div className="flex flex-col items-center gap-3">
        <Button size="xl" onClick={onNext} disabled={room.questionsInPool === 0}>
          Next round
        </Button>
        {room.questionsInPool === 0 ? (
          <span className="text-lg text-ink-dim">
            Out of questions — everyone add a few more on your phones.
          </span>
        ) : (
          <button onClick={onEnd} className="text-ink-dim underline underline-offset-4">
            End the game
          </button>
        )}
      </div>
    </>
  )
}

function Results({ playing }: { playing: PlayerView[] }) {
  const ranked = [...playing].sort((a, b) => b.score - a.score)
  const winner = ranked[0]

  return (
    <>
      <Eyebrow>Winner</Eyebrow>

      {winner && (
        <motion.div
          initial={{ scale: 0.5, opacity: 0 }}
          animate={{ scale: 1, opacity: 1 }}
          transition={{ type: 'spring', stiffness: 260, damping: 18 }}
          className="flex flex-col items-center gap-4"
        >
          <PlayerChip player={winner} size="xl" crowned />
          <span className="stage-title" style={{ color: colourOf(winner) }}>
            {winner.name}
          </span>
          <span className="flex items-center gap-2">
            <Star className="h-8 w-8 text-gold" />
            <span className="score-figure text-4xl">{winner.score}</span>
          </span>
        </motion.div>
      )}

      <div className="flex flex-col gap-2">
        {ranked.slice(1).map((p, i) => (
          <div key={p.id} className="flex items-center gap-3 text-xl text-ink-dim">
            <span className="tabular-nums">{i + 2}.</span>
            <span style={{ color: colourOf(p) }}>{p.name}</span>
            <span className="score-figure text-xl">{p.score}</span>
          </div>
        ))}
      </div>

      <Button size="xl" onClick={leaveToStart}>
        Play again
      </Button>
    </>
  )
}
