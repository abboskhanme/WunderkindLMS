/**
 * Buyurtmalar voronkasi (§4, #9) — FAQAT O'QISH uchun hisobot.
 *
 * Tiplar `SchoolLms.Application/Dtos/AnalyticsReportDtos.cs` dagi DTO'larning
 * aynan nusxasi (camelCase). Hech bir foiz bu yerda hisoblanmaydi — hammasi
 * serverdan keladi.
 *
 * DIQQAT: bu servis lidlar DOSKASIGA aloqador emas; doskaning fayllari
 * (`pages/admin/leads/*`) dizayn bo'yicha muzlatilgan.
 */
import { api, USE_MOCK } from '../client'

/** Voronkaning bitta bosqichi. */
export interface LeadFunnelStage {
  stageId: string
  title: string
  color: string
  order: number
  /** Hozir shu bosqichda turganlar. */
  currentCount: number
  /** Shu bosqichga yetganlar (shu va keyingi bosqichlar). */
  reachedCount: number
  /** Keyingi bosqichga o'tganlar. */
  movedOnCount: number
  sharePercent: number
  /** Oxirgi bosqichda — null (undan keyin bosqich yo'q). */
  stepConversionPercent: number | null
  dropOffPercent: number | null
}

/** Yo'qotish "sababi" — mijozning o'zi yo'qotish deb belgilagan ustun nomi. */
export interface LeadFunnelLoss {
  stageId: string
  title: string
  color: string
  count: number
  sharePercent: number
}

/** Maqsadli sinf kesimi (manba maydoni bazada yo'q — o'rniga shu). */
export interface LeadFunnelGrade {
  targetGrade: number
  total: number
  inFunnelCount: number
  reachedFinalCount: number
  lostCount: number
  conversionPercent: number | null
}

export interface LeadFunnel {
  totalLeads: number
  funnelLeads: number
  lostCount: number
  /** Ustuni o'chirilgan lidlar — jim yo'qolmasin. */
  orphanCount: number
  overallConversionPercent: number | null
  stages: LeadFunnelStage[]
  losses: LeadFunnelLoss[]
  grades: LeadFunnelGrade[]
}

const EMPTY: LeadFunnel = {
  totalLeads: 0,
  funnelLeads: 0,
  lostCount: 0,
  orphanCount: 0,
  overallConversionPercent: null,
  stages: [],
  losses: [],
  grades: [],
}

/**
 * Voronka.
 *
 * @param lostStageIds Qaysi ustunlar "yo'qotildi" deb hisoblansin. Bazada bunday
 *   bayroq yo'q — tanlovni foydalanuvchi qiladi, server esa shu ro'yxatga qarab
 *   voronkani va sabab kesimini quradi.
 */
export async function getLeadFunnel(lostStageIds: string[] = []): Promise<LeadFunnel> {
  if (USE_MOCK) return EMPTY
  const { data } = await api.get<LeadFunnel>('/admin/leads/funnel', {
    params: lostStageIds.length ? { lostStages: lostStageIds.join(',') } : undefined,
  })
  return data
}
