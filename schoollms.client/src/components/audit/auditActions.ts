/**
 * Audit action badges — one place for every history list (docs/modules/audit-log.md).
 *
 * The server writes more than create/update/delete (storno, cancel, approve, archive…). A list that
 * only knew the three crashed on the rest (payment storno broke "To'lov tarixi"). Every action now
 * maps to one of the three kinds plus an optional sub-label; an unknown action is a change.
 * Mirrors `AuditCatalog.KindOf` / `ActionLabel` on the server — the audit screen gets both from
 * the server, the per-record lists use this.
 */

export type AuditKind = 'create' | 'update' | 'delete'

const CREATE = new Set(['create', 'enrol', 'import', 'request', 'add', 'request-reversal'])
const DELETE = new Set(['delete', 'remove'])

const SUB_LABELS: Record<string, string> = {
  reverse: 'Storno',
  cancel: 'Bekor qildi',
  void: 'Bekor qildi',
  adjust: 'Tuzatdi',
  adjust_cancel: 'Tuzatishni bekor qildi',
  approve: 'Tasdiqladi',
  'approve-reversal': 'Stornoni tasdiqladi',
  reject: 'Rad etdi',
  request: "So'radi",
  'request-reversal': "Storno so'radi",
  publish: "E'lon qildi",
  unpublish: "E'londan oldi",
  retire: 'Faolsizlantirdi',
  resolve: 'Hal qildi',
  end: 'Yakunladi',
  enrol: "O'quvchiga aylantirdi",
  import: 'Import',
  remove: 'Olib tashladi',
  rollover: "Yangi o'quv yili",
  archive: 'Arxivladi',
  restore: 'Arxivdan qaytardi',
  password: 'Parol yangilandi',
  accept: 'Qabul qildi',
  return: 'Qaytardi',
  activate: 'Yoqdi',
  deactivate: "O'chirib qo'ydi",
  add: "Qo'shdi",
  score: "Ball qo'ydi",
  absent: 'Kelmadi deb belgiladi',
  send: 'Yubordi',
}

export function auditKind(action: string): AuditKind {
  if (CREATE.has(action)) return 'create'
  if (DELETE.has(action)) return 'delete'
  return 'update'
}

export function auditSubLabel(action: string): string | undefined {
  return SUB_LABELS[action]
}

/** Badge text and colour per kind: "Yaratdi" / "Tahrirladi" / "O'chirdi". */
export const KIND_BADGE: Record<AuditKind, { label: string; cls: string }> = {
  create: { label: 'Yaratdi', cls: 'bg-emerald-50 text-emerald-700' },
  update: { label: 'Tahrirladi', cls: 'bg-amber-50 text-amber-700' },
  delete: { label: "O'chirdi", cls: 'bg-red-50 text-red-700' },
}

/** Past-tense badge for the per-record lists ("Qo'shildi" / "Tahrirlandi" / "O'chirildi") + sub-label. */
export function auditBadge(action: string): { label: string; cls: string } {
  const kind = auditKind(action)
  const base = {
    create: { label: "Qo'shildi", cls: KIND_BADGE.create.cls },
    update: { label: 'Tahrirlandi', cls: KIND_BADGE.update.cls },
    delete: { label: "O'chirildi", cls: KIND_BADGE.delete.cls },
  }[kind]
  const sub = auditSubLabel(action)
  return sub ? { label: sub, cls: base.cls } : base
}
