import { useState } from 'react'
import { motion } from 'motion/react'
import type { QuestionView, RoomView } from '../../realtime/types'
import { ChallengeCard, KindBadge } from '../../design/primitives'
import { Waiting } from '../../design/Loader'
import { useSend } from '../../store/gameStore'

export function Choosing({ room }: { room: RoomView }) {
  const send = useSend()

  if (room.youAreChallenger) {
    return <Hand hand={room.yourHand} onPick={(id) => send('ChooseQuestion', id)} />
  }

  const challenger = room.players.find((p) => p.id === room.round?.challengerId)

  return (
    <Waiting
      scale={0.6}
      title={`${challenger?.name ?? 'Someone'} is choosing…`}
      subtitle={
        room.youAreTarget ? 'Picking your fate from three cards.' : 'Hold tight.'
      }
    />
  )
}

/** §7: private to the challenger. Nobody else is ever sent these. */
function Hand({ hand, onPick }: { hand: QuestionView[]; onPick: (id: string) => void }) {
  const [picked, setPicked] = useState<string | null>(null)

  return (
    <div className="flex flex-1 flex-col gap-4">
      <div>
        <h1 className="font-display text-3xl font-extrabold">Your hand</h1>
        <p className="text-ink-dim">Pick one. Only you can see these.</p>
      </div>

      <div className="flex flex-col gap-4">
        {hand.map((q, i) => (
          <motion.button
            key={q.id}
            // Dealt, not listed: each card lands with a slight angle, in turn.
            initial={{ opacity: 0, y: 60, rotate: i % 2 === 0 ? -4 : 3 }}
            animate={{
              opacity: picked && picked !== q.id ? 0.25 : 1,
              y: 0,
              rotate: picked === q.id ? 0 : i % 2 === 0 ? -1.5 : 1.5,
              scale: picked === q.id ? 1.03 : 1,
            }}
            transition={{
              delay: picked ? 0 : i * 0.09,
              type: 'spring',
              stiffness: 260,
              damping: 22,
            }}
            disabled={picked !== null}
            onClick={() => {
              setPicked(q.id)
              onPick(q.id)
            }}
            className="text-left active:scale-[0.98]"
          >
            <ChallengeCard kind={q.kind} className="p-5">
              <KindBadge kind={q.kind} />
              <span className="mt-3 block text-xl font-bold">{q.text}</span>
            </ChallengeCard>
          </motion.button>
        ))}
      </div>

      {hand.length === 0 && <Waiting scale={0.6} title="No cards" subtitle="The pool ran dry." />}
    </div>
  )
}
