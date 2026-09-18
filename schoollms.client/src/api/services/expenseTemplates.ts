/**
 * Rejalashtirilgan chiqim shabloni — F6.01 (finance-parity.md §2.6.3).
 *
 * `ExpenseTemplatesController` ga to'g'ridan-to'g'ri boradi
 * (`/api/admin/finance/expense-templates`). Shakl `billingCatalog.ts` bilan
 * bir xil: xatoni bu yerda o'rab qo'ymaydi, sahifa o'zi
 * `billingErrorMessage` bilan o'qiydi — boshqa `pages/admin/billing/*`
 * sahifalari qanday ishlasa, shu yerda ham xuddi shunday.
 */
import { api } from '../client'

const BASE = '/admin/finance/expense-templates'

export interface ExpenseTemplate {
  id: string
  name: string
  /** `Accounts.ExpenseCategories` dan (`config/constants.ts` dagi `expenseCategories`). */
  categoryCode: string
  /** Serverda hisoblangan ko'rsatiladigan nom — qo'shimcha xaritalash shart emas. */
  categoryName: string
  amount: number
  /** 1..28. */
  dayOfMonth: number
  isActive: boolean
}

export interface ExpenseTemplateInput {
  name: string
  categoryCode: string
  amount: number
  dayOfMonth: number
  isActive: boolean
}

/** Qisman tahrir — faqat berilgan maydonlar o'zgaradi (server ham xuddi shunday). */
export type ExpenseTemplatePatch = Partial<ExpenseTemplateInput>

export async function getExpenseTemplates(): Promise<ExpenseTemplate[]> {
  const { data } = await api.get<ExpenseTemplate[]>(BASE)
  return data
}

export async function createExpenseTemplate(
  input: ExpenseTemplateInput,
): Promise<ExpenseTemplate> {
  const { data } = await api.post<ExpenseTemplate>(BASE, input)
  return data
}

export async function updateExpenseTemplate(
  id: string,
  patch: ExpenseTemplatePatch,
): Promise<ExpenseTemplate> {
  const { data } = await api.put<ExpenseTemplate>(`${BASE}/${id}`, patch)
  return data
}

/** Haqiqiy o'chirish — shablon moliyaviy emas (SPEC §4.1 bu yerga tegishli emas). */
export async function deleteExpenseTemplate(id: string): Promise<void> {
  await api.delete(`${BASE}/${id}`)
}
