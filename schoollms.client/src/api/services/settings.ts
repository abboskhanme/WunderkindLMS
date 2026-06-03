import type { AbsenceReason, LessonTime, QuarterPeriod, SchoolSettings } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { settingsMock } from '../mock/settings'

export async function getSettings(): Promise<SchoolSettings> {
  if (USE_MOCK) {
    await delay()
    return settingsMock
  }
  const { data } = await api.get<SchoolSettings>('/admin/settings')
  return data
}

export async function saveQuarters(quarters: QuarterPeriod[]): Promise<void> {
  if (USE_MOCK) {
    await delay(250)
    return
  }
  await api.put('/admin/settings/quarters', { quarters })
}

export async function saveLessonTimes(lessonTimes: LessonTime[]): Promise<void> {
  if (USE_MOCK) {
    await delay(250)
    return
  }
  await api.put('/admin/settings/lesson-times', { lessonTimes })
}

export async function saveAbsenceReasons(absenceReasons: AbsenceReason[]): Promise<void> {
  if (USE_MOCK) {
    await delay(250)
    return
  }
  await api.put('/admin/settings/absence-reasons', { absenceReasons })
}

/* ---------- Maktab ma'lumotlari ---------- */

export interface SchoolInfo {
  name: string
  director: string
  phone: string
  email: string
  address: string
  region: string
  district: string
}

const emptySchoolInfo: SchoolInfo = {
  name: '',
  director: '',
  phone: '',
  email: '',
  address: '',
  region: '',
  district: '',
}

export async function getSchoolInfo(): Promise<SchoolInfo> {
  if (USE_MOCK) {
    await delay()
    return emptySchoolInfo
  }
  const { data } = await api.get<SchoolInfo>('/admin/settings/school')
  return data
}

export async function saveSchoolInfo(info: SchoolInfo): Promise<void> {
  if (USE_MOCK) {
    await delay(250)
    return
  }
  await api.put('/admin/settings/school', info)
}

/* ---------- Telegram bot sozlamasi ---------- */

export interface TelegramConfig {
  botToken: string
  botUsername: string
  /** Bot ko'rsatiladigan nomi (masalan "Maktab LMS Bot") */
  botName: string
  /** Token bo'sh emasligini bildiradi (bot ishlashga tayyor) */
  configured: boolean
}

export async function getTelegramSettings(): Promise<TelegramConfig> {
  if (USE_MOCK) {
    await delay()
    return { botToken: '', botUsername: '', botName: '', configured: false }
  }
  const { data } = await api.get<TelegramConfig>('/admin/settings/telegram')
  return data
}

export async function saveTelegramSettings(cfg: {
  botToken: string
  botUsername: string
  botName: string
}): Promise<TelegramConfig> {
  if (USE_MOCK) {
    await delay(250)
    return { ...cfg, configured: !!cfg.botToken.trim() }
  }
  const { data } = await api.put<TelegramConfig>('/admin/settings/telegram', cfg)
  return data
}

/* ---------- Push (Firebase / FCM) sozlamasi ---------- */

export interface FirebaseConfig {
  /** Firebase service account (JSON, to'liq) */
  serviceAccountJson: string
  /** JSON to'g'ri kiritilgan (push yuborishga tayyor) */
  configured: boolean
}

export async function getFirebaseSettings(): Promise<FirebaseConfig> {
  if (USE_MOCK) {
    await delay()
    return { serviceAccountJson: '', configured: false }
  }
  const { data } = await api.get<FirebaseConfig>('/admin/settings/firebase')
  return data
}

export async function saveFirebaseSettings(serviceAccountJson: string): Promise<FirebaseConfig> {
  if (USE_MOCK) {
    await delay(250)
    return { serviceAccountJson, configured: !!serviceAccountJson.trim() }
  }
  const { data } = await api.put<FirebaseConfig>('/admin/settings/firebase', { serviceAccountJson })
  return data
}

/** Maktab nomi (brending — barcha rollar uchun) */
export async function getSchoolName(): Promise<string> {
  if (USE_MOCK) {
    await delay()
    return ''
  }
  const { data } = await api.get<{ name: string }>('/school')
  return data.name
}
