import { motion } from 'motion/react'
import type { PlayerView, RoomView } from '../../realtime/types'
import { Button, ChallengeCard, KindBadge, Stage } from '../../design/primitives'
import { colourOf } from '../../design/colours'
import { kindStyle } from '../../design/kinds'
import { useSend } from '../../store/gameStore'
import { Countdown } from '../shared/Countdown'

/**
 * The challenge, live.
 *
 * Three people are looking at three different things here: the target needs to
 * know it is them, the challenger needs the two verdict buttons, and everyone
 * else needs to be able to read along. Same round, same card, one component.
 */
export function Reveal({ room }: { room: RoomView }) {
  const round = room.round
  const target = room.players.find((p) => p.id === round?.targetId)
  const challenger = room.players.find((p) => p.id === round?.challengerId)

  if (!round?.text || !round.kind || !target) return null

  if (room.youAreTarget) {
    return <YourTurn room={room} />
  }

  // The engine lets the owner close a round whose challenger has dropped —
  // nothing else can, and a locked phone in a pocket would otherwise end the
  // party. Mirror that here or the rule is unreachable in practice.
  const challengerGone = !challenger?.connected
  const standingIn = !room.youAreChallenger && room.youAreOwner && challengerGone

  return (
    <Stage className="gap-5">
      <div className="flex flex-col items-center gap-3">
        <KindBadge kind={round.kind} large />
        <span className="stage-subtitle" style={{ color: colourOf(target) }}>
          {target.name}, you're up
        </span>
      </div>

      {/* The emotional peak of the loop (§6.3) — so it gets the whole screen. */}
      <motion.div
        initial={{ rotateX: -70, opacity: 0, scale: 0.9 }}
        animate={{ rotateX: 0, opacity: 1, scale: 1 }}
        transition={{ type: 'spring', stiffness: 180, damping: 20 }}
        className="w-full"
        style={{ perspective: 1200 }}
      >
        <ChallengeCard kind={round.kind} className="px-6 py-8">
          <p className="font-display text-3xl leading-tight font-extrabold">{round.text}</p>
        </ChallengeCard>
      </motion.div>

      {room.deadline && <Countdown deadline={room.deadline} serverTime={room.serverTime} />}

      {room.youAreChallenger || standingIn ? (
        <Verdict target={target} standingInFor={standingIn ? challenger : undefined} />
      ) : (
        <Watching target={target} challenger={challenger} />
      )}
    </Stage>
  )
}

/**
 * You picked the card, so you call it. §7 wants two big buttons and no
 * confirmation dialog: pacing beats safety here, and a misjudged round costs
 * exactly one point.
 */
function Verdict({
  target,
  standingInFor,
}: {
  target: PlayerView
  standingInFor?: PlayerView
}) {
  const send = useSend()

  return (
    <div className="flex w-full flex-col gap-3">
      <span className="text-sm text-ink-dim">
        {standingInFor
          ? `${standingInFor.name} dropped — your call instead.`
          : `Your call — did ${target.name} do it?`}
      </span>

      <div className="flex w-full gap-3">
        <Button variant="quiet" size="xl" full onClick={() => send('Judge', 'Skipped')}>
          Skip
        </Button>
        <Button size="xl" full onClick={() => send('Judge', 'Success')}>
          Nailed it
        </Button>
      </div>
    </div>
  )
}

/** Everyone else: read along, watch the room, wait for the verdict. */
function Watching({ target, challenger }: { target: PlayerView; challenger?: PlayerView }) {
  return (
    <div className="flex flex-col items-center gap-1">
      <motion.span
        animate={{ opacity: [0.5, 1, 0.5] }}
        transition={{ duration: 2.2, repeat: Infinity }}
        className="text-ink-dim"
      >
        Eyes on {target.name}…
      </motion.span>
      {challenger && (
        <span className="text-sm text-ink-dim/70">{challenger.name} calls this one.</span>
      )}
    </div>
  )
}

/**
 * The target's own phone goes full-bleed in the challenge colour — you should
 * know it is your turn from the glow in your hand, before you read a word.
 */
function YourTurn({ room }: { room: RoomView }) {
  const round = room.round!
  const s = kindStyle(round.kind!)

  return (
    <Stage glow={s.deep}>
      <KindBadge kind={round.kind!} large />

      <motion.span
        animate={{ scale: [1, 1.05, 1] }}
        transition={{ duration: 1.6, repeat: Infinity }}
        className="font-display text-5xl font-extrabold"
        style={{ color: s.colour, textShadow: `0 0 40px ${s.glow}` }}
      >
        You're up
      </motion.span>

      <p className="text-2xl font-bold">{round.text}</p>

      {room.deadline && <Countdown deadline={room.deadline} serverTime={room.serverTime} />}
    </Stage>
  )
}
