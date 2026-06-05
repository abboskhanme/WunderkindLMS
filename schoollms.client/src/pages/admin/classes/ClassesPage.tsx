import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Plus, Pencil, Trash2, Users, Archive, ArchiveRestore } from 'lucide-react'
import type { SchoolClass } from '@/types'
import type { ClassPayload } from '@/api/services/classes'
import {
  getClasses,
  createClass,
  updateClass,
  deleteClass,
  getArchivedClasses,
  archiveClass,
  unarchiveClass,
} from '@/api/services/classes'
import { getClassesStats, type ClassStats } from '@/api/services/classPerformance'
import { languageLabels } from '@/config/constants'
import { formatMoney, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { ClassFormModal } from './ClassFormModal'
import { ClassGroupsModal } from './ClassGroupsModal'

export function ClassesPage() {
  const navigate = useNavigate()
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [stats, setStats] = useState<Record<string, ClassStats>>({})
  const [loading, setLoading] = useState(true)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<SchoolClass | null>(null)
  // Oylik to'lov o'zgarganda — yangi narxni o'quvchilarga qachondan qo'llashni so'rash uchun
  const [feePrompt, setFeePrompt] = useState<{
    id: string
    values: ClassPayload
    oldFee: number
  } | null>(null)
  /** Sinf guruhlarini boshqarish oynasi (Guruhlar tugmasi bilan ochiladi) */
  const [groupsFor, setGroupsFor] = useState<SchoolClass | null>(null)
  /** Arxivlangan sinflar ro'yxati + arxiv ko'rinishi yoqilganmi */
  const [archived, setArchived] = useState<SchoolClass[]>([])
  const [showArchived, setShowArchived] = useState(false)

  useEffect(() => {
    Promise.all([getClasses(), getClassesStats(), getArchivedClasses()])
      .then(([cl, st, ar]) => {
        setClasses(cl)
        setStats(st)
        setArchived(ar)
      })
      .finally(() => setLoading(false))
  }, [])

  const applyUpdate = (id: string, values: ClassPayload, applyFee?: boolean) =>
    updateClass(id, values, applyFee).then((u) =>
      setClasses((prev) => prev.map((c) => (c.id === u.id ? u : c))),
    )

  const handleSubmit = (values: ClassPayload) => {
    if (editing) {
      // Oylik to'lov o'zgargan bo'lsa — o'quvchilarga qo'llashni so'raymiz (Ha/Yo'q)
      if (values.monthlyFee !== editing.monthlyFee) {
        setFeePrompt({ id: editing.id, values, oldFee: editing.monthlyFee })
        setFormOpen(false)
        setEditing(null)
        return
      }
      applyUpdate(editing.id, values)
    } else {
      createClass(values).then((c) => setClasses((prev) => [...prev, c]))
    }
    setFormOpen(false)
    setEditing(null)
  }

  const resolveFeePrompt = (applyFee: boolean) => {
    if (!feePrompt) return
    applyUpdate(feePrompt.id, feePrompt.values, applyFee)
    setFeePrompt(null)
  }

  const handleDelete = (c: SchoolClass) => {
    if (!confirm(`"${c.name}" sinfini o'chirasizmi?`)) return
    deleteClass(c.id)
      .then(() => {
        setClasses((prev) => prev.filter((x) => x.id !== c.id))
        setArchived((prev) => prev.filter((x) => x.id !== c.id))
      })
      .catch((e) => alert(e?.response?.data?.message ?? "Sinfni o'chirib bo'lmadi"))
  }

  const handleArchive = (c: SchoolClass) => {
    if (!confirm(`"${c.name}" sinfini arxivlaysizmi?\nSinfdagi barcha o'quvchilar ham arxivlanadi (login bloklanadi).`))
      return
    archiveClass(c.id)
      .then((r) => {
        setClasses((prev) => prev.filter((x) => x.id !== c.id))
        setArchived((prev) => [{ ...c, isArchived: true }, ...prev])
        alert(`"${c.name}" arxivlandi — ${r.archivedStudents} ta o'quvchi ham arxivlandi.`)
      })
      .catch((e) => alert(e?.response?.data?.message ?? 'Arxivlashda xatolik'))
  }

  const handleUnarchive = (c: SchoolClass) => {
    if (!confirm(`"${c.name}" sinfini arxivdan chiqarasizmi?\nSinf bilan arxivlangan o'quvchilar ham qaytariladi.`))
      return
    unarchiveClass(c.id)
      .then((r) => {
        setArchived((prev) => prev.filter((x) => x.id !== c.id))
        setClasses((prev) => [...prev, { ...c, isArchived: false }])
        alert(`"${c.name}" arxivdan chiqarildi — ${r.restoredStudents} ta o'quvchi ham qaytarildi.`)
      })
      .catch((e) => alert(e?.response?.data?.message ?? 'Arxivdan chiqarishda xatolik'))
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">
            {showArchived ? 'Arxivlangan sinflar' : 'Sinflar va xonalar'}
          </h1>
          <p className="text-sm text-slate-400">
            {showArchived ? `${archived.length} ta arxivlangan sinf` : `Jami ${classes.length} ta sinf`}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="secondary" onClick={() => setShowArchived((v) => !v)}>
            {showArchived ? (
              <>
                <Users className="h-4 w-4" /> Faol sinflar
              </>
            ) : (
              <>
                <Archive className="h-4 w-4" /> Arxiv ({archived.length})
              </>
            )}
          </Button>
          {!showArchived && (
            <Button
              onClick={() => {
                setEditing(null)
                setFormOpen(true)
              }}
            >
              <Plus className="h-4 w-4" /> Yangi sinf
            </Button>
          )}
        </div>
      </div>

      <Card className="p-0">
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : showArchived ? (
          <ArchivedTable items={archived} onUnarchive={handleUnarchive} onDelete={handleDelete} />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-10 px-4 py-3">#</th>
                  <th className="px-4 py-3">Sinf nomi</th>
                  <th className="px-4 py-3">Til</th>
                  <th className="px-4 py-3">Xona</th>
                  <th className="px-4 py-3">O'rtacha baho</th>
                  <th className="px-4 py-3">Davomat</th>
                  <th className="px-4 py-3">Oylik to'lov</th>
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {classes.map((c, i) => (
                  <tr
                    key={c.id}
                    onClick={() => navigate(`/admin/classes/${c.id}`)}
                    className="cursor-pointer hover:bg-slate-50/60"
                  >
                    <td className="px-4 py-3 text-slate-400">{i + 1}</td>
                    <td className="px-4 py-3 font-medium text-slate-800">{c.name}</td>
                    <td className="px-4 py-3">
                      <span
                        className={cn(
                          'rounded-md px-2 py-0.5 text-xs font-medium',
                          c.language === 'uz'
                            ? 'bg-blue-50 text-blue-700'
                            : 'bg-amber-50 text-amber-700',
                        )}
                      >
                        {languageLabels[c.language]}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-slate-600">{c.room || '—'}</td>
                    <td className="px-4 py-3">
                      {stats[c.id] ? (
                        <span className={cn('font-semibold', gradeColor(stats[c.id].averageGrade))}>
                          {stats[c.id].averageGrade.toFixed(1)}
                        </span>
                      ) : (
                        <span className="text-slate-400">—</span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {stats[c.id] && stats[c.id].attendance != null ? (
                        <span className={cn('font-medium', attColor(stats[c.id].attendance!))}>
                          {stats[c.id].attendance}%
                        </span>
                      ) : (
                        <span className="text-slate-400">—</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-slate-600">{formatMoney(c.monthlyFee)}</td>
                    <td className="px-4 py-3">
                      <div
                        className="flex items-center justify-end gap-0.5"
                        onClick={(e) => e.stopPropagation()}
                      >
                        <IconBtn
                          icon={Users}
                          title="Guruhlar (1/2)"
                          onClick={() => setGroupsFor(c)}
                        />
                        <IconBtn
                          icon={Pencil}
                          title="Tahrirlash"
                          onClick={() => {
                            setEditing(c)
                            setFormOpen(true)
                          }}
                        />
                        <IconBtn
                          icon={Archive}
                          title="Arxivlash (o'quvchilari bilan)"
                          onClick={() => handleArchive(c)}
                        />
                        <IconBtn
                          icon={Trash2}
                          title="O'chirish"
                          danger
                          onClick={() => handleDelete(c)}
                        />
                      </div>
                    </td>
                  </tr>
                ))}
                {classes.length === 0 && (
                  <tr>
                    <td colSpan={8} className="px-4 py-12 text-center text-slate-400">
                      Sinflar yo'q
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <ClassFormModal
        open={formOpen}
        onClose={() => {
          setFormOpen(false)
          setEditing(null)
        }}
        onSubmit={handleSubmit}
        initial={editing}
      />

      <ClassGroupsModal
        open={!!groupsFor}
        classId={groupsFor?.id ?? ''}
        className={groupsFor?.name ?? ''}
        onClose={() => setGroupsFor(null)}
      />

      <Modal
        open={!!feePrompt}
        onClose={() => setFeePrompt(null)}
        title="Oylik to'lovni o'quvchilarga qo'llash"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => resolveFeePrompt(false)}>
              Yo'q — keyingi oydan
            </Button>
            <Button onClick={() => resolveFeePrompt(true)}>Ha — joriy oydan</Button>
          </>
        }
      >
        {feePrompt && (
          <div className="space-y-3 text-sm text-slate-600">
            <p>
              <span className="font-medium text-slate-800">{feePrompt.values.name}</span> sinfining
              oylik to'lovi{' '}
              <span className="font-medium">{formatMoney(feePrompt.oldFee)}</span> →{' '}
              <span className="font-medium">{formatMoney(feePrompt.values.monthlyFee)}</span> so'mga
              o'zgardi. Yangi narx shu sinfdagi o'quvchilarga qachondan qo'llansin?
            </p>
            <div className="rounded-lg bg-slate-50 px-3 py-2 text-slate-500">
              <p>
                <b className="text-slate-700">Ha</b> — joriy oy to'lovi yangi narxga o'zgaradi
                (balans farqqa moslab to'g'rilanadi).
              </p>
              <p className="mt-1">
                <b className="text-slate-700">Yo'q</b> — joriy oy eski narxda qoladi, yangi narx
                keyingi oydan hisoblanadi.
              </p>
            </div>
          </div>
        )}
      </Modal>
    </div>
  )
}

function gradeColor(g: number): string {
  if (g >= 4.5) return 'text-emerald-600'
  if (g >= 4) return 'text-brand-600'
  if (g >= 3.5) return 'text-amber-600'
  return 'text-red-600'
}

function attColor(a: number): string {
  if (a >= 95) return 'text-emerald-600'
  if (a >= 90) return 'text-amber-600'
  return 'text-red-600'
}

function ArchivedTable({
  items,
  onUnarchive,
  onDelete,
}: {
  items: SchoolClass[]
  onUnarchive: (c: SchoolClass) => void
  onDelete: (c: SchoolClass) => void
}) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
          <tr>
            <th className="w-10 px-4 py-3">#</th>
            <th className="px-4 py-3">Sinf nomi</th>
            <th className="px-4 py-3">Til</th>
            <th className="px-4 py-3">Xona</th>
            <th className="px-4 py-3">Arxiv sanasi</th>
            <th className="px-4 py-3 text-right">Amallar</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {items.map((c, i) => (
            <tr key={c.id} className="hover:bg-slate-50/60">
              <td className="px-4 py-3 text-slate-400">{i + 1}</td>
              <td className="px-4 py-3 font-medium text-slate-800">{c.name}</td>
              <td className="px-4 py-3">
                <span
                  className={cn(
                    'rounded-md px-2 py-0.5 text-xs font-medium',
                    c.language === 'uz' ? 'bg-blue-50 text-blue-700' : 'bg-amber-50 text-amber-700',
                  )}
                >
                  {languageLabels[c.language]}
                </span>
              </td>
              <td className="px-4 py-3 text-slate-600">{c.room || '—'}</td>
              <td className="px-4 py-3 text-slate-500">{c.archivedAt || '—'}</td>
              <td className="px-4 py-3">
                <div className="flex items-center justify-end gap-0.5">
                  <IconBtn
                    icon={ArchiveRestore}
                    title="Arxivdan chiqarish (o'quvchilari bilan)"
                    onClick={() => onUnarchive(c)}
                  />
                  <IconBtn icon={Trash2} title="O'chirish" danger onClick={() => onDelete(c)} />
                </div>
              </td>
            </tr>
          ))}
          {items.length === 0 && (
            <tr>
              <td colSpan={6} className="px-4 py-12 text-center text-slate-400">
                Arxivlangan sinf yo'q
              </td>
            </tr>
          )}
        </tbody>
      </table>
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
