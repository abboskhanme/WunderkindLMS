import { useEffect, useState } from 'react'
import { BarChart3 } from 'lucide-react'
import {
  getCertificateResults,
  type CertificateResults,
  type CertificateType,
} from '@/api/services/certificates'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { formatDate, cn } from '@/lib/utils'

interface Props {
  /** Faqat BALLIK turlar — "Natijalar" boshqa turlar uchun ma'noga ega emas. */
  scoredTypes: CertificateType[]
  classNames: string[]
}

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/**
 * "Natijalar" tab'i (§2.3) — bitta standart test bo'yicha o'quvchi × ball jadvali.
 *
 * <b>Registrdan farqi shu tab'da.</b> Ro'yxat hujjatlarni ko'rsatadi, bu esa
 * NATIJANI: har bolaning eng yaxshi va eng oxirgi balli. `is_scored` ustuni
 * aynan shu ekran uchun bor.
 *
 * Yig'ma raqamlar (o'rtacha / eng yuqori / eng past) SERVERDAN keladi — brauzer
 * ularni o'zi sanamaydi.
 */
export function CertificateResultsTab({ scoredTypes, classNames }: Props) {
  const [typeId, setTypeId] = useState(scoredTypes[0]?.id ?? '')
  const [className, setClassName] = useState('')
  const [data, setData] = useState<CertificateResults | null>(null)
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    if (!typeId) return
    // Poyga (race) himoyasi: tez almashtirilganda faqat oxirgi javob qabul qilinadi.
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi so'rovdan oldin holatni belgilaymiz (maqsadli)
    setLoading(true)
    getCertificateResults(typeId, className || undefined)
      .then((d) => {
        if (active) setData(d)
      })
      .finally(() => {
        if (active) setLoading(false)
      })
    return () => {
      active = false
    }
  }, [typeId, className])

  if (scoredTypes.length === 0) {
    return (
      <Card className="flex flex-col items-center justify-center gap-3 py-16 text-center">
        <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100">
          <BarChart3 className="h-7 w-7 text-slate-400" />
        </div>
        <p className="text-sm font-medium text-slate-600">Ballik tur yo'q</p>
        <p className="max-w-md text-sm text-slate-400">
          Natijalar jadvali faqat "ball qo'yiladi" deb belgilangan turlar uchun tuziladi
          (IELTS, SAT kabi standart testlar). Sertifikat turlarida kamida bittasini shunday
          belgilang.
        </p>
      </Card>
    )
  }

  return (
    <div className="space-y-4">
      <Card className="flex flex-wrap items-center gap-3">
        <select value={typeId} onChange={(e) => setTypeId(e.target.value)} className={control}>
          {scoredTypes.map((t) => (
            <option key={t.id} value={t.id}>
              {t.name}
            </option>
          ))}
        </select>
        <select
          value={className}
          onChange={(e) => setClassName(e.target.value)}
          className={control}
        >
          <option value="">Barcha sinflar</option>
          {classNames.map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
        </select>
      </Card>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : !data ? null : data.rows.length === 0 ? (
        <Card className="py-12 text-center text-sm text-slate-400">
          Bu tur bo'yicha hali sertifikat kiritilmagan.
        </Card>
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-4">
            <Stat label="O'quvchilar" value={String(data.studentCount)} />
            <Stat label="Hujjatlar" value={String(data.certificateCount)} />
            <Stat
              label="O'rtacha ball"
              value={data.averageScore != null ? String(data.averageScore) : '—'}
            />
            <Stat
              label="Eng yuqori / eng past"
              value={
                data.maxScore != null && data.minScore != null
                  ? `${data.maxScore} / ${data.minScore}`
                  : '—'
              }
            />
          </div>

          <Card className="p-0">
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="w-10 px-4 py-3">#</th>
                    <th className="px-4 py-3">O'quvchi</th>
                    <th className="px-4 py-3">Sinf</th>
                    <th className="px-4 py-3 text-right">Eng yaxshi ball</th>
                    <th className="px-4 py-3 text-right">Oxirgi ball</th>
                    <th className="px-4 py-3">Oxirgi sana</th>
                    <th className="px-4 py-3 text-right">Hujjatlar</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {data.rows.map((r, i) => (
                    <tr key={r.studentId} className="hover:bg-slate-50/60">
                      <td className="px-4 py-3 text-slate-400">{i + 1}</td>
                      <td className="px-4 py-3 font-medium text-slate-800">
                      <span className="block max-w-[14rem] truncate" title={r.studentName}>
                        {r.studentName}
                      </span>
                    </td>
                      <td className="px-4 py-3 text-slate-500">{r.className || '—'}</td>
                      <td
                        className={cn(
                          'px-4 py-3 text-right font-semibold',
                          r.bestScore != null ? 'text-slate-800' : 'text-slate-300',
                        )}
                      >
                        {r.bestScore != null ? r.bestScore : '—'}
                      </td>
                      <td className="px-4 py-3 text-right text-slate-600">
                        {r.latestScore != null ? r.latestScore : '—'}
                      </td>
                      <td className="px-4 py-3 text-slate-500">{formatDate(r.latestIssuedOn)}</td>
                      <td className="px-4 py-3 text-right text-slate-500">{r.count}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Card>
        </>
      )}
    </div>
  )
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <Card className="p-4">
      <p className="text-xs uppercase tracking-wide text-slate-400">{label}</p>
      <p className="mt-1 text-lg font-semibold text-slate-800">{value}</p>
    </Card>
  )
}
