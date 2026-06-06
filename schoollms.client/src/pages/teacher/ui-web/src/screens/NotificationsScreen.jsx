import { ArrowLeft, Bell } from 'lucide-react'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'

// Notifications — no API endpoint yet; clean empty state under the header.
export default function NotificationsScreen({ onBack }) {
  return (
    <div className="h-full flex flex-col bg-bg">
      <div className="flex items-center gap-1 px-2 pt-2 pb-2">
        {onBack && (
          <button onClick={onBack} className="w-10 h-10 rounded-xl flex items-center justify-center text-text">
            <ArrowLeft size={20} />
          </button>
        )}
        <p className="flex-1 text-[17px] font-extrabold text-text">Bildirishnomalar</p>
      </div>

      <EmptyState
        icon={<EmptyIllustration><Bell size={30} /></EmptyIllustration>}
        title="Bildirishnomalar yo'q"
        subtitle="Hozircha yangi bildirishnoma yo'q"
      />
    </div>
  )
}
