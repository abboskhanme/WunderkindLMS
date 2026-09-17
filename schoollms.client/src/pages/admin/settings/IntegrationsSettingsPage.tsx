import { useState } from 'react'
import { TelegramSettings } from './TelegramSettings'
import { TurnstileSettings } from './TurnstileSettings'
import { GpsSettings } from './GpsSettings'
import { CameraSettings } from './CameraSettings'
import { FirebaseSettings } from './FirebaseSettings'
import { cn } from '@/lib/utils'

interface IntegrationSection {
  key: string
  label: string
  /** Mahsulot yo'l xaritasida endi yo'q — kamroq ko'zga tashlanadigan holatda ko'rsatiladi. */
  legacy?: boolean
}

const sections: IntegrationSection[] = [
  { key: 'telegram', label: 'Telegram bot' },
  { key: 'turnstile', label: 'Turniket integratsiya' },
  { key: 'gps', label: 'GPS integratsiya' },
  { key: 'cameras', label: 'Kamera integratsiya' },
  // CLAUDE.md: mobil push (Firebase/FCM) mahsulot yo'l xaritasida yo'q — faqat
  // Telegram orqali xabar yuboriladi. Ekran o'chirilmagan (buyurtma qilinmagan
  // tozalash — alohida vazifa), shuning uchun ro'yxat oxirida "eskirgan" belgisi
  // bilan qoldirilgan: hali ham ochish mumkin, lekin yangi ish shu ustida qurilmaydi.
  { key: 'firebase', label: 'Push (Firebase)', legacy: true },
]

/**
 * "Sozlamalar → Integratsiyalar" hub sahifasi (EduSchool naqshi: chapda bo'limlar
 * ro'yxati, o'ngda tanlangan bo'lim tarkibi). Beshta tashqi integratsiya bo'limini
 * bitta sahifada jamlaydi — har biri avvaldan mavjud, o'z holicha ham ishlaydigan
 * komponent (masalan `/admin/settings/telegram` hamon ochiladi, `SettingsPage`
 * orqali). Bu yerda faqat qayta ishlatiladi: forma va API chaqiruvlari bir xil.
 */
export function IntegrationsSettingsPage() {
  const [active, setActive] = useState<string>(sections[0].key)

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Integratsiyalar</h1>
        <p className="text-sm text-slate-400">
          Tashqi tizimlar bilan bog'lanish: Telegram bot, turniket, GPS va kameralar.
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
                'flex shrink-0 items-center gap-2 whitespace-nowrap rounded-xl px-3 py-2.5 text-left text-sm font-medium transition-colors',
                active === s.key
                  ? 'bg-brand-50 text-brand-700'
                  : 'text-slate-500 hover:bg-slate-50 hover:text-slate-800',
              )}
            >
              {s.label}
              {s.legacy && (
                <span className="rounded-full bg-slate-100 px-1.5 py-0.5 text-[10px] font-medium text-slate-400">
                  eskirgan
                </span>
              )}
            </button>
          ))}
        </nav>

        <div className="min-w-0 max-w-3xl flex-1">
          {active === 'telegram' && <TelegramSettings />}
          {active === 'turnstile' && <TurnstileSettings />}
          {active === 'gps' && <GpsSettings />}
          {active === 'cameras' && <CameraSettings />}
          {active === 'firebase' && (
            <div className="space-y-3">
              <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-700">
                Bu integratsiya eskirgan: mahsulotda mobil push endi yo'q, barcha xabar Telegram
                orqali yuboriladi. Ekran o'chirilmagan — mavjud sozlama yo'qolmasin — lekin yangi
                ish uning ustiga qurilmaydi.
              </div>
              <FirebaseSettings />
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
