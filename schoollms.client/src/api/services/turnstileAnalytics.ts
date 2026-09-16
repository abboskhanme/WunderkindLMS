import { api } from '../client'

/**
 * Turniket hisobotlari (docs/modules/existing-module-gaps.md §4 — #11, #12, #13).
 *
 * Tiplar serverdagi `TurnstileAnalyticsDtos.cs` ning AYNAN nusxasi (camelCase).
 * Foiz va o'rtachalar SERVERDA hisoblanadi — bu yerda qayta hisoblanmaydi,
 * aks holda ikki ekran bir xil raqamni ikki xil yaxlitlab ko'rsatardi.
 */

// ---------- #11 Turniket analitikasi ----------

export interface TurnstileAttendanceSummary {
  /** Filtrga tushgan o'quvchilar */
  students: number
  /** Qurilma ID biriktirilganlar — hisobotga kiradiganlar */
  linked: number
  /** Biriktirilmaganlar — turniket ularni ko'rmaydi, hisobotdan tashqarida */
  unlinked: number
  entered: number
  neverEntered: number
  lateStudents: number
  lateDays: number
  earlyStudents: number
  earlyDays: number
  /** Chiqishi qayd etilmagan kunlar (bitta o'tish) */
  noExitDays: number
  attendanceRate: number
  lateRate: number
}

export interface TurnstileStudentRow {
  studentId: string
  fullName: string
  className: string
  deviceUserId: string
  daysEntered: number
  daysMissed: number
  lateDays: number
  earlyDays: number
  lateMinutes: number
  /** "HH:mm" */
  avgCheckIn: string
  /** "yyyy-MM-dd HH:mm" */
  lastSeen: string
  attendanceRate: number
}

export interface TurnstileAttendanceReport {
  from: string
  to: string
  schoolDays: number
  turnstileEnabled: boolean
  lastSync: string
  summary: TurnstileAttendanceSummary
  rows: TurnstileStudentRow[]
}

export interface TurnstileViolation {
  date: string
  studentId: string
  fullName: string
  className: string
  /** "late" | "early" */
  type: string
  checkIn: string
  checkOut: string
  /** Kutilgan vaqt "HH:mm" */
  expected: string
  minutes: number
}

export interface TurnstileViolationsPage {
  from: string
  to: string
  total: number
  lateTotal: number
  earlyTotal: number
  page: number
  pageSize: number
  pages: number
  items: TurnstileViolation[]
}

export interface TurnstileDaySummary {
  date: string
  expected: number
  entered: number
  missing: number
  late: number
  early: number
  lateRate: number
}

export interface TurnstileClassSummary {
  className: string
  students: number
  enteredDays: number
  lateDays: number
  earlyDays: number
  lateRate: number
  avgCheckIn: string
}

export interface TurnstileLateEarlySummary {
  from: string
  to: string
  schoolDays: number
  lateTotal: number
  earlyTotal: number
  lateRate: number
  earlyRate: number
  days: TurnstileDaySummary[]
  classes: TurnstileClassSummary[]
}

export interface TurnstileTodayLate {
  date: string
  turnstileEnabled: boolean
  lastSync: string
  schoolDay: boolean
  late: number
  entered: number
  expected: number
  notEntered: number
  /** O'sha kungi oxirgi o'tish "HH:mm" */
  lastEventAt: string
}

// ---------- #12 Kirib-chiqish statistikasi ----------

export interface TurnstileFlowBucket {
  /** "08:00" yoki "3-dars" */
  label: string
  startsAt: string
  entered: number
  exited: number
  passes: number
  enteredPct: number
}

export interface TurnstileFlowClass {
  className: string
  students: number
  entered: number
  exited: number
  peakLabel: string
  peakEntered: number
  /** Eng gavjum oraliqdan KEYIN kirganlar */
  afterPeak: number
  avgCheckIn: string
}

export interface TurnstileFlowReport {
  from: string
  to: string
  /** "hour" | "period" */
  groupBy: string
  days: number
  entered: number
  exited: number
  passes: number
  peakLabel: string
  peakEntered: number
  afterPeak: number
  afterPeakPct: number
  avgCheckIn: string
  earliestCheckIn: string
  latestCheckIn: string
  buckets: TurnstileFlowBucket[]
  classes: TurnstileFlowClass[]
}

// ---------- #13 Kunlik davomat hisoboti ----------

export interface TurnstileDailyClassRow {
  classId: string
  className: string
  expected: number
  linked: number
  turnstileEntered: number
  journalPresent: number
  journalAbsent: number
  /** Jurnalda UMUMAN belgilanmaganlar — "bor" ga hech qachon qo'shilmaydi */
  unchecked: number
  /** Turniket − jurnal (ishorali) */
  gap: number
  turnstileOnly: number
  journalOnly: number
  turnstilePct: number
  journalPct: number
}

export interface TurnstileDailyMismatch {
  studentId: string
  fullName: string
  className: string
  /** "turnstile-only" | "journal-only" */
  kind: string
  checkIn: string
  checkOut: string
  /** "present" | "absent" | "unchecked" */
  journalStatus: string
  reason: string
}

export interface TurnstileDailyReport {
  date: string
  schoolDay: boolean
  turnstileEnabled: boolean
  lastSync: string
  expected: number
  linked: number
  unlinked: number
  turnstileEntered: number
  journalPresent: number
  journalAbsent: number
  unchecked: number
  gap: number
  turnstileOnly: number
  journalOnly: number
  turnstilePct: number
  journalPct: number
  classes: TurnstileDailyClassRow[]
  mismatches: TurnstileDailyMismatch[]
  mismatchTotal: number
}

// ---------- So'rovlar ----------

const BASE = '/admin/turnstile-analytics'

export interface RangeParams {
  from: string
  to: string
  className?: string
}

/** #11 — davomat: kim kirdi, kim kirmadi, kim kechikdi. */
export async function getTurnstileAttendance(
  p: RangeParams & { status?: string },
): Promise<TurnstileAttendanceReport> {
  const { data } = await api.get<TurnstileAttendanceReport>(`${BASE}/attendance`, { params: p })
  return data
}

/** #11 — buzilishlar (sahifalangan). */
export async function getTurnstileViolations(
  p: RangeParams & { type?: string; page?: number; pageSize?: number },
): Promise<TurnstileViolationsPage> {
  const { data } = await api.get<TurnstileViolationsPage>(`${BASE}/violations`, { params: p })
  return data
}

/** #11 — kechikish/erta ketish jamlamasi (kunlar va sinflar kesimi). */
export async function getTurnstileLateEarly(p: RangeParams): Promise<TurnstileLateEarlySummary> {
  const { data } = await api.get<TurnstileLateEarlySummary>(`${BASE}/late-early/summary`, {
    params: p,
  })
  return data
}

/** #11 — bugungi kechikkanlar soni (sarlavhadagi raqam). */
export async function getTurnstileTodayLate(date?: string): Promise<TurnstileTodayLate> {
  const { data } = await api.get<TurnstileTodayLate>(`${BASE}/today-late-count`, {
    params: date ? { date } : undefined,
  })
  return data
}

/** #12 — kirib-chiqish taqsimoti. */
export async function getTurnstileFlow(
  p: RangeParams & { groupBy?: string },
): Promise<TurnstileFlowReport> {
  const { data } = await api.get<TurnstileFlowReport>(`${BASE}/flow`, { params: p })
  return data
}

/** #13 — kunlik davomat hisoboti (turniket ↔ jurnal). */
export async function getTurnstileDailyReport(date: string): Promise<TurnstileDailyReport> {
  const { data } = await api.get<TurnstileDailyReport>(`${BASE}/daily-report`, {
    params: { date },
  })
  return data
}
