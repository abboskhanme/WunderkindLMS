/**
 * Moliya → Pul aylanmasi (P1-26).
 *
 * Maktabning butun pul aylanmasi 3D halqa ko'rinishida: kirim manbalari bir
 * yoyda, chiqimlar qarama-qarshi yoyda, markazda umumiy aylanma tuguni.
 *
 * <b>Three.js LAZY yuklanadi.</b> `MoneyFlowRing` `React.lazy` orqali
 * chaqiriladi, ya'ni `three` alohida chunk'ga chiqadi va bu sahifani
 * ochmagan foydalanuvchi uni umuman yuklamaydi. Shu sababli halqa ustida
 * `Suspense` bor va uning `fallback`i — oddiy `Loader`.
 *
 * <b>Jadval — bezak emas.</b> U bir vaqtning o'zida uch vazifani bajaradi:
 * (1) aniq raqamlarni har doim ko'rsatadi, (2) WebGL yo'q qurilmada
 * zaxira ko'rinish bo'ladi, (3) ekran o'quvchi (screen reader) uchun
 * ma'lumotning matnli manbai.
 */
import { Suspense, lazy, useCallback, useEffect, useState } from 'react'
import { AlertCircle, RefreshCw, TrendingDown, TrendingUp, Wallet } from 'lucide-react'
import {
  formatSom,
  getMoneyFlow,
  type MoneyFlow,
  type MoneyFlowNode,
} from '@/api/services/moneyFlow'
import { useAuth } from '@/context/auth-context'
import { formatDate } from '@/lib/utils'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { StatCard } from '@/components/ui/StatCard'

/**
 * Halqa — FAQAT shu yerda, dinamik import orqali. Statik `import` qilinsa
 * `three` moliya bo'limining umumiy bundle'iga qo'shilib ketardi.
 */
const MoneyFlowRing = lazy(() =>
  import('@/components/charts/MoneyFlowRing').then((m) => ({ default: m.MoneyFlowRing })),
)

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

const today = new Date().toISOString().slice(0, 10)
const yearStart = `${today.slice(0, 4)}-01-01`

/** SPEC §4.3: moliya hisobotlari faqat admin va direktorga ochiq. */
const ALLOWED_ROLES = ['admin', 'superadmin']

type Status = 'loading' | 'ready' | 'error'

export function MoneyFlowPage() {
  const { user } = useAuth()
  const allowed = user !== null && ALLOWED_ROLES.includes(user.role)

  const [from, setFrom] = useState(yearStart)
  const [to, setTo] = useState(today)
  const [flow, setFlow] = useState<MoneyFlow | null>(null)
  const [status, setStatus] = useState<Status>('loading')
  const [errorText, setErrorText] = useState('')

  const load = useCallback(() => {
    if (!allowed) return
    setStatus('loading')
    getMoneyFlow(from, to)
      .then((data) => {
        setFlow(data)
        setStatus('ready')
      })
      .catch((error: unknown) => {
        setFlow(null)
        setErrorText(describeError(error))
        setStatus('error')
      })
  }, [allowed, from, to])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- davr o'zgarganda qayta yuklash (loyihadagi mavjud naqsh, FinancePage bilan bir xil)
  useEffect(() => load(), [load])

  if (!allowed) {
    return (
      <Card>
        <p className="py-12 text-center text-slate-400">Bu bo'limga ruxsatingiz yo'q.</p>
      </Card>
    )
  }

  const income = sumByKind(flow, 'income')
  const expense = sumByKind(flow, 'expense')
  const hub = flow?.nodes.find((n) => n.kind === 'hub') ?? null
  const net = flow?.nodes.find((n) => n.kind === 'net') ?? null
  const isDeficit = net !== null && flow !== null
    ? flow.links.some((l) => l.source === net.id && l.target === 'hub')
    : false

  return (
    <div className="space-y-5">
      {/* Sarlavha va davr filtri */}
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Pul aylanmasi</h1>
          <p className="mt-0.5 text-sm text-slate-500">
            {formatDate(from)} — {formatDate(to)} oralig'idagi butun pul harakati. Manba:
            buxgalteriya jurnali (ledger).
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <input
            type="date"
            value={from}
            max={to}
            onChange={(e) => setFrom(e.target.value)}
            aria-label="Boshlanish sanasi"
            className={control}
          />
          <span className="text-slate-400">—</span>
          <input
            type="date"
            value={to}
            min={from}
            onChange={(e) => setTo(e.target.value)}
            aria-label="Tugash sanasi"
            className={control}
          />
          <Button variant="secondary" onClick={load} disabled={status === 'loading'}>
            <RefreshCw className="h-4 w-4" />
            Yangilash
          </Button>
        </div>
      </div>

      {/* Umumiy raqamlar */}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard
          label="Kirim"
          value={status === 'ready' ? formatSom(income) : '—'}
          icon={TrendingUp}
          iconBg="bg-green-50"
          iconColor="text-green-600"
        />
        <StatCard
          label="Chiqim"
          value={status === 'ready' ? formatSom(expense) : '—'}
          icon={TrendingDown}
          iconBg="bg-red-50"
          iconColor="text-red-600"
        />
        <StatCard
          label="Umumiy aylanma"
          value={status === 'ready' && hub ? formatSom(hub.value) : '—'}
          icon={Wallet}
          hint="Kirim va chiqim shu tugun orqali o'tadi"
        />
        <StatCard
          label={isDeficit ? 'Kamomad' : 'Sof foyda'}
          value={status === 'ready' ? formatSom(net ? net.value : 0) : '—'}
          icon={isDeficit ? TrendingDown : TrendingUp}
          iconBg={isDeficit ? 'bg-red-50' : 'bg-amber-50'}
          iconColor={isDeficit ? 'text-red-600' : 'text-amber-600'}
        />
      </div>

      {/* --- Uchta holat: yuklanmoqda / xato / bo'sh --- */}
      {status === 'loading' && (
        <Card>
          <Loader label="Pul aylanmasi hisoblanmoqda…" />
        </Card>
      )}

      {status === 'error' && (
        <Card>
          <div className="flex flex-col items-center gap-3 py-12 text-center">
            <AlertCircle className="h-8 w-8 text-red-500" />
            <p className="text-sm font-medium text-slate-700">Ma'lumotni yuklab bo'lmadi</p>
            <p className="max-w-md text-sm text-slate-500">{errorText}</p>
            <Button variant="secondary" onClick={load}>
              <RefreshCw className="h-4 w-4" />
              Qayta urinish
            </Button>
          </div>
        </Card>
      )}

      {status === 'ready' && flow !== null && flow.nodes.length === 0 && (
        <Card>
          <div className="flex flex-col items-center gap-2 py-12 text-center">
            <Wallet className="h-8 w-8 text-slate-300" />
            <p className="text-sm font-medium text-slate-700">Bu davrda pul harakati yo'q</p>
            <p className="max-w-md text-sm text-slate-500">
              Buxgalteriya jurnalida {formatDate(from)} — {formatDate(to)} oralig'i uchun
              birorta yozuv topilmadi. Boshqa davrni tanlang yoki to'lov va chiqimlar
              kiritilishini kuting.
            </p>
          </div>
        </Card>
      )}

      {/* --- Halqa + jadval --- */}
      {status === 'ready' && flow !== null && flow.nodes.length > 0 && (
        <>
          {/* Card ishlatilmaydi: halqaning o'z konteyneri to'q fonli va
              to'ldirishsiz bo'lishi kerak (Card `p-5` beradi). */}
          <div className="overflow-hidden rounded-2xl shadow-sm">
            <Suspense
              fallback={
                <div className="flex h-[26rem] items-center justify-center rounded-2xl bg-slate-900 sm:h-[32rem]">
                  <Loader label="3D ko'rinish yuklanmoqda…" />
                </div>
              }
            >
              <MoneyFlowRing flow={flow} />
            </Suspense>
          </div>

          <div className="grid gap-4 lg:grid-cols-2">
            <BreakdownCard
              title="Kirim manbalari"
              nodes={flow.nodes.filter((n) => n.kind === 'income')}
              total={income}
              accent="text-green-600"
            />
            <BreakdownCard
              title="Chiqimlar"
              nodes={flow.nodes.filter((n) => n.kind === 'expense')}
              total={expense}
              accent="text-red-600"
            />
          </div>

          <Card>
            <p className="text-sm text-slate-500">
              <span className="font-medium text-slate-700">Tekshiruv:</span> kirim yig'indisi (
              {formatSom(sumLinks(flow, 'in'))}) = umumiy aylanma (
              {formatSom(hub ? hub.value : 0)}) = chiqim yig'indisi (
              {formatSom(sumLinks(flow, 'out'))}). Raqamlar buxgalteriya jurnalidan tiyingacha
              olinadi.
            </p>
          </Card>
        </>
      )}
    </div>
  )
}

/* ------------------------------------------------------------------ */

interface BreakdownProps {
  title: string
  nodes: MoneyFlowNode[]
  total: number
  accent: string
}

function BreakdownCard({ title, nodes, total, accent }: BreakdownProps) {
  return (
    <Card>
      <h2 className="mb-3 text-sm font-semibold text-slate-700">{title}</h2>
      {nodes.length === 0 ? (
        <p className="py-6 text-center text-sm text-slate-400">Bu davrda yozuv yo'q.</p>
      ) : (
        <table className="w-full text-sm">
          <tbody>
            {nodes.map((node) => (
              <tr key={node.id} className="border-b border-slate-100 last:border-0">
                <td className="py-2 pr-3 text-slate-600">{node.label}</td>
                <td className="py-2 text-right text-slate-400">
                  {total > 0 ? `${Math.round((node.value / total) * 100)}%` : '—'}
                </td>
                <td className={`py-2 pl-3 text-right font-medium ${accent}`}>
                  {formatSom(node.value)}
                </td>
              </tr>
            ))}
            <tr>
              <td className="pt-2.5 font-medium text-slate-700">Jami</td>
              <td />
              <td className="pt-2.5 text-right font-semibold text-slate-800">
                {formatSom(total)}
              </td>
            </tr>
          </tbody>
        </table>
      )}
    </Card>
  )
}

/* ------------------------------------------------------------------ */

function sumByKind(flow: MoneyFlow | null, kind: MoneyFlowNode['kind']): number {
  if (!flow) return 0
  return flow.nodes.filter((n) => n.kind === kind).reduce((sum, n) => sum + n.value, 0)
}

/** Markazga kiruvchi / markazdan chiquvchi bog'lanishlar yig'indisi. */
function sumLinks(flow: MoneyFlow, side: 'in' | 'out'): number {
  return flow.links
    .filter((l) => (side === 'in' ? l.target === 'hub' : l.source === 'hub'))
    .reduce((sum, l) => sum + l.value, 0)
}

/**
 * Xatoni foydalanuvchi tiliga o'giradi. 403 alohida: "server ishlamayapti"
 * emas, "sizga ruxsat yo'q" — bu ikkisi butunlay boshqa muammo va
 * foydalanuvchi ularni farqlay olishi kerak.
 */
function describeError(error: unknown): string {
  const status = (error as { response?: { status?: number } })?.response?.status
  if (status === 403) return "Moliya hisobotlarini ko'rish uchun ruxsatingiz yo'q."
  if (status === 400) return "So'ralgan davr noto'g'ri. Sanalarni tekshiring."
  if (status !== undefined && status >= 500) return 'Serverda xatolik yuz berdi. Birozdan so’ng qayta urinib ko’ring.'
  return "Server bilan bog'lanib bo'lmadi. Internet aloqasini tekshiring."
}

export default MoneyFlowPage
