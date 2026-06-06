// AppButton — 4 styles: filled (default), ghost, soft, danger.
export default function AppButton({
  label,
  onClick,
  style = 'filled',
  loading = false,
  expand = false,
  height = 48,
  leadingIcon,
  trailingIcon,
  radius = 14,
  disabled = false,
}) {
  const styles = {
    filled: 'bg-primary text-white',
    ghost: 'bg-transparent text-text border border-border',
    soft: 'bg-primary-soft text-primary',
    danger: 'bg-transparent text-danger border border-border',
  }
  const isDisabled = disabled || (!onClick && !loading)
  return (
    <button
      onClick={onClick}
      disabled={isDisabled}
      style={{ height, borderRadius: radius, width: expand ? '100%' : undefined }}
      className={[
        'inline-flex items-center justify-center gap-2 px-5 font-semibold text-[15px] transition-opacity duration-150',
        styles[style] || styles.filled,
        isDisabled ? 'opacity-50' : 'opacity-100',
      ].join(' ')}
    >
      {loading ? (
        <span className="flex gap-1">
          {[0, 1, 2].map((i) => (
            <span
              key={i}
              className="w-[7px] h-[7px] rounded-full bg-current"
              style={{ animation: `pulse-dot 1.2s ${i * 0.2}s infinite ease-in-out` }}
            />
          ))}
        </span>
      ) : (
        <>
          {leadingIcon}
          {label}
          {trailingIcon}
        </>
      )}
    </button>
  )
}
