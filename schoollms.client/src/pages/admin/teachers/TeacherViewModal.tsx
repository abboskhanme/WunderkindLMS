import { useEffect, useState } from 'react'
import { Award, Paperclip } from 'lucide-react'
import type { Credentials, Subject, Teacher } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { CredentialsBox } from '@/components/ui/CredentialsBox'
import { getTeacherCredentials, resetTeacherPassword } from '@/api/services/teachers'
import { getCertificates, type Certificate } from '@/api/services/certificates'
import { genderLabels, formatMonth, teacherCategoryLabel } from '@/config/constants'
import { formatDate, cn } from '@/lib/utils'

interface Props {
  teacher: Teacher | null
  subjects: Subject[]
  onClose: () => void
}

type Tab = 'umumiy' | 'sertifikatlar'

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-4 border-b border-slate-100 py-2.5 last:border-0">
      <span className="text-sm text-slate-400">{label}</span>
      <span className="text-right text-sm font-medium text-slate-800">{value}</span>
    </div>
  )
}

/**
 * O'qituvchi kartochkasi — bugungacha faqat "Umumiy" ma'lumot (modal, sahifa emas:
 * ro'yxatdagi "ko'z" tugmasidan ochiladi).
 *
 * <b>"Sertifikatlar" tab'i — Z-4</b> (docs/modules/students-parity.md §2.7). Bu
 * o'qituvchi BERGAN hujjatlar: registr API'si allaqachon `teacherId` bo'yicha
 * filtrlaydi (`CertificatesController.GetAll`), shuning uchun yangi backend yo'li
 * kerak emas — faqat shu ekran. Ro'yxat FAQAT O'QISH uchun: qo'shish/tahrirlash —
 * "Sertifikatlar" bo'limining o'zida yoki o'quvchi kartochkasida (Z-1).
 */
export function TeacherViewModal({ teacher, subjects, onClose }: Props) {
  const [credentials, setCredentials] = useState<Credentials | null>(null)
  const [tab, setTab] = useState<Tab>('umumiy')
  const [certs, setCerts] = useState<Certificate[]>([])
  const [certsLoading, setCertsLoading] = useState(false)
  const [certsLoaded, setCertsLoaded] = useState(false)

  useEffect(() => {
    if (!teacher) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- tanlov bo'shaganda eski natijani tozalaymiz (maqsadli)
      setCredentials(null)
      return
    }
    let active = true
    setCredentials(null)
    getTeacherCredentials(teacher.id)
      .then((c) => active && setCredentials(c))
      .catch(() => active && setCredentials(null))
    return () => {
      active = false
    }
  }, [teacher])

  useEffect(() => {
    // O'qituvchi almashganda (yoki oyna yopilganda) "Sertifikatlar" keshi eskiradi —
    // boshqa o'qituvchining hujjatlari birinchi kadrda ko'rinib qolmasin.
    // eslint-disable-next-line react-hooks/set-state-in-effect -- o'qituvchi almashdi: tab va kesh boshidan (maqsadli)
    setTab('umumiy')
    setCerts([])
    setCertsLoaded(false)
  }, [teacher])

  useEffect(() => {
    if (tab !== 'sertifikatlar' || !teacher || certsLoaded) return
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- tab ochilganda hujjatlarni yuklaymiz (maqsadli)
    setCertsLoading(true)
    getCertificates({ teacherId: teacher.id })
      .then((data) => {
        if (!active) return
        setCerts(data)
        setCertsLoaded(true)
      })
      .finally(() => active && setCertsLoading(false))
    return () => {
      active = false
    }
  }, [tab, teacher, certsLoaded])

  const subjectNames = teacher
    ? teacher.subjectIds
        .map((id) => subjects.find((s) => s.id === id)?.name)
        .filter(Boolean)
        .join(', ')
    : ''

  return (
    <Modal
      open={!!teacher}
      onClose={onClose}
      title="O'qituvchi ma'lumotlari"
      size="lg"
      footer={
        <Button variant="secondary" onClick={onClose}>
          Yopish
        </Button>
      }
    >
      {teacher && (
        <div className="space-y-4">
          <div className="flex gap-1 rounded-lg bg-slate-100 p-1">
            <button
              type="button"
              onClick={() => setTab('umumiy')}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                tab === 'umumiy' ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              Umumiy
            </button>
            <button
              type="button"
              onClick={() => setTab('sertifikatlar')}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                tab === 'sertifikatlar'
                  ? 'bg-white text-brand-700 shadow-sm'
                  : 'text-slate-500 hover:text-slate-700',
              )}
            >
              Sertifikatlar
            </button>
          </div>

          {tab === 'umumiy' ? (
            <div>
              <Row label="F.I.SH" value={teacher.fullName} />
              <Row label="Jinsi" value={genderLabels[teacher.gender]} />
              <Row label="Tug'ilgan kun" value={formatDate(teacher.birthDate)} />
              <Row label="Manzil" value={teacher.address || '—'} />
              <Row label="Sinf rahbarligi" value={teacher.homeroomClass || '—'} />
              <Row label="Toifa" value={teacherCategoryLabel(teacher.category)} />
              <Row
                label="Maosh hisoblanadi"
                value={
                  teacher.salaryStartDate
                    ? `${formatDate(teacher.salaryStartDate)} dan`
                    : teacher.salaryStartMonth
                      ? `${formatMonth(teacher.salaryStartMonth)} dan`
                      : 'o\'quv yili boshidan'
                }
              />
              <Row label="Fanlar" value={subjectNames || '—'} />
              <CredentialsBox
                credentials={credentials}
                onReset={async () => {
                  const c = await resetTeacherPassword(teacher.id)
                  setCredentials(c)
                }}
              />
            </div>
          ) : certsLoading ? (
            <Loader label="Yuklanmoqda..." />
          ) : certs.length === 0 ? (
            <div className="flex flex-col items-center gap-2 py-10 text-center">
              <Award className="h-8 w-8 text-slate-300" />
              <p className="text-sm text-slate-400">
                Bu o'qituvchi hali sertifikat/hujjat bermagan
              </p>
            </div>
          ) : (
            <div className="space-y-2">
              {certs.map((row) => (
                <div
                  key={row.id}
                  className="flex flex-wrap items-center gap-3 rounded-lg border border-slate-100 px-3 py-2"
                >
                  <div className="min-w-0 flex-1">
                    <p className="flex flex-wrap items-center gap-2 text-sm font-medium text-slate-800">
                      {row.studentName}
                      <span className="text-slate-400">— {row.typeName}</span>
                      {row.number && <span className="text-slate-400">№ {row.number}</span>}
                      {row.typeIsScored && row.score != null && (
                        <span className="rounded bg-brand-50 px-2 py-0.5 text-xs font-semibold text-brand-700">
                          {row.score} ball
                        </span>
                      )}
                      {row.isExpired && (
                        <span className="rounded bg-red-50 px-2 py-0.5 text-xs font-medium text-red-600">
                          Muddati o'tgan
                        </span>
                      )}
                    </p>
                    <p className={cn('mt-0.5 text-xs', row.isExpired ? 'text-red-400' : 'text-slate-400')}>
                      {row.className || '—'} · {formatDate(row.issuedOn)}
                      {row.expiresOn ? ` — ${formatDate(row.expiresOn)}` : ''}
                      {row.subjectNames.length > 0 ? ` · ${row.subjectNames.join(', ')}` : ''}
                    </p>
                  </div>
                  {row.fileUrl && (
                    <a
                      href={row.fileUrl}
                      target="_blank"
                      rel="noreferrer"
                      title="Faylni ochish"
                      className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-brand-600"
                    >
                      <Paperclip className="h-4 w-4" />
                    </a>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </Modal>
  )
}
