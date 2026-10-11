import { useState } from 'react'
import { TelegramSettings } from './TelegramSettings'
import { TurnstileSettings } from './TurnstileSettings'
import { GpsSettings } from './GpsSettings'
import { CameraSettings } from './CameraSettings'
import { cn } from '@/lib/utils'

interface IntegrationSection {
  key: string
  label: string
}

const sections: IntegrationSection[] = [
  { key: 'telegram', label: 'Telegram bot' },
  { key: 'turnstile', label: 'Turniket integratsiya' },
  { key: 'gps', label: 'GPS integratsiya' },
  { key: 'cameras', label: 'Kamera integratsiya' },
  // Push (Firebase) olib tashlandi (mijoz, 2026-10-11: "bizda app yo'q hammasi telegram mini app
  // bilan ishlaydi") — barcha xabar Telegram orqali.
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
            </button>
          ))}
        </nav>

        <div className="min-w-0 max-w-3xl flex-1">
          {active === 'telegram' && <TelegramSettings />}
          {active === 'turnstile' && <TurnstileSettings />}
          {active === 'gps' && <GpsSettings />}
          {active === 'cameras' && <CameraSettings />}
        </div>
      </div>
    </div>
  )
}
