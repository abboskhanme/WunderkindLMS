/**
 * O'QITUVCHI PANELI — qobiq.
 *
 * Qobiq uchta ish qiladi va boshqa hech narsa: profil bilan meta'ni BIR MARTA
 * oladi (har tab o'zi so'ramasin), faol tabni saqlaydi va tab panelini chizadi.
 * Har tabning mazmuni `teacher/*.jsx` da.
 *
 * RUXSAT — bu yerda hal bo'ladi: o'qituvchining ruxsati bo'lmagan bo'lim
 * pastdagi panelda umuman KO'RSATILMAYDI. Yopiq eshikni ko'rsatib, bosilganda
 * "ruxsat yo'q" deyishdan ko'ra, eshikni umuman chizmaslik to'g'ri: server
 * baribir 403 qaytaradi, ya'ni bu ko'z bo'yash emas.
 */
import { useState } from 'react'
import { CalendarDays, ClipboardCheck, Home, MessageSquare, Wallet } from 'lucide-react'
import { ErrorState, Loader, Screen, TabBar } from '../components/ui'
import { useAsync } from '../lib/useAsync'
import { teacherApi } from '../lib/teacherApi'
import { TodayTab } from './teacher/TodayTab'
import { AttendanceTab } from './teacher/AttendanceTab'
import { ScheduleTab } from './teacher/ScheduleTab'
import { SalaryTab } from './teacher/SalaryTab'
import { MessagesTab } from './teacher/MessagesTab'

/** `perm` — shu tabni ko'rsatish uchun kerakli ruxsat (null = hammaga). */
const TABS = [
  { value: 'home', label: 'Bugun', icon: Home, perm: null },
  { value: 'attendance', label: 'Davomat', icon: ClipboardCheck, perm: 'journal' },
  { value: 'schedule', label: 'Jadval', icon: CalendarDays, perm: 'schedule' },
  { value: 'salary', label: 'Maosh', icon: Wallet, perm: 'salary' },
  { value: 'chat', label: 'Xabarlar', icon: MessageSquare, perm: 'messages' },
]

export function TeacherPanel({ user }) {
  const ctx = useAsync(() => Promise.all([teacherApi.profile(), teacherApi.meta()]), [])
  const [tab, setTab] = useState('home')
  // "Bugun" dan darsga bosilganda davomat o'sha darsda ochilishi uchun.
  const [focusLesson, setFocusLesson] = useState(null)

  if (ctx.loading && ctx.data === null) {
    return (
      <Screen>
        <Loader label="Panel yuklanmoqda…" />
      </Screen>
    )
  }
  if (ctx.error) {
    return (
      <Screen>
        <ErrorState message={ctx.error} onRetry={ctx.reload} />
      </Screen>
    )
  }

  const [profile, meta] = ctx.data
  const perms = profile?.permissions || []
  const tabs = TABS.filter((t) => !t.perm || perms.includes(t.perm))
  // Ruxsat olib qo'yilgan bo'lim ochiq qolib ketmasin.
  const active = tabs.some((t) => t.value === tab) ? tab : 'home'

  const openAttendance = (lesson) => {
    setFocusLesson(lesson)
    setTab('attendance')
  }

  const change = (next) => {
    if (next !== 'attendance') setFocusLesson(null)
    setTab(next)
  }

  return (
    <>
      {active === 'home' && (
        <TodayTab profile={profile} meta={meta} onOpenAttendance={openAttendance} />
      )}
      {active === 'attendance' && (
        <AttendanceTab
          profile={profile}
          meta={meta}
          focusLesson={focusLesson}
          onClearFocus={() => setFocusLesson(null)}
        />
      )}
      {active === 'schedule' && <ScheduleTab meta={meta} />}
      {active === 'salary' && <SalaryTab meta={meta} />}
      {/* `user` — sessiya identifikatori: chatda o'z xabarlarini ajratish uchun. */}
      {active === 'chat' && <MessagesTab profile={profile} user={user} />}

      <TabBar tabs={tabs} value={active} onChange={change} />
    </>
  )
}
