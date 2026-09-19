import { useEffect } from 'react'
import { BrowserRouter, Navigate, Route, Routes, useNavigate } from 'react-router-dom'
import { AnimatePresence, motion } from 'motion/react'
import { Home } from './features/entry/Home'
import { GameView } from './features/game/GameView'
import { Waiting } from './design/Loader'
import { useGame } from './store/gameStore'

export default function App() {
  useVisibleErrors()

  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<Home />} />
        <Route path="/room" element={<Room />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
      <ConnectionBanner />
      <Toasts />
    </BrowserRouter>
  )
}

/**
 * Surfaces anything that blows up in a handler.
 *
 * A thrown handler is invisible: the tap does nothing, no error reaches the
 * server, and there is no console to read because the failure is on someone
 * else's phone. That is exactly how a secure-context-only API silently broke
 * question submission over LAN. Better a toast than a dead button.
 */
function useVisibleErrors() {
  const report = useGame((s) => s.reportError)

  useEffect(() => {
    const onError = (e: ErrorEvent) => report(e.message)
    const onRejection = (e: PromiseRejectionEvent) => report(String(e.reason?.message ?? e.reason))

    window.addEventListener('error', onError)
    window.addEventListener('unhandledrejection', onRejection)

    return () => {
      window.removeEventListener('error', onError)
      window.removeEventListener('unhandledrejection', onRejection)
    }
  }, [report])
}

function Room() {
  const { room, session, status, restore, connect } = useGame()
  const navigate = useNavigate()

  // Survives a refresh: the token in sessionStorage is enough to walk straight
  // back into the room you were already in.
  useEffect(() => {
    if (session || status !== 'idle') return

    const saved = restore()
    if (saved) {
      connect(saved).catch(() => navigate('/'))
    } else {
      navigate('/')
    }
  }, [session, status, restore, connect, navigate])

  if (!room) {
    return (
      <div className="flex min-h-dvh items-center justify-center">
        <Waiting title="Connecting…" subtitle="Finding your room." />
      </div>
    )
  }

  // One view for everyone. Whoever made the room is playing in it, so there
  // is nothing left for a second layout to be.
  return <GameView room={room} />
}

/**
 * §9.5: a dropped connection must never look like the game died. The banner is
 * deliberately calm — the server holds the room open and a reconnect resyncs.
 */
function ConnectionBanner() {
  const status = useGame((s) => s.status)

  if (status !== 'reconnecting' && status !== 'closed') return null

  return (
    <motion.div
      initial={{ y: -60 }}
      animate={{ y: 0 }}
      className="fixed inset-x-0 top-0 z-50 bg-dare-deep px-4 py-2 text-center text-sm font-bold text-dare"
    >
      {status === 'reconnecting'
        ? 'Reconnecting… hang tight, your spot is safe.'
        : 'Disconnected. Refresh to rejoin.'}
    </motion.div>
  )
}

function Toasts() {
  const cues = useGame((s) => s.cues)
  const consume = useGame((s) => s.consumeCue)
  const visible = cues.filter((c) => c.kind === 'error' || c.kind === 'poolLow')

  // Keyed on ids, not the array: `visible` is a fresh array every render, which
  // would reset every dismissal timer on each incoming state push — and state
  // pushes are constant during a game, so toasts would never go away.
  const ids = visible.map((c) => c.id).join(',')

  useEffect(() => {
    if (!ids) return
    const timers = ids.split(',').map((id) => setTimeout(() => consume(Number(id)), 3500))
    return () => timers.forEach(clearTimeout)
  }, [ids, consume])

  return (
    <div className="pointer-events-none fixed inset-x-0 bottom-6 z-50 flex flex-col items-center gap-2 px-4">
      <AnimatePresence>
        {visible.map((cue) => (
          <motion.div
            key={cue.id}
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: 10 }}
            className="rounded-2xl border-2 border-edge bg-stage-raised px-5 py-3 text-center shadow-xl"
          >
            {cue.message}
          </motion.div>
        ))}
      </AnimatePresence>
    </div>
  )
}
