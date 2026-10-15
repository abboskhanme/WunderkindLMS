import type {
  AddProgressInput,
  CompleteTaskInput,
  CreateTaskInput,
  HrBatchResult,
  HrDateString,
  HrEmployee,
  HrEmployeeCreatePayload,
  HrEmployeeCreateResult,
  HrEmployeeFilters,
  HrEmployeePage,
  HrEmployeeReport,
  HrEmployeeTree,
  HrEmployeeTreeFilters,
  HrEmployeeUpdatePayload,
  HrInvite,
  HrMyTaskFilters,
  HrPosition,
  HrPositionCreatePayload,
  HrPositionFilters,
  HrPositionPage,
  HrPositionUpdatePayload,
  HrReportFilters,
  HrSettings,
  HrSettingsPayload,
  HrSummaryReport,
  HrAccess,
  HrAttachmentKind,
  HrTask,
  HrTaskAttachment,
  HrTaskBatchPayload,
  HrTaskCancelPayload,
  HrTaskCreatePayload,
  HrTaskDetail,
  HrTaskFilters,
  HrTaskPage,
  HrTaskReturnPayload,
  HrTaskTemplate,
  HrTaskTemplatePage,
  HrTaskUpdatePayload,
  HrTemplateBatchPayload,
  HrTemplateCreatePayload,
  HrTemplateFilters,
  HrTemplateUpdatePayload,
  HrUserCandidate,
  Me,
  Page,
  Proof,
  Summary,
  Task,
  TaskAttachment,
  TaskList,
  UploadedProof,
} from '@/types/hrTasks'
import { HR_DEFAULT_PAGE_SIZE } from '@/types/hrTasks'
import { api } from '../client'

/**
 * HR tasks — API (docs/modules/hr-tasks.md §6).
 *
 * Mirrors every call in WunderkindHR's `frontend/src/entities/hr/api.ts`: panel under
 * `/api/admin/hr/...`, the employee's own endpoints under `/api/hr/me/...`, sub-paths
 * unchanged. Response shapes live in `@/types/hrTasks` (camelCase).
 *
 * Array params go out as `?status=open&status=returned` (the old `REPEAT_ARRAY_PARAMS`);
 * there is no `?page=` — keyset paging (`cursor`).
 */

const ADMIN = '/admin/hr'
const ME = '/hr/me'

/** `?status=a&status=b` — how ASP.NET binds an array. */
const REPEAT_ARRAY_PARAMS = { paramsSerializer: { indexes: null } } as const

/** Upload timeout — a 10 MB voice note takes longer than 30 s on a slow mobile network. */
const UPLOAD_TIMEOUT_MS = 120_000

type ParamValue = string | number | boolean | readonly string[] | null | undefined

/** Empty values, empty arrays and blank strings are left out of the query ("no filter"). */
function clean(params: Record<string, ParamValue>) {
  const out: Record<string, string | number | boolean | readonly string[]> = {}
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === null) continue
    if (typeof v === 'string' && v.trim() === '') continue
    if (Array.isArray(v) && v.length === 0) continue
    out[k] = typeof v === 'string' ? v.trim() : v
  }
  return out
}

/** Always normalises the response to `Page<T>` (bare array, `{items}` or a full page). */
function toPage<T>(data: Page<T> | T[] | { items?: T[] } | null | undefined): Page<T> {
  if (Array.isArray(data)) return { items: data, nextCursor: null, hasMore: false, total: data.length }
  const page = (data ?? {}) as Partial<Page<T>>
  return {
    items: page.items ?? [],
    nextCursor: page.nextCursor ?? null,
    hasMore: page.hasMore ?? false,
    total: page.total,
  }
}

/* ------------------------------------------------------------------- errors */

interface HttpErrorShape {
  response?: { status?: number; data?: unknown }
  message?: string
}

function bodyMessage(data: unknown): string | null {
  if (typeof data === 'string') return data.trim() || null
  if (typeof data !== 'object' || data === null) return null
  const body = data as { message?: unknown; detail?: unknown; title?: unknown; errors?: unknown }
  if (typeof body.message === 'string' && body.message) return body.message
  if (typeof body.detail === 'string' && body.detail) return body.detail
  if (body.errors && typeof body.errors === 'object') {
    const first = Object.values(body.errors as Record<string, unknown>)[0]
    if (Array.isArray(first) && typeof first[0] === 'string') return first[0]
  }
  if (typeof body.title === 'string' && body.title) return body.title
  return null
}

/** HTTP status (undefined on a network failure). */
export function hrErrorStatus(err: unknown): number | undefined {
  return typeof err === 'object' && err !== null ? (err as HttpErrorShape).response?.status : undefined
}

/** Backend error code (`hr_not_employee`, `hr_manager_cycle`, `hr_proof_required` …). */
export function hrErrorCode(err: unknown): string | null {
  if (typeof err !== 'object' || err === null) return null
  const data = (err as HttpErrorShape).response?.data
  if (typeof data !== 'object' || data === null) return null
  const body = data as { code?: unknown; type?: unknown; error?: unknown }
  for (const value of [body.code, body.error, body.type]) {
    if (typeof value === 'string' && value.startsWith('hr_')) return value
  }
  return typeof body.code === 'string' ? body.code : null
}

/** Uzbek error text shown to the user. */
export function hrErrorMessage(err: unknown, fallback = 'Kutilmagan xatolik'): string {
  if (typeof err !== 'object' || err === null) return fallback
  const e = err as HttpErrorShape
  const fromBody = bodyMessage(e.response?.data)
  if (fromBody) return fromBody
  const status = e.response?.status
  if (status === 403) return "Bu amal uchun ruxsatingiz yo'q"
  if (status === 404) return 'Topilmadi'
  if (status === 409) return "Amalni bajarib bo'lmadi — ma'lumot o'zgargan"
  if (status === 422 || status === 400) return "Kiritilgan ma'lumotlarni tekshiring"
  if (status && status >= 500) return `Server xatosi (${status})`
  if (!e.response && e.message === 'Network Error') return "Server bilan aloqa yo'q"
  return err instanceof Error && err.message ? err.message : fallback
}

/** `/api/hr/me/*` — the user has no HR employee row (403 `hr_not_employee`). */
export function hrIsNotEmployee(err: unknown): boolean {
  return hrErrorCode(err) === 'hr_not_employee' || hrErrorStatus(err) === 403
}

/* ---------------------------------------------------------------- download */

/**
 * Fetches a protected file (CSV) through axios — with the JWT header — and saves it in
 * the browser (the `lessonAnalysis.ts` pattern). File name from `Content-Disposition`.
 */
async function downloadFile(path: string, params: Record<string, ParamValue>, fallbackName: string): Promise<void> {
  let res
  try {
    res = await api.get<Blob>(path, { params: clean(params), responseType: 'blob' })
  } catch (err) {
    const data = (err as HttpErrorShape).response?.data
    if (data instanceof Blob) {
      let text = ''
      try {
        text = await data.text()
      } catch {
        /* unreadable — generic message */
      }
      let parsed: unknown = text
      try {
        parsed = JSON.parse(text)
      } catch {
        /* not JSON */
      }
      throw new Error(bodyMessage(parsed) ?? hrErrorMessage(err, "Faylni yuklab bo'lmadi"), { cause: err })
    }
    throw err
  }
  const blob = res.data
  if (blob.type.includes('application/json')) throw new Error("Faylni yuklab bo'lmadi")

  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const star = cd.match(/filename\*=UTF-8''([^;]+)/i)
  const plain = cd.match(/filename="?([^";]+)"?/i)
  a.download = star ? decodeURIComponent(star[1]) : (plain?.[1] ?? fallbackName)
  document.body.appendChild(a)
  a.click()
  a.remove()
  window.setTimeout(() => URL.revokeObjectURL(url), 30_000)
}

/* ------------------------------------------------------------------ access */

/** `GET /access` — director?, may manage?, scope and the caller's own HR row. */
export async function getAccess(): Promise<HrAccess> {
  const { data } = await api.get<HrAccess>(`${ADMIN}/access`)
  return data
}

/* --------------------------------------------------------------- positions */

export async function getPositions(filters: HrPositionFilters = {}): Promise<HrPositionPage> {
  const { data } = await api.get<HrPositionPage>(`${ADMIN}/positions`, {
    params: clean({
      q: filters.q,
      isActive: filters.isActive,
      limit: filters.limit ?? HR_DEFAULT_PAGE_SIZE,
      cursor: filters.cursor,
    }),
  })
  return toPage(data)
}

export async function createPosition(payload: HrPositionCreatePayload): Promise<HrPosition> {
  const { data } = await api.post<HrPosition>(`${ADMIN}/positions`, payload)
  return data
}

export async function updatePosition(id: string, payload: HrPositionUpdatePayload): Promise<HrPosition> {
  const { data } = await api.patch<HrPosition>(`${ADMIN}/positions/${id}`, payload)
  return data
}

/** Deactivate — NOT delete (the position stays in history). */
export async function deactivatePosition(id: string): Promise<HrPosition | null> {
  const { data } = await api.post<HrPosition | ''>(`${ADMIN}/positions/${id}/deactivate`)
  return typeof data === 'object' && data !== null ? data : null
}

/* --------------------------------------------------------------- employees */

function employeeParams(filters: HrEmployeeTreeFilters) {
  return {
    q: filters.q,
    positionId: filters.positionId,
    isActive: filters.isActive,
    linked: filters.linked,
    branch: filters.branch,
  }
}

export async function getEmployees(filters: HrEmployeeFilters = {}): Promise<HrEmployeePage> {
  const { data } = await api.get<HrEmployeePage>(`${ADMIN}/employees`, {
    params: clean({
      ...employeeParams(filters),
      limit: filters.limit ?? HR_DEFAULT_PAGE_SIZE,
      cursor: filters.cursor,
      withTotal: filters.withTotal,
    }),
  })
  return toPage(data)
}

/** Reporting tree — both arrays are ALWAYS present, even on a partial response. */
export async function getEmployeeTree(filters: HrEmployeeTreeFilters = {}): Promise<HrEmployeeTree> {
  const { data } = await api.get<Partial<HrEmployeeTree> | null>(`${ADMIN}/employees/tree`, {
    params: clean(employeeParams(filters)),
  })
  return { roots: data?.roots ?? [], unassigned: data?.unassigned ?? [] }
}

export async function getEmployee(id: string): Promise<HrEmployee> {
  const { data } = await api.get<HrEmployee>(`${ADMIN}/employees/${id}`)
  return data
}

/** Makes an existing user (`userId`) — or a new `staff` user from name + phone — an HR employee. */
export async function createEmployee(payload: HrEmployeeCreatePayload): Promise<HrEmployeeCreateResult> {
  const { data } = await api.post<HrEmployeeCreateResult>(`${ADMIN}/employees`, payload)
  return data
}

export async function updateEmployee(id: string, payload: HrEmployeeUpdatePayload): Promise<HrEmployee> {
  const { data } = await api.patch<HrEmployee>(`${ADMIN}/employees/${id}`, payload)
  return data
}

/** Replaces the manager list WHOLESALE; a cycle is 409 `hr_manager_cycle`. */
export async function setEmployeeManagers(id: string, managerIds: string[]): Promise<HrEmployee | null> {
  const { data } = await api.put<HrEmployee | ''>(`${ADMIN}/employees/${id}/managers`, { managerIds })
  return typeof data === 'object' && data !== null ? data : null
}

/** Dismissal: HR row inactive, open tasks cancelled (the user account is untouched). */
export async function deactivateEmployee(id: string): Promise<HrEmployee> {
  const { data } = await api.post<HrEmployee>(`${ADMIN}/employees/${id}/deactivate`)
  return data
}

/** One-time Telegram link (our link-code mechanism). */
export async function issueInvite(id: string): Promise<HrInvite> {
  const { data } = await api.post<HrInvite>(`${ADMIN}/employees/${id}/invite`)
  return data
}

/** "Pick an existing user" — staff, teacher, admin, cashier. */
export async function searchUserCandidates(q: string): Promise<HrUserCandidate[]> {
  const { data } = await api.get<HrUserCandidate[] | Page<HrUserCandidate>>(`${ADMIN}/users`, {
    params: clean({ q, limit: 50 }),
  })
  return toPage(data).items
}

/* ------------------------------------------------------ recurring (template) */

export async function getTemplates(filters: HrTemplateFilters = {}): Promise<HrTaskTemplatePage> {
  const { data } = await api.get<HrTaskTemplatePage>(`${ADMIN}/templates`, {
    params: clean({
      q: filters.q,
      isActive: filters.isActive,
      targetType: filters.targetType,
      targetEmployeeId: filters.targetEmployeeId,
      targetPositionId: filters.targetPositionId,
      recurrence: filters.recurrence,
      branch: filters.branch,
      limit: filters.limit ?? HR_DEFAULT_PAGE_SIZE,
      cursor: filters.cursor,
      withTotal: filters.withTotal,
    }),
  })
  return toPage(data)
}

export async function createTemplate(payload: HrTemplateCreatePayload): Promise<HrTaskTemplate> {
  const { data } = await api.post<HrTaskTemplate>(`${ADMIN}/templates`, payload)
  return data
}

/** A SEPARATE template per employee; an inactive employee comes back in `skipped`. */
export async function createTemplateBatch(payload: HrTemplateBatchPayload): Promise<HrBatchResult> {
  const { data } = await api.post<HrBatchResult>(`${ADMIN}/templates/batch`, payload)
  return { ...data, skipped: data.skipped ?? [] }
}

export async function updateTemplate(id: string, payload: HrTemplateUpdatePayload): Promise<HrTaskTemplate> {
  const { data } = await api.patch<HrTaskTemplate>(`${ADMIN}/templates/${id}`, payload)
  return data
}

export async function toggleTemplate(id: string): Promise<HrTaskTemplate> {
  const { data } = await api.post<HrTaskTemplate>(`${ADMIN}/templates/${id}/toggle`)
  return data
}

/** Tasks created from a deleted template stay in history (`templateId = null`). */
export async function deleteTemplate(id: string): Promise<void> {
  await api.delete(`${ADMIN}/templates/${id}`)
}

/* ------------------------------------------------------------------- tasks */

export async function getTasks(filters: HrTaskFilters = {}): Promise<HrTaskPage> {
  const { data } = await api.get<HrTaskPage>(`${ADMIN}/tasks`, {
    params: clean({
      dateFrom: filters.dateFrom,
      dateTo: filters.dateTo,
      employeeId: filters.employeeId,
      positionId: filters.positionId,
      status: filters.status,
      source: filters.source,
      overdue: filters.overdue,
      branch: filters.branch,
      accepted: filters.accepted,
      q: filters.q,
      sort: filters.sort ?? '-due_at',
      view: filters.view,
      limit: filters.limit ?? HR_DEFAULT_PAGE_SIZE,
      cursor: filters.cursor,
      withTotal: filters.withTotal,
    }),
    ...REPEAT_ARRAY_PARAMS,
  })
  return toPage(data)
}

/** With proofs, KPI steps and attachments. */
export async function getTask(id: string): Promise<HrTaskDetail> {
  const { data } = await api.get<HrTaskDetail>(`${ADMIN}/tasks/${id}`)
  return { ...data, proofs: data.proofs ?? [], progress: data.progress ?? [], attachments: data.attachments ?? [] }
}

/** One-off task — the employee gets a Telegram message immediately. */
export async function createTask(payload: HrTaskCreatePayload): Promise<HrTask> {
  const { data } = await api.post<HrTask>(`${ADMIN}/tasks`, payload)
  return data
}

/** `employeeIds × items` rows in one transaction; an inactive employee lands in `skipped`. */
export async function createTaskBatch(payload: HrTaskBatchPayload): Promise<HrBatchResult> {
  const { data } = await api.post<HrBatchResult>(`${ADMIN}/tasks/batch`, payload)
  return { ...data, skipped: data.skipped ?? [] }
}

/** Title / description / due date only. */
export async function updateTask(id: string, payload: HrTaskUpdatePayload): Promise<HrTask> {
  const { data } = await api.patch<HrTask>(`${ADMIN}/tasks/${id}`, payload)
  return data
}

/** Return — the comment reaches the employee immediately. */
export async function returnTask(id: string, payload: HrTaskReturnPayload): Promise<HrTask> {
  const { data } = await api.post<HrTask>(`${ADMIN}/tasks/${id}/return`, payload)
  return data
}

/** Cancel — a final state. */
export async function cancelTask(id: string, payload: HrTaskCancelPayload = {}): Promise<HrTask> {
  const { data } = await api.post<HrTask>(`${ADMIN}/tasks/${id}/cancel`, { comment: payload.comment ?? null })
  return data
}

/* ---------------------------------------------- attachment (manager's voice) */

/** A `MediaRecorder` `Blob` has no name — derive one from the MIME type. */
const VOICE_EXTENSION: Record<string, string> = {
  'audio/webm': 'webm',
  'audio/ogg': 'ogg',
  'audio/mp4': 'm4a',
  'audio/aac': 'm4a',
  'audio/mpeg': 'mp3',
  'audio/wav': 'wav',
  'audio/x-wav': 'wav',
}

function attachmentFileName(file: File | Blob, given: string | undefined): string {
  if (given) return given
  if ('name' in file && file.name) return file.name
  const type = file.type.split(';')[0]?.trim().toLowerCase() ?? ''
  return `ovoz.${VOICE_EXTENSION[type] ?? 'bin'}`
}

export interface HrUploadVars {
  file: File | Blob
  filename?: string
  /** 0..100 — progress for long recordings. */
  onProgress?: (percent: number) => void
  signal?: AbortSignal
}

function uploadConfig(vars: HrUploadVars) {
  return {
    timeout: UPLOAD_TIMEOUT_MS,
    signal: vars.signal,
    // Replaces the default `application/json`; axios adds the `boundary` (students.ts pattern).
    headers: { 'Content-Type': 'multipart/form-data' },
    onUploadProgress: (event: { loaded: number; total?: number }) => {
      if (!vars.onProgress || !event.total) return
      vars.onProgress(Math.min(100, Math.round((event.loaded / event.total) * 100)))
    },
  }
}

/**
 * DRAFT attachment (multipart): uploaded BEFORE the task exists; the returned `id` goes
 * into `HrTaskBatchItem.attachmentId` and is shared by N tasks.
 */
export async function uploadAttachment(vars: HrUploadVars & { kind?: HrAttachmentKind }): Promise<HrTaskAttachment> {
  const form = new FormData()
  form.append('file', vars.file, attachmentFileName(vars.file, vars.filename))
  form.append('kind', vars.kind ?? 'voice')
  const { data } = await api.post<HrTaskAttachment>(`${ADMIN}/attachments`, form, uploadConfig(vars))
  return data
}

export async function deleteAttachment(id: string): Promise<void> {
  await api.delete(`${ADMIN}/attachments/${id}`)
}

/* ----------------------------------------------------------------- reports */

function reportParams(range: HrReportFilters) {
  return { dateFrom: range.dateFrom, dateTo: range.dateTo, branch: range.branch }
}

export async function getSummaryReport(range: HrReportFilters): Promise<HrSummaryReport> {
  const { data } = await api.get<HrSummaryReport>(`${ADMIN}/reports/summary`, { params: clean(reportParams(range)) })
  return data
}

export async function getEmployeeReport(range: HrReportFilters): Promise<HrEmployeeReport> {
  const { data } = await api.get<HrEmployeeReport>(`${ADMIN}/reports/employees`, {
    params: clean(reportParams(range)),
  })
  return { ...data, items: data.items ?? [] }
}

/** CSV copy of the per-employee report (JWT, blob). */
export async function downloadReportCsv(range: HrReportFilters): Promise<void> {
  await downloadFile(
    `${ADMIN}/reports/export.csv`,
    reportParams(range),
    `hr-hisobot-${range.dateFrom}_${range.dateTo}.csv`,
  )
}

/** Excel copy of the same report (`reports/export.xlsx`). */
export async function downloadReportXlsx(range: HrReportFilters): Promise<void> {
  await downloadFile(
    `${ADMIN}/reports/export.xlsx`,
    reportParams(range),
    `hr-hisobot-${range.dateFrom}_${range.dateTo}.xlsx`,
  )
}

/* ---------------------------------------------------------------- settings */

export async function getSettings(): Promise<HrSettings> {
  const { data } = await api.get<HrSettings>(`${ADMIN}/settings`)
  return data
}

/** Replaced wholesale. */
export async function updateSettings(payload: HrSettingsPayload): Promise<HrSettings> {
  const { data } = await api.put<HrSettings>(`${ADMIN}/settings`, payload)
  return data
}

/* ============================================================================
 * The employee's own endpoints — `/api/hr/me/*` (old `miniapp.py` + `/hr/me/tasks` in `router.py`).
 * A user without an HR row gets 403 `hr_not_employee` — see `hrIsNotEmployee()`.
 * ========================================================================== */

/**
 * A "Mening topshiriqlarim" row — the common part of the panel (`HrTask`) and Mini App
 * (`Task`) shapes, so the page works whichever one the backend returns.
 */
export interface HrMyTask {
  id: string
  title: string
  description: string | null
  kind: Task['kind']
  status: Task['status']
  source: Task['source']
  dueAt: string | null
  isOverdue: boolean | null
  isLate: boolean
  targetAmount: string | number | null
  currentAmount: string | number | null
  unit: string | null
  employeeComment: string | null
  directorComment: string | null
  completedAt: string | null
  acceptedAt: string | null
  needsAcceptance: boolean
  voiceUrl: string | null
  attachments: TaskAttachment[]
  proofs: Proof[]
}

type RawMyTask = Partial<Omit<HrTask, 'currentAmount'>> &
  Partial<Omit<Task, 'currentAmount' | 'targetAmount'>> & {
    currentAmount?: string | number | null
    targetAmount?: string | number | null
  }

function toMyTask(raw: RawMyTask): HrMyTask {
  return {
    id: raw.id ?? '',
    title: raw.title ?? '',
    description: raw.description ?? null,
    kind: raw.kind ?? 'check',
    status: raw.status ?? 'open',
    source: raw.source ?? 'director',
    dueAt: raw.dueAt ?? null,
    isOverdue: typeof raw.isOverdue === 'boolean' ? raw.isOverdue : null,
    isLate: raw.isLate ?? false,
    targetAmount: raw.targetAmount ?? null,
    currentAmount: raw.currentAmount ?? null,
    unit: raw.unit ?? null,
    employeeComment: raw.employeeComment ?? null,
    directorComment: raw.directorComment ?? null,
    completedAt: raw.completedAt ?? null,
    acceptedAt: raw.acceptedAt ?? null,
    needsAcceptance: raw.needsAcceptance === true,
    voiceUrl: raw.voiceUrl ?? null,
    attachments: raw.attachments ?? [],
    proofs: raw.proofs ?? [],
  }
}

/** `GET /api/hr/me` — the employee card and the server's local "today". */
export async function getMe(): Promise<Me> {
  const { data } = await api.get<Me>(ME)
  return data
}

/**
 * `GET /api/hr/me/tasks` — own tasks (panel filters: status, search, keyset).
 * A bare array, a `Page` or the Mini App `{date, items}` shape are all normalised to a page.
 */
export async function getMyTasks(filters: HrMyTaskFilters = {}): Promise<Page<HrMyTask>> {
  const { data } = await api.get<Page<RawMyTask> | RawMyTask[] | TaskList>(`${ME}/tasks`, {
    params: clean({
      dateFrom: filters.dateFrom,
      dateTo: filters.dateTo,
      status: filters.status,
      source: filters.source,
      overdue: filters.overdue,
      accepted: filters.accepted,
      q: filters.q,
      sort: filters.sort ?? '-due_at',
      limit: filters.limit ?? HR_DEFAULT_PAGE_SIZE,
      cursor: filters.cursor,
      withTotal: filters.withTotal,
    }),
    ...REPEAT_ARRAY_PARAMS,
  })
  const page = toPage<RawMyTask>(data as Page<RawMyTask> | RawMyTask[])
  return { ...page, items: page.items.map(toMyTask) }
}

/** `GET /api/hr/me/tasks?date=` — one day's list (the Mini App view). */
export async function getMyTasksByDay(date: HrDateString): Promise<TaskList> {
  const { data } = await api.get<TaskList>(`${ME}/tasks`, { params: { date } })
  return data
}

/** Add a task for oneself (`source='self'`). */
export async function createMyTask(input: CreateTaskInput): Promise<HrMyTask> {
  const { data } = await api.post<RawMyTask>(`${ME}/tasks`, input)
  return toMyTask(data)
}

/** "Bajardim" — needs a proof file OR a non-empty comment. */
export async function completeMyTask(id: string, input: CompleteTaskInput): Promise<HrMyTask> {
  const { data } = await api.post<RawMyTask>(`${ME}/tasks/${id}/complete`, input)
  return toMyTask(data)
}

/** Only the same local day and before the evening digest has run. */
export async function reopenMyTask(id: string): Promise<HrMyTask> {
  const { data } = await api.post<RawMyTask>(`${ME}/tasks/${id}/reopen`)
  return toMyTask(data)
}

/** "Ko'rdim" — NOT a status, only sets `acceptedAt`. */
export async function acceptMyTask(id: string): Promise<HrMyTask> {
  const { data } = await api.post<RawMyTask>(`${ME}/tasks/${id}/accept`)
  return toMyTask(data)
}

/** KPI step; the task completes itself when the target is reached. */
export async function addMyProgress(id: string, input: AddProgressInput): Promise<HrMyTask> {
  const { data } = await api.post<RawMyTask>(`${ME}/tasks/${id}/progress`, input)
  return toMyTask(data)
}

/** Proof file (multipart, `file` + `taskId`) — the returned `id` goes into "Bajardim"'s `proofIds`. */
export async function uploadMyProof(taskId: string, vars: HrUploadVars): Promise<UploadedProof> {
  const form = new FormData()
  form.append('file', vars.file, attachmentFileName(vars.file, vars.filename))
  form.append('taskId', taskId)
  const { data } = await api.post<UploadedProof>(`${ME}/proofs`, form, uploadConfig(vars))
  return data
}

/** Own result for the current month. */
export async function getMySummary(): Promise<Summary> {
  const { data } = await api.get<Summary>(`${ME}/summary`)
  return data
}
