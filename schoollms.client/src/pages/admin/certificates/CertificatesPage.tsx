import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Award, FileSpreadsheet, Paperclip, Pencil, Plus, Search, Settings2, Trash2 } from 'lucide-react'
import type { Student, Subject, Teacher } from '@/types'
import {
  getCertificates,
  getCertificateTypes,
  getIssuingTeachers,
  deleteCertificate,
  exportCertificates,
  certificateError,
  type Certificate,
  type CertificateType,
  type IssuingTeacher,
} from '@/api/services/certificates'
import { getStudents } from '@/api/services/students'
import { getSubjects } from '@/api/services/subjects'
import { getTeachers } from '@/api/services/teachers'
import { getClasses } from '@/api/services/classes'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { formatDate, cn } from '@/lib/utils'
import { CertificateFormModal } from './CertificateFormModal'
import { CertificateResultsTab } from './CertificateResultsTab'

type Tab = 'list' | 'results'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/**
 * Sertifikatlar registri (docs/modules/existing-module-gaps.md §2.3).
 *
 * Ikki tab: <b>Ro'yxat</b> — hujjatlar (kim, qaysi tur, qachon, fayl);
 * <b>Natijalar</b> — bitta standart test bo'yicha o'quvchi × ball jadvali.
 *
 * Filtrlash SERVERDA: har o'zgarishda so'rov qayta yuboriladi. Sabab — registr
 * o'sib boradigan jadval, "muddati tugayapti" esa sana hisobi; ikkalasini ham
 * brauzerga topshirsak, sahifa o'sishi bilan sekinlashardi va maktab vaqti
 * o'rniga brauzer vaqti ishlatilardi.
 */
export function CertificatesPage() {
  const [tab, setTab] = useState<Tab>('list')

  const [rows, setRows] = useState<Certificate[]>([])
  const [types, setTypes] = useState<CertificateType[]>([])
  const [issuers, setIssuers] = useState<IssuingTeacher[]>([])
  const [students, setStudents] = useState<Student[]>([])
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [classNames, setClassNames] = useState<string[]>([])

  const [ready, setReady] = useState(false)
  const [listLoading, setListLoading] = useState(true)

  // Filtrlar — hammasi serverga ketadi.
  const [search, setSearch] = useState('')
  const [typeFilter, setTypeFilter] = useState('')
  const [teacherFilter, setTeacherFilter] = useState('')
  const [classFilter, setClassFilter] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [expiring, setExpiring] = useState(false)

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Certificate | null>(null)

  useEffect(() => {
    Promise.all([
      getCertificateTypes(),
      getIssuingTeachers(),
      getStudents(),
      getSubjects(),
      getTeachers(),
      getClasses(),
    ])
      .then(([t, iss, st, sub, te, cls]) => {
        setTypes(t)
        setIssuers(iss)
        setStudents(st)
        setSubjects(sub)
        setTeachers(te)
        setClassNames(cls.map((c) => c.name))
      })
      .finally(() => setReady(true))
  }, [])

  useEffect(() => {
    // Poyga (race) himoyasi: tez filtrlanganda faqat oxirgi javob qabul qilinadi.
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi so'rovdan oldin holatni belgilaymiz (maqsadli)
    setListLoading(true)
    getCertificates({
      search: search.trim() || undefined,
      typeId: typeFilter || undefined,
      teacherId: teacherFilter || undefined,
      className: classFilter || undefined,
      from: from || undefined,
      to: to || undefined,
      // "Muddati tugayapti" — 60 kun: IELTS/SAT hujjatlarini yangilashga yetadigan oraliq.
      expiringInDays: expiring ? 60 : undefined,
    })
      .then((data) => {
        if (active) setRows(data)
      })
      .finally(() => {
        if (active) setListLoading(false)
      })
    return () => {
      active = false
    }
  }, [search, typeFilter, teacherFilter, classFilter, from, to, expiring])

  const openCreate = () => {
    setEditing(null)
    setFormOpen(true)
  }

  const openEdit = (row: Certificate) => {
    setEditing(row)
    setFormOpen(true)
  }

  const onSaved = (saved: Certificate) => {
    setRows((prev) => {
      const without = prev.filter((r) => r.id !== saved.id)
      return [saved, ...without].sort((a, b) => b.issuedOn.localeCompare(a.issuedOn))
    })
    // Turdagi hujjatlar soni o'zgardi — katalog ekrani va "o'chirish" tugmasi
    // shu songa qaraydi, shuning uchun yangilab qo'yamiz.
    getCertificateTypes().then(setTypes)
    getIssuingTeachers().then(setIssuers)
    setFormOpen(false)
    setEditing(null)
  }

  const remove = async (row: Certificate) => {
    if (!confirm(`"${row.studentName}" — «${row.typeName}» sertifikatini o'chirasizmi?`)) return
    try {
      await deleteCertificate(row.id)
      setRows((prev) => prev.filter((r) => r.id !== row.id))
      getCertificateTypes().then(setTypes)
    } catch (err) {
      alert(certificateError(err))
    }
  }

  const scoredTypes = types.filter((t) => t.isScored)

  /** Z-2 — joriy filtrlar bilan xlsx eksporti (ro'yxatning o'zi so'ralayotgani bilan bir xil). */
  const doExport = () =>
    exportCertificates({
      search: search.trim() || undefined,
      typeId: typeFilter || undefined,
      teacherId: teacherFilter || undefined,
      className: classFilter || undefined,
      from: from || undefined,
      to: to || undefined,
      expiringInDays: expiring ? 60 : undefined,
    })

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Sertifikatlar</h1>
          <p className="text-sm text-slate-400">
            O'quvchilarning IELTS, SAT, olimpiada va boshqa hujjatlari
          </p>
        </div>
        <div className="flex items-center gap-2">
          <div className="flex gap-1 rounded-lg bg-slate-100 p-1">
            <button
              type="button"
              onClick={() => setTab('list')}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                tab === 'list' ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              Ro'yxat
            </button>
            <button
              type="button"
              onClick={() => setTab('results')}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                tab === 'results' ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              Natijalar
            </button>
          </div>
          <Link to="/admin/certificates/types">
            <Button variant="secondary">
              <Settings2 className="h-4 w-4" /> Turlar
            </Button>
          </Link>
          <Button variant="secondary" onClick={doExport} disabled={rows.length === 0}>
            <FileSpreadsheet className="h-4 w-4" /> Eksport
          </Button>
          <Button onClick={openCreate} disabled={types.length === 0}>
            <Plus className="h-4 w-4" /> Yangi sertifikat
          </Button>
        </div>
      </div>

      {!ready ? (
        <Loader label="Yuklanmoqda..." />
      ) : types.length === 0 ? (
        // Birinchi qadam — tur. Usiz sertifikat qo'shib bo'lmaydi, shuning uchun
        // bo'sh ekran to'g'ridan-to'g'ri katalogga yuboradi.
        <Card className="flex flex-col items-center justify-center gap-3 py-16 text-center">
          <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100">
            <Award className="h-7 w-7 text-slate-400" />
          </div>
          <p className="text-sm font-medium text-slate-600">Avval sertifikat turini qo'shing</p>
          <p className="max-w-md text-sm text-slate-400">
            Registr bo'sh holda yetkaziladi: qaysi imtihonlar hisobga olinishini maktabning
            o'zi belgilaydi. "IELTS" (ball qo'yiladi), "Matematika olimpiadasi" — masalan.
          </p>
          <Link to="/admin/certificates/types">
            <Button>
              <Plus className="h-4 w-4" /> Sertifikat turlari
            </Button>
          </Link>
        </Card>
      ) : tab === 'results' ? (
        <CertificateResultsTab scoredTypes={scoredTypes} classNames={classNames} />
      ) : (
        <Card className="p-0">
          <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
            <div className="relative min-w-[200px] flex-1">
              <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
              <input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="O'quvchi yoki hujjat raqami..."
                className={cn(control, 'w-full pl-9')}
              />
            </div>
            <select
              value={typeFilter}
              onChange={(e) => setTypeFilter(e.target.value)}
              className={control}
            >
              <option value="">Barcha turlar</option>
              {types.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name}
                </option>
              ))}
            </select>
            <select
              value={teacherFilter}
              onChange={(e) => setTeacherFilter(e.target.value)}
              className={control}
            >
              <option value="">Barcha o'qituvchilar</option>
              {issuers.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.fullName}
                </option>
              ))}
            </select>
            <select
              value={classFilter}
              onChange={(e) => setClassFilter(e.target.value)}
              className={control}
            >
              <option value="">Barcha sinflar</option>
              {classNames.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
            <input
              type="date"
              value={from}
              onChange={(e) => setFrom(e.target.value)}
              title="Berilgan sana — dan"
              className={control}
            />
            <input
              type="date"
              value={to}
              onChange={(e) => setTo(e.target.value)}
              title="Berilgan sana — gacha"
              className={control}
            />
            <label className="inline-flex cursor-pointer items-center gap-1.5 whitespace-nowrap text-sm text-slate-600">
              <input
                type="checkbox"
                checked={expiring}
                onChange={(e) => setExpiring(e.target.checked)}
                className="h-4 w-4 rounded border-slate-300 accent-brand-600"
              />
              Muddati tugayapti
            </label>
          </div>

          {listLoading ? (
            <div className="p-6">
              <Loader label="Yuklanmoqda..." />
            </div>
          ) : rows.length === 0 ? (
            <p className="p-10 text-center text-sm text-slate-400">
              Bu shartlar bo'yicha sertifikat topilmadi.
            </p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-4 py-3">O'quvchi</th>
                    <th className="px-4 py-3">Sinf</th>
                    <th className="px-4 py-3">Turi</th>
                    <th className="px-4 py-3">Fan(lar)</th>
                    <th className="px-4 py-3">O'qituvchi</th>
                    <th className="px-4 py-3">Raqami</th>
                    <th className="px-4 py-3 text-right">Ball</th>
                    <th className="px-4 py-3">Berilgan</th>
                    <th className="px-4 py-3">Muddati</th>
                    <th className="px-4 py-3 text-right">Amallar</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {rows.map((r) => (
                    <tr key={r.id} className="hover:bg-slate-50/60">
                      <td className="px-4 py-3 font-medium text-slate-800">{r.studentName}</td>
                      <td className="px-4 py-3 text-slate-500">{r.className || '—'}</td>
                      <td className="px-4 py-3 text-slate-600">{r.typeName}</td>
                      <td className="px-4 py-3 text-slate-500">
                        {r.subjectNames.length > 0 ? r.subjectNames.join(', ') : '—'}
                      </td>
                      <td className="px-4 py-3 text-slate-500">{r.teacherName ?? '—'}</td>
                      <td className="px-4 py-3 text-slate-500">{r.number ?? '—'}</td>
                      <td className="px-4 py-3 text-right font-semibold text-slate-800">
                        {r.score != null ? r.score : '—'}
                      </td>
                      <td className="px-4 py-3 text-slate-500">{formatDate(r.issuedOn)}</td>
                      <td className="px-4 py-3">
                        {r.expiresOn ? (
                          <span
                            className={cn(
                              r.isExpired ? 'font-medium text-red-600' : 'text-slate-500',
                            )}
                            title={r.isExpired ? "Muddati o'tgan" : undefined}
                          >
                            {formatDate(r.expiresOn)}
                          </span>
                        ) : (
                          <span className="text-slate-400">Muddatsiz</span>
                        )}
                      </td>
                      <td className="px-4 py-3">
                        <div className="flex items-center justify-end gap-0.5">
                          {r.fileUrl && (
                            <a
                              href={r.fileUrl}
                              target="_blank"
                              rel="noreferrer"
                              title="Faylni ochish"
                              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-brand-600"
                            >
                              <Paperclip className="h-4 w-4" />
                            </a>
                          )}
                          <button
                            type="button"
                            title="Tahrirlash"
                            onClick={() => openEdit(r)}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                          >
                            <Pencil className="h-4 w-4" />
                          </button>
                          <button
                            type="button"
                            title="O'chirish"
                            onClick={() => remove(r)}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>
      )}

      {/* Forma har ochilganda QAYTA ULANADI — boshlang'ich qiymatlar `useState` da
          beriladi, ya'ni effekt ichida `setState` qilishga hojat yo'q. */}
      {formOpen && (
        <CertificateFormModal
          key={editing?.id ?? 'new'}
          editing={editing}
          types={types}
          students={students}
          subjects={subjects}
          teachers={teachers}
          onClose={() => {
            setFormOpen(false)
            setEditing(null)
          }}
          onSaved={onSaved}
        />
      )}
    </div>
  )
}
