import type { RoomView } from '../../realtime/types'
import { colourOf } from '../../design/colours'
import { Crown, Star } from '../../design/Motifs'
import { PlayerChip } from '../../design/primitives'

/**
 * Who you are, where you are, what you have. It stays put through every phase
 * so your score ticking up is something you can watch happen rather than
 * something you find out about on the scoreboard.
 */
export function Header({ room }: { room: RoomView }) {
  const you = room.players.find((p) => p.id === room.youId)

  return (
    <header className="mb-5 flex items-center gap-3">
      {you && <PlayerChip player={you} />}

      <div className="flex min-w-0 flex-col text-left">
        <span className="flex items-center gap-1.5">
          <span
            className="truncate text-lg font-bold"
            style={{ color: you ? colourOf(you) : undefined }}
          >
            {you?.name}
          </span>
          {/* The crown says who paces the room, not who outranks you. */}
          {room.youAreOwner && (
            <span className="shrink-0" title="You started this room">
              <Crown className="h-4 w-4 text-gold" />
            </span>
          )}
        </span>
        <span className="text-sm text-ink-dim">Room {room.code}</span>
      </div>

      <span className="ml-auto flex items-center gap-1.5">
        <Star className="h-5 w-5 text-gold" />
        <span className="score-figure text-2xl">{you?.score ?? 0}</span>
      </span>
    </header>
  )
}
