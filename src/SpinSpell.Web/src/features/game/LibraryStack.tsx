import { useMemo, useState } from 'react'
import { AnimatePresence, motion } from 'motion/react'
import type { QuestionKind } from '../../realtime/types'
import { kindStyle } from '../../design/kinds'
import { Motif } from '../../design/Motifs'
import { Button } from '../../design/primitives'
import library from '../../content/library.json'

export type Tier = 'warmup' | 'spicy'

interface TierContent {
  label: string
  blurb: string
  truths: string[]
  dares: string[]
}

const TIERS = library.tiers as Record<Tier, TierContent>

/**
 * Which prompts this player has already taken, remembered across a refresh.
 * Deliberately per-player and client-side: two people can independently pick
 * the same prompt, so this is not shared state and has no business on the
 * server. Keyed by room and player so a second game starts clean.
 */
function takenKey(roomCode: string, playerId: string) {
  return `spinspell.taken.${roomCode}.${playerId}`
}

function loadTaken(roomCode: string, playerId: string): Set<string> {
  try {
    return new Set(JSON.parse(sessionStorage.getItem(takenKey(roomCode, playerId)) ?? '[]'))
  } catch {
    return new Set()
  }
}

function saveTaken(roomCode: string, playerId: string, taken: Set<string>) {
  try {
    sessionStorage.setItem(takenKey(roomCode, playerId), JSON.stringify([...taken]))
  } catch {
    // A private-mode browser refusing storage is not worth breaking the game for.
  }
}

interface LibraryStackProps {
  roomCode: string
  playerId: string
  kind: QuestionKind
  tier: Tier
  onTier: (tier: Tier) => void
  onAdd: (text: string) => void
  onWriteOwn: () => void
  showTierToggle: boolean
}

/**
 * One prompt at a time, never a scrollable wall of them — a blank page and an
 * endless list are the same problem wearing different clothes. Two big targets:
 * take this one, or see the next.
 */
export function LibraryStack({
  roomCode,
  playerId,
  kind,
  tier,
  onTier,
  onAdd,
  onWriteOwn,
  showTierToggle,
}: LibraryStackProps) {
  const [taken, setTaken] = useState(() => loadTaken(roomCode, playerId))
  const [cursor, setCursor] = useState(0)

  const s = kindStyle(kind)

  // Shuffled once per lane so two players don't walk the same order, and so a
  // second game with the same group doesn't replay the same opening cards.
  const pool = useMemo(() => {
    const source = kind === 'Truth' ? TIERS[tier].truths : TIERS[tier].dares
    const shuffled = [...source]
    for (let i = shuffled.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1))
      ;[shuffled[i], shuffled[j]] = [shuffled[j], shuffled[i]]
    }
    return shuffled
  }, [kind, tier])

  const remaining = useMemo(() => pool.filter((p) => !taken.has(p)), [pool, taken])
  const current = remaining.length > 0 ? remaining[cursor % remaining.length] : null

  const next = () => setCursor((c) => c + 1)

  const add = () => {
    if (!current) return
    const updated = new Set(taken).add(current)
    setTaken(updated)
    saveTaken(roomCode, playerId, updated)
    onAdd(current)
    // Cursor stays put: the list shrank beneath it, so the next card slides in.
  }

  return (
    <div className="flex flex-col gap-4">
      {showTierToggle && <TierToggle tier={tier} onTier={onTier} />}

      <div className="relative min-h-56">
        {/* Two ghost cards behind, so the stack reads as a deck with depth. */}
        {remaining.length > 1 && <GhostCard kind={kind} depth={2} />}
        {remaining.length > 0 && <GhostCard kind={kind} depth={1} />}

        <AnimatePresence mode="popLayout" initial={false}>
          {current ? (
            <motion.div
              key={current}
              initial={{ opacity: 0, y: 28, rotate: -2, scale: 0.96 }}
              animate={{ opacity: 1, y: 0, rotate: 0, scale: 1 }}
              exit={{ opacity: 0, x: -180, rotate: -8 }}
              transition={{ type: 'spring', stiffness: 320, damping: 28 }}
              className="relative flex min-h-56 flex-col justify-between overflow-hidden rounded-[1.75rem] border-2 p-6"
              style={{
                borderColor: s.colour,
                background: `linear-gradient(160deg, ${s.deep} 0%, var(--color-stage-sunken) 100%)`,
                boxShadow: `0 0 44px ${s.glow}`,
              }}
            >
              <Motif
                kind={kind}
                className="pointer-events-none absolute -right-5 -bottom-6 h-36 w-36 opacity-[0.13]"
              />
              <p className="relative text-left text-xl leading-snug font-bold">{current}</p>
              <span className="relative mt-4 text-left text-xs text-ink-dim">
                {remaining.length} left in {TIERS[tier].label.toLowerCase()}
              </span>
            </motion.div>
          ) : (
            <div className="flex min-h-56 flex-col items-center justify-center gap-2 rounded-[1.75rem] border-2 border-dashed border-edge p-6 text-center">
              <span className="font-display text-2xl font-extrabold">
                You've taken them all
              </span>
              <span className="text-sm text-ink-dim">
                {showTierToggle && tier === 'warmup'
                  ? 'Try the spicier ones, or write your own.'
                  : 'Time to write your own.'}
              </span>
            </div>
          )}
        </AnimatePresence>
      </div>

      {current && (
        <div className="flex gap-3">
          <Button variant="ghost" size="lg" full onClick={next} disabled={remaining.length < 2}>
            Next
          </Button>
          <Button size="lg" full onClick={add}>
            Add this
          </Button>
        </div>
      )}

      {/*
        Same visual weight as a library card and always one tap away, per the
        brief — writing your own is an equal option, not a fallback hidden
        behind an exhausted deck.
      */}
      <button
        onClick={onWriteOwn}
        className="flex min-h-20 items-center justify-center gap-3 rounded-[1.75rem] border-2 border-dashed border-edge bg-stage-raised/50 font-display text-lg font-extrabold text-ink-dim transition active:scale-[0.98] hover:border-ink-dim hover:text-ink"
      >
        <span className="text-2xl leading-none">+</span>
        Write your own
      </button>
    </div>
  )
}

function GhostCard({ kind, depth }: { kind: QuestionKind; depth: 1 | 2 }) {
  const s = kindStyle(kind)
  return (
    <div
      aria-hidden
      className="absolute inset-x-0 top-0 min-h-56 rounded-[1.75rem] border-2"
      style={{
        borderColor: s.colour,
        background: 'var(--color-stage-sunken)',
        opacity: depth === 1 ? 0.5 : 0.25,
        transform: `translateY(${depth * 7}px) scale(${1 - depth * 0.03})`,
      }}
    />
  )
}

function TierToggle({ tier, onTier }: { tier: Tier; onTier: (tier: Tier) => void }) {
  const spicy = tier === 'spicy'

  // A single tap, and Warm-Up is always where you land first.
  return (
    <button
      onClick={() => onTier(spicy ? 'warmup' : 'spicy')}
      className="flex min-h-12 items-center justify-center gap-2 rounded-2xl border-2 text-sm font-bold transition active:scale-[0.98]"
      style={{
        borderColor: spicy ? 'var(--color-dare)' : 'var(--color-edge)',
        background: spicy ? 'var(--color-dare-deep)' : 'transparent',
        color: spicy ? 'var(--color-dare)' : 'var(--color-ink-dim)',
      }}
    >
      {spicy ? '← Back to warm-ups' : 'Show spicier ones 🌶️'}
    </button>
  )
}
