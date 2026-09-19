import { useEffect, useState } from 'react'

/**
 * §9.4: a soft timer that adds energy without punishing anyone. It never ends
 * the round on its own — the server does that, and the challenger can call the
 * round at any point regardless of what this shows.
 *
 * Offsets against the server clock so a phone with a skewed clock does not show
 * a different number from the phone next to it.
 */
export function Countdown({ deadline, serverTime }: { deadline: string; serverTime: string }) {
  const [remaining, setRemaining] = useState(0)

  useEffect(() => {
    const skew = Date.now() - new Date(serverTime).getTime()
    const endsAt = new Date(deadline).getTime()

    const tick = () => setRemaining(Math.max(0, Math.round((endsAt + skew - Date.now()) / 1000)))

    tick()
    const id = setInterval(tick, 250)
    return () => clearInterval(id)
  }, [deadline, serverTime])

  const urgent = remaining <= 10

  return (
    <div
      className={`font-display text-5xl font-extrabold tabular-nums transition-colors ${
        urgent ? 'text-dare' : 'text-ink-dim'
      }`}
      style={urgent ? { animation: 'pulse 1s ease-in-out infinite' } : undefined}
      aria-live="off"
    >
      {remaining}s
    </div>
  )
}
