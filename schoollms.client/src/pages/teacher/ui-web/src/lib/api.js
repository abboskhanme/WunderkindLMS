// Teacher PWA — REST klient.
// Bir origin (`/api`), JWT token localStorage'da ('token' kaliti — asosiy sayt bilan bir xil,
// shuning uchun asosiy login'dan /teacher ga redirect bo'lganda token bo'linadi).

const TOKEN_KEY = 'token'
const USER_KEY = 'user'

export const tokenStore = {
  get: () => localStorage.getItem(TOKEN_KEY),
  set: (t) => localStorage.setItem(TOKEN_KEY, t),
  clear: () => {
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(USER_KEY)
  },
}

export const userStore = {
  get: () => {
    try {
      return JSON.parse(localStorage.getItem(USER_KEY) || 'null')
    } catch {
      return null
    }
  },
  set: (u) => localStorage.setItem(USER_KEY, JSON.stringify(u)),
}

// 401 (sessiya tugadi) bo'lganda chaqiriladigan global handler — App.jsx login'ga qaytaradi.
let onUnauthorized = null
export function setUnauthorizedHandler(fn) {
  onUnauthorized = fn
}

export class ApiError extends Error {
  constructor(status, body) {
    super((body && body.message) || `Xatolik (${status})`)
    this.status = status
    this.body = body
  }
}

function qs(query) {
  if (!query) return ''
  const p = new URLSearchParams()
  for (const [k, v] of Object.entries(query)) {
    if (v !== undefined && v !== null && v !== '') p.append(k, String(v))
  }
  const s = p.toString()
  return s ? `?${s}` : ''
}

async function request(method, path, opts = {}) {
  const { body, form, query, auth = true } = opts
  const headers = {}
  if (auth) {
    const t = tokenStore.get()
    if (t) headers.Authorization = `Bearer ${t}`
  }

  let payload
  if (form) {
    payload = form // FormData — Content-Type'ni brauzer o'zi qo'yadi (boundary bilan)
  } else if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
    payload = JSON.stringify(body)
  }

  let res
  try {
    res = await fetch(`/api${path}${qs(query)}`, { method, headers, body: payload })
  } catch {
    throw new ApiError(0, { message: "Tarmoq xatosi — internet aloqasini tekshiring" })
  }

  if (res.status === 401 && auth) {
    tokenStore.clear()
    if (onUnauthorized) onUnauthorized()
    throw new ApiError(401, { message: 'Sessiya tugadi — qayta kiring' })
  }

  let data = null
  const text = await res.text()
  if (text) {
    try {
      data = JSON.parse(text)
    } catch {
      data = text
    }
  }

  if (!res.ok) throw new ApiError(res.status, typeof data === 'object' ? data : { message: data })
  return data
}

const GET = (p, query) => request('GET', p, { query })
const POST = (p, body) => request('POST', p, { body })
const PUT = (p, body) => request('PUT', p, { body })
const DEL = (p, query) => request('DELETE', p, { query })

export const api = {
  // ---- Auth ----
  login: (email, password) => request('POST', '/auth/login', { body: { email, password }, auth: false }),
  account: (body) => PUT('/auth/account', body),

  // ---- Profil / umumiy ----
  profile: () => GET('/teacher/me'),
  meta: () => GET('/teacher/meta'),
  school: () => GET('/teacher/school'),
  holidays: () => GET('/teacher/holidays'),
  classes: () => GET('/teacher/classes'),
  schedule: (quarter, week) => GET('/teacher/schedule', { quarter, week }),
  salary: (from, to) => GET('/teacher/salary', { from, to }),
  progress: (quarter) => GET('/teacher/progress', { quarter }),

  // ---- Sinf rahbarligi ----
  homeroom: () => GET('/teacher/homeroom'),
  pickups: () => GET('/teacher/pickups'),
  acceptPickup: (id) => POST(`/teacher/pickups/${id}/accept`),
  handover: (studentId) => POST('/teacher/homeroom/handover', { studentId }),

  // ---- Jurnal ----
  journalStudents: (classId) => GET('/teacher/journal/students', { classId }),
  journalColumns: (classId, subjectId, quarter) => GET('/teacher/journal/columns', { classId, subjectId, quarter }),
  journalEntries: (classId, subjectId, quarter) => GET('/teacher/journal', { classId, subjectId, quarter }),
  setJournalEntry: (body) => PUT('/teacher/journal', body),
  clearJournalEntry: (q) => DEL('/teacher/journal', q),
  journalNotes: (classId, subjectId, quarter) => GET('/teacher/journal/notes', { classId, subjectId, quarter }),
  setJournalNote: (body) => PUT('/teacher/journal/notes', body),
  // Mavzular Excel: shablon (.xlsx blob) + import (mavzu+uy vazifa; darsni o'tilgan qilmaydi).
  journalTopicsTemplate: async (classId, subjectId, quarter) => {
    const t = tokenStore.get()
    const res = await fetch(`/api/teacher/journal/topics-template${qs({ classId, subjectId, quarter })}`, {
      headers: t ? { Authorization: `Bearer ${t}` } : {},
    })
    if (!res.ok) throw new ApiError(res.status, { message: "Shablonni yuklab bo'lmadi" })
    return res.blob()
  },
  importTopics: (file, classId, subjectId, quarter) => {
    const fd = new FormData()
    fd.append('file', file)
    fd.append('classId', classId)
    fd.append('subjectId', subjectId)
    fd.append('quarter', String(quarter))
    return request('POST', '/teacher/journal/topics-import', { form: fd })
  },
  quarterGrades: (classId, subjectId, quarter) => GET('/teacher/journal/quarter-grades', { classId, subjectId, quarter }),
  setQuarterGrade: (body) => PUT('/teacher/journal/quarter-grades', body),

  // ---- Baholash (Feedback nomi) ----
  evalTypes: () => GET('/teacher/evaluation/types'),
  evalBoard: (classId, subjectId, month) => GET('/teacher/evaluation/board', { classId, subjectId, month }),
  setEvalGrade: (body) => POST('/teacher/evaluation/grade', body),

  // ---- Topshiriqlar ----
  assignments: () => GET('/teacher/assignments'),
  createAssignment: (body) => POST('/teacher/assignments', body),
  updateAssignment: (id, body) => PUT(`/teacher/assignments/${id}`, body),
  deleteAssignment: (id) => DEL(`/teacher/assignments/${id}`),
  assignmentResults: (id) => GET(`/teacher/assignments/${id}/results`),
  setSubmission: (id, studentId, body) => PUT(`/teacher/assignments/${id}/submissions/${studentId}`, body),
  assignmentTypes: () => GET('/teacher/assignment-types'),
  upload: (file) => {
    const fd = new FormData()
    fd.append('file', file)
    return request('POST', '/teacher/uploads', { form: fd })
  },

  // ---- Chat ----
  chatClasses: () => GET('/teacher/chat/classes'),
  chatLastMessages: () => GET('/teacher/chat/last-messages'),
  chat: (className, since) => GET(`/teacher/chat/${encodeURIComponent(className)}`, { since }),
  sendChat: (className, text) => POST(`/teacher/chat/${encodeURIComponent(className)}`, { text }),

  // ---- LMS (faqat ko'rish) ----
  lmsSubjects: (classId) => GET('/teacher/lms/subjects', { classId }),
  lmsTopics: (subjectId) => GET(`/teacher/lms/subjects/${subjectId}/topics`),
  lmsProgress: (subjectId) => GET(`/teacher/lms/subjects/${subjectId}/progress`),

  // ---- Taklif / shikoyat ----
  feedback: (type, text, image) => {
    const fd = new FormData()
    fd.append('type', type)
    fd.append('text', text)
    if (image) fd.append('image', image)
    return request('POST', '/teacher/feedback', { form: fd })
  },

  // ---- Push qurilma / web push ----
  pushConfig: () => GET('/teacher/push-config'),
  registerDevice: (body) => POST('/teacher/notifications/register', body),
  unregisterDevice: (token) => DEL('/teacher/notifications/register', { token }),
}
