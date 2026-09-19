import { AnimatePresence, motion } from 'motion/react'
import { QRCodeSVG } from 'qrcode.react'
import type { QuestionMode, RoomView } from '../../realtime/types'
import { Button, Eyebrow, PlayerChip, RoomCode } from '../../design/primitives'
import { colourOf } from '../../design/colours'
import { MODE_LABELS, kindStyle, kindsFor } from '../../design/kinds'
import { Motif } from '../../design/Motifs'
import { useSend } from '../../store/gameStore'
import { OwnerWaiting } from './OwnerWaiting'

/**
 * The room filling up.
 *
 * The code and the QR are on everyone's phone, not just the owner's: whoever is
 * standing next to the person still deciding whether to join is the one who
 * should be able to show them the code.
 */
export function Lobby({ room }: { room: RoomView }) {
  const send = useSend()
  const joinUrl = `${window.location.origin}/?join=${room.code}`

  return (
    <div className="flex flex-1 flex-col gap-6">
      <div className="flex flex-col items-center gap-4">
        <Eyebrow>Join at this code</Eyebrow>
        <RoomCode code={room.code} />

        {/* Scanning beats typing a code and a name. Time-to-first-challenge is
            one of the four fun-signals in §11, and this is the cheapest minute
            to win back. */}
        <div className="rounded-2xl bg-ink p-2.5">
          <QRCodeSVG value={joinUrl} size={112} bgColor="#f4f0ff" fgColor="#140b2e" level="M" />
        </div>
      </div>

      <Roster room={room} />

      <ModePicker
        mode={room.settings.mode}
        onMode={(mode) => send('SetQuestionMode', mode)}
        readOnly={!room.youAreOwner}
      />

      <div className="mt-auto flex flex-col items-center gap-3 pt-2">
        {room.youAreOwner ? (
          <>
            <Button size="xl" full onClick={() => send('OpenSubmissions')} disabled={!!room.startBlockedReason}>
              Start writing questions
            </Button>
            {/* §7: disabled with a reason, never just greyed out. */}
            {room.startBlockedReason && (
              <span className="text-center text-ink-dim">{room.startBlockedReason}</span>
            )}
          </>
        ) : (
          <OwnerWaiting room={room} verb="kick things off" />
        )}
      </div>
    </div>
  )
}

/** Everyone in the room, including you and including whoever started it. */
function Roster({ room }: { room: RoomView }) {
  const emptySeats = Math.max(0, 3 - room.players.length)

  return (
    <div className="flex min-h-28 flex-wrap items-start justify-center gap-4">
      <AnimatePresence mode="popLayout">
        {room.players.map((p) => (
          <motion.div
            key={p.id}
            layout
            initial={{ scale: 0, opacity: 0, y: 16 }}
            animate={{ scale: 1, opacity: 1, y: 0 }}
            exit={{ scale: 0, opacity: 0 }}
            transition={{ type: 'spring', stiffness: 400, damping: 22 }}
            className="flex w-20 flex-col items-center gap-1.5"
          >
            <PlayerChip player={p} size="lg" />
            <span className="w-full truncate text-center font-bold" style={{ color: colourOf(p) }}>
              {p.name}
            </span>
          </motion.div>
        ))}
      </AnimatePresence>

      {/* Empty seats read as "room for more" — warmer than a line of text. */}
      {Array.from({ length: emptySeats }, (_, i) => (
        <motion.div
          key={`seat-${i}`}
          layout
          className="flex w-20 flex-col items-center gap-1.5"
          animate={{ opacity: [0.15, 0.32, 0.15] }}
          transition={{ duration: 2.4, repeat: Infinity, delay: i * 0.25 }}
        >
          <span className="h-20 w-20 rounded-full border-4 border-dashed border-ink-dim" />
          <span className="text-ink-dim">…</span>
        </motion.div>
      ))}
    </div>
  )
}

/**
 * Truths, dares, or both. The owner picks; everyone else sees what was picked,
 * because the engine locks the setting the moment submissions open and a room
 * full of people writing to the wrong brief is the failure this prevents.
 */
function ModePicker({
  mode,
  onMode,
  readOnly,
}: {
  mode: QuestionMode
  onMode: (mode: QuestionMode) => void
  readOnly: boolean
}) {
  const options: QuestionMode[] = readOnly ? [mode] : ['Both', 'TruthsOnly', 'DaresOnly']

  return (
    <div className="flex flex-col items-center gap-2">
      <Eyebrow>Tonight we play</Eyebrow>

      <div className="flex flex-wrap justify-center gap-2 rounded-2xl border-2 border-edge bg-stage-sunken p-2">
        {options.map((option) => {
          const on = option === mode
          const kinds = kindsFor(option)
          // A single-kind mode takes that kind's colour; "both" stays gold,
          // because gold is the app's own accent rather than a challenge type.
          const accent = kinds.length === 1 ? kindStyle(kinds[0]) : null

          const style = {
            background: on ? (accent?.deep ?? 'var(--color-gold-deep)') : 'transparent',
            color: on ? (accent?.colour ?? 'var(--color-gold)') : 'var(--color-ink-dim)',
            boxShadow: on ? `0 0 26px ${accent?.glow ?? 'var(--color-gold-glow)'}` : 'none',
          }

          const label = (
            <>
              {kinds.map((k) => (
                <Motif key={k} kind={k} className="h-5 w-5" />
              ))}
              {MODE_LABELS[option]}
            </>
          )

          const className =
            'flex min-h-12 items-center gap-2 rounded-xl px-4 font-display font-extrabold'

          return readOnly ? (
            <span key={option} className={className} style={style}>
              {label}
            </span>
          ) : (
            <button
              key={option}
              onClick={() => onMode(option)}
              className={`${className} transition active:scale-[0.97]`}
              style={style}
            >
              {label}
            </button>
          )
        })}
      </div>
    </div>
  )
}
