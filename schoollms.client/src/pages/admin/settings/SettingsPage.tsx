import { useParams } from 'react-router-dom'
import { SchoolSettings } from './SchoolSettings'
import { TelegramSettings } from './TelegramSettings'
import { FirebaseSettings } from './FirebaseSettings'
import { TurnstileSettings } from './TurnstileSettings'
import { GpsSettings } from './GpsSettings'
import { CameraSettings } from './CameraSettings'
import { ArchiveReasonsSettings } from './ArchiveReasonsSettings'
import { QuartersSettings } from './QuartersSettings'
import { LessonTimesSettings } from './LessonTimesSettings'
import { AttendanceReasonsSettings } from './AttendanceReasonsSettings'

const sectionTitles: Record<string, string> = {
  quarters: 'Choraklar sanalari',
  'lesson-times': 'Dars vaqtlari',
  reasons: 'Davomat sabablari',
  school: "Maktab ma'lumotlari",
  'archive-reasons': 'Arxivlash sabablari',
  telegram: 'Telegram bot',
  firebase: 'Push (Firebase)',
  turnstile: 'Turniket integratsiya',
  gps: 'GPS integratsiya',
  cameras: 'Kamera integratsiya',
}

/**
 * Har bir bo'lim endi o'z-o'zini yuklaydigan, mustaqil komponent (quyida import
 * qilingan) — bu sahifa faqat marshrut bo'yicha kerakli bo'limni tanlab beradi.
 * SABAB: xuddi shu komponentlar "Integratsiyalar" va "Umumiy sozlamalar" hub
 * sahifalarida ham qayta ishlatiladi (docs: 2026-09-18 sozlamalar hub vazifasi) —
 * forma va API chaqiruvlarini ikki joyda yozmaslik uchun.
 */
export function SettingsPage() {
  const { section = 'quarters' } = useParams()

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Sozlamalar</h1>
        <p className="text-sm text-slate-400">{sectionTitles[section] ?? 'Sozlamalar'}</p>
      </div>

      <div className="max-w-3xl space-y-6">
        {/* Choraklar sanalari */}
        {section === 'quarters' && <QuartersSettings />}

        {/* Dars vaqtlari */}
        {section === 'lesson-times' && <LessonTimesSettings />}

        {/* Davomat sabablari */}
        {section === 'reasons' && <AttendanceReasonsSettings />}

        {/* Maktab ma'lumotlari */}
        {section === 'school' && <SchoolSettings />}

        {/* Telegram bot */}
        {section === 'telegram' && <TelegramSettings />}

        {/* Push (Firebase) */}
        {section === 'firebase' && <FirebaseSettings />}

        {/* Turniket / FaceID integratsiya */}
        {section === 'turnstile' && <TurnstileSettings />}

        {/* GPS (avtobus kuzatuvi) integratsiya */}
        {section === 'gps' && <GpsSettings />}

        {/* Kamera (videokuzatuv) integratsiya */}
        {section === 'cameras' && <CameraSettings />}

        {/* Arxivlash sabablari katalogi (§2.2) */}
        {section === 'archive-reasons' && <ArchiveReasonsSettings />}
      </div>
    </div>
  )
}
