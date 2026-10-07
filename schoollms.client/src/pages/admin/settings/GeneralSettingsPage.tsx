import { useState } from 'react'
import { SchoolSettings } from './SchoolSettings'
import { ArchiveReasonsSettings } from './ArchiveReasonsSettings'
import { QuartersSettings } from './QuartersSettings'
import { LessonTimesSettings } from './LessonTimesSettings'
import { AttendanceReasonsSettings } from './AttendanceReasonsSettings'
// Bayram kunlari — Dars jadvalidan KO'CHDI: u jadval emas, sozlama.
import { HolidaysPage } from '../schedule/HolidaysPage'
// Oshxona menyusi — Future menyusidan KO'CHDI (mijoz, 2026-10-07). `/admin/canteen` ham ishlaydi.
import { CanteenPage } from '../canteen/CanteenPage'
import { useAuth } from '@/context/auth-context'
import { hasSection } from '@/lib/access'
import { cn } from '@/lib/utils'

const sections = [
  { key: 'school', label: "Maktab ma'lumotlari" },
  { key: 'archive-reasons', label: 'Arxivlash sabablari' },
  // Dars jadvali bo'limidan ko'chirilmagan, faqat shu yerda ham ko'rsatiladi —
  // tabiati bo'yicha sozlama (bir martalik kiritiladi, dars jadvali qurishda
  // ishlatiladi), "Dars jadvali → SOZLAMA" guruhida ham qoladi.
  { key: 'quarters', label: 'Choraklar' },
  { key: 'lesson-times', label: 'Dars vaqtlari' },
  { key: 'reasons', label: 'Davomat sabablari' },
  { key: 'holidays', label: 'Bayram kunlari' },
  // Its API sits behind `app` (CanteenController), so the section shows only to who holds it.
  { key: 'canteen', label: 'Oshxona menyusi', perm: 'app' },
] as const

/**
 * "Sozlamalar → Umumiy sozlamalar" hub sahifasi (EduSchool naqshi: chapda
 * bo'limlar ro'yxati, o'ngda tanlangan bo'lim tarkibi). Har bir bo'lim — avvaldan
 * mavjud, o'z holicha ham ochiladigan komponent (masalan `/admin/settings/school`
 * hamon ishlaydi, `SettingsPage` orqali). Bu yerda faqat qayta ishlatiladi.
 *
 * "Yangi o'quv yiliga o'tish" (`/admin/academic-year`) ATAYLAB bu yerda YO'Q —
 * u sozlama emas, yiliga bir marta bosiladigan, orqaga qaytarib bo'lmaydigan
 * amal (promote/arxivlash/tozalash). Vazifa topshirig'ida ko'rsatilganidek, o'z
 * joyida (masalan "Boshqaruv" ostida, tashkiliy amal sifatida) qolgani ma'qul —
 * sozlamalar orasida turishi "shunchaki bir tugma" degan noto'g'ri taassurot
 * qoldiradi.
 */
export function GeneralSettingsPage() {
  const { user } = useAuth()
  const visible = sections.filter((s) => !('perm' in s) || hasSection(user, s.perm))
  const [active, setActive] = useState<(typeof sections)[number]['key']>(sections[0].key)

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Umumiy sozlamalar</h1>
        <p className="text-sm text-slate-400">
          Maktab ma'lumotlari, arxivlash sabablari, dars jadvali katalogi va oshxona menyusi.
        </p>
      </div>

      <div className="flex flex-col gap-6 lg:flex-row">
        <nav className="flex shrink-0 gap-1 overflow-x-auto pb-1 lg:w-56 lg:flex-col lg:overflow-visible lg:pb-0">
          {visible.map((s) => (
            <button
              key={s.key}
              type="button"
              onClick={() => setActive(s.key)}
              className={cn(
                'shrink-0 whitespace-nowrap rounded-xl px-3 py-2.5 text-left text-sm font-medium transition-colors',
                active === s.key
                  ? 'bg-brand-50 text-brand-700'
                  : 'text-slate-500 hover:bg-slate-50 hover:text-slate-800',
              )}
            >
              {s.label}
            </button>
          ))}
        </nav>

        {/* The canteen menu has a weekly/monthly grid — it gets the full width. */}
        <div className={cn('min-w-0 flex-1', active !== 'canteen' && 'max-w-3xl')}>
          {active === 'school' && <SchoolSettings />}
          {active === 'archive-reasons' && <ArchiveReasonsSettings />}
          {active === 'quarters' && <QuartersSettings />}
          {active === 'lesson-times' && <LessonTimesSettings />}
          {active === 'holidays' && <HolidaysPage />}
          {active === 'reasons' && <AttendanceReasonsSettings />}
          {active === 'canteen' && <CanteenPage />}
        </div>
      </div>
    </div>
  )
}
