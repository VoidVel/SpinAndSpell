import { motion } from 'motion/react'
import type { RoomView } from '../../realtime/types'
import { Button, PlayerChip } from '../../design/primitives'
import { colourOf } from '../../design/colours'
import { useSend } from '../../store/gameStore'
import { Compose } from './Compose'
import { OwnerWaiting } from './OwnerWaiting'

/**
 * Everyone writing at once.
 *
 * The progress strip used to live on the shared screen, which meant the people
 * who were actually late writing their questions were the only ones who could
 * not see it. It is on every phone now.
 */
export function Writing({ room }: { room: RoomView }) {
  const send = useSend()

  return (
    <div className="flex flex-1 flex-col gap-5">
      <Compose room={room} />

      <Progress room={room} />

      {/* Sticky, because the composer above is a long scroll and the room
          should never have to hunt for the button that starts the game. */}
      <div className="sticky bottom-0 -mx-5 mt-auto flex flex-col items-center gap-2 bg-stage/95 px-5 pt-3 pb-1 backdrop-blur">
        {room.youAreOwner ? (
          <>
            <Button size="xl" full onClick={() => send('StartGame')} disabled={!!room.startBlockedReason}>
              Let's play
            </Button>
            {room.startBlockedReason && (
              <span className="text-center text-sm text-ink-dim">{room.startBlockedReason}</span>
            )}
          </>
        ) : (
          <OwnerWaiting room={room} verb="start the game" />
        )}
      </div>
    </div>
  )
}

function Progress({ room }: { room: RoomView }) {
  const target = room.settings.minQuestionsPerPlayer
  const pot = room.players.reduce((sum, p) => sum + p.questionsSubmitted, 0)

  return (
    <div className="flex flex-col gap-3 rounded-3xl border-2 border-edge bg-stage-raised/60 p-4">
      <div className="flex items-baseline justify-between">
        <span className="font-display text-lg font-extrabold">Who's written what</span>
        <span className="score-figure text-lg">{pot} in the pot</span>
      </div>

      {room.players.map((p) => (
        <div key={p.id} className="flex items-center gap-3">
          <PlayerChip player={p} size="sm" />
          <span className="w-20 truncate text-left font-bold" style={{ color: colourOf(p) }}>
            {p.name}
          </span>

          <div className="relative h-3 flex-1 overflow-hidden rounded-full bg-stage-sunken ring-1 ring-edge">
            <motion.div
              className="h-full rounded-full"
              style={{
                background: p.meetsMinimum ? 'var(--color-gold)' : colourOf(p),
                boxShadow: p.meetsMinimum ? '0 0 16px var(--color-gold-glow)' : 'none',
              }}
              animate={{ width: `${Math.min(100, (p.questionsSubmitted / target) * 100)}%` }}
              transition={{ type: 'spring', stiffness: 220, damping: 26 }}
            />
          </div>

          <span className="w-10 text-right">
            {p.meetsMinimum ? (
              <motion.span
                initial={{ scale: 0, rotate: -30 }}
                animate={{ scale: 1, rotate: 0 }}
                transition={{ type: 'spring', stiffness: 500, damping: 14 }}
                className="inline-block text-gold"
              >
                ✓
              </motion.span>
            ) : (
              <span className="text-sm tabular-nums text-ink-dim">
                {p.questionsSubmitted}/{target}
              </span>
            )}
          </span>
        </div>
      ))}
    </div>
  )
}
