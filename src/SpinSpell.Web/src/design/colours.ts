import type { PlayerView } from '../realtime/types'

/**
 * §6.2: the accent palette is reserved exclusively for player identity and is
 * used for nothing else. That is what makes colour a reliable way to recognise
 * yourself on a screen across the room (§6.1).
 *
 * Kept out of primitives.tsx so that file exports only components — otherwise
 * React Fast Refresh gives up on it and every tweak costs a full reload.
 */
export const playerColour = (slot: number) => `var(--color-player-${slot % 12})`

export const colourOf = (player: Pick<PlayerView, 'colourSlot'>) => playerColour(player.colourSlot)
