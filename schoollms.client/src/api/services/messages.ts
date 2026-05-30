import * as signalR from '@microsoft/signalr'
import type {
  Broadcast,
  ChatMessage,
  MessageClass,
  TelegramParent,
  TelegramStatus,
} from '@/types'
import { api, USE_MOCK } from '../client'

/** Xabarlar bo'limi uchun sinflar ro'yxati (o'quvchi soni, ro'yxatdagi ota-onalar, oxirgi xabar) */
export async function getMessageClasses(): Promise<MessageClass[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<MessageClass[]>('/admin/messages/classes')
  return data
}

/** Sinf guruh chati xabarlari. since berilsa — shu vaqtdan keyingilar. */
export async function getChat(className: string, since?: string): Promise<ChatMessage[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<ChatMessage[]>(
    `/admin/messages/chat/${encodeURIComponent(className)}`,
    { params: since ? { since } : undefined },
  )
  return data
}

/** Sinf guruh chatiga xabar yuborish (admin sifatida) */
export async function sendChat(className: string, text: string): Promise<ChatMessage> {
  const { data } = await api.post<ChatMessage>(
    `/admin/messages/chat/${encodeURIComponent(className)}`,
    { text },
  )
  return data
}

/** Yuborilgan e'lonlar tarixi (ixtiyoriy sinf bo'yicha) */
export async function getBroadcasts(className?: string): Promise<Broadcast[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<Broadcast[]>('/admin/messages/broadcasts', {
    params: className ? { className } : undefined,
  })
  return data
}

/** Sinf ota-onalariga Telegram bot orqali e'lon yuborish */
export async function sendBroadcast(className: string, text: string): Promise<Broadcast> {
  const { data } = await api.post<Broadcast>('/admin/messages/broadcast', { className, text })
  return data
}

/** Sinf bo'yicha Telegramda ro'yxatdan o'tgan ota-onalar */
export async function getTelegramRegistrations(className?: string): Promise<TelegramParent[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<TelegramParent[]>('/admin/messages/telegram/registrations', {
    params: className ? { className } : undefined,
  })
  return data
}

/** Telegram bot sozlanganmi (token bormi) — admin UIga ko'rsatish uchun */
export async function getTelegramStatus(): Promise<TelegramStatus> {
  if (USE_MOCK) return { configured: false, botUsername: '' }
  const { data } = await api.get<TelegramStatus>('/admin/messages/telegram/status')
  return data
}

/**
 * Har bir kanal uchun oxirgi xabar vaqti — o'qilmagan xabarlarni aniqlash uchun.
 * Qaytadi: { [channelName]: ISO vaqt yoki null (xabari yo'q kanal) }
 */
export async function getAdminLastMessages(): Promise<Record<string, string | null>> {
  if (USE_MOCK) return {}
  const { data } = await api.get<Record<string, string | null>>('/admin/messages/last-messages')
  return data
}

/**
 * Guruh chati uchun real-time SignalR ulanishi. Yangi xabar kelganda onMessage chaqiriladi
 * (foydalanuvchi a'zo bo'lgan BARCHA sinflar bo'yicha — komponent kerakli sinfni filtrlaydi).
 * Mock rejimida null qaytaradi.
 */
export function connectChat(onMessage: (m: ChatMessage) => void): signalR.HubConnection | null {
  if (USE_MOCK) return null
  const conn = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/chat', { accessTokenFactory: () => localStorage.getItem('token') ?? '' })
    .withAutomaticReconnect()
    .build()
  conn.on('message', (m: ChatMessage) => onMessage(m))
  return conn
}
