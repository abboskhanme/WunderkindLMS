/**
 * Dars tahlili — API shartnomasi (docs/modules/lesson-analysis.md §5).
 *
 * Eski ilovaning `frontend/src/types/index.ts` dan AYNAN ko'chirilgan: maydonlar
 * camelCase, id'lar satr (bizda GUID), holatlar kichik harfda. Backend DTO'lari shu
 * faylga moslab yoziladi — ikki tomon bir manbadan o'qiydi.
 */

export type ObservationStatus = 'draft' | 'completed'

/** Tahlil uchun o'qituvchi — bizning `teachers` jadvalidan. */
export interface LaTeacher {
  id: string
  fullName: string
  phone: string | null
  /** O'qituvchi fanlari, ", " bilan qo'shilgan. */
  subject: string | null
  photoUrl: string | null
}

/** O'qituvchi + tahlil ko'rsatkichlari (`GET /teachers`, `GET /teachers/{id}`). */
export interface LaTeacherSummary extends LaTeacher {
  observationsCount: number
  drafts: number
  /** Yakunlangan tahlillar o'rtachasi; tahlil yo'q bo'lsa null. */
  rating: number | null
  lastObservationDate: string | null
}

/** O'qituvchining shu kungi darsi — formani to'ldirish uchun (`GET /teachers/{id}/lessons`). */
export interface LaTeacherLesson {
  period: number
  startTime: string | null
  endTime: string | null
  className: string
  subjectName: string
}

export interface LaCriterion {
  id: string
  text: string
  orderIndex: number
  isActive: boolean
}

export interface LaSection {
  id: string
  title: string
  orderIndex: number
  color: string
  criteria: LaCriterion[]
}

/** Tahlil varag'i (mijoz, 2026-10-07): o'z bo'limlari va mezonlari bilan. */
export interface LaTemplate {
  id: string
  name: string
  description: string | null
  orderIndex: number
  isActive: boolean
  /** Yangi tahlilda oldindan tanlanadi; bittasi standart. */
  isDefault: boolean
  sectionCount: number
  criterionCount: number
  observationCount: number
}

export interface LaDetail {
  criterionId: string
  isDone: boolean | null
  comment: string | null
  /** Javob berilgan paytdagi mezon matni (faqat o'qish) — mezon keyin tahrirlansa ham o'zgarmaydi. */
  criterionText?: string | null
  /** Javob berilgan paytdagi bo'lim nomi (faqat o'qish). */
  sectionTitle?: string | null
}

export interface LaObservationListItem {
  id: string
  teacherId: string
  teacherName: string
  observerName: string
  /** "yyyy-MM-dd" */
  lessonDate: string
  className: string | null
  subject: string | null
  status: ObservationStatus
  scoreDone: number
  scoreTotal: number
  scorePercent: number
  hasAiSummary: boolean
  templateName?: string | null
}

export interface LaObservation {
  id: string
  teacherId: string
  teacherName: string
  observerId: string
  observerName: string
  lessonDate: string
  lessonTime: string | null
  className: string | null
  subject: string | null
  status: ObservationStatus
  strengths: string | null
  growthAreas: string | null
  nextLessonAdvice: string | null
  /** Model qaytargan JSON matni (umumiy_xulosa, kuchli_tomonlar, rivojlantirish, tavsiyalar). */
  aiSummary: string | null
  aiError: string | null
  scoreDone: number
  scoreTotal: number
  scorePercent: number
  createdAt: string
  /** Joriy foydalanuvchi yakunlangan tahlilni tahrirlay oladimi (muallif yoki admin). */
  canEdit: boolean
  details: LaDetail[]
  /**
   * Mezonlar katalogi — `GET /criteria?includeInactive=true` bilan bir xil (nofaol mezonlar
   * ham, chunki eski tahlilda ular javob berilgan bo'lishi mumkin). Frontend qo'shdi,
   * 2026-09-27: o'qituvchi API'sida `/criteria` yo'q, `details` esa faqat `criterionId`
   * beradi — o'qituvchi sahifasi mezon matnini shu yerdan oladi. Teacher endpoint
   * (`GET /teacher/lesson-analysis/observations/{id}`) buni to'ldirishi kerak; staff
   * endpoint'i to'ldirmasa ham bo'ladi (staff sahifasi `/criteria` ni o'zi o'qiydi).
   */
  sections?: LaSection[]
  /** Qaysi varaq bo'yicha tahlil qilingan. */
  templateId?: string | null
  templateName?: string | null
}

/** `POST /observations`, `PATCH /observations/{id}` tanasi. */
export interface LaObservationInput {
  teacherId: string
  lessonDate: string
  lessonTime: string | null
  className: string | null
  subject: string | null
  strengths: string | null
  growthAreas: string | null
  nextLessonAdvice: string | null
  details: LaDetail[]
  /** Tahlil varag'i; berilmasa — standart varaq. Javob berilgach almashtirib bo'lmaydi. */
  templateId?: string | null
}

export interface LaRatingRow {
  teacherId: string
  teacherName: string
  subject: string | null
  observationsCount: number
  rating: number
  lastObservationDate: string | null
}

/** `GET /rating` */
export interface LaRatingBoard {
  rows: LaRatingRow[]
  /** Faol, lekin bu oraliqda tahlil qilinmagan o'qituvchilar. */
  notEvaluated: LaTeacher[]
}

/** `GET /dashboard` */
export interface LaDashboard {
  teachersActive: number
  observationsTotal: number
  observationsLast30Days: number
  averageRating: number
  drafts: number
  notEvaluated: string[]
  top: LaRatingRow[]
  bottom: LaRatingRow[]
}

export interface LaMonthlyPoint {
  /** "YYYY-MM", xronologik. */
  month: string
  count: number
  average: number
}

export interface LaSectionProgress {
  sectionId: string
  title: string
  color: string
  done: number
  total: number
  percent: number
}

export interface LaDistributionBucket {
  /** "0–59%" | "60–74%" | "75–89%" | "90–100%" */
  bucket: string
  count: number
}

export interface LaTopTeacher {
  teacherId: string
  teacherName: string
  subject: string | null
  rating: number
  observationsCount: number
}

export interface LaCriterionMiss {
  criterionId: string
  text: string
  sectionTitle: string
  missed: number
  total: number
  missPercent: number
}

/** `GET /analytics` (o'qituvchi uchun — faqat o'zi). */
export interface LaAnalytics {
  monthly: LaMonthlyPoint[]
  sections: LaSectionProgress[]
  distribution: LaDistributionBucket[]
  topTeachers: LaTopTeacher[]
  criteriaMisses: LaCriterionMiss[]
}

/**
 * `POST /criteria` tanasi (faqat admin/superadmin).
 * Frontend qo'shdi (2026-09-27): §5 da endpoint bor, tana shakli yo'q edi.
 */
export interface LaCriterionCreateInput {
  sectionId: string
  text: string
  /** Berilmasa — server bo'lim oxiriga qo'yadi. */
  orderIndex?: number | null
}

/** `PATCH /criteria/{id}` tanasi — faqat berilgan maydonlar o'zgaradi (o'chirish yo'q). */
export interface LaCriterionPatchInput {
  text?: string
  orderIndex?: number
  isActive?: boolean
  /** Shu varaq ichidagi boshqa bo'limga ko'chirish. */
  sectionId?: string
}

export interface LaTemplateCreateInput {
  name: string
  description?: string | null
  /** Shu varaqning bo'limlari va faol mezonlaridan nusxa. */
  copyFromId?: string | null
}

export interface LaTemplatePatchInput {
  name?: string
  description?: string | null
  isActive?: boolean
  isDefault?: boolean
  orderIndex?: number
}

export interface LaSectionCreateInput {
  templateId: string
  title: string
  color?: string
}

export interface LaSectionPatchInput {
  title?: string
  color?: string
  orderIndex?: number
}
