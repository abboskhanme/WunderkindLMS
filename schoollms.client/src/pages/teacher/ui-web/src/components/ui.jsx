import { ArrowLeft } from 'lucide-react'

// Sub-screen header: round back button + title (+ optional subtitle / trailing).
export function ScreenHeader({ title, subtitle, onBack, trailing, titleSize = 20 }) {
  return (
    <div className="flex items-center gap-2 px-3 pt-2 pb-1">
      {onBack && (
        <button
          onClick={onBack}
          className="w-10 h-10 shrink-0 rounded-xl bg-surface2 flex items-center justify-center text-text"
        >
          <ArrowLeft size={20} />
        </button>
      )}
      <div className="flex-1 min-w-0">
        <p className="font-extrabold text-text truncate" style={{ fontSize: titleSize, letterSpacing: '-0.025em' }}>
          {title}
        </p>
        {subtitle && <p className="text-[12px] text-muted truncate">{subtitle}</p>}
      </div>
      {trailing}
    </div>
  )
}

// Tab-screen big title (no back button).
export function BigTitle({ title, subtitle, trailing }) {
  return (
    <div className="flex items-center px-4 pt-2 pb-1">
      <div className="flex-1">
        <p className="text-[22px] font-extrabold text-text" style={{ letterSpacing: '-0.025em' }}>
          {title}
        </p>
        {subtitle && <p className="text-[12px] text-muted">{subtitle}</p>}
      </div>
      {trailing}
    </div>
  )
}

export function SectionLabel({ children, className = '' }) {
  return (
    <p className={['text-[13px] font-bold text-text tracking-tight', className].join(' ')}>{children}</p>
  )
}

// Uppercase field/form label.
export function FieldLabel({ children }) {
  return <p className="text-[12px] font-bold text-muted uppercase tracking-wide">{children}</p>
}
