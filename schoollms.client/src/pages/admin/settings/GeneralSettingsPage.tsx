import { useState } from 'react'
import { SchoolSettings } from './SchoolSettings'
import { ArchiveReasonsSettings } from './ArchiveReasonsSettings'
import { QuartersSettings } from './QuartersSettings'
import { LessonTimesSettings } from './LessonTimesSettings'
import { AttendanceReasonsSettings } from './AttendanceReasonsSettings'
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
  const [active, setActive] = useState<(typeof sections)[number]['key']>(sections[0].key)

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Umumiy sozlamalar</h1>
        <p className="text-sm text-slate-400">
          Maktab ma'lumotlari, arxivlash sabablari va dars jadvali katalogi.
        </p>
      </div>

      <div className="flex flex-col gap-6 lg:flex-row">
        <nav className="flex shrink-0 gap-1 overflow-x-auto pb-1 lg:w-56 lg:flex-col lg:overflow-visible lg:pb-0">
          {sections.map((s) => (
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

        <div className="min-w-0 max-w-3xl flex-1">
          {active === 'school' && <SchoolSettings />}
          {active === 'archive-reasons' && <ArchiveReasonsSettings />}
          {active === 'quarters' && <QuartersSettings />}
          {active === 'lesson-times' && <LessonTimesSettings />}
          {active === 'reasons' && <AttendanceReasonsSettings />}
        </div>
      </div>
    </div>
  )
}
