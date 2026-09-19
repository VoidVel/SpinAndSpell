import { useEffect, useRef, useState } from 'react'
import { motion } from 'motion/react'
import type { PlayerView } from '../../realtime/types'
import { colourOf } from '../../design/colours'
import { DareBolt } from '../../design/Motifs'

const easeOutCubic = (t: number) => 1 - Math.pow(1 - t, 3)

/**
 * Cycles through players like a slot reel and decelerates onto a predetermined
 * landing index.
 *
 * The result is already decided server-side before this ever runs (§2.3 of the
 * development plan) — this animates *toward* a known answer rather than picking
 * one. That is what makes it safe for a phone to lock mid-spin: the game
 * advances on a server timer regardless of what this does.
 */
function useReel(players: PlayerView[], landOn: number, startMs: number, durationMs: number) {
  const [index, setIndex] = useState(0)
  const [settled, setSettled] = useState(false)
  const frame = useRef(0)

  useEffect(() => {
    if (players.length === 0 || landOn < 0) return

    const begin = performance.now() + startMs
    // Enough whole revolutions to read as a spin, then the offset that lands it.
    const revolutions = 4
    const totalSteps = players.length * revolutions + landOn

    const tick = (now: number) => {
      const elapsed = now - begin

      if (elapsed < 0) {
        frame.current = requestAnimationFrame(tick)
        return
      }

      const progress = Math.min(elapsed / durationMs, 1)
      setIndex(Math.floor(easeOutCubic(progress) * totalSteps) % players.length)

      if (progress >= 1) {
        setIndex(landOn)
        setSettled(true)
        return
      }

      frame.current = requestAnimationFrame(tick)
    }

    frame.current = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(frame.current)
  }, [players.length, landOn, startMs, durationMs])

  return { index, settled }
}

interface SpinProps {
  players: PlayerView[]
  challengerId: string
  targetId: string
  animationMs: number
}

export function Spin({ players, challengerId, targetId, animationMs }: SpinProps) {
  const pool = players.filter((p) => p.connected)
  const challengerIndex = pool.findIndex((p) => p.id === challengerId)
  const targetIndex = pool.findIndex((p) => p.id === targetId)

  // Two beats rather than one: the challenger lands, then the room waits a
  // moment to find out who they get to point it at.
  const challengerSpin = Math.round(animationMs * 0.42)
  const targetStart = Math.round(animationMs * 0.46)
  const targetSpin = animationMs - targetStart

  const challenger = useReel(pool, challengerIndex, 0, challengerSpin)
  const target = useReel(pool, targetIndex, targetStart, targetSpin)

  return (
    <div className="flex w-full flex-col items-center gap-10">
      <Reel label="Challenger" players={pool} index={challenger.index} settled={challenger.settled} />

      <motion.div
        initial={{ opacity: 0, scale: 0.6 }}
        animate={challenger.settled ? { opacity: 1, scale: 1 } : {}}
        transition={{ type: 'spring', stiffness: 400, damping: 16 }}
        className="flex items-center gap-3"
      >
        <DareBolt className="h-7 w-7 text-dare" />
        <span className="font-display text-2xl font-extrabold uppercase tracking-[0.3em] text-dare">
          dares
        </span>
        <DareBolt className="h-7 w-7 text-dare" />
      </motion.div>

      <Reel label="Target" players={pool} index={target.index} settled={target.settled} highlight />
    </div>
  )
}

function Reel({
  label,
  players,
  index,
  settled,
  highlight,
}: {
  label: string
  players: PlayerView[]
  index: number
  settled: boolean
  highlight?: boolean
}) {
  const player = players[index]
  if (!player) return null

  return (
    <div className="flex flex-col items-center gap-3">
      <span className="text-sm font-bold uppercase tracking-[0.3em] text-ink-dim">{label}</span>

      <motion.div
        key={settled ? 'settled' : player.id}
        animate={
          settled
            ? { scale: highlight ? [1, 1.25, 1.1] : [1, 1.15, 1], rotate: [0, -2, 2, 0] }
            : { scale: 1 }
        }
        transition={{ duration: 0.45, ease: [0.2, 0.9, 0.3, 1] }}
        className="rounded-3xl px-10 py-6"
        style={{
          background: settled ? colourOf(player) : 'var(--color-stage-raised)',
          color: settled ? 'var(--color-stage)' : 'var(--color-ink)',
          boxShadow: settled ? `0 0 60px ${colourOf(player)}55` : 'none',
        }}
      >
        <span className="stage-title block">{player.name}</span>
      </motion.div>
    </div>
  )
}
