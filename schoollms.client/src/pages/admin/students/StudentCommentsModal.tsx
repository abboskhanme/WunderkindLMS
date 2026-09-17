import { useEffect, useState } from 'react'
import { Paperclip, Pencil, Plus, Trash2 } from 'lucide-react'
import {
  createStudentComment,
  deleteStudentComment,
  getStudentComments,
  updateStudentComment,
  type StudentComment,
  type StudentCommentKind,
} from '@/api/services/studentComments'
import { uploadAdminFile } from '@/api/services/students'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Textarea } from '@/components/ui/Input'

/**
 * O'quvchi izohlari (§2.3, S-11) — ijobiy / salbiy, matn, rasm va fayl.
 *
 * BALL EMAS: "Intizom" ekrani ballni 100 dan ayiradi va hisobotga tushiradi;
 * bu yerdagi izoh esa ballsiz kuzatuv. Ikkalasini aralashtirmaslik uchun
 * alohida oyna.
 *
 * MUALLIF QOIDASI: o'zganing izohini faqat administrator o'zgartira oladi —
 * server ham shunday qaraydi va `canEdit` ni har qator uchun o'zi aytadi.
 */

interface Props {
  studentId: string | null
  studentName: string
  onClose: () => void
}

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

const kindLabels: Record<StudentCommentKind, string> = {
  positive: 'Ijobiy',
  negative: 'Salbiy',
}

export function StudentCommentsModal({ studentId, studentName, onClose }: Props) {
  const [rows, setRows] = useState<StudentComment[]>([])
  const [loading, setLoading] = useState(false)
  const [filter, setFilter] = useState<'all' | StudentCommentKind>('all')

  const [editing, setEditing] = useState<StudentComment | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const [kind, setKind] = useState<StudentCommentKind>('positive')
  const [body, setBody] = useState('')
  const [fileUrl, setFileUrl] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!studentId) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda izohlarni yuklaymiz (maqsadli)
    setLoading(true)
    setRows([])
    getStudentComments(studentId, filter === 'all' ? undefined : filter)
      .then(setRows)
      .finally(() => setLoading(false))
  }, [studentId, filter])

  const openCreate = () => {
    setEditing(null)
    setKind('positive')
    setBody('')
    setFileUrl('')
    setError(null)
    setFormOpen(true)
  }

  const openEdit = (c: StudentComment) => {
    setEditing(c)
    setKind(c.kind)
    setBody(c.body)
    setFileUrl(c.fileUrl ?? '')
    setError(null)
    setFormOpen(true)
  }

  const attach = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const picked = e.target.files?.[0]
    e.target.value = ''
    if (!picked) return
    setBusy(true)
    try {
      const uploaded = await uploadAdminFile(picked)
      setFileUrl(uploaded.url)
    } catch (err) {
      setError(errorText(err, "Faylni yuklab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!studentId || !body.trim()) return
    setBusy(true)
    setError(null)
    try {
      const input = { kind, body: body.trim(), fileUrl }
      const saved = editing
        ? await updateStudentComment(editing.id, input)
        : await createStudentComment(studentId, input)
      setRows((prev) => {
        const rest = prev.filter((x) => x.id !== saved.id)
        // Tur bo'yicha filtr yoqilgan bo'lsa, boshqa turdagi izoh ro'yxatda ko'rinmaydi.
        if (filter !== 'all' && saved.kind !== filter) return rest
        return [saved, ...rest].sort((a, b) => b.createdAt.localeCompare(a.createdAt))
      })
      setFormOpen(false)
    } catch (err) {
      setError(errorText(err, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const remove = async (c: StudentComment) => {
    if (!confirm("Izohni o'chirasizmi?")) return
    try {
      await deleteStudentComment(c.id)
      setRows((prev) => prev.filter((x) => x.id !== c.id))
    } catch (err) {
      alert(errorText(err, "O'chirib bo'lmadi"))
    }
  }

  return (
    <>
      <Modal
        open={!!studentId}
        onClose={onClose}
        title={`Izohlar — ${studentName}`}
        size="lg"
        footer={<Button onClick={onClose}>Yopish</Button>}
      >
        <div className="space-y-4">
          <div className="flex flex-wrap items-center gap-2">
            <div className="flex gap-1 rounded-lg bg-slate-100 p-1">
              {(['all', 'positive', 'negative'] as const).map((value) => (
                <button
                  key={value}
                  type="button"
                  onClick={() => setFilter(value)}
                  className={cn(
                    'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                    filter === value
                      ? 'bg-white text-brand-700 shadow-sm'
                      : 'text-slate-500 hover:text-slate-700',
                  )}
                >
                  {value === 'all' ? 'Hammasi' : kindLabels[value]}
                </button>
              ))}
            </div>
            <Button className="ml-auto" onClick={openCreate}>
              <Plus className="h-4 w-4" /> Izoh qo'shish
            </Button>
          </div>

          {loading ? (
            <Loader label="Yuklanmoqda..." />
          ) : rows.length === 0 ? (
            <p className="py-8 text-center text-sm text-slate-400">Hali izoh yozilmagan</p>
          ) : (
            <ul className="space-y-3">
              {rows.map((c) => (
                <li key={c.id} className="rounded-xl border border-slate-100 p-3">
                  <div className="flex flex-wrap items-center gap-2">
                    <span
                      className={cn(
                        'rounded-md px-2 py-0.5 text-xs font-medium',
                        c.kind === 'positive'
                          ? 'bg-emerald-50 text-emerald-600'
                          : 'bg-red-50 text-red-600',
                      )}
                    >
                      {kindLabels[c.kind]}
                    </span>
                    <span className="text-sm text-slate-500">{c.createdByName}</span>
                    <span className="text-xs text-slate-400">
                      {c.createdAt.slice(0, 16).replace('T', ' ')}
                      {c.updatedAt && ' · tahrirlangan'}
                    </span>
                    {c.canEdit && (
                      <div className="ml-auto flex items-center gap-0.5">
                        <button
                          type="button"
                          title="Tahrirlash"
                          onClick={() => openEdit(c)}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                        >
                          <Pencil className="h-4 w-4" />
                        </button>
                        <button
                          type="button"
                          title="O'chirish"
                          onClick={() => remove(c)}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                        >
                          <Trash2 className="h-4 w-4" />
                        </button>
                      </div>
                    )}
                  </div>
                  <p className="mt-2 whitespace-pre-wrap text-sm text-slate-700">{c.body}</p>
                  {c.fileUrl && (
                    <a
                      href={c.fileUrl}
                      target="_blank"
                      rel="noreferrer"
                      className="mt-2 inline-flex items-center gap-1 text-sm text-brand-600 hover:underline"
                    >
                      <Paperclip className="h-4 w-4" /> Biriktirilgan fayl
                    </a>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>
      </Modal>

      <Modal
        open={formOpen}
        onClose={() => setFormOpen(false)}
        title={editing ? 'Izohni tahrirlash' : 'Yangi izoh'}
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setFormOpen(false)} disabled={busy}>
              Bekor qilish
            </Button>
            <Button type="submit" form="student-comment-form" disabled={busy || !body.trim()}>
              Saqlash
            </Button>
          </>
        }
      >
        <form id="student-comment-form" onSubmit={submit} className="space-y-4">
          <div className="flex gap-1 rounded-lg bg-slate-100 p-1">
            {(['positive', 'negative'] as const).map((value) => (
              <button
                key={value}
                type="button"
                onClick={() => setKind(value)}
                className={cn(
                  'flex-1 rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                  kind === value
                    ? 'bg-white text-brand-700 shadow-sm'
                    : 'text-slate-500 hover:text-slate-700',
                )}
              >
                {kindLabels[value]}
              </button>
            ))}
          </div>

          <Textarea
            label="Izoh"
            required
            rows={5}
            placeholder="masalan: onasi bilan gaplashildi, olimpiadaga tayyorlanmoqda"
            value={body}
            onChange={(e) => setBody(e.target.value)}
          />

          <div>
            <span className="mb-1 block text-sm font-medium text-slate-600">Fayl (ixtiyoriy)</span>
            <label className="inline-flex cursor-pointer items-center gap-2 rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-600 hover:bg-slate-50">
              <Paperclip className="h-4 w-4" />
              {fileUrl ? 'Boshqa fayl' : 'Fayl biriktirish'}
              <input type="file" className="hidden" onChange={attach} />
            </label>
            {fileUrl && (
              <button
                type="button"
                onClick={() => setFileUrl('')}
                className="ml-2 text-sm text-slate-400 hover:text-slate-600"
              >
                Olib tashlash
              </button>
            )}
          </div>

          {error && <p className="text-sm text-red-600">{error}</p>}
        </form>
      </Modal>
    </>
  )
}
