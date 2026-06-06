// SegmentedControl — iOS-style pill toggle. options: [{value,label}].
export default function SegmentedControl({ value, onChange, options }) {
  return (
    <div className="flex p-[3px] bg-surface2 rounded-xl">
      {options.map((o) => {
        const on = value === o.value
        return (
          <button
            key={o.value}
            onClick={() => onChange(o.value)}
            className={[
              'flex-1 py-2 px-3 rounded-[10px] text-[13px] transition-all duration-150 text-center',
              on ? 'bg-surface shadow-soft font-bold text-text' : 'font-semibold text-muted',
            ].join(' ')}
          >
            {o.label}
          </button>
        )
      })}
    </div>
  )
}
