import { api } from '../client'

// Xodimlar samaradorligi — KPI (client, 2026-10-07) — `/api/admin/kpi`, gated by the `kpi` key.

export interface KpiWeights {
  tasks: number
  lessonAnalysis: number
  journal: number
  parentNps: number
  staffNps: number
  penalties: number
  attendance: number
}

export interface KpiRow {
  employeeId: string
  fullName: string
  position: string | null
  hrRole: string
  teacherId: string | null
  total: number | null
  tasks: number | null
  tasksOnTime: number
  tasksTotal: number
  lessonAnalysis: number | null
  observations: number
  journal: number | null
  parentNps: number | null
  parentNpsRaw: number | null
  parentAnswers: number
  staffNps: number | null
  staffNpsRaw: number | null
  staffAnswers: number
  penalties: number | null
  penaltyCount: number
  attendance: number | null
  absentDays: number
  lateDays: number
}

export interface KpiReport {
  month: string
  quarter: number | null
  weights: KpiWeights
  rows: KpiRow[]
  schoolNps: number | null
  schoolAnswers: number
  managementNps: number | null
  managementAnswers: number
}

export async function getKpi(month: string): Promise<KpiReport> {
  const { data } = await api.get<KpiReport>('/admin/kpi', { params: { month } })
  return data
}

export async function saveKpiWeights(weights: KpiWeights): Promise<KpiWeights> {
  const { data } = await api.put<KpiWeights>('/admin/kpi/weights', weights)
  return data
}
