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

/** Hammasini o'qilgan deb belgilash. */
export async function markNotificationsRead(): Promise<void> {
  await api.post('/admin/notifications/read')
}

export type { NotificationItem }
