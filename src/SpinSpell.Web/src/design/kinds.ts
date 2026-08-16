import type { QuestionKind, QuestionMode } from '../realtime/types'

/** Which kinds a room's mode permits. One source of truth for every screen. */
export const kindsFor = (mode: QuestionMode): QuestionKind[] =>
  mode === 'TruthsOnly' ? ['Truth'] : mode === 'DaresOnly' ? ['Dare'] : ['Truth', 'Dare']

export const MODE_LABELS: Record<QuestionMode, string> = {
  Both: 'Truths & Dares',
  TruthsOnly: 'Truths only',
  DaresOnly: 'Dares only',
}

/**
 * Every Truth/Dare styling decision resolves through here, so the two never
 * drift apart across screens — §6.2 wants them recognisable by colour and shape
 * alone, and that only holds if there is exactly one definition of each.
 *
 * Kept out of Motifs.tsx so that file exports only components and Fast Refresh
 * keeps working on it.
 */
export const kindStyle = (kind: QuestionKind) =>
  kind === 'Truth'
    ? {
        colour: 'var(--color-truth)',
        deep: 'var(--color-truth-deep)',
        glow: 'var(--color-truth-glow)',
      }
    : {
        colour: 'var(--color-dare)',
        deep: 'var(--color-dare-deep)',
        glow: 'var(--color-dare-glow)',
      }
