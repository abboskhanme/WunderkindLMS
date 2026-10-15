/**
 * Avtomatik Telegram xabarlari — ota-onalarga qoida bo'yicha (docs/modules/auto-messages.md,
 * 2026-10-13). Har qoida alohida yoqiladi; har biri sukut bo'yicha O'CHIQ.
 */
import { api } from '../client'

export type AutoMessageKey = 'absence' | 'invoice' | 'debt' | 'birthday'

export interface AutoMessageToken {
  token: string
  meaning: string
}

export interface AutoMessageRule {
  key: AutoMessageKey
  enabled: boolean
  sendTime: string
  dayOfMonth: number
  minAmount: number
  text: string
  monthly: boolean
  tokens: AutoMessageToken[]
  defaultText: string
  lastPeriod: string | null
  lastRunAt: string | null
  lastSent: number
  lastFailed: number
  lastNoTelegram: number
  updatedAt: string
  updatedByName: string | null
}

export interface SaveAutoMessageRule {
  enabled: boolean
  sendTime: string
  dayOfMonth: number
  minAmount: number
  text: string
}

export interface AutoMessagePreview {
  key: AutoMessageKey
  period: string
  pupils: number
  withTelegram: number
  alreadySent: number
  sample: string | null
  sampleFor: string | null
}

export interface AutoMessageLogRow {
  id: string
  ruleKey: AutoMessageKey
  period: string
  studentId: string
  fullName: string
  className: string
  chatId: number | null
  parentName: string | null
  status: 'sent' | 'failed' | 'no_telegram'
  text: string
  at: string
}

export async function getAutoMessageRules(): Promise<AutoMessageRule[]> {
  const { data } = await api.get<AutoMessageRule[]>('/admin/auto-messages')
  return data
}

export async function saveAutoMessageRule(key: AutoMessageKey, body: SaveAutoMessageRule): Promise<AutoMessageRule> {
  const { data } = await api.put<AutoMessageRule>(`/admin/auto-messages/${key}`, body)
  return data
}

export async function previewAutoMessage(key: AutoMessageKey, text?: string): Promise<AutoMessagePreview> {
  const { data } = await api.post<AutoMessagePreview>(`/admin/auto-messages/${key}/preview`, { text: text ?? null })
  return data
}

export async function getAutoMessageLog(params: {
  rule?: string
  from?: string
  to?: string
  status?: string
}): Promise<AutoMessageLogRow[]> {
  const { data } = await api.get<AutoMessageLogRow[]>('/admin/auto-messages/log', { params })
  return data
}
