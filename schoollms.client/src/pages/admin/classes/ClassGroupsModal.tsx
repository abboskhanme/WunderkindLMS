import { useEffect, useState } from 'react'
import { Lock, Shuffle, Users, Shield } from 'lucide-react'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import {
  getClassGroups,
  saveClassGroups,
  autoSplitClassGroups,
} from '@/api/services/classes'
import type { ClassGroups } from '@/types'
import { useAuth } from '@/context/auth-context'

interface Props {
  open: boolean
  classId: string
  className: string
  onClose: () => void
}

type Bucket = 0 | 1 | 2

const bucketTitle: Record<Bucket, string> = {
  0: 'Guruhsiz',
  1: '1-guruh',
  2: '2-guruh',
}

const bucketColor: Record<Bucket, string> = {
  0: 'bg-slate-50 border-slate-200',
  1: 'bg-sky-50 border-sky-200',
  2: 'bg-violet-50 border-violet-200',
}

const bucketBadge: Record<Bucket, string> = {
  0: 'bg-slate-200 text-slate-600',
  1: 'bg-sky-200 text-sky-700',
  2: 'bg-violet-200 text-violet-700',
}

/**
 * Sinf ichidagi o'quvchilarni ikki guruhga bo'lish oynasi.
 *
 * - O'quv yili boshlangan bo'lsa (jurnalda yozuv bor) — backend `locked=true` qaytaradi
 *   va guruhni o'zgartirish bloklanadi (faqat ko'rinish).
 * - Avtomatik bo'lish: alifbo bo'yicha o'quvchilar 1/2 ga teng taqsimlanadi.
 * - O'quvchini kerakli guruhga belgilash uchun "1-guruh"/"2-guruh"/"Guruhsiz" tugmalarini bosing.
 *   "Saqlash" bosilgunicha o'zgarishlar faqat oynada turadi.
 */
export function ClassGroupsModal({ open, classId, className, onClose }: Props) {
  const { user } = useAuth()
  const isSuperAdmin = user?.role === 'superadmin'
  const [data, setData] = useState<ClassGroups | null>(null)
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  // Mahalliy o'zgarish: studentId -> subGroup. Saqlanmagan o'zgarishlar shu yerda.
  const [local, setLocal] = useState<Record<string, Bucket>>({})
  const [error, setError] = useState<string | null>(null)
  // Tahrirlash huquqi: backend canEdit (locked emas YOKI superadmin) — fallback klient hisobi.
  const canEdit = data ? (data.canEdit ?? (!data.locked || isSuperAdmin)) : false

  useEffect(() => {
    if (!open || !classId) return
    setLoading(true)
    setError(null)
    getClassGroups(classId)
      .then((d) => {
        setData(d)
        setLocal(Object.fromEntries(d.students.map((s) => [s.id, (s.subGroup as Bucket) ?? 0])))
      })
      .catch((e) => setError(e?.response?.data?.message ?? 'Yuklab bo\'lmadi'))
      .finally(() => setLoading(false))
  }, [open, classId])

  if (!open) return null

  const move = (studentId: string, sub: Bucket) => {
    if (!canEdit) return
    setLocal((prev) => ({ ...prev, [studentId]: sub }))
  }

  const handleSave = async () => {
    if (!data || !canEdit) return
    setSaving(true)
    setError(null)
    try {
      const assignments = Object.entries(local).map(([studentId, subGroup]) => ({
        studentId,
        subGroup,
      }))
      await saveClassGroups(classId, assignments)
      onClose()
    } catch (e) {
      // axios error: data.message
      const msg =
        (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
        'Saqlab bo\'lmadi'
      setError(msg)
    } finally {
      setSaving(false)
    }
  }

  const handleAutoSplit = async () => {
    if (!data || !canEdit) return
    if (!confirm("O'quvchilarni alifbo bo'yicha 1 va 2 guruhga teng bo'laymi? Joriy guruhlash ustiga yoziladi.")) return
    setSaving(true)
    setError(null)
    try {
      const next = await autoSplitClassGroups(classId)
      setData(next)
      setLocal(Object.fromEntries(next.students.map((s) => [s.id, (s.subGroup as Bucket) ?? 0])))
    } catch (e) {
      const msg =
        (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
        'Avtomatik bo\'lib bo\'lmadi'
      setError(msg)
    } finally {
      setSaving(false)
    }
  }

  const counts = (Object.values(local) as Bucket[]).reduce(
    (acc, b) => ((acc[b] = (acc[b] ?? 0) + 1), acc),
    { 0: 0, 1: 0, 2: 0 } as Record<Bucket, number>,
  )

  const studentsByBucket = (b: Bucket) =>
    (data?.students ?? []).filter((s) => (local[s.id] ?? 0) === b)

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={`${className} — guruhlar`}
      size="xl"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Yopish
          </Button>
          {canEdit && (
            <Button onClick={handleSave} disabled={saving || loading}>
              {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          )}
        </>
      }
    >
      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : !data ? (
        <p className="py-6 text-center text-slate-400">{error ?? "Ma'lumot yo'q"}</p>
      ) : (
        <div className="space-y-4">
          {/* Lock holati va avtomatik bo'lish */}
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="flex items-center gap-2 text-sm text-slate-600">
              <Users className="h-4 w-4 text-slate-400" />
              <span>
                Jami {data.students.length} o'quvchi · Guruhsiz {counts[0]} · 1-guruh{' '}
                <b className="text-sky-700">{counts[1]}</b> · 2-guruh{' '}
                <b className="text-violet-700">{counts[2]}</b>
              </span>
            </div>
            <div className="flex items-center gap-2">
              {data.locked && (
                <span
                  className={cn(
                    'inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs font-medium',
                    isSuperAdmin
                      ? 'bg-emerald-50 text-emerald-700'
                      : 'bg-amber-50 text-amber-700',
                  )}
                >
                  {isSuperAdmin ? (
                    <>
                      <Shield className="h-3.5 w-3.5" />
                      Yopiq — superadmin override
                    </>
                  ) : (
                    <>
                      <Lock className="h-3.5 w-3.5" />
                      Yopiq — o'quv yili boshlangan
                    </>
                  )}
                </span>
              )}
              {canEdit && (
                <Button variant="secondary" onClick={handleAutoSplit} disabled={saving}>
                  <Shuffle className="h-4 w-4" /> Avtomatik teng bo'lish
                </Button>
              )}
            </div>
          </div>

          {data.locked && data.lockReason && (
            <p
              className={cn(
                'rounded-lg border px-3 py-2 text-xs',
                isSuperAdmin
                  ? 'border-emerald-200 bg-emerald-50 text-emerald-700'
                  : 'border-amber-200 bg-amber-50 text-amber-700',
              )}
            >
              {data.lockReason}
              {!isSuperAdmin && '. Yangi o\'quv yiliga o\'tishda guruhlash qayta ochiladi.'}
            </p>
          )}

          {error && (
            <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
              {error}
            </p>
          )}

          {/* 3 ustun: Guruhsiz | 1-guruh | 2-guruh */}
          <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
            {([0, 1, 2] as Bucket[]).map((b) => (
              <div
                key={b}
                className={cn(
                  'rounded-xl border p-3',
                  bucketColor[b],
                )}
              >
                <div className="mb-2 flex items-center justify-between">
                  <h4 className="text-sm font-semibold text-slate-700">{bucketTitle[b]}</h4>
                  <span
                    className={cn(
                      'rounded-full px-2 text-xs font-semibold',
                      bucketBadge[b],
                    )}
                  >
                    {counts[b]}
                  </span>
                </div>
                <div className="max-h-[22rem] space-y-1 overflow-y-auto pr-1">
                  {studentsByBucket(b).map((s) => (
                    <div
                      key={s.id}
                      className="flex items-center justify-between gap-2 rounded-lg bg-white px-2 py-1.5 shadow-sm"
                    >
                      <span className="truncate text-sm font-medium text-slate-700">{s.fullName}</span>
                      {canEdit && (
                        <div className="flex shrink-0 gap-0.5">
                          {([0, 1, 2] as Bucket[])
                            .filter((x) => x !== b)
                            .map((x) => (
                              <button
                                key={x}
                                type="button"
                                title={`${bucketTitle[x]}ga`}
                                onClick={() => move(s.id, x)}
                                className={cn(
                                  'rounded px-1.5 py-0.5 text-[11px] font-semibold transition-colors',
                                  bucketBadge[x],
                                  'hover:brightness-95',
                                )}
                              >
                                {x === 0 ? 'X' : `G${x}`}
                              </button>
                            ))}
                        </div>
                      )}
                    </div>
                  ))}
                  {studentsByBucket(b).length === 0 && (
                    <p className="py-4 text-center text-xs text-slate-400">Bo'sh</p>
                  )}
                </div>
              </div>
            ))}
          </div>

          <p className="text-xs text-slate-400">
            Eslatma: guruhlash faqat o'quv yili boshida (jurnalga yozuv kiritilmasdan oldin)
            o'zgartirilishi mumkin — odatdagi admin uchun. Superadmin (tizim egasi) qulflangan
            bo'lsa ham istalgan vaqtda o'zgartira oladi. Bo'lingan darslar (jadvalda
            1-guruh/2-guruh sifatida belgilangan) faqat shu guruh o'quvchilariga ko'rinadi.
          </p>
        </div>
      )}
    </Modal>
  )
}
