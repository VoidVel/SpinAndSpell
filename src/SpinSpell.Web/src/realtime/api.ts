import type { Session } from './types'

/**
 * Same origin by default. In dev, Vite proxies /api and /hub to the API; in a
 * container, nginx does. Either way a phone on the wifi only ever talks to the
 * one port it already loaded the page from — no LAN IP to guess, no second
 * firewall exception to grant before a playtest can start.
 *
 * Set VITE_API_URL only if the API genuinely lives on another host.
 */
export const API_URL: string = import.meta.env.VITE_API_URL ?? ''

export class ApiError extends Error {
  readonly code: string

  constructor(code: string, message: string) {
    super(message)
    this.code = code
  }
}

async function post<T>(path: string, body: unknown): Promise<T> {
  let response: Response

  try {
    response = await fetch(`${API_URL}${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
  } catch {
    // Almost always a cold start on a sleeping free-tier instance (§10 of the
    // development plan). Say something human rather than "Failed to fetch".
    throw new ApiError('offline', "Can't reach the game right now. Give it a moment and try again.")
  }

  const payload = await response.json().catch(() => null)

  if (!response.ok) {
    throw new ApiError(payload?.code ?? 'error', payload?.message ?? 'Something went wrong.')
  }

  return payload as T
}

export interface CreateRoomOptions {
  minQuestionsPerPlayer?: number
  timerEnabled?: boolean
}

/**
 * The creator joins their own room as an ordinary player, so this takes their
 * real name — there is nobody sitting out to be called "Host".
 */
export const createRoom = (name: string, options: CreateRoomOptions = {}) =>
  post<Session>('/api/rooms', {
    name,
    minQuestionsPerPlayer: options.minQuestionsPerPlayer ?? 4,
    timerEnabled: options.timerEnabled ?? false,
  })

export const joinRoom = (code: string, name: string) =>
  post<Session>(`/api/rooms/${code.toUpperCase()}/join`, { name })

export interface RoomSummary {
  code: string
  phase: string
  players: number
  accepting: boolean
}

export async function lookupRoom(code: string): Promise<RoomSummary | null> {
  try {
    const response = await fetch(`${API_URL}/api/rooms/${code.toUpperCase()}`)
    return response.ok ? ((await response.json()) as RoomSummary) : null
  } catch {
    return null
  }
}

const SESSION_KEY = 'spinspell.session'

/**
 * sessionStorage, not localStorage: a token is scoped to one room and one
 * evening, and a stale one on a shared laptop is pure confusion.
 */
export const saveSession = (session: Session) =>
  sessionStorage.setItem(SESSION_KEY, JSON.stringify(session))

export function loadSession(): Session | null {
  const raw = sessionStorage.getItem(SESSION_KEY)
  if (!raw) return null
  try {
    return JSON.parse(raw) as Session
  } catch {
    return null
  }
}

export const clearSession = () => sessionStorage.removeItem(SESSION_KEY)
