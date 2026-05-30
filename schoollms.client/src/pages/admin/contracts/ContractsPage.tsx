import { useEffect, useState } from 'react'
import {
  Upload,
  FileText,
  Trash2,
  Send,
  CheckCircle2,
  XCircle,
  AlertTriangle,
} from 'lucide-react'
import type { ContractTemplate, ParentRecipient, StaffRecipient, SendResult } from '@/types'
import {
  getTemplates,
  createTemplate,
  deleteTemplate,
  getParentRecipients,
  getStaffRecipients,
  sendContracts,
} from '@/api/services/contracts'
import { uploadAdminFile } from '@/api/services/students'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'

type Target = 'parent' | 'staff'

const TOKENS: Record<Target, string[]> = {
  parent: ['@ota_ona', '@telefon', '@farzandlar', '@sana', '@raqam'],
  staff: ['@fish', '@telefon', '@lavozim', '@fanlar', '@tugilgan_kun', '@manzil', '@oylik', '@sana', '@raqam'],
}

const DOCX = '.docx,application/vnd.openxmlformats-officedocument.wordprocessingml.document'

export function ContractsPage() {
  const [target, setTarget] = useState<Target>('staff')
  const [templates, setTemplates] = useState<ContractTemplate[]>([])
  const [selectedTpl, setSelectedTpl] = useState('')
  const [parents, setParents] = useState<ParentRecipient[]>([])
  const [staff, setStaff] = useState<StaffRecipient[]>([])
  const [checked, setChecked] = useState<Set<string>>(new Set())
  const [loading, setLoading] = useState(true)
  const [uploading, setUploading] = useState(false)
  const [sending, setSending] = useState(false)
  const [results, setResults] = useState<SendResult[] | null>(null)

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- target almashganda qayta yuklash (maqsadli)
    setLoading(true)
    setChecked(new Set())
    setResults(null)
    setSelectedTpl('')
    const recipients = target === 'staff' ? getStaffRecipients() : getParentRecipients()
    Promise.all([getTemplates(target), recipients])
      .then(([tpls, recs]) => {
        setTemplates(tpls)
        setSelectedTpl(tpls[0]?.id ?? '')
        if (target === 'staff') setStaff(recs as StaffRecipient[])
        else setParents(recs as ParentRecipient[])
      })
      .finally(() => setLoading(false))
  }, [target])

  const handleUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const f = e.target.files?.[0]
    if (!f) return
    setUploading(true)
    try {
      const up = await uploadAdminFile(f)
      const tpl = await createTemplate(target, f.name, up.url, f.name)
      setTemplates((prev) => [tpl, ...prev])
      setSelectedTpl(tpl.id)
    } finally {
      setUploading(false)
      e.target.value = ''
    }
  }

  const handleDeleteTpl = async (id: string) => {
    if (!confirm('Andozani o\'chirasizmi?')) return
    await deleteTemplate(id)
    setTemplates((prev) => prev.filter((t) => t.id !== id))
    if (selectedTpl === id) setSelectedTpl('')
  }

  // Oluvchilar ro'yxatini target bo'yicha umumiy ko'rinishga keltiramiz.
  const rows = target === 'staff'
    ? staff.map((s) => ({
        key: s.teacherId,
        name: s.fullName,
        sub: s.phone || '—',
        registered: s.registered,
        lastNumber: s.lastNumber,
      }))
    : parents.map((p) => ({
        key: p.key,
        name: p.parentName || '(nomsiz)',
        sub: `${p.phone || '—'} · ${p.children.join(', ')}`,
        registered: p.registered,
        lastNumber: p.lastNumber,
      }))

  const selectableKeys = rows.filter((r) => r.registered).map((r) => r.key)
  const allChecked = selectableKeys.length > 0 && selectableKeys.every((k) => checked.has(k))

  const toggle = (key: string) =>
    setChecked((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  const toggleAll = () =>
    setChecked(allChecked ? new Set() : new Set(selectableKeys))

  const handleSend = async () => {
    if (!selectedTpl || checked.size === 0) return
    setSending(true)
    setResults(null)
    try {
      const res = await sendContracts(target, selectedTpl, [...checked])
      setResults(res)
      setChecked(new Set())
      if (target === 'staff') setStaff(await getStaffRecipients())
      else setParents(await getParentRecipients())
    } finally {
      setSending(false)
    }
  }

  const sentOk = results?.filter((r) => r.ok).length ?? 0
  const sentFail = results?.filter((r) => !r.ok).length ?? 0

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Shartnomalar</h1>
        <p className="text-sm text-slate-400">
          Word andoza yuklang, oluvchilarni tanlang — @-o'rinbosarlar to'ldirilib Telegram orqali yuboriladi
        </p>
      </div>

      {/* Target tab */}
      <div className="inline-flex rounded-lg border border-slate-200 bg-white p-1">
        <TabButton active={target === 'staff'} onClick={() => setTarget('staff')}>
          Xodimlar
        </TabButton>
        <TabButton active={target === 'parent'} onClick={() => setTarget('parent')}>
          Ota-onalar
        </TabButton>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          {/* Andoza paneli */}
          <Card>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <h2 className="font-semibold text-slate-800">Word andoza</h2>
              <label
                className={cn(
                  'inline-flex cursor-pointer items-center gap-2 rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-brand-700',
                  uploading && 'pointer-events-none opacity-60',
                )}
              >
                <Upload className="h-4 w-4" />
                {uploading ? 'Yuklanmoqda...' : 'Word yuklash (.docx)'}
                <input type="file" accept={DOCX} hidden onChange={handleUpload} />
              </label>
            </div>

            <div className="mt-3 space-y-2">
              {templates.map((t) => (
                <label
                  key={t.id}
                  className={cn(
                    'flex cursor-pointer items-center gap-3 rounded-xl border p-3 transition-colors',
                    selectedTpl === t.id
                      ? 'border-brand-300 bg-brand-50'
                      : 'border-slate-100 hover:bg-slate-50',
                  )}
                >
                  <input
                    type="radio"
                    name="tpl"
                    checked={selectedTpl === t.id}
                    onChange={() => setSelectedTpl(t.id)}
                    className="h-4 w-4 accent-brand-600"
                  />
                  <FileText className="h-5 w-5 text-slate-400" />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium text-slate-800">{t.name || t.fileName}</p>
                    <p className="truncate text-xs text-slate-400">{t.fileName}</p>
                  </div>
                  <button
                    type="button"
                    title="O'chirish"
                    onClick={(e) => {
                      e.preventDefault()
                      handleDeleteTpl(t.id)
                    }}
                    className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </label>
              ))}
              {templates.length === 0 && (
                <p className="py-4 text-center text-sm text-slate-400">
                  Hali andoza yuklanmagan. Yuqoridan Word (.docx) yuklang.
                </p>
              )}
            </div>

            {/* Token yordami */}
            <div className="mt-3 rounded-lg bg-slate-50 p-3">
              <p className="mb-1 text-xs font-medium text-slate-500">
                Andozada quyidagi o'rinbosarlardan foydalaning (yuborishda almashtiriladi):
              </p>
              <div className="flex flex-wrap gap-1.5">
                {TOKENS[target].map((tok) => (
                  <code
                    key={tok}
                    className="rounded bg-white px-1.5 py-0.5 text-xs font-medium text-brand-700 ring-1 ring-slate-200"
                  >
                    {tok}
                  </code>
                ))}
              </div>
            </div>
          </Card>

          {/* Yuborish natijasi */}
          {results && (
            <Card className={cn('border', sentFail ? 'border-amber-200' : 'border-emerald-200')}>
              <p className="text-sm font-medium text-slate-700">
                {sentOk} ta yuborildi{sentFail > 0 ? `, ${sentFail} ta yuborilmadi` : ''}
              </p>
              {sentFail > 0 && (
                <ul className="mt-2 space-y-1 text-xs text-slate-500">
                  {results
                    .filter((r) => !r.ok)
                    .map((r) => (
                      <li key={r.recipientKey} className="flex items-center gap-1">
                        <XCircle className="h-3 w-3 text-red-500" /> {r.message}
                      </li>
                    ))}
                </ul>
              )}
            </Card>
          )}

          {/* Oluvchilar */}
          <Card className="p-0">
            <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 p-4">
              <h2 className="font-semibold text-slate-800">
                Oluvchilar {target === 'staff' ? '(xodimlar)' : '(ota-onalar)'}
              </h2>
              <Button onClick={handleSend} disabled={!selectedTpl || checked.size === 0 || sending}>
                <Send className="h-4 w-4" />
                {sending ? 'Yuborilmoqda...' : `Tanlanganlarni yuborish (${checked.size})`}
              </Button>
            </div>

            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="w-10 px-4 py-3">
                      <input
                        type="checkbox"
                        checked={allChecked}
                        onChange={toggleAll}
                        disabled={selectableKeys.length === 0}
                        className="h-4 w-4 accent-brand-600"
                      />
                    </th>
                    <th className="px-4 py-3">{target === 'staff' ? 'F.I.SH' : 'Ota-ona'}</th>
                    <th className="px-4 py-3">{target === 'staff' ? 'Telefon' : 'Telefon · farzandlar'}</th>
                    <th className="px-4 py-3">Telegram</th>
                    <th className="px-4 py-3 text-center">Oxirgi raqam</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {rows.map((r) => (
                    <tr key={r.key} className={cn('hover:bg-slate-50/60', !r.registered && 'opacity-60')}>
                      <td className="px-4 py-3">
                        <input
                          type="checkbox"
                          checked={checked.has(r.key)}
                          disabled={!r.registered}
                          onChange={() => toggle(r.key)}
                          className="h-4 w-4 accent-brand-600 disabled:cursor-not-allowed"
                        />
                      </td>
                      <td className="px-4 py-3 font-medium text-slate-800">{r.name}</td>
                      <td className="px-4 py-3 text-slate-500">{r.sub}</td>
                      <td className="px-4 py-3">
                        {r.registered ? (
                          <span className="inline-flex items-center gap-1 rounded bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">
                            <CheckCircle2 className="h-3 w-3" /> Ro'yxatda
                          </span>
                        ) : (
                          <span className="inline-flex items-center gap-1 rounded bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700">
                            <AlertTriangle className="h-3 w-3" /> Ro'yxatdan o'tmagan
                          </span>
                        )}
                      </td>
                      <td className="px-4 py-3 text-center text-slate-600">
                        {r.lastNumber != null ? `№ ${r.lastNumber}` : '—'}
                      </td>
                    </tr>
                  ))}
                  {rows.length === 0 && (
                    <tr>
                      <td colSpan={5} className="px-4 py-12 text-center text-slate-400">
                        Oluvchi yo'q
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
            <p className="border-t border-slate-100 px-4 py-3 text-xs text-slate-400">
              Faqat Telegramda ro'yxatdan o'tganlarga yuborish mumkin. Ro'yxatdan o'tish: bot orqali
              telefon raqamini ulashish ({target === 'staff' ? 'xodim telefoni' : 'ota-ona telefoni'} bilan moslashadi).
            </p>
          </Card>
        </>
      )}
    </div>
  )
}

function TabButton({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded-md px-4 py-1.5 text-sm font-medium transition-colors',
        active ? 'bg-brand-600 text-white' : 'text-slate-600 hover:bg-slate-50',
      )}
    >
      {children}
    </button>
  )
}
