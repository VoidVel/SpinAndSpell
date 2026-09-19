import { AnimatePresence, motion } from 'motion/react'
import type { RoomView } from '../../realtime/types'
import { PhoneShell } from '../../design/primitives'
import { Waiting } from '../../design/Loader'
import { Celebration } from '../round/Celebration'
import { Header } from './Header'
import { Lobby } from './Lobby'
import { Writing } from './Writing'
import { Spinning } from './Spinning'
import { Choosing } from './Choosing'
import { Reveal } from './Reveal'
import { Scoreboard } from './Scoreboard'
import { Results } from './Results'

/**
 * One view for everyone in the room.
 *
 * There is no host screen and no spectator: whoever made the room is playing
 * too, so the only thing that varies between two phones is what this round
 * asked of the person holding it. Every screen below reads that off the
 * projection rather than off who you are.
 */
export function GameView({ room }: { room: RoomView }) {
  return (
    <PhoneShell>
      <Header room={room} />

      <AnimatePresence mode="wait">
        <motion.div
          key={sceneKey(room)}
          initial={{ opacity: 0, x: 24 }}
          animate={{ opacity: 1, x: 0 }}
          exit={{ opacity: 0, x: -24 }}
          // §6.3: 200-300ms, consistent direction of travel.
          transition={{ duration: 0.22, ease: [0.2, 0.9, 0.3, 1] }}
          className="flex flex-1 flex-col"
        >
          <Scene room={room} />
        </motion.div>
      </AnimatePresence>
    </PhoneShell>
  )
}

/**
 * Keyed on the phase *and* your part in it, so the transition replays when the
 * spin hands you a role — the screen changing under you is the cue that it is
 * suddenly your turn.
 */
function sceneKey(room: RoomView): string {
  return `${room.phase}:${room.youAreChallenger}:${room.youAreTarget}`
}

function Scene({ room }: { room: RoomView }) {
  switch (room.phase) {
    case 'Lobby':
      return <Lobby room={room} />

    case 'Submission':
      return <Writing room={room} />

    case 'Spinning':
      return <Spinning room={room} />

    case 'Choosing':
      return <Choosing room={room} />

    case 'Revealed':
    case 'Judging':
      return <Reveal room={room} />

    case 'Celebration':
      return <Celebrate room={room} />

    case 'Scoreboard':
      return <Scoreboard room={room} />

    case 'Ended':
      return <Results room={room} />
  }
}

/** The burst, in everyone's hand at once — it used to be the big screen's. */
function Celebrate({ room }: { room: RoomView }) {
  const target = room.players.find((p) => p.id === room.round?.targetId)

  if (!target) {
    return <Waiting scale={0.6} title="Nice" subtitle="Scores coming up." />
  }

  return (
    <div className="flex flex-1 items-center justify-center">
      <Celebration player={target} outcome={room.round?.outcome ?? null} />
    </div>
  )
}
