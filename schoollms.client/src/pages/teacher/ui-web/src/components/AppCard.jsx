// AppCard — surface panel with border + soft shadow (radius 20 default).
export function AppCard({ children, className = '', onClick, noBorder = false, style }) {
  return (
    <div
      onClick={onClick}
      style={style}
      className={[
        'bg-surface rounded-4xl',
        noBorder ? '' : 'border border-border shadow-card',
        onClick ? 'cursor-pointer' : '',
        className,
      ].join(' ')}
    >
      {children}
    </div>
  )
}

// TapScale — press-to-shrink wrapper used on every interactive card.
export function TapScale({ children, onClick, className = '' }) {
  return (
    <div onClick={onClick} className={['tap-scale', onClick ? 'cursor-pointer' : '', className].join(' ')}>
      {children}
    </div>
  )
}
