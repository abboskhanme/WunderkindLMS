import { useCallback, useEffect, useState } from 'react'
import { MessageSquare, Paperclip, Pencil, Plus, Trash2 } from 'lucide-react'
import type { StudentComment, StudentCommentKind } from '@/api/services/studentComments'
import {
  createStudentComment,
  deleteStudentComment,
  getStudentComments,
  updateStudentComment,
} from '@/api/services/studentComments'
import { uploadAdminFile } from '@/api/services/students'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Textarea } from '@/components/ui/Input'
import { cn } from '@/lib/utils'
import { ProfileEmpty, ProfileError, ProfileSection } from './ProfileUi'

/**
 * Kartochkaning "Izohlar" tab'i — docs/modules/students-parity.md §2.3
 * (S-11). EduSchool izohlarni aynan PROFILDA ko'rsatadi, shuning uchun
 * ro'yxatdagi oyna (`StudentCommentsModal`) o'z joyida qoladi, kartochkada
 * esa izohlar to'g'ridan-to'g'ri ochiq turadi.
 *
 * BALL EMAS: "Intizom" tab'i ballni 100 dan ayiradi va hisobotga tushiradi;
 * bu yerdagi izoh ballsiz kuzatuv.
 *
 * MUALLIF QOIDASI: o'zganing izohini faqat administrator o'zgartira oladi —
 * server ham shunday qaraydi va har qator uchun `canEdit` ni o'zi aytadi.
 */

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

const kindLabels: Record<StudentCommentKind, string> = {
  positive: 'Ijobiy',
  negative: 'Salbiy',
}

export function CommentsTab({ studentId }: { studentId: string }) {
  const [rows, setRows] = useState<StudentComment[]>([])
  const [loading, setLoading] = useState(true)
  const [listError, setListError] = useState<string | null>(null)
  const [filter, setFilter] = useState<'all' | StudentCommentKind>('all')

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<StudentComment | null>(null)
  const [kind, setKind] = useState<StudentCommentKind>('positive')
  const [body, setBody] = useState('')
  const [fileUrl, setFileUrl] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    setListError(null)
    getStudentComments(studentId, filter === 'all' ? undefined : filter)
      .then(setRows)
      .catch((e) => setListError(errorText(e, "Izohlarni olib bo'lmadi")))
      .finally(() => setLoading(false))
  }, [studentId, filter])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- tab ochilganda va filtr almashganda izohlar yuklanadi (maqsadli)
    load()
  }, [load])

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
    if (!body.trim()) return
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

  const head = (
    <div className="flex flex-wrap items-center gap-2">
      <div className="flex gap-1 rounded-lg bg-slate-100 p-1">
        {(['all', 'positive', 'negative'] as const).map((value) => (
          <button
            key={value}
            type="button"
            onClick={() => setFilter(value)}
            className={cn(
              'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
              filter === value ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
            )}
          >
            {value === 'all' ? 'Hammasi' : kindLabels[value]}
          </button>
        ))}
      </div>
      <Button onClick={openCreate}>
        <Plus className="h-4 w-4" /> Izoh qo'shish
      </Button>
    </div>
  )

  return (
    <ProfileSection title="Izohlar" icon={MessageSquare} action={head}>
      {listError ? (
        <ProfileError message={listError} />
      ) : loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : rows.length === 0 ? (
        <ProfileEmpty>Hali izoh yozilmagan</ProfileEmpty>
      ) : (
        <ul className="space-y-3">
          {rows.map((c) => (
            <li key={c.id} className="rounded-xl border border-slate-100 p-3">
              <div className="flex flex-wrap items-center gap-2">
                <span
                  className={cn(
                    'rounded-md px-2 py-0.5 text-xs font-medium',
                    c.kind === 'positive' ? 'bg-emerald-50 text-emerald-600' : 'bg-red-50 text-red-600',
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
            <Button type="submit" form="profile-comment-form" disabled={busy || !body.trim()}>
              Saqlash
            </Button>
          </>
        }
      >
        <form id="profile-comment-form" onSubmit={submit} className="space-y-4">
          <div className="flex gap-1 rounded-lg bg-slate-100 p-1">
            {(['positive', 'negative'] as const).map((value) => (
              <button
                key={value}
                type="button"
                onClick={() => setKind(value)}
                className={cn(
                  'flex-1 rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                  kind === value ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
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
    </ProfileSection>
  )
}
