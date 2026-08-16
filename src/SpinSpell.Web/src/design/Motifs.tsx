import type { QuestionKind } from '../realtime/types'

/**
 * §6.2: "give these two distinct, memorable colours/shapes from day one —
 * players should recognise which mode they're in without reading text."
 *
 * So the bubble and the bolt are defined once, here, and everything else in the
 * app draws them from this file. Thick strokes and solid fills only: they have
 * to survive being 16px on a phone and 400px on a screen across the room (§6.2).
 */

export function TruthBubble({ className = '', ...props }: React.SVGProps<SVGSVGElement>) {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" className={className} aria-hidden {...props}>
      <path d="M12 2C6.5 2 2 5.8 2 10.5c0 2.6 1.4 4.9 3.6 6.4-.2 1.5-.8 3-1.9 4.3-.3.3 0 .8.4.7 2.6-.5 4.6-1.7 5.8-2.6.7.1 1.4.2 2.1.2 5.5 0 10-3.8 10-8.5S17.5 2 12 2Z" />
    </svg>
  )
}

export function DareBolt({ className = '', ...props }: React.SVGProps<SVGSVGElement>) {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" className={className} aria-hidden {...props}>
      <path d="M14.5 1.5 4.8 13.2c-.4.5-.1 1.3.6 1.3h4.3l-1.6 7.6c-.1.7.8 1.1 1.3.5l9.8-11.7c.4-.5.1-1.3-.6-1.3h-4.3l1.6-7.6c.1-.7-.8-1.1-1.4-.5Z" />
    </svg>
  )
}

export function Star({ className = '', ...props }: React.SVGProps<SVGSVGElement>) {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" className={className} aria-hidden {...props}>
      <path d="m12 2 2.9 6.3 6.9.8-5.1 4.7 1.4 6.8L12 17.2 5.9 20.6l1.4-6.8L2.2 9.1l6.9-.8L12 2Z" />
    </svg>
  )
}

export function Crown({ className = '', ...props }: React.SVGProps<SVGSVGElement>) {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" className={className} aria-hidden {...props}>
      <path d="M3 8.5 6.5 12 12 4l5.5 8L21 8.5V19a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V8.5Z" />
    </svg>
  )
}

export const Motif = ({ kind, className }: { kind: QuestionKind; className?: string }) =>
  kind === 'Truth' ? <TruthBubble className={className} /> : <DareBolt className={className} />
