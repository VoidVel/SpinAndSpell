import { motion } from 'motion/react'
import type { RoomView } from '../../realtime/types'
import { Button, Card, Eyebrow, PlayerChip } from '../../design/primitives'
import { colourOf } from '../../design/colours'
import { useSend } from '../../store/gameStore'
import { Compose } from './Compose'
import { OwnerWaiting } from './OwnerWaiting'

export function Scoreboard({ room }: { room: RoomView }) {
  const send = useSend()
  const ranked = [...room.players].sort((a, b) => b.score - a.score)
  const leading = ranked[0]?.score ?? 0
  const dry = room.questionsInPool <= room.settings.handSize

  return (
    <div className="flex flex-1 flex-col gap-4">
      <div className="flex flex-col items-center gap-1">
        <Eyebrow>After {room.roundsPlayed} rounds</Eyebrow>
        <h1 className="font-display text-4xl font-extrabold">Scores</h1>
      </div>

      <div className="flex flex-col gap-2">
        {ranked.map((p, i) => {
          const top = p.score === leading && leading > 0
          return (
            <motion.div
              key={p.id}
              layout
              transition={{ type: 'spring', stiffness: 300, damping: 26 }}
              className="flex items-center gap-3 rounded-2xl px-4 py-3"
              style={{
                background: top ? 'var(--color-gold-deep)' : 'var(--color-stage-raised)',
                boxShadow: top ? '0 0 32px var(--color-gold-glow)' : 'none',
                outline: p.id === room.youId ? '2px solid var(--color-edge)' : undefined,
              }}
            >
              <span className="w-5 tabular-nums text-ink-dim">{i + 1}</span>
              <PlayerChip player={p} crowned={top && i === 0} />
              <span className="flex-1 truncate text-left text-xl font-bold" style={{ color: colourOf(p) }}>
                {p.name}
              </span>
              <span className="score-figure text-2xl">{p.score}</span>
            </motion.div>
          )
        })}
      </div>

      {/* Topping up between rounds is what keeps a long session from running dry. */}
      {dry && (
        <Card className="text-left">
          <p className="mb-3 font-display text-lg font-extrabold text-gold">
            {room.questionsInPool === 0
              ? 'The pot is empty — everyone add a few more'
              : `Only ${room.questionsInPool} left in the pot`}
          </p>
          <Compose room={room} compact />
        </Card>
      )}

      <div className="sticky bottom-0 -mx-5 mt-auto flex flex-col items-center gap-2 bg-stage/95 px-5 pt-3 pb-1 backdrop-blur">
        {room.youAreOwner ? (
          <>
            <Button size="xl" full onClick={() => send('NextRound')} disabled={room.questionsInPool === 0}>
              Next round
            </Button>
            {room.questionsInPool === 0 ? (
              <span className="text-center text-sm text-ink-dim">
                Out of questions — everyone add a few more.
              </span>
            ) : (
              <button
                onClick={() => send('EndGame')}
                className="text-sm text-ink-dim underline underline-offset-4"
              >
                End the game
              </button>
            )}
          </>
        ) : (
          <OwnerWaiting room={room} verb="spin again" />
        )}
      </div>
    </div>
  )
}
