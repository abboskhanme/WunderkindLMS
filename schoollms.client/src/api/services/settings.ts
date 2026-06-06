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
  /** Firebase service account (JSON, to'liq) — push YUBORISH uchun */
  serviceAccountJson: string
  /** Service account to'g'ri kiritilgan (push yuborishga tayyor) */
  configured: boolean
  /** Firebase WEB app config (JSON) — web (PWA) push OLISH uchun */
  webConfigJson: string
  /** Web Push VAPID ochiq kaliti */
  vapidKey: string
  /** Web push to'liq sozlangan (service account + web config + VAPID) */
  webConfigured: boolean
}

export interface SaveFirebaseInput {
  serviceAccountJson: string
  webConfigJson: string
  vapidKey: string
}

export async function getFirebaseSettings(): Promise<FirebaseConfig> {
  if (USE_MOCK) {
    await delay()
    return { serviceAccountJson: '', configured: false, webConfigJson: '', vapidKey: '', webConfigured: false }
  }
  const { data } = await api.get<FirebaseConfig>('/admin/settings/firebase')
  return data
}

export async function saveFirebaseSettings(input: SaveFirebaseInput): Promise<FirebaseConfig> {
  if (USE_MOCK) {
    await delay(250)
    return {
      ...input,
      configured: !!input.serviceAccountJson.trim(),
      webConfigured: !!(input.serviceAccountJson.trim() && input.webConfigJson.trim() && input.vapidKey.trim()),
    }
  }
  const { data } = await api.put<FirebaseConfig>('/admin/settings/firebase', input)
  return data
}

/* ---------- Turniket / FaceID integratsiyasi ---------- */

export interface TeacherDeviceMap {
  teacherId: string
  fullName: string
  deviceUserId: string
}

export interface TurnstileConfig {
  enabled: boolean
  vendor: string
  host: string
  port: number
  username: string
  /** Parol saqlanganmi (parolning o'zi qaytmaydi) */
  hasPassword: boolean
  /** Ish boshlanish vaqti "HH:mm" */
  workStartTime: string
  /** Kechikishga yo'l qo'yiladigan daqiqalar */
  lateGraceMinutes: number
  /** Oxirgi sinxronlash (ISO) */
  lastSync: string
  teachers: TeacherDeviceMap[]
}

export interface SaveTurnstilePayload {
  enabled: boolean
  vendor: string
  host: string
  port: number
  username: string
  /** Bo'sh = o'zgartirilmaydi (eski parol saqlanadi) */
  password?: string
  workStartTime: string
  lateGraceMinutes: number
  teachers: TeacherDeviceMap[]
}

export async function getTurnstileSettings(): Promise<TurnstileConfig> {
  const { data } = await api.get<TurnstileConfig>('/admin/settings/turnstile')
  return data
}

export async function saveTurnstileSettings(payload: SaveTurnstilePayload): Promise<TurnstileConfig> {
  const { data } = await api.put<TurnstileConfig>('/admin/settings/turnstile', payload)
  return data
}

/* ---------- GPS (avtobus kuzatuvi) integratsiyasi ---------- */

export interface GpsConfig {
  enabled: boolean
  ingestToken: string
  onlineMinutes: number
  stopRadiusM: number
  stopMinMinutes: number
  busCount: number
}

export interface SaveGpsPayload {
  enabled: boolean
  ingestToken?: string
  onlineMinutes: number
  stopRadiusM: number
  stopMinMinutes: number
}

export async function getGpsSettings(): Promise<GpsConfig> {
  const { data } = await api.get<GpsConfig>('/admin/settings/gps')
  return data
}

export async function saveGpsSettings(payload: SaveGpsPayload): Promise<GpsConfig> {
  const { data } = await api.put<GpsConfig>('/admin/settings/gps', payload)
  return data
}

/* ---------- Kamera (videokuzatuv) integratsiyasi ---------- */

export interface CameraConfig {
  enabled: boolean
  cameraCount: number
}

export async function getCameraSettings(): Promise<CameraConfig> {
  const { data } = await api.get<CameraConfig>('/admin/settings/cameras')
  return data
}

export async function saveCameraSettings(payload: { enabled: boolean }): Promise<CameraConfig> {
  const { data } = await api.put<CameraConfig>('/admin/settings/cameras', payload)
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
