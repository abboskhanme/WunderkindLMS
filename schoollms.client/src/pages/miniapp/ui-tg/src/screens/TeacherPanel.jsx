/**
 * O'QITUVCHI PANELI — karkas.
 *
 * Tab'lar va qobiq shu yerda; har tabning mazmuni alohida faylda
 * (`teacher/*.jsx`). Endpointlar `docs/PENDING_WIRING.md` dagi Mini App
 * shartnomasida.
 */
import { useState } from 'react'
import { CalendarDays, ClipboardCheck, Home, MessageSquare, Wallet } from 'lucide-react'
import { Hero, Screen, TabBar, EmptyState } from '../components/ui'

const TABS = [
  { value: 'home', label: 'Bugun', icon: Home },
  { value: 'attendance', label: 'Davomat', icon: ClipboardCheck },
  { value: 'schedule', label: 'Jadval', icon: CalendarDays },
  { value: 'salary', label: 'Maosh', icon: Wallet },
  { value: 'chat', label: 'Xabarlar', icon: MessageSquare },
]

export function TeacherPanel({ user }) {
  const [tab, setTab] = useState('home')

  return (
    <>
      <Screen>
        <Hero title={user.fullName} subtitle="O'qituvchi" />
        <EmptyState title="Tayyorlanmoqda" note={`«${TABS.find((t) => t.value === tab).label}» bo'limi`} />
      </Screen>
      <TabBar tabs={TABS} value={tab} onChange={setTab} />
    </>
  )
}
