import type { ReactNode, ButtonHTMLAttributes } from 'react'
import type { PlayerView, QuestionKind } from '../realtime/types'
import { colourOf } from './colours'
import { Crown, Motif } from './Motifs'
import { kindStyle } from './kinds'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'gold' | 'ghost' | 'quiet'
  size?: 'md' | 'lg' | 'xl'
  full?: boolean
}

/**
 * One primary action per screen (§6.1.2), so this leans large and obvious.
 * Touch targets never go below 48px — these get tapped fast, in the dark,
 * often by someone holding a drink.
 *
 * Gold is the primary action everywhere. It is the only warm colour in the UI
 * chrome, so the next step is always the brightest thing on the screen.
 */
export function Button({
  variant = 'gold',
  size = 'lg',
  full,
  className = '',
  ...props
}: ButtonProps) {
  const base =
    'inline-flex items-center justify-center gap-2 rounded-2xl font-display font-extrabold ' +
    'transition duration-150 active:scale-[0.97] disabled:opacity-35 disabled:pointer-events-none ' +
    'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ink'

  const sizes = {
    md: 'min-h-12 px-5 text-base',
    lg: 'min-h-14 px-7 text-lg',
    xl: 'min-h-20 px-10 text-2xl',
  }

  const variants = {
    gold: 'bg-gold text-stage shadow-[0_8px_0_0_#a37d00] hover:brightness-110 active:shadow-[0_3px_0_0_#a37d00] active:translate-y-[5px]',
    ghost: 'bg-stage-raised text-ink border-2 border-edge hover:border-ink-dim',
    quiet: 'bg-transparent text-ink-dim border-2 border-edge hover:text-ink hover:border-ink-dim',
  }

  return (
    <button
      className={`${base} ${sizes[size]} ${variants[variant]} ${full ? 'w-full' : ''} ${className}`}
      {...props}
    />
  )
}

export function KindBadge({ kind, large }: { kind: QuestionKind; large?: boolean }) {
  const s = kindStyle(kind)

  return (
    <span
      className={`inline-flex items-center rounded-full font-display font-extrabold uppercase tracking-[0.15em] ${
        large ? 'gap-3 px-7 py-2.5 text-2xl' : 'gap-1.5 px-3 py-1 text-xs'
      }`}
      style={{ background: s.deep, color: s.colour, boxShadow: `0 0 24px ${s.glow}` }}
    >
      <Motif kind={kind} className={large ? 'h-7 w-7' : 'h-3.5 w-3.5'} />
      {kind}
    </span>
  )
}

export function PlayerChip({
  player,
  size = 'md',
  crowned,
}: {
  player: PlayerView
  size?: 'sm' | 'md' | 'lg' | 'xl'
  crowned?: boolean
}) {
  const sizes = {
    sm: 'h-8 w-8 text-sm',
    md: 'h-12 w-12 text-lg',
    lg: 'h-20 w-20 text-3xl',
    xl: 'h-28 w-28 text-5xl',
  }

  return (
    <span className="relative inline-flex shrink-0">
      {crowned && (
        <Crown className="absolute -top-3 left-1/2 h-6 w-6 -translate-x-1/2 -rotate-12 text-gold" />
      )}
      <span
        className={`inline-flex items-center justify-center rounded-full font-display font-extrabold text-stage ring-2 ring-white/15 ${sizes[size]} ${
          player.connected ? '' : 'opacity-35 grayscale'
        }`}
        style={{
          background: colourOf(player),
          boxShadow: player.connected ? `0 0 28px ${colourOf(player)}55` : 'none',
        }}
        title={player.connected ? player.name : `${player.name} (disconnected)`}
      >
        {player.name.slice(0, 2).toUpperCase()}
      </span>
    </span>
  )
}

export function Card({ children, className = '' }: { children: ReactNode; className?: string }) {
  return (
    <div className={`rounded-3xl border-2 border-edge bg-stage-raised/80 p-5 ${className}`}>
      {children}
    </div>
  )
}

/** A card flooded with a challenge's colour — the reveal, and the dealt hand. */
export function ChallengeCard({
  kind,
  children,
  className = '',
  style,
}: {
  kind: QuestionKind
  children: ReactNode
  className?: string
  style?: React.CSSProperties
}) {
  const s = kindStyle(kind)

  return (
    <div
      className={`relative overflow-hidden rounded-[2rem] border-2 ${className}`}
      style={{
        borderColor: s.colour,
        background: `linear-gradient(160deg, ${s.deep} 0%, var(--color-stage-sunken) 100%)`,
        boxShadow: `0 0 60px ${s.glow}`,
        ...style,
      }}
    >
      {/* Oversized watermark so the mode is readable before a word is (§6.2). */}
      <Motif
        kind={kind}
        className="pointer-events-none absolute -right-6 -bottom-8 h-48 w-48 opacity-[0.12]"
      />
      <div className="relative">{children}</div>
    </div>
  )
}

/**
 * The host screen: built to be read from across a room, so it centres one thing
 * and pads generously. Player screens use PhoneShell instead (§6.4).
 */
export function StageShell({ children }: { children: ReactNode }) {
  return (
    <div className="mx-auto flex min-h-dvh w-full max-w-6xl flex-col items-center justify-center p-6 text-center">
      {children}
    </div>
  )
}

/** The player's phone: denser, form-like, held close, one person at a time. */
export function PhoneShell({ children }: { children: ReactNode }) {
  return <div className="mx-auto flex min-h-dvh w-full max-w-lg flex-col p-5">{children}</div>
}

/** The room code, spaced out so it can be read aloud across a noisy room. */
export function RoomCode({ code }: { code: string }) {
  return (
    <span className="flex gap-2">
      {code.split('').map((c, i) => (
        <span
          key={i}
          className="flex h-16 w-14 items-center justify-center rounded-xl border-2 border-edge bg-stage-sunken font-display text-4xl font-extrabold text-ink sm:h-24 sm:w-20 sm:text-6xl"
        >
          {c}
        </span>
      ))}
    </span>
  )
}

export function Eyebrow({ children }: { children: ReactNode }) {
  return (
    <span className="text-xs font-bold uppercase tracking-[0.35em] text-ink-dim sm:text-sm">
      {children}
    </span>
  )
}
