import { useEffect, useRef, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { motion } from 'motion/react'
import { ApiError, createRoom, joinRoom } from '../../realtime/api'
import { Button, PhoneShell } from '../../design/primitives'
import { DareBolt, TruthBubble } from '../../design/Motifs'
import { Waiting } from '../../design/Loader'
import { useGame } from '../../store/gameStore'

type Mode = 'choose' | 'join' | 'creating'

const CODE_LENGTH = 4

/** §7: zero friction. Two buttons, no signup wall. */
export function Home() {
  const [params] = useSearchParams()
  const scannedCode = (params.get('join') ?? '').toUpperCase().slice(0, CODE_LENGTH)

  // Arriving from a scanned QR skips the menu entirely — code already filled,
  // one field left to complete.
  const [mode, setMode] = useState<Mode>(scannedCode ? 'join' : 'choose')
  const [name, setName] = useState('')
  const [code, setCode] = useState(scannedCode)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const nameRef = useRef<HTMLInputElement>(null)
  const connect = useGame((s) => s.connect)
  const navigate = useNavigate()

  useEffect(() => {
    if (scannedCode) nameRef.current?.focus()
  }, [scannedCode])

  const run = async (work: () => Promise<void>) => {
    setBusy(true)
    setError(null)
    try {
      await work()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Something went wrong.')
      setMode(scannedCode || code ? 'join' : 'choose')
    } finally {
      setBusy(false)
    }
  }

  const host = () =>
    run(async () => {
      setMode('creating')
      const session = await createRoom({ hostName: 'Host' })
      await connect(session)
      navigate('/room')
    })

  const join = (e: React.FormEvent) => {
    e.preventDefault()
    run(async () => {
      const session = await joinRoom(code.trim(), name.trim())
      await connect(session)
      navigate('/room')
    })
  }

  if (mode === 'creating') {
    return (
      <PhoneShell>
        {/* §10 of the plan: a cold start can cost 30-60s. Give it personality. */}
        <Waiting title="Warming up the stage…" subtitle="Getting your room ready." />
      </PhoneShell>
    )
  }

  return (
    <PhoneShell>
      <div className="flex flex-1 flex-col items-center justify-center gap-10 text-center">
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          className="flex flex-col items-center gap-4"
        >
          <div className="flex items-center gap-3">
            <TruthBubble className="h-9 w-9 text-truth" />
            <DareBolt className="h-9 w-9 text-dare" />
          </div>
          <h1 className="font-display text-6xl font-extrabold tracking-tight">
            Spin<span className="text-gold">Spell</span>
          </h1>
          <p className="text-lg text-ink-dim">Your friends. Your dares. Your game.</p>
        </motion.div>

        {mode === 'choose' && (
          <div className="flex w-full flex-col gap-3">
            <Button size="xl" full onClick={host} disabled={busy}>
              Host a game
            </Button>
            <Button variant="ghost" size="xl" full onClick={() => setMode('join')}>
              Join a game
            </Button>
          </div>
        )}

        {mode === 'join' && (
          <form onSubmit={join} className="flex w-full flex-col gap-4">
            <CodeInput value={code} onChange={setCode} />

            <input
              ref={nameRef}
              value={name}
              onChange={(e) => setName(e.target.value.slice(0, 20))}
              placeholder="Your name"
              autoComplete="off"
              className="w-full rounded-2xl border-2 border-edge bg-stage-sunken p-4 text-center text-xl outline-none placeholder:text-ink-dim/40 focus:border-ink-dim"
            />

            <Button
              type="submit"
              size="xl"
              full
              disabled={busy || code.length < CODE_LENGTH || !name.trim()}
            >
              {busy ? 'Joining…' : 'Let me in'}
            </Button>

            <button
              type="button"
              onClick={() => setMode('choose')}
              className="text-ink-dim underline underline-offset-4"
            >
              Back
            </button>
          </form>
        )}

        {error && (
          <motion.p initial={{ opacity: 0 }} animate={{ opacity: 1 }} className="text-dare">
            {error}
          </motion.p>
        )}
      </div>
    </PhoneShell>
  )
}

/**
 * One box per character. It reads as a game code rather than a form field, and
 * the boxes make it obvious how many characters are expected — which matters
 * when someone is typing it in from across a noisy room.
 *
 * A single hidden input carries the value so that paste, autofill and the
 * mobile keyboard all behave normally; the boxes are purely presentational.
 */
function CodeInput({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const ref = useRef<HTMLInputElement>(null)

  return (
    <div className="relative">
      <input
        ref={ref}
        value={value}
        onChange={(e) => onChange(e.target.value.toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, CODE_LENGTH))}
        inputMode="text"
        autoCapitalize="characters"
        autoComplete="off"
        autoCorrect="off"
        spellCheck={false}
        aria-label="Room code"
        className="absolute inset-0 h-full w-full opacity-0"
      />

      <div className="pointer-events-none flex justify-center gap-2">
        {Array.from({ length: CODE_LENGTH }, (_, i) => {
          const char = value[i]
          const active = i === value.length
          return (
            <span
              key={i}
              className={`flex h-20 w-16 items-center justify-center rounded-2xl border-2 font-display text-4xl font-extrabold transition ${
                char
                  ? 'border-gold bg-gold-deep text-gold'
                  : active
                    ? 'border-ink-dim bg-stage-sunken'
                    : 'border-edge bg-stage-sunken text-ink-dim'
              }`}
            >
              {char ?? ''}
            </span>
          )
        })}
      </div>
    </div>
  )
}
