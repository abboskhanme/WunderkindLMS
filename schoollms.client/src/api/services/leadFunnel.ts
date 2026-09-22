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

/**
 * Manba kesimi (sales-marketing.md §2.7): qo'lda kiritilganlar bitta qator,
 * ariza formasidan kelganlar — har bir ariza alohida qator.
 */
export interface LeadFunnelSource {
  source: 'manual' | 'survey'
  /** Faqat `survey` qatorida. */
  surveyId: string | null
  label: string
  total: number
  inFunnelCount: number
  reachedFinalCount: number
  lostCount: number
  conversionPercent: number | null
  /** Shu manbadan o'quvchiga aylangan (doskadan o'chirilgan) lidlar. */
  enrolledCount: number
  enrolledPercent: number | null
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
  sources: LeadFunnelSource[]
  /** O'quvchiga aylangan lidlar — doskadan o'chirilgan, son statistikada qoladi. */
  enrolledCount: number
  /** Jami lidlar: doskadagilar + o'quvchiga aylanganlar. */
  allTimeLeads: number
  enrolledPercent: number | null
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
  sources: [],
  enrolledCount: 0,
  allTimeLeads: 0,
  enrolledPercent: null,
}

/**
 * Voronka.
 *
 * @param lostStageIds Qaysi ustunlar "yo'qotildi" deb hisoblansin. Bazada bunday
 *   bayroq yo'q — tanlovni foydalanuvchi qiladi, server esa shu ro'yxatga qarab
 *   voronkani va sabab kesimini quradi.
 * @param surveyId Faqat shu ariza formasidan kelgan lidlar. Bo'sh — hammasi.
 */
export async function getLeadFunnel(lostStageIds: string[] = [], surveyId = ''): Promise<LeadFunnel> {
  if (USE_MOCK) return EMPTY
  const params: Record<string, string> = {}
  if (lostStageIds.length) params.lostStages = lostStageIds.join(',')
  if (surveyId) params.surveyId = surveyId
  const { data } = await api.get<LeadFunnel>('/admin/leads/funnel', { params })
  return data
}
