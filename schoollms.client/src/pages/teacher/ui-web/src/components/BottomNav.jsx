import { Home, BookOpen, ClipboardList, MessageCircle, User } from 'lucide-react'

// AppBottomNav — 5 tabs. Active tab gets a soft pill behind the icon.
const TABS = [
  { icon: Home, label: 'Dashboard' },
  { icon: BookOpen, label: 'Jurnal' },
  { icon: ClipboardList, label: 'Vazifa' },
  { icon: MessageCircle, label: 'Suhbat' },
  { icon: User, label: 'Profil' },
]

export default function BottomNav({ activeIndex, onChange }) {
  return (
    <div className="bg-surface border-t border-border">
      <div className="h-[60px] flex">
        {TABS.map((tab, i) => {
          const active = activeIndex === i
          const Icon = tab.icon
          return (
            <button
              key={i}
              onClick={() => onChange(i)}
              className="flex-1 flex flex-col items-center justify-center gap-0.5"
            >
              <span
                className={[
                  'w-14 h-7 rounded-xl flex items-center justify-center transition-colors duration-200',
                  active ? 'bg-primary-soft' : 'bg-transparent',
                ].join(' ')}
              >
                <Icon size={22} className={active ? 'text-primary' : 'text-faint'} strokeWidth={active ? 2.4 : 2} />
              </span>
              <span
                className={[
                  'text-[10.5px] tracking-tight',
                  active ? 'font-bold text-primary' : 'font-medium text-faint',
                ].join(' ')}
              >
                {tab.label}
              </span>
            </button>
          )
        })}
      </div>
    </div>
  )
}
