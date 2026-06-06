// EmptyState — centered icon + title + subtitle + optional action.
export default function EmptyState({ icon, title, subtitle, action }) {
  return (
    <div className="flex-1 flex items-center justify-center p-10">
      <div className="flex flex-col items-center text-center">
        {icon && <div className="mb-4">{icon}</div>}
        <p className="text-[18px] font-bold text-text">{title}</p>
        {subtitle && <p className="mt-1.5 text-[14px] text-muted leading-relaxed">{subtitle}</p>}
        {action && <div className="mt-4">{action}</div>}
      </div>
    </div>
  )
}

// EmptyIllustration — soft rounded badge holding an icon (inbox/chat/search).
export function EmptyIllustration({ children }) {
  return (
    <div className="w-20 h-20 rounded-6xl bg-primary-soft flex items-center justify-center text-primary">
      {children}
    </div>
  )
}
