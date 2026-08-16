import { useState } from 'react'
import { AnimatePresence, motion } from 'motion/react'
import type { QuestionKind, QuestionView, RoomView } from '../../realtime/types'
import {
  Button,
  Card,
  ChallengeCard,
  KindBadge,
  PhoneShell,
  PlayerChip,
} from '../../design/primitives'
import { colourOf } from '../../design/colours'
import { Motif, Star } from '../../design/Motifs'
import { kindStyle, kindsFor } from '../../design/kinds'
import { Waiting } from '../../design/Loader'
import { leaveToStart, useSend } from '../../store/gameStore'
import { newId } from '../../realtime/ids'
import { Countdown } from '../shared/Countdown'
import { LibraryStack, type Tier } from './LibraryStack'

export function PlayerView({ room }: { room: RoomView }) {
  const send = useSend()
  const you = room.players.find((p) => p.id === room.youId)

  return (
    <PhoneShell>
      <header className="mb-6 flex items-center gap-3">
        {you && <PlayerChip player={you} />}
        <div className="flex flex-col text-left">
          <span className="text-lg font-bold" style={{ color: you ? colourOf(you) : undefined }}>
            {you?.name}
          </span>
          <span className="text-sm text-ink-dim">Room {room.code}</span>
        </div>
        <span className="ml-auto flex items-center gap-1.5">
          <Star className="h-5 w-5 text-gold" />
          <span className="score-figure text-2xl">{you?.score ?? 0}</span>
        </span>
      </header>

      <AnimatePresence mode="wait">
        <motion.div
          key={room.phase + String(room.youAreChallenger)}
          initial={{ opacity: 0, x: 24 }}
          animate={{ opacity: 1, x: 0 }}
          exit={{ opacity: 0, x: -24 }}
          transition={{ duration: 0.22, ease: [0.2, 0.9, 0.3, 1] }}
          className="flex flex-1 flex-col"
        >
          {room.phase === 'Lobby' && (
            <Waiting scale={0.6} title="You're in" subtitle="Waiting for the host to start…" />
          )}

          {room.phase === 'Submission' && <Compose room={room} />}

          {room.phase === 'Spinning' && (
            <Waiting scale={0.6} title="Spinning…" subtitle="Eyes on the big screen." />
          )}

          {room.phase === 'Choosing' &&
            (room.youAreChallenger ? (
              <Hand hand={room.yourHand} onPick={(id) => send('ChooseQuestion', id)} />
            ) : (
              <Waiting scale={0.6} title="Someone's choosing…" subtitle="Hold tight." />
            ))}

          {(room.phase === 'Revealed' || room.phase === 'Judging') && <Challenge room={room} />}

          {room.phase === 'Celebration' && (
            <Waiting scale={0.6} title="Watch the screen" subtitle="Someone's about to score." />
          )}

          {room.phase === 'Scoreboard' && <BetweenRounds room={room} />}

          {room.phase === 'Ended' && <Finished room={room} />}
        </motion.div>
      </AnimatePresence>
    </PhoneShell>
  )
}

/**
 * §7: fast repeated entry — add one, immediately ready for the next. The
 * minimum is a nudge in the app's own voice, not a form validation error.
 */
function Compose({ room, compact }: { room: RoomView; compact?: boolean }) {
  const send = useSend()
  const allowed = kindsFor(room.settings.mode)
  const [chosen, setChosen] = useState<QuestionKind>('Truth')

  // A single-kind room has nothing to toggle, so the choice is made for you.
  const kind = allowed.length === 1 ? allowed[0] : chosen
  const [text, setText] = useState('')
  const [tier, setTier] = useState<Tier>('warmup') // §: always land on Warm-Up
  const [writing, setWriting] = useState(false)

  const target = room.settings.minQuestionsPerPlayer
  const written = room.yourQuestions.length
  const toGo = Math.max(0, target - written)

  const add = (value: string) => {
    const trimmed = value.trim()
    if (!trimmed) return
    // A picked prompt and a typed one take exactly the same path from here.
    send('SubmitQuestion', newId(), kind, trimmed)
  }

  const submit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!text.trim()) return

    // Clear optimistically: the input must be ready for the next question
    // immediately (§7), and the server echoes the real list back either way.
    add(text)
    setText('')
    setWriting(false)
  }

  return (
    <div className="flex flex-1 flex-col gap-4">
      {!compact && (
        <div className="flex flex-col gap-3">
          <h1 className="font-display text-3xl font-extrabold">
            {allowed.length === 1 ? `Pick your ${kind.toLowerCase()}s` : 'Fill the pot'}
          </h1>

          {/* Progress as dots: countable at a glance, no reading required. */}
          <div className="flex items-center gap-2">
            {Array.from({ length: Math.max(target, written) }, (_, i) => (
              <motion.span
                key={i}
                initial={false}
                animate={{ scale: i < written ? 1 : 0.62 }}
                className="h-3 w-3 rounded-full"
                style={{
                  background: i < written ? 'var(--color-gold)' : 'var(--color-edge)',
                  boxShadow: i < written ? '0 0 12px var(--color-gold-glow)' : 'none',
                }}
              />
            ))}
            <span className="ml-2 text-sm text-ink-dim">
              {toGo > 0 ? `${toGo} to go` : 'Done — add more if you like'}
            </span>
          </div>
        </div>
      )}

      {/* Two big motif cards: §6.2 wants the mode recognisable without reading.
          In a single-kind room there is no choice to make, so it becomes a
          banner instead of a dead toggle. */}
      {allowed.length === 1 ? (
        <div
          className="flex min-h-16 items-center justify-center gap-3 rounded-2xl border-2 font-display text-xl font-extrabold uppercase tracking-wider"
          style={{
            borderColor: kindStyle(kind).colour,
            background: kindStyle(kind).deep,
            color: kindStyle(kind).colour,
            boxShadow: `0 0 28px ${kindStyle(kind).glow}`,
          }}
        >
          <Motif kind={kind} className="h-7 w-7" />
          {kind}s only
        </div>
      ) : (
        <div className="grid grid-cols-2 gap-3">
          {allowed.map((k) => {
            const s = kindStyle(k)
            const on = kind === k
            return (
              <button
                key={k}
                type="button"
                onClick={() => setChosen(k)}
                className="flex min-h-20 flex-col items-center justify-center gap-1 rounded-2xl border-2 font-display text-lg font-extrabold uppercase tracking-wider transition active:scale-[0.97]"
                style={{
                  borderColor: on ? s.colour : 'var(--color-edge)',
                  background: on ? s.deep : 'var(--color-stage-raised)',
                  color: on ? s.colour : 'var(--color-ink-dim)',
                  boxShadow: on ? `0 0 28px ${s.glow}` : 'none',
                }}
              >
                <Motif kind={k} className="h-6 w-6" />
                {k}
              </button>
            )
          })}
        </div>
      )}

      {writing ? (
        <form onSubmit={submit} className="flex flex-col gap-3">
          <textarea
            value={text}
            onChange={(e) => setText(e.target.value)}
            maxLength={280}
            rows={3}
            autoFocus
            placeholder={
              kind === 'Truth'
                ? "What's the most embarrassing thing in your camera roll?"
                : 'Call your ex and sing them the chorus of anything.'
            }
            className="w-full resize-none rounded-2xl border-2 border-edge bg-stage-sunken p-4 text-lg outline-none placeholder:text-ink-dim/50 focus:border-ink-dim"
          />

          <div className="flex gap-3">
            <Button
              type="button"
              variant="ghost"
              full
              onClick={() => {
                setText('')
                setWriting(false)
              }}
            >
              Back to picks
            </Button>
            <Button type="submit" full disabled={!text.trim()}>
              Add it
            </Button>
          </div>
        </form>
      ) : (
        <LibraryStack
          roomCode={room.code}
          playerId={room.youId}
          kind={kind}
          tier={tier}
          onTier={setTier}
          onAdd={add}
          onWriteOwn={() => setWriting(true)}
          showTierToggle={!compact}
        />
      )}

      <div className="flex flex-col gap-2 overflow-y-auto">
        <AnimatePresence initial={false}>
          {room.yourQuestions.map((q) => (
            <motion.div key={q.id} layout>
              <YourQuestion question={q} />
            </motion.div>
          ))}
        </AnimatePresence>
      </div>
    </div>
  )
}

/**
 * A question you've added, whether you typed it or took it from the library —
 * there is no distinction, here or in the data model. Tap to reword it before
 * it gets played.
 */
function YourQuestion({ question }: { question: QuestionView }) {
  const send = useSend()
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState(question.text)

  const commit = () => {
    const trimmed = draft.trim()
    setEditing(false)

    if (!trimmed || trimmed === question.text) {
      setDraft(question.text) // Nothing to save; snap back.
      return
    }

    send('EditQuestion', question.id, trimmed)
  }

  if (editing) {
    return (
      <div
        className="flex flex-col gap-2 rounded-2xl border-l-4 bg-stage-sunken p-3"
        style={{ borderLeftColor: kindStyle(question.kind).colour }}
      >
        <textarea
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          maxLength={280}
          rows={3}
          autoFocus
          className="w-full resize-none rounded-xl border-2 border-edge bg-stage-sunken p-3 text-sm outline-none focus:border-ink-dim"
        />
        <div className="flex gap-2">
          <Button
            variant="ghost"
            size="md"
            full
            onClick={() => {
              setDraft(question.text)
              setEditing(false)
            }}
          >
            Cancel
          </Button>
          <Button size="md" full onClick={commit} disabled={!draft.trim()}>
            Save
          </Button>
        </div>
      </div>
    )
  }

  return (
    <div
      className="flex items-start gap-3 rounded-2xl border-l-4 bg-stage-raised px-4 py-3"
      style={{ borderLeftColor: kindStyle(question.kind).colour }}
    >
      <button
        onClick={() => {
          setDraft(question.text)
          setEditing(true)
        }}
        className="flex-1 text-left text-sm hover:text-ink"
      >
        {question.text}
        <span className="ml-2 text-xs text-ink-dim">edit</span>
      </button>
      <button
        onClick={() => send('DeleteQuestion', question.id)}
        className="text-ink-dim hover:text-ink"
        aria-label="Delete"
      >
        ✕
      </button>
    </div>
  )
}

/** §7: private to the challenger. Nobody else is ever sent these. */
function Hand({ hand, onPick }: { hand: QuestionView[]; onPick: (id: string) => void }) {
  const [picked, setPicked] = useState<string | null>(null)

  return (
    <div className="flex flex-1 flex-col gap-4">
      <div>
        <h1 className="font-display text-3xl font-extrabold">Your hand</h1>
        <p className="text-ink-dim">Pick one. Only you can see these.</p>
      </div>

      <div className="flex flex-col gap-4">
        {hand.map((q, i) => (
          <motion.button
            key={q.id}
            // Dealt, not listed: each card lands with a slight angle, in turn.
            initial={{ opacity: 0, y: 60, rotate: i % 2 === 0 ? -4 : 3 }}
            animate={{
              opacity: picked && picked !== q.id ? 0.25 : 1,
              y: 0,
              rotate: picked === q.id ? 0 : i % 2 === 0 ? -1.5 : 1.5,
              scale: picked === q.id ? 1.03 : 1,
            }}
            transition={{
              delay: picked ? 0 : i * 0.09,
              type: 'spring',
              stiffness: 260,
              damping: 22,
            }}
            disabled={picked !== null}
            onClick={() => {
              setPicked(q.id)
              onPick(q.id)
            }}
            className="text-left active:scale-[0.98]"
          >
            <ChallengeCard kind={q.kind} className="p-5">
              <KindBadge kind={q.kind} />
              <span className="mt-3 block text-xl font-bold">{q.text}</span>
            </ChallengeCard>
          </motion.button>
        ))}
      </div>

      {hand.length === 0 && <Waiting scale={0.6} title="No cards" subtitle="The pool ran dry." />}
    </div>
  )
}

function Challenge({ room }: { room: RoomView }) {
  const round = room.round
  if (!round?.kind || !round.text) return null

  const s = kindStyle(round.kind)

  // The target's own phone goes full-bleed in the challenge colour — you should
  // know it is your turn from the glow in your hand, before you read a word.
  if (room.youAreTarget) {
    return (
      <div
        className="-m-5 flex flex-1 flex-col items-center justify-center gap-6 p-8 text-center"
        style={{ background: `radial-gradient(120% 80% at 50% 0%, ${s.deep} 0%, transparent 70%)` }}
      >
        <KindBadge kind={round.kind} large />
        <motion.span
          animate={{ scale: [1, 1.05, 1] }}
          transition={{ duration: 1.6, repeat: Infinity }}
          className="font-display text-6xl font-extrabold"
          style={{ color: s.colour, textShadow: `0 0 40px ${s.glow}` }}
        >
          You're up
        </motion.span>
        <p className="text-2xl font-bold">{round.text}</p>
        {room.deadline && <Countdown deadline={room.deadline} serverTime={room.serverTime} />}
      </div>
    )
  }

  const target = room.players.find((p) => p.id === round.targetId)

  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-4 text-center">
      <KindBadge kind={round.kind} />
      <span
        className="font-display text-3xl font-extrabold"
        style={{ color: target && colourOf(target) }}
      >
        {target?.name}
      </span>
      <p className="text-lg text-ink-dim">{round.text}</p>
    </div>
  )
}

function BetweenRounds({ room }: { room: RoomView }) {
  const ranked = [...room.players]
    .filter((p) => room.settings.hostPlays || !p.isHost)
    .sort((a, b) => b.score - a.score)

  const dry = room.questionsInPool <= room.settings.handSize

  return (
    <div className="flex flex-1 flex-col gap-4">
      <h1 className="font-display text-3xl font-extrabold">Scores</h1>

      <div className="flex flex-col gap-2">
        {ranked.map((p, i) => (
          <motion.div
            key={p.id}
            layout
            className="flex items-center gap-3 rounded-2xl bg-stage-raised px-4 py-3"
          >
            <span className="w-6 tabular-nums text-ink-dim">{i + 1}</span>
            <PlayerChip player={p} size="sm" />
            <span className="flex-1 text-left font-bold" style={{ color: colourOf(p) }}>
              {p.name}
            </span>
            <span className="score-figure text-xl">{p.score}</span>
          </motion.div>
        ))}
      </div>

      {/* Topping up between rounds is what keeps a long session from running dry. */}
      {dry && (
        <Card className="text-left">
          <p className="mb-3 font-display text-lg font-extrabold text-gold">
            Only {room.questionsInPool} left in the pot
          </p>
          <Compose room={room} compact />
        </Card>
      )}
    </div>
  )
}

function Finished({ room }: { room: RoomView }) {
  const you = room.players.find((p) => p.id === room.youId)

  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-5 text-center">
      <span className="font-display text-4xl font-extrabold">That's a wrap</span>
      <span className="flex items-center gap-2">
        <Star className="h-8 w-8 text-gold" />
        <span className="score-figure text-5xl">{you?.score ?? 0}</span>
      </span>
      <span className="text-ink-dim">Check the big screen for the winner.</span>
      <Button onClick={leaveToStart}>Back to start</Button>
    </div>
  )
}
