import { useState } from 'react'
import { AnimatePresence, motion } from 'motion/react'
import type { QuestionKind, QuestionView, RoomView } from '../../realtime/types'
import { Button } from '../../design/primitives'
import { Motif } from '../../design/Motifs'
import { kindStyle, kindsFor } from '../../design/kinds'
import { useSend } from '../../store/gameStore'
import { newId } from '../../realtime/ids'
import { LibraryStack, type Tier } from './LibraryStack'

/**
 * §7: fast repeated entry — add one, immediately ready for the next. The
 * minimum is a nudge in the app's own voice, not a form validation error.
 *
 * `compact` is the between-rounds top-up, where the heading and the progress
 * dots would just be noise.
 */
export function Compose({ room, compact }: { room: RoomView; compact?: boolean }) {
  const send = useSend()
  const allowed = kindsFor(room.settings.mode)
  const [chosen, setChosen] = useState<QuestionKind>('Truth')

  // A single-kind room has nothing to toggle, so the choice is made for you.
  const kind = allowed.length === 1 ? allowed[0] : chosen
  const [text, setText] = useState('')
  const [tier, setTier] = useState<Tier>('warmup') // Always land on Warm-Up.
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
    <div className="flex flex-col gap-4">
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

      <div className="flex flex-col gap-2">
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
