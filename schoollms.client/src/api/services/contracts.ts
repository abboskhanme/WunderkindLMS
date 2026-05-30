import type {
  ContractTemplate,
  ParentRecipient,
  StaffRecipient,
  SendResult,
} from '@/types'
import { api, USE_MOCK } from '../client'

type Target = 'parent' | 'staff'

/** Berilgan target (ota-ona/xodim) bo'yicha yuklangan Word andozalar */
export async function getTemplates(target: Target): Promise<ContractTemplate[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<ContractTemplate[]>('/admin/contracts/templates', {
    params: { target },
  })
  return data
}

/** Yangi andoza yozuvi (fayl avval uploadAdminFile orqali yuklanadi) */
export async function createTemplate(
  target: Target,
  name: string,
  fileUrl: string,
  fileName: string,
): Promise<ContractTemplate> {
  const { data } = await api.post<ContractTemplate>('/admin/contracts/templates', {
    target,
    name,
    fileUrl,
    fileName,
  })
  return data
}

export async function deleteTemplate(id: string): Promise<void> {
  await api.delete(`/admin/contracts/templates/${id}`)
}

/** Ota-ona oluvchilari (telefon bo'yicha guruhlangan) */
export async function getParentRecipients(): Promise<ParentRecipient[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<ParentRecipient[]>('/admin/contracts/recipients/parents')
  return data
}

/** Xodim oluvchilari */
export async function getStaffRecipients(): Promise<StaffRecipient[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<StaffRecipient[]>('/admin/contracts/recipients/staff')
  return data
}

/** Tanlangan oluvchilarga shartnoma yuborish (Telegram bot orqali) */
export async function sendContracts(
  target: Target,
  templateId: string,
  recipientKeys: string[],
): Promise<SendResult[]> {
  const { data } = await api.post<SendResult[]>('/admin/contracts/send', {
    target,
    templateId,
    recipientKeys,
  })
  return data
}
