/** Mirrors SpinSpell.Server.Projections — the only shape the client ever trusts. */

export type Phase =
  | 'Lobby'
  | 'Submission'
  | 'Spinning'
  | 'Choosing'
  | 'Revealed'
  | 'Judging'
  | 'Celebration'
  | 'Scoreboard'
  | 'Ended'

export type QuestionKind = 'Truth' | 'Dare'
export type RoundOutcome = 'Success' | 'Skipped'

/** What the host lets the room write. Chosen in the lobby, then locked. */
export type QuestionMode = 'Both' | 'TruthsOnly' | 'DaresOnly'

export interface PlayerView {
  id: string
  name: string
  colourSlot: number
  isHost: boolean
  score: number
  connected: boolean
  questionsSubmitted: number
  meetsMinimum: boolean
}

export interface QuestionView {
  id: string
  kind: QuestionKind
  text: string
}

export interface RoundView {
  index: number
  challengerId: string
  targetId: string
  kind: QuestionKind | null
  text: string | null
  outcome: RoundOutcome | null
}

export interface SettingsView {
  minQuestionsPerPlayer: number
  timerEnabled: boolean
  revealTimerSeconds: number
  handSize: number
  hostPlays: boolean
  mode: QuestionMode
}

export interface RoomView {
  code: string
  phase: Phase
  deadline: string | null
  serverTime: string
  settings: SettingsView
  players: PlayerView[]
  round: RoundView | null
  youId: string
  youAreHost: boolean
  youAreChallenger: boolean
  youAreTarget: boolean
  yourQuestions: QuestionView[]
  yourHand: QuestionView[]
  questionsInPool: number
  roundsPlayed: number
  startBlockedReason: string | null
}

export interface Session {
  token: string
  playerId: string
  roomId: string
  code: string
  isHost: boolean
}

/** Server-pushed events. These drive animation and tone; `state` is the truth. */
export interface SpinResolvedEvent {
  challengerId: string
  targetId: string
  animationMs: number
}

export interface RoundJudgedEvent {
  outcome: RoundOutcome
  targetId: string
  points: number
}

export interface CommandRejectedEvent {
  code: string
  message: string
}

export interface QuestionPoolLowEvent {
  remaining: number
}
