import { api } from '../client'
import type { NotificationItem, NotificationList } from '@/types'

/** Barcha bildirishnomalar + o'qilmaganlar soni. */
export async function getNotifications(): Promise<NotificationList> {
  const { data } = await api.get<NotificationList>('/admin/notifications')
  return data
}

/** Faqat o'qilmaganlar soni — nishonni tez yangilash uchun (ro'yxatsiz). */
export async function getUnreadCount(): Promise<number> {
  const { data } = await api.get<{ unreadCount: number }>('/admin/notifications/unread-count')
  return data.unreadCount
}

/**
 * O'qilgan deb belgilash. `ids` berilmasa — hammasi ("O'qildi" tugmasi); berilsa — faqat
 * shular (bildirishnoma ochilganda). Ochilmaganlari "yangi" bo'lib qoladi va 1 kunlik
 * o'chish muddati ular uchun boshlanmaydi.
 */
export async function markNotificationsRead(ids?: string[]): Promise<void> {
  await api.post('/admin/notifications/read', ids?.length ? { ids } : undefined)
}

export type { NotificationItem }

/**
 * Tanlangan bildirishnomalarni o'chirish — faqat joriy foydalanuvchi uchun
 * (voqeaning o'zi tegilmaydi). Bir martada ko'pi bilan 200 ta.
 */
export async function dismissNotifications(ids: string[]): Promise<void> {
  await api.post('/admin/notifications/dismiss', { ids })
}
