import { motion } from 'motion/react'
import type { RoomView } from '../../realtime/types'
import { Button, Eyebrow, PlayerChip, Stage } from '../../design/primitives'
import { colourOf } from '../../design/colours'
import { Star } from '../../design/Motifs'
import { leaveToStart } from '../../store/gameStore'

/**
 * The winner spotlight, which used to be the shared screen's last act and is
 * now everybody's. Your own line is called out, because coming second is a
 * result too and it should be legible on your own phone.
 */
export function Results({ room }: { room: RoomView }) {
  const ranked = [...room.players].sort((a, b) => b.score - a.score)
  const winner = ranked[0]
  const youWon = winner?.id === room.youId

  return (
    <Stage className="gap-5">
      <Eyebrow>{youWon ? 'You won' : 'Winner'}</Eyebrow>

      {winner && (
        <motion.div
          initial={{ scale: 0.5, opacity: 0 }}
          animate={{ scale: 1, opacity: 1 }}
          transition={{ type: 'spring', stiffness: 260, damping: 18 }}
          className="flex flex-col items-center gap-3"
        >
          <PlayerChip player={winner} size="xl" crowned />
          <span className="stage-title" style={{ color: colourOf(winner) }}>
            {winner.name}
          </span>
          <span className="flex items-center gap-2">
            <Star className="h-7 w-7 text-gold" />
            <span className="score-figure text-4xl">{winner.score}</span>
          </span>
        </motion.div>
      )}

      <div className="flex w-full flex-col gap-1.5">
        {ranked.slice(1).map((p, i) => (
          <div
            key={p.id}
            className="flex items-center gap-3 rounded-xl px-3 py-1.5 text-lg"
            style={{
              background: p.id === room.youId ? 'var(--color-stage-raised)' : 'transparent',
            }}
          >
            <span className="w-5 tabular-nums text-ink-dim">{i + 2}</span>
            <span className="flex-1 truncate text-left" style={{ color: colourOf(p) }}>
              {p.name}
            </span>
            <span className="score-figure">{p.score}</span>
          </div>
        ))}
      </div>

      <Button size="xl" onClick={leaveToStart}>
        Play again
      </Button>
    </Stage>
  )
}
