/**
 * Words and small pure helpers for the Test bazasi screens
 * (`docs/modules/admission-and-testing.md` §3.1 screens 3–4, unit C1).
 *
 * The service (`api/services/admissionBanks.ts`) carries the contract; this
 * file carries the Uzbek and the input parsing, so the register, the detail
 * screen and the modals can never spell a grade or a state two ways.
 *
 * No components here: a file that exports both trips `react-refresh`.
 */
import type {
  BankSettings,
  BankState,
  QuestionBank,
  QuestionImportReason,
} from '@/api/services/admissionBanks'

/**
 * The admin permission key (§4.1). Registered in `config/constants.ts` by the
 * wiring pass; the pages only need the string.
 */
export const ADMISSION_PERM = 'admission'

/** Admin and superadmin carry no `permissions` list — they are not limited. */
export function canManageAdmission(permissions: string[] | undefined): boolean {
  return !permissions || permissions.includes(ADMISSION_PERM)
}

/** §3.1 routes. */
export const BANKS_PATH = '/admin/admission/banks'

export function bankPath(id: string): string {
  return `${BANKS_PATH}/${id}`
}

/** One dash everywhere a value is absent. */
export const DASH = '—'

/** §5.1: `grade between 0 and 11`. */
export const GRADES: readonly number[] = Array.from({ length: 12 }, (_, i) => i)

/** `0` is nol sinf (preparatory), never "unknown" — the same wording as the survey screens. */
export function gradeLabel(grade: number): string {
  return grade === 0 ? 'Nol sinf' : `${grade}-sinf`
}

/** "5-sinf · Matematika" — how a bank is named everywhere on screen. */
export function bankTitle(bank: Pick<QuestionBank, 'grade' | 'subjectName'>): string {
  const subject = bank.subjectName?.trim() || 'Fan'
  return `${gradeLabel(bank.grade)} · ${subject}`
}

// ---------------------------------------------------------------------------
//  State badge — rendered from the server's value, never recomputed
// ---------------------------------------------------------------------------

const STATE_LABELS: Record<BankState, string> = {
  unconfigured: 'Sozlanmagan',
  notEnough: 'Savol yetarli emas',
  ready: 'Tayyor',
}

const STATE_TONES: Record<BankState, string> = {
  unconfigured: 'bg-slate-100 text-slate-500',
  notEnough: 'bg-amber-50 text-amber-700',
  ready: 'bg-emerald-50 text-emerald-700',
}

/** An unknown state from a newer server still prints as something readable. */
export function bankStateLabel(state: BankState): string {
  return STATE_LABELS[state] ?? state
}

export function bankStateTone(state: BankState): string {
  return STATE_TONES[state] ?? 'bg-slate-100 text-slate-500'
}

// ---------------------------------------------------------------------------
//  Numbers
// ---------------------------------------------------------------------------

/** 1.5 → "1,5"; the same locale the money formatter uses. */
export function formatPoints(value: number | null): string {
  if (value === null || value === undefined) return DASH
  return value.toLocaleString('ru-RU', { maximumFractionDigits: 2 })
}

export function formatMinutes(value: number | null): string {
  if (value === null || value === undefined) return DASH
  return `${value} daq`
}

export function formatCount(value: number | null): string {
  if (value === null || value === undefined) return DASH
  return String(value)
}

// ---------------------------------------------------------------------------
//  Options
// ---------------------------------------------------------------------------

/** §5.3: `order` 0 → A … 5 → F. */
const OPTION_LETTERS = 'ABCDEF'

export function optionLetter(index: number): string {
  return OPTION_LETTERS[index] ?? String(index + 1)
}

// ---------------------------------------------------------------------------
//  Import reasons — §6.2, the closed set of seven, one chip each
// ---------------------------------------------------------------------------

const IMPORT_REASON_LABELS: Record<QuestionImportReason, string> = {
  emptyText: "Savol matni bo'sh",
  tooFewOptions: 'Variant 2 tadan kam',
  tooManyOptions: "Variant 6 tadan ko'p",
  noCorrect: "To'g'ri javob ko'rsatilmagan",
  badCorrect: "To'g'ri javob noto'g'ri",
  duplicateOption: 'Bir xil variant',
  badImageUrl: "Rasm havolasi noto'g'ri",
}

export function importReasonLabel(reason: QuestionImportReason): string {
  return IMPORT_REASON_LABELS[reason] ?? reason
}

// ---------------------------------------------------------------------------
//  Bank settings — the three numeric fields (§5.1)
// ---------------------------------------------------------------------------

/** What the inputs hold: text, so a half-typed "1," is not lost. */
export interface SettingsDraft {
  questionsPerTest: string
  timeLimitMin: string
  pointsPerCorrect: string
}

export type SettingsField = keyof SettingsDraft

export const SETTINGS_LABELS: Record<SettingsField, string> = {
  questionsPerTest: 'Testdagi savollar',
  timeLimitMin: 'Vaqt (daqiqa)',
  pointsPerCorrect: "Ball (1 to'g'ri javob)",
}

export const EMPTY_SETTINGS_DRAFT: SettingsDraft = {
  questionsPerTest: '',
  timeLimitMin: '',
  pointsPerCorrect: '',
}

export function settingsDraftOf(settings: BankSettings): SettingsDraft {
  const text = (value: number | null) => (value === null || value === undefined ? '' : String(value))
  return {
    questionsPerTest: text(settings.questionsPerTest),
    timeLimitMin: text(settings.timeLimitMin),
    pointsPerCorrect: text(settings.pointsPerCorrect),
  }
}

export type SettingsParse =
  | { ok: true; value: BankSettings }
  | { ok: false; field: SettingsField; message: string }

const WHOLE = /^\d+$/
/** `numeric(6,2)`: up to four whole digits and two decimals; comma or dot. */
const DECIMAL = /^\d{1,4}([.,]\d{1,2})?$/

/**
 * The database checks of §5.1, mirrored so the user hears about a typo
 * before a round trip: `questions_per_test >= 1`,
 * `time_limit_min between 1 and 600`, `points_per_correct > 0`.
 * An empty field is `null` — "not configured yet" is a legitimate value.
 */
export function parseSettingsDraft(draft: SettingsDraft): SettingsParse {
  const perTest = draft.questionsPerTest.trim()
  const minutes = draft.timeLimitMin.trim()
  const points = draft.pointsPerCorrect.trim()

  let questionsPerTest: number | null = null
  if (perTest) {
    const n = Number(perTest)
    if (!WHOLE.test(perTest) || n < 1 || n > 2_147_483_647) {
      return {
        ok: false,
        field: 'questionsPerTest',
        message: 'Testdagi savollar soni — 1 yoki undan katta butun son',
      }
    }
    questionsPerTest = n
  }

  let timeLimitMin: number | null = null
  if (minutes) {
    const n = Number(minutes)
    if (!WHOLE.test(minutes) || n < 1 || n > 600) {
      return { ok: false, field: 'timeLimitMin', message: 'Vaqt — 1 dan 600 gacha daqiqa' }
    }
    timeLimitMin = n
  }

  let pointsPerCorrect: number | null = null
  if (points) {
    const n = Number(points.replace(',', '.'))
    if (!DECIMAL.test(points) || !(n > 0)) {
      return {
        ok: false,
        field: 'pointsPerCorrect',
        message: "Ball — 0 dan katta son, ko'pi bilan 2 xonali kasr (masalan 1,5)",
      }
    }
    pointsPerCorrect = n
  }

  return { ok: true, value: { questionsPerTest, timeLimitMin, pointsPerCorrect } }
}

export function sameSettings(a: BankSettings, b: BankSettings): boolean {
  return (
    a.questionsPerTest === b.questionsPerTest &&
    a.timeLimitMin === b.timeLimitMin &&
    a.pointsPerCorrect === b.pointsPerCorrect
  )
}

// ---------------------------------------------------------------------------
//  Navigation state — "Baza yaratildi" carried from the register to the bank
// ---------------------------------------------------------------------------

export interface BankNavState {
  notice: string
}

export function noticeFromState(state: unknown): string | null {
  if (typeof state !== 'object' || state === null || !('notice' in state)) return null
  return typeof state.notice === 'string' && state.notice ? state.notice : null
}
