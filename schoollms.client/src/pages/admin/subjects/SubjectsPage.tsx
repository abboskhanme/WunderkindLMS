import { useEffect, useState } from 'react'
import { Plus, Pencil, Trash2, BookOpen } from 'lucide-react'
import type { Subject } from '@/types'
import type { SubjectPayload } from '@/api/services/subjects'
import {
  getSubjects,
  createSubject,
  updateSubject,
  deleteSubject,
  getSubjectUsage,
  type SubjectUsage,
} from '@/api/services/subjects'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { SubjectFormModal } from './SubjectFormModal'
import { Modal } from '@/components/ui/Modal'

/**
 * Fanlar katalogi (students-parity.md §2.5, G-9/F-1/F-3).
 *
 * Ro'yxat ATAYLAB filtrsiz so'raladi (faol ham, faolsiz ham) — bu KATALOG
 * ekrani, StudentStatusesPage/CertificateTypesPage bilan bir xil naqsh:
 * boshqaruvchi arxivlangan fanni ham ko'rishi va qaytadan faollashtirishi
 * kerak. Yangi tanlovlar (jadval, guruh) o'zlari faqat faollarni so'raydi.
 */
export function SubjectsPage() {
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [loading, setLoading] = useState(true)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Subject | null>(null)

  useEffect(() => {
    getSubjects()
      .then(setSubjects)
      .finally(() => setLoading(false))
  }, [])

  // Server saqlashni yoki o'chirishni RAD ETISHI mumkin (G-9: faol guruhi bor
  // fandan belgini olib bo'lmaydi; F-1: ishlatilayotgan fan o'chirilmaydi).
  // Ilgari javob jimgina tashlab yuborilardi — tugma bosilardi, hech narsa
  // o'zgarmasdi va sababi hech qayerda ko'rinmasdi.
  const handleSubmit = (values: SubjectPayload) => {
    const failed = (e: unknown) =>
      alert(
        (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
          "Fanni saqlab bo'lmadi",
      )
    if (editing) {
      updateSubject(editing.id, values)
        .then((u) => setSubjects((prev) => prev.map((s) => (s.id === u.id ? u : s))))
        .catch(failed)
    } else {
      createSubject(values)
        .then((c) => setSubjects((prev) => [...prev, c]))
        .catch(failed)
    }
    setFormOpen(false)
    setEditing(null)
  }

  // O'chirish — tasdiqlash oynasi bilan (mijoz, 2026-09-23). Oyna ochilishi bilan fan qayerda
  // ishlatilayotgani so'raladi: guruh, dars jadvali, jurnal ... bo'lsa, o'chirish tugmasi
  // umuman chiqmaydi — sababi yoziladi. Server ham aynan shu ro'yxat bilan rad etadi.
  const [deleting, setDeleting] = useState<Subject | null>(null)
  const [usage, setUsage] = useState<SubjectUsage | null>(null)
  const [usageError, setUsageError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const handleDelete = (s: Subject) => {
    setDeleting(s)
    setUsage(null)
    setUsageError(null)
    getSubjectUsage(s.id)
      .then(setUsage)
      .catch(() => setUsageError("Tekshirib bo'lmadi — qayta urinib ko'ring"))
  }

  const confirmDelete = () => {
    if (!deleting) return
    setBusy(true)
    deleteSubject(deleting.id)
      .then(() => {
        setSubjects((prev) => prev.filter((x) => x.id !== deleting.id))
        setDeleting(null)
      })
      .catch((e) =>
        setUsageError(
          (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
            "Fanni o'chirib bo'lmadi",
        ),
      )
      .finally(() => setBusy(false))
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Fanlar</h1>
          <p className="text-sm text-slate-400">Jami {subjects.length} ta fan</p>
        </div>
        <Button
          onClick={() => {
            setEditing(null)
            setFormOpen(true)
          }}
        >
          <Plus className="h-4 w-4" /> Yangi fan
        </Button>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {subjects.map((s) => (
            <Card key={s.id} className="flex items-center justify-between gap-3">
              <div className="flex items-center gap-3">
                <div
                  className="flex h-10 w-10 items-center justify-center rounded-xl bg-brand-50 text-brand-600"
                  style={
                    s.color
                      ? { backgroundColor: `${s.color}1F`, color: s.color }
                      : undefined
                  }
                >
                  <BookOpen className="h-5 w-5" />
                </div>
                <div>
                  <p className="flex items-center gap-1.5 font-medium text-slate-800">
                    {s.name}
                    {s.color && (
                      <span
                        title={s.color}
                        className="h-2.5 w-2.5 shrink-0 rounded-full"
                        style={{ backgroundColor: s.color }}
                      />
                    )}
                  </p>
                  <div className="mt-0.5 flex flex-wrap items-center gap-1">
                    {s.isActive === false && (
                      <span className="inline-flex items-center rounded-md bg-slate-100 px-1.5 py-0.5 text-[11px] font-medium text-slate-400">
                        Faol emas
                      </span>
                    )}
                  </div>
                </div>
              </div>
              <div className="flex items-center gap-0.5">
                <IconBtn
                  icon={Pencil}
                  title="Tahrirlash"
                  onClick={() => {
                    setEditing(s)
                    setFormOpen(true)
                  }}
                />
                <IconBtn icon={Trash2} title="O'chirish" danger onClick={() => handleDelete(s)} />
              </div>
            </Card>
          ))}
          {subjects.length === 0 && (
            <p className="col-span-full py-12 text-center text-slate-400">Fanlar yo'q</p>
          )}
        </div>
      )}

      <Modal
        open={Boolean(deleting)}
        onClose={() => setDeleting(null)}
        title="Fanni o'chirish"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setDeleting(null)}>
              {usage && !usage.canDelete ? 'Yopish' : 'Bekor qilish'}
            </Button>
            {usage?.canDelete && (
              <Button variant="danger" onClick={confirmDelete} disabled={busy}>
                {busy ? "O'chirilmoqda..." : "O'chirish"}
              </Button>
            )}
          </>
        }
      >
        {!usage && !usageError ? (
          <p className="py-4 text-center text-sm text-slate-400">Tekshirilmoqda...</p>
        ) : usage && !usage.canDelete ? (
          <div className="space-y-3 text-sm">
            <p className="text-slate-700">
              <b>"{deleting?.name}"</b> fanini o'chirib bo'lmaydi — u ishlatilmoqda:
            </p>
            <ul className="space-y-1 rounded-xl bg-amber-50 p-3 text-amber-800">
              {usage.usedIn.map((u) => (
                <li key={u}>• {u}</li>
              ))}
            </ul>
            <p className="text-xs text-slate-400">
              Kerak bo'lmasa, uni tahrirlab "Faol" belgisini olib tashlang — yangi tanlovlarda
              ko'rinmay qoladi, eski yozuvlar esa saqlanadi.
            </p>
          </div>
        ) : (
          <p className="text-sm text-slate-700">
            <b>"{deleting?.name}"</b> fanini o'chirasizmi? Bu amalni qaytarib bo'lmaydi.
          </p>
        )}
        {usageError && <p className="mt-3 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{usageError}</p>}
      </Modal>

      <SubjectFormModal
        open={formOpen}
        onClose={() => {
          setFormOpen(false)
          setEditing(null)
        }}
        onSubmit={handleSubmit}
        initial={editing}
      />
    </div>
  )
}

interface IconBtnProps {
  icon: typeof Pencil
  title: string
  onClick: () => void
  danger?: boolean
}

function IconBtn({ icon: Icon, title, onClick, danger }: IconBtnProps) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      className={cn(
        'rounded-lg p-1.5 transition-colors',
        danger
          ? 'text-slate-400 hover:bg-red-50 hover:text-red-600'
          : 'text-slate-400 hover:bg-slate-100 hover:text-slate-700',
      )}
    >
      <Icon className="h-4 w-4" />
    </button>
  )
}
