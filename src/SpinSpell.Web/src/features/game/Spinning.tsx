import type { RoomView } from '../../realtime/types'
import { Stage } from '../../design/primitives'
import { Spin } from '../round/Spin'

/**
 * The signature moment (§6.3), now in everyone's hand rather than on a screen
 * across the room.
 *
 * The reel animates *toward* a result the server already decided, and the phase
 * advances on a server timer — so a phone that locks mid-spin rejoins in the
 * right place instead of desyncing.
 */
export function Spinning({ room }: { room: RoomView }) {
  if (!room.round) return null

  return (
    <Stage>
      <Spin
        key={room.round.index}
        players={room.players}
        challengerId={room.round.challengerId}
        targetId={room.round.targetId}
        animationMs={remainingSpinMs(room)}
      />
    </Stage>
  )
}

/**
 * How long the reel has left, taken from the server's own deadline rather than
 * from the SpinResolved event. That way a phone that reloads mid-spin picks the
 * animation up part-way and still lands exactly when the server advances the
 * phase, instead of staring at a blank stage because the one-shot event is gone.
 */
function remainingSpinMs(room: RoomView): number {
  if (!room.deadline) return 3200
  const left = new Date(room.deadline).getTime() - new Date(room.serverTime).getTime()
  return Math.max(400, left)
}
