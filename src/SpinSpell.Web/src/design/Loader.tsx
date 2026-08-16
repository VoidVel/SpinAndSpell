/**
 * Three slot-machine reels — a Truth bubble, a Dare bolt, a gold score bar —
 * scrolling at three different tempos with motion blur.
 *
 * Adapted from a design by StealthWorm (Uiverse.io). All the styling and the
 * keyframes live in index.css under `.ss-loader`; this is just the markup.
 *
 * It shows up in every waiting state in the app, which is what makes it read as
 * the game's own idle animation rather than a spinner: the same three symbols
 * that carry meaning during a round are the ones tumbling while you wait.
 */

const REEL_LENGTH = 7

interface LoaderProps {
  /** 1 is the host screen. Around 0.6 suits a phone. */
  scale?: number
  label?: string
}

export function Loader({ scale = 1, label = 'Loading' }: LoaderProps) {
  return (
    <div
      className="ss-loader"
      style={{ '--ss-loader-scale': scale } as React.CSSProperties}
      role="status"
      aria-label={label}
    >
      <Reel symbol="ss-bubble" />
      <Reel symbol="ss-bolt" />
      <Reel symbol="ss-score" />
    </div>
  )
}

function Reel({ symbol }: { symbol: string }) {
  return (
    <div className="ss-reel">
      <div className="ss-carousel">
        {Array.from({ length: REEL_LENGTH }, (_, i) => (
          <div key={i} className={symbol} />
        ))}
      </div>
    </div>
  )
}

/**
 * The loader plus a line of copy, centred. Every waiting screen in the game
 * uses this so they all feel like the same moment.
 */
export function Waiting({
  title,
  subtitle,
  scale = 1,
  big,
}: {
  title: string
  subtitle?: string
  scale?: number
  big?: boolean
}) {
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-6 text-center">
      <Loader scale={scale} label={title} />
      <div className="flex flex-col gap-2">
        <span className={big ? 'stage-title' : 'font-display text-3xl font-extrabold'}>{title}</span>
        {subtitle && <span className="text-lg text-ink-dim">{subtitle}</span>}
      </div>
    </div>
  )
}
