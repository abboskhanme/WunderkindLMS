import { useEffect } from 'react'

// AppSheet — bottom sheet modal with grab handle + optional title.
// Slides up over a dimmed backdrop, matching showAppSheet().
export default function AppSheet({ open, onClose, title, children, maxHeightVh = 85 }) {
  useEffect(() => {
    if (!open) return
    const onKey = (e) => e.key === 'Escape' && onClose?.()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onClose])

  if (!open) return null
  return (
    <div className="absolute inset-0 z-50 flex flex-col justify-end">
      <div className="absolute inset-0 bg-black/40" onClick={onClose} />
      <div
        className="relative bg-surface rounded-t-6xl shadow-sheet flex flex-col"
        style={{ maxHeight: `${maxHeightVh}%` }}
      >
        <div className="py-2.5 flex justify-center">
          <div className="w-9 h-1 rounded-full bg-border" />
        </div>
        {title && <p className="px-5 pb-4 text-[18px] font-bold text-text">{title}</p>}
        <div className="overflow-y-auto no-scrollbar">{children}</div>
      </div>
    </div>
  )
}
