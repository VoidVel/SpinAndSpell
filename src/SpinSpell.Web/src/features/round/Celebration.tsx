import { motion } from 'motion/react'
import type { PlayerView, RoundOutcome } from '../../realtime/types'
import { colourOf } from '../../design/colours'
import { Star } from '../../design/Motifs'

/**
 * §6.3: short and punchy, 1-2 seconds. Players want the next round, not a
 * cutscene — so this is a single burst tinted to the scorer's colour and then
 * it is gone.
 *
 * The point itself is gold, because gold means points everywhere in this app.
 */
export function Celebration({
  player,
  outcome,
}: {
  player: PlayerView
  outcome: RoundOutcome | null
}) {
  const scored = outcome === 'Success'
  const colour = colourOf(player)
  const shards = Array.from({ length: scored ? 22 : 0 }, (_, i) => i)

  return (
    <div className="relative flex flex-col items-center gap-6">
      {shards.map((i) => {
        const angle = (i / shards.length) * Math.PI * 2
        // Every third shard is gold, so the burst reads as points being won
        // rather than as generic confetti.
        const gold = i % 3 === 0
        return (
          <motion.span
            key={i}
            className="absolute left-1/2 top-1/2 rounded-sm"
            style={{
              background: gold ? 'var(--color-gold)' : colour,
              width: gold ? 10 : 14,
              height: gold ? 10 : 14,
            }}
            initial={{ x: 0, y: 0, opacity: 1, scale: 1 }}
            animate={{
              x: Math.cos(angle) * (150 + (i % 4) * 45),
              y: Math.sin(angle) * (150 + (i % 3) * 45),
              opacity: 0,
              scale: 0.3,
              rotate: 220,
            }}
            transition={{ duration: 1.1, ease: 'easeOut' }}
          />
        )
      })}

      <motion.div
        initial={{ scale: 0.4, opacity: 0 }}
        animate={{ scale: 1, opacity: 1 }}
        transition={{ type: 'spring', stiffness: 320, damping: 18 }}
        className="relative z-10 flex flex-col items-center gap-3"
      >
        <span className="stage-title" style={{ color: scored ? colour : 'var(--color-ink-dim)' }}>
          {player.name}
        </span>

        {scored ? (
          <motion.span
            initial={{ scale: 0, rotate: -20 }}
            animate={{ scale: 1, rotate: 0 }}
            transition={{ delay: 0.15, type: 'spring', stiffness: 400, damping: 14 }}
            className="flex items-center gap-3"
          >
            <Star className="h-10 w-10 text-gold" />
            <span className="score-figure text-5xl">+1</span>
          </motion.span>
        ) : (
          <span className="stage-subtitle text-ink-dim">skipped — no points</span>
        )}
      </motion.div>
    </div>
  )
}
