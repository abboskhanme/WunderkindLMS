import { useEffect, useState } from 'react'
import { MessageSquareWarning, Lightbulb, CheckCircle2, GraduationCap, Users } from 'lucide-react'
import type { Feedback } from '@/types'
import { getFeedback, resolveFeedback } from '@/api/services/feedback'
import { formatDate, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'

type TypeFilter = '' | 'suggestion' | 'complaint'
type StatusFilter = '' | 'new' | 'resolved'

export function FeedbackPage() {
  const [items, setItems] = useState<Feedback[]>([])
  const [loading, setLoading] = useState(true)
  const [type, setType] = useState<TypeFilter>('')
  const [status, setStatus] = useState<StatusFilter>('')

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    getFeedback(type, status)
      .then(setItems)
      .finally(() => setLoading(false))
  }, [type, status])

  const resolve = (id: string) => {
    resolveFeedback(id).then(() =>
      setItems((p) => p.map((f) => (f.id === id ? { ...f, status: 'resolved' } : f))),
    )
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Taklif va shikoyatlar</h1>
        <p className="text-sm text-slate-400">Ota-onalar ilova orqali yuborgan murojaatlar</p>
      </div>

      <div className="flex flex-wrap gap-2">
        <Seg label="Hammasi" active={type === ''} onClick={() => setType('')} />
        <Seg label="Takliflar" active={type === 'suggestion'} onClick={() => setType('suggestion')} />
        <Seg label="Shikoyatlar" active={type === 'complaint'} onClick={() => setType('complaint')} />
        <span className="mx-1 w-px bg-slate-200" />
        <Seg label="Barcha holat" active={status === ''} onClick={() => setStatus('')} />
        <Seg label="Yangi" active={status === 'new'} onClick={() => setStatus('new')} />
        <Seg label="Hal qilingan" active={status === 'resolved'} onClick={() => setStatus('resolved')} />
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : items.length === 0 ? (
        <Card>
          <p className="py-10 text-center text-slate-400">Murojaatlar yo'q</p>
        </Card>
      ) : (
        <div className="space-y-3">
          {items.map((f) => (
            <Card key={f.id} className="flex flex-col gap-2">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex items-center gap-2">
                  {f.type === 'complaint' ? (
                    <span className="inline-flex items-center gap-1 rounded-md bg-red-50 px-2 py-0.5 text-xs font-medium text-red-700">
                      <MessageSquareWarning className="h-3.5 w-3.5" /> Shikoyat
                    </span>
                  ) : (
                    <span className="inline-flex items-center gap-1 rounded-md bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700">
                      <Lightbulb className="h-3.5 w-3.5" /> Taklif
                    </span>
                  )}
                  {f.senderRole === 'teacher' ? (
                    <span className="inline-flex items-center gap-1 rounded-md bg-indigo-50 px-2 py-0.5 text-xs font-medium text-indigo-700">
                      <GraduationCap className="h-3.5 w-3.5" /> O'qituvchi
                    </span>
                  ) : (
                    <span className="inline-flex items-center gap-1 rounded-md bg-sky-50 px-2 py-0.5 text-xs font-medium text-sky-700">
                      <Users className="h-3.5 w-3.5" /> Ota-ona
                    </span>
                  )}
                  {f.status === 'resolved' ? (
                    <span className="inline-flex items-center gap-1 rounded-md bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">
                      <CheckCircle2 className="h-3.5 w-3.5" /> Hal qilingan
                    </span>
                  ) : (
                    <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-500">
                      Yangi
                    </span>
                  )}
                </div>
                {f.status === 'new' && (
                  <Button variant="secondary" onClick={() => resolve(f.id)}>
                    <CheckCircle2 className="h-4 w-4" /> Hal qilindi
                  </Button>
                )}
              </div>
              <p className="whitespace-pre-wrap break-words text-sm text-slate-700">{f.text}</p>
              {f.imageUrl && (
                <a href={f.imageUrl} target="_blank" rel="noreferrer" className="block w-fit">
                  <img
                    src={f.imageUrl}
                    alt="Biriktirilgan rasm"
                    className="max-h-44 rounded-lg border border-slate-200 object-cover"
                  />
                </a>
              )}
              <div className="text-xs text-slate-400">
                {f.senderName || f.parentName || f.studentName || 'Noma\'lum'}
                {f.senderRole === 'parent' && f.studentName ? ` · farzandi: ${f.studentName}` : ''}
                {f.senderRole === 'parent' && f.className ? ` · ${f.className}` : ''}
                {' · '}
                {formatDate(f.createdAt)}
              </div>
            </Card>
          ))}
        </div>
      )}
    </div>
  )
}

function Seg({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
        active ? 'border-brand-500 bg-brand-50 text-brand-700' : 'border-slate-200 text-slate-600 hover:bg-slate-50',
      )}
    >
      {label}
    </button>
  )
}
