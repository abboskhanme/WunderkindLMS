/**
 * OTA-ONA PANELI — karkas.
 *
 * FARZAND ALMASHTIRGICH shu qobiqda turadi: ota-onaning bir nechta farzandi
 * bo'lishi mumkin va tanlangan farzand hamma tabga tarqaladi. Har tab o'zi
 * alohida so'rov yubormaydi — tanlov bitta joyda.
 */
import { useState } from 'react'
import { CalendarDays, GraduationCap, Home, UtensilsCrossed, Wallet } from 'lucide-react'
import { Hero, Screen, TabBar, EmptyState } from '../components/ui'

const TABS = [
  { value: 'home', label: 'Bosh', icon: Home },
  { value: 'grades', label: 'Baholar', icon: GraduationCap },
  { value: 'schedule', label: 'Jadval', icon: CalendarDays },
  { value: 'finance', label: "To'lov", icon: Wallet },
  { value: 'menu', label: 'Ovqat', icon: UtensilsCrossed },
]

export function ParentPanel({ user }) {
  const [tab, setTab] = useState('home')

  return (
    <>
      <Screen>
        <Hero title={user.fullName} subtitle="Ota-ona" />
        <EmptyState title="Tayyorlanmoqda" note={`«${TABS.find((t) => t.value === tab).label}» bo'limi`} />
      </Screen>
      <TabBar tabs={TABS} value={tab} onChange={setTab} />
    </>
  )
}
