// ProgressRing — circular progress with centered label (SVG version of the
// Flutter CustomPainter). value is 0–100.
export default function ProgressRing({
  value,
  size = 64,
  strokeWidth = 6,
  color = '#0D9488',
  trackColor,
  label,
  sub,
}) {
  const v = Math.max(0, Math.min(100, value))
  const r = (size - strokeWidth) / 2
  const c = 2 * Math.PI * r
  const offset = c * (1 - v / 100)
  return (
    <div className="relative inline-flex items-center justify-center" style={{ width: size, height: size }}>
      <svg width={size} height={size} className="-rotate-90">
        <circle
          cx={size / 2}
          cy={size / 2}
          r={r}
          fill="none"
          strokeWidth={strokeWidth}
          stroke={trackColor || 'var(--surface3)'}
        />
        <circle
          cx={size / 2}
          cy={size / 2}
          r={r}
          fill="none"
          strokeWidth={strokeWidth}
          stroke={color}
          strokeLinecap="round"
          strokeDasharray={c}
          strokeDashoffset={offset}
        />
      </svg>
      <div className="absolute flex flex-col items-center text-white">
        {label && (
          <span className="font-mono font-bold leading-none" style={{ fontSize: size * 0.26 }}>
            {label}
          </span>
        )}
        {sub && <span className="text-[9px] text-white/70">{sub}</span>}
      </div>
    </div>
  )
}
