import { create } from 'zustand'
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr'
import { API_URL, clearSession, loadSession, saveSession } from '../realtime/api'
import type {
  CommandRejectedEvent,
  RoomView,
  RoundJudgedEvent,
  Session,
  SpinResolvedEvent,
} from '../realtime/types'

export type ConnectionStatus = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'closed'

/** A transient thing the UI should react to once, then forget. */
export interface Cue {
  id: number
  kind: 'spin' | 'judged' | 'error' | 'poolLow' | 'hostChanged'
  spin?: SpinResolvedEvent
  judged?: RoundJudgedEvent
  message?: string
}

interface GameStore {
  session: Session | null
  room: RoomView | null
  status: ConnectionStatus
  cues: Cue[]

  connect: (session: Session) => Promise<void>
  disconnect: () => Promise<void>
  send: (method: string, ...args: unknown[]) => Promise<void>
  consumeCue: (id: number) => void
  reportError: (message: string) => void
  restore: () => Session | null
}

let connection: HubConnection | null = null
let cueId = 0

export const useGame = create<GameStore>((set, get) => {
  const pushCue = (cue: Omit<Cue, 'id'>) =>
    set((s) => ({ cues: [...s.cues, { ...cue, id: ++cueId }] }))

  return {
    session: null,
    room: null,
    status: 'idle',
    cues: [],

    restore: () => {
      const session = loadSession()
      if (session) set({ session })
      return session
    },

    connect: async (session) => {
      if (connection) await get().disconnect()

      saveSession(session)
      set({ session, status: 'connecting' })

      const hub = new HubConnectionBuilder()
        .withUrl(`${API_URL}/hub/game`, { accessTokenFactory: () => session.token })
        // Aggressive early retries: a phone that locked for ten seconds should
        // be back in the game before its owner notices it left.
        .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 15000, 30000])
        .configureLogging(LogLevel.Warning)
        .build()

      // The server is authoritative. Every push replaces the view outright —
      // the client never patches from a base it cannot verify.
      hub.on('state', (room: RoomView) => set({ room }))

      hub.on('spinResolved', (e: SpinResolvedEvent) => pushCue({ kind: 'spin', spin: e }))
      hub.on('roundJudged', (e: RoundJudgedEvent) => pushCue({ kind: 'judged', judged: e }))
      hub.on('commandRejected', (e: CommandRejectedEvent) =>
        pushCue({ kind: 'error', message: e.message }),
      )
      hub.on('questionPoolLow', () =>
        pushCue({ kind: 'poolLow', message: 'Questions are running low — add a few more.' }),
      )
      hub.on('hostChanged', () => pushCue({ kind: 'hostChanged', message: 'The host has changed.' }))

      hub.onreconnecting(() => set({ status: 'reconnecting' }))
      hub.onreconnected(async () => {
        set({ status: 'connected' })
        // Anything could have happened while we were away. Start from scratch.
        await hub.invoke('Resync').catch(() => undefined)
      })
      hub.onclose(() => set({ status: 'closed' }))

      connection = hub
      await hub.start()
      set({ status: 'connected' })
    },

    disconnect: async () => {
      const hub = connection
      connection = null
      set({ status: 'idle', room: null, session: null })
      clearSession()
      if (hub) await hub.stop().catch(() => undefined)
    },

    send: async (method, ...args) => {
      if (connection?.state !== HubConnectionState.Connected) {
        pushCue({ kind: 'error', message: 'Still reconnecting — hang on a sec.' })
        return
      }

      try {
        await connection.invoke(method, ...args)
      } catch (error) {
        pushCue({ kind: 'error', message: (error as Error).message ?? 'That did not go through.' })
      }
    },

    consumeCue: (id) => set((s) => ({ cues: s.cues.filter((c) => c.id !== id) })),

    reportError: (message) => pushCue({ kind: 'error', message }),
  }
})

/**
 * Drops the finished session before going home. Without this the old token
 * lingers in sessionStorage and a refresh tries to walk back into a room that
 * has already ended.
 */
export async function leaveToStart() {
  await useGame.getState().disconnect()
  window.location.assign('/')
}

/** Narrow selectors, so a scoreboard tick does not re-render the whole stage. */
export const useRoom = () => useGame((s) => s.room)
export const useStatus = () => useGame((s) => s.status)
export const useSend = () => useGame((s) => s.send)
