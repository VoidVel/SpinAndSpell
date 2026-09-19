import type { RoomView } from '../../realtime/types'
import { colourOf } from '../../design/colours'

/**
 * What everyone who is not the owner sees where the owner sees a button.
 *
 * Naming them matters: "waiting for the host" is a dead end, but "waiting for
 * Ana" is a thing you can solve by looking up and saying her name.
 */
export function OwnerWaiting({ room, verb }: { room: RoomView; verb: string }) {
  const owner = room.players.find((p) => p.isOwner)

  return (
    <p className="text-center text-ink-dim">
      Waiting for{' '}
      <span className="font-bold" style={{ color: owner ? colourOf(owner) : undefined }}>
        {owner?.name ?? 'whoever started the room'}
      </span>{' '}
      to {verb}.
    </p>
  )
}
