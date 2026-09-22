/**
 * Sotuv va marketing → Yangiliklar (SM-10,
 * `docs/modules/sales-marketing.md` §6.2).
 *
 * Chapda ro'yxat, o'ngda yozish maydoni, uning ostida O'QUVCHI KO'RADIGAN
 * ko'rinish. E'lon qilish — alohida qadam: u lentaga yozuv qo'yadi VA
 * Telegram orqali xabar yuboradi, shuning uchun tasdiq oynasidan o'tadi
 * (§3.3 N3, N4).
 *
 * UCHTA QOIDA, TAKRORLANMASLIGI UCHUN SHU YERDA:
 *
 * 1. MATN — ODDIY MATN (§3.3 N2). Bu yerda `dangerouslySetInnerHTML` ham,
 *    markdown ham yo'q: matn `whitespace-pre-line` bilan chiziladi, qator
 *    tashlash saqlanadi, qolgan hammasi matnning o'zi.
 * 2. KANAL — FAQAT TELEGRAM (`CLAUDE.md`). Kanal tanlagich yo'q: SMS ham,
 *    mobil push ham mahsulotda yo'q.
 * 3. Bu ekran "Xabarlar → E'lon" ning o'rnini bosmaydi: u bitta sinfga
 *    moslangan xabar yuboradi, bu esa butun maktabga, tarixi bilan
 *    (§3.3 N1). Farqi sarlavha ostida bir jumlada yozilgan.
 */
import { useState } from 'react'
import {
  AlertTriangle,
  ArrowLeftRight,
  FilePlus2,
  Newspaper,
  RefreshCw,
  Save,
  Send,
  Trash2,
  ChevronLeft,
  ChevronRight,
  Pencil,
} from 'lucide-react'
import {
  createNews,
  deleteNews,
  getNews,
  listNews,
  newsErrorMessage,
  publishNews,
  unpublishNews,
  updateNews,
  type NewsAdminDto,
  type NewsAudience,
  type NewsSaveRequest,
  type NewsState,
} from '@/api/services/news'
import { useAsync } from '@/hooks/useAsync'
import { useAuth } from '@/context/auth-context'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Input, Textarea } from '@/components/ui/Input'
import { PhotoUpload } from '@/components/ui/PhotoUpload'
import { Toast } from '@/components/ui/Toast'
import { cn } from '@/lib/utils'
import { NewsConfirmDialog, NewsPublishDialog } from './NewsDialogs'
import {
  AUDIENCE_ORDER,
  audienceLabel,
  audienceListText,
  emptyText,
  formatWhen,
  sortAudience,
  STATE_TABS,
  telegramSummary,
} from './newsLabels'
import { MarketingTabs } from './MarketingTabs'

const PAGE_SIZE = 20

/** Shaklda tahrirlanadigan maydonlar (server nusxasi alohida turadi). */
interface NewsForm {
  title: string
  body: string
  imageUrl: string | null
  audience: NewsAudience[]
}

const EMPTY_FORM: NewsForm = { title: '', body: '', imageUrl: null, audience: [] }

function formOf(item: NewsAdminDto): NewsForm {
  return {
    title: item.title,
    body: item.body,
    imageUrl: item.imageUrl,
    audience: sortAudience(item.audience),
  }
}

function requestOf(form: NewsForm): NewsSaveRequest {
  return {
    title: form.title.trim(),
    body: form.body.trim(),
    imageUrl: form.imageUrl,
    audience: form.audience,
  }
}

export function NewsPage() {
  const { user } = useAuth()
  // Marshrut darajasidagi darvoza SM-12 da qo'shiladi; bu undan mustaqil
  // ikkinchi qavat — ruxsati yo'q odamga tugmalar umuman chizilmaydi.
  const allowed = !user?.permissions || user.permissions.includes('marketing')

  const [state, setState] = useState<NewsState>('all')
  const [page, setPage] = useState(1)

  const { data, loading, error, refetch } = useAsync(
    () =>
      listNews({ state, page, pageSize: PAGE_SIZE }).catch((err: unknown) => {
        throw new Error(newsErrorMessage(err, "Yangiliklarni yuklab bo'lmadi"))
      }),
    [state, page],
  )

  /** Serverdagi nusxa: `null` — yangi yangilik yozilmoqda. */
  const [editing, setEditing] = useState<NewsAdminDto | null>(null)
  /**
   * Ochilgan yangilik arxivdanmi. DTO buni aytmaydi (§5.4 `deleted_at` ni
   * qaytarmaydi), shuning uchun uni QAYSI ro'yxatdan ochilgani hal qiladi:
   * `archived` filtridan ochilgan yozuv faqat o'qish uchun.
   */
  const [editingArchived, setEditingArchived] = useState(false)
  const [form, setForm] = useState<NewsForm>(EMPTY_FORM)
  const [formError, setFormError] = useState<string | null>(null)
  const [opening, setOpening] = useState(false)
  const [saving, setSaving] = useState(false)
  const [sendTelegram, setSendTelegram] = useState(true)

  /**
   * Saqlanmagan matn ustiga boshqa yangilik ochilmoqchi. `row: null` —
   * "Yangi yangilik" tugmasi. Yozilgan matnni jimgina yo'qotish — bu
   * bo'limda eng qimmat nosozlik bo'lardi.
   */
  const [pendingOpen, setPendingOpen] = useState<{ row: NewsAdminDto | null; archived: boolean } | null>(
    null,
  )

  const [publishTarget, setPublishTarget] = useState<NewsAdminDto | null>(null)
  const [unpublishTarget, setUnpublishTarget] = useState<NewsAdminDto | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<NewsAdminDto | null>(null)
  const [dialogBusy, setDialogBusy] = useState(false)
  const [dialogError, setDialogError] = useState<string | null>(null)

  /** Muvaffaqiyat xabarchasi (xatolar shaklning o'zida, qizil chiziqda). */
  const [toast, setToast] = useState<string | null>(null)

  const rows = data?.rows ?? []
  const total = data?.total ?? 0
  const lastPage = Math.max(1, Math.ceil(total / PAGE_SIZE))

  const titleFilled = form.title.trim().length > 0
  const bodyFilled = form.body.trim().length > 0
  const canSave = titleFilled && bodyFilled && form.audience.length > 0
  const isPublished = editing?.publishedAt != null
  const isArchived = editing !== null && editingArchived
  /** Arxiv ro'yxati: o'chirilgan yozuvlar — ularda amal tugmalari yo'q. */
  const archivedView = state === 'archived'

  const changeState = (next: NewsState) => {
    setState(next)
    setPage(1)
  }

  const resetForm = () => {
    setEditing(null)
    setEditingArchived(false)
    setForm(EMPTY_FORM)
    setFormError(null)
    setSendTelegram(true)
  }

  /** Shakl serverdagi nusxadan farq qiladimi (yoki yangi yangilikda nimadir yozilganmi). */
  const isDirty = (): boolean => {
    const base = editing ? formOf(editing) : EMPTY_FORM
    return (
      form.title !== base.title ||
      form.body !== base.body ||
      form.imageUrl !== base.imageUrl ||
      form.audience.join(',') !== base.audience.join(',')
    )
  }

  /** Boshqa yangilikka o'tish: saqlanmagan matn bo'lsa — avval so'raladi. */
  const requestOpen = (row: NewsAdminDto | null, archived: boolean) => {
    if (isDirty()) {
      setPendingOpen({ row, archived })
      return
    }
    if (row) void openEdit(row, archived)
    else resetForm()
  }

  /** Tahrirlash — ro'yxatdagi nusxa emas, serverdagi HOZIRGI holat olinadi. */
  const openEdit = async (row: NewsAdminDto, archived: boolean) => {
    setOpening(true)
    setFormError(null)
    try {
      const fresh = await getNews(row.id)
      setEditing(fresh)
      setEditingArchived(archived)
      setForm(formOf(fresh))
      setSendTelegram(fresh.publishedAt == null)
    } catch (err) {
      setFormError(newsErrorMessage(err, "Yangilikni ochib bo'lmadi"))
    } finally {
      setOpening(false)
    }
  }

  /** Saqlash. Muvaffaqiyatda serverdagi yangi nusxani qaytaradi. */
  const save = async (): Promise<NewsAdminDto | null> => {
    if (!canSave || saving) return null
    setSaving(true)
    setFormError(null)
    try {
      const req = requestOf(form)
      const saved = editing ? await updateNews(editing.id, req) : await createNews(req)
      setEditing(saved)
      setForm(formOf(saved))
      refetch()
      return saved
    } catch (err) {
      setFormError(newsErrorMessage(err, "Saqlab bo'lmadi"))
      return null
    } finally {
      setSaving(false)
    }
  }

  const handleSaveDraft = async () => {
    const saved = await save()
    if (saved) {
      setToast(saved.publishedAt ? "O'zgarishlar saqlandi" : 'Qoralama saqlandi')
    }
  }

  /**
   * "E'lon qilish" — avval saqlanadi (e'lon uchun id kerak), keyin tasdiq
   * oynasi ochiladi. Odam oynani bekor qilsa ham yozgani qoralama sifatida
   * turadi: yozilgan matn yo'qolmaydi.
   */
  const handlePublishClick = async () => {
    const saved = await save()
    if (!saved) return
    setDialogError(null)
    setPublishTarget(saved)
  }

  const confirmPublish = async () => {
    if (!publishTarget) return
    setDialogBusy(true)
    setDialogError(null)
    try {
      const published = await publishNews(publishTarget.id, sendTelegram)
      if (editing?.id === published.id) {
        setEditing(published)
        setForm(formOf(published))
      }
      setPublishTarget(null)
      refetch()
      setToast(publishResultText(published, sendTelegram))
    } catch (err) {
      setDialogError(newsErrorMessage(err, "E'lon qilib bo'lmadi"))
      refetch()
    } finally {
      setDialogBusy(false)
    }
  }

  const confirmUnpublish = async () => {
    if (!unpublishTarget) return
    setDialogBusy(true)
    setDialogError(null)
    try {
      const updated = await unpublishNews(unpublishTarget.id)
      if (editing?.id === updated.id) {
        setEditing(updated)
        setForm(formOf(updated))
      }
      setUnpublishTarget(null)
      refetch()
      setToast("Yangilik e'londan qaytarildi")
    } catch (err) {
      setDialogError(newsErrorMessage(err, "E'londan qaytarib bo'lmadi"))
    } finally {
      setDialogBusy(false)
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    setDialogBusy(true)
    setDialogError(null)
    try {
      await deleteNews(deleteTarget.id)
      if (editing?.id === deleteTarget.id) resetForm()
      setDeleteTarget(null)
      refetch()
      setToast('Yangilik arxivga olindi')
    } catch (err) {
      setDialogError(newsErrorMessage(err, "O'chirib bo'lmadi"))
    } finally {
      setDialogBusy(false)
    }
  }

  const toggleAudience = (value: NewsAudience) => {
    setForm((prev) => ({
      ...prev,
      audience: prev.audience.includes(value)
        ? prev.audience.filter((a) => a !== value)
        : sortAudience([...prev.audience, value]),
    }))
  }

  if (!allowed) {
    return (
      <Card>
        <p className="py-12 text-center text-slate-400">Bu bo'limga ruxsatingiz yo'q.</p>
      </Card>
    )
  }

  return (
    <div className="space-y-6">
      <MarketingTabs />
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Yangiliklar</h1>
          <p className="text-sm text-slate-400">
            Butun maktabga e'lon — lentada qoladi va Telegram orqali yetkaziladi.
            Bitta sinfga moslangan xabar esa "Xabarlar → E'lon" da.
          </p>
        </div>
        <Button variant="secondary" onClick={() => requestOpen(null, false)}>
          <FilePlus2 className="h-4 w-4" /> Yangi yangilik
        </Button>
      </div>

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
        {/* ---------------- Ro'yxat ---------------- */}
        <div className="space-y-3">
          <div className="flex flex-wrap gap-2">
            {STATE_TABS.map((tab) => (
              <button
                key={tab.value}
                type="button"
                onClick={() => changeState(tab.value)}
                className={cn(
                  'rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
                  state === tab.value
                    ? 'border-brand-500 bg-brand-50 text-brand-700'
                    : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                )}
              >
                {tab.label}
              </button>
            ))}
          </div>

          {loading ? (
            <Card>
              <Loader label="Yuklanmoqda..." />
            </Card>
          ) : error ? (
            <Card className="flex flex-col items-center gap-3 py-10 text-center">
              <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-red-50 text-red-600">
                <AlertTriangle className="h-6 w-6" />
              </div>
              <div>
                <p className="font-medium text-slate-800">Ro'yxatni yuklab bo'lmadi</p>
                <p className="mt-1 max-w-sm text-sm text-slate-500">{error}</p>
              </div>
              <Button variant="secondary" onClick={refetch}>
                <RefreshCw className="h-4 w-4" /> Qayta urinish
              </Button>
            </Card>
          ) : rows.length === 0 ? (
            <Card className="flex flex-col items-center gap-2 py-12 text-center">
              <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-slate-100 text-slate-400">
                <Newspaper className="h-6 w-6" />
              </div>
              <p className="text-sm text-slate-400">{emptyText(state)}</p>
              {state !== 'archived' && (
                <p className="max-w-xs text-xs text-slate-400">
                  O'ng tomondagi shaklda sarlavha va matnni yozing, so'ng "Qoralama
                  saqlash" yoki "E'lon qilish".
                </p>
              )}
            </Card>
          ) : (
            <div className="space-y-3">
              {rows.map((row) => (
                <NewsRow
                  key={row.id}
                  row={row}
                  active={editing?.id === row.id}
                  archived={archivedView}
                  busy={opening}
                  onEdit={() => requestOpen(row, archivedView)}
                  onPublish={() => {
                    // Shu qator shaklda ochiq va saqlanmagan bo'lsa — e'lon
                    // serverdagi ESKI matnni yuborardi. Avval saqlatamiz.
                    if (editing?.id === row.id && isDirty()) {
                      setFormError(
                        "Saqlanmagan o'zgarishlar bor — avval \"Qoralama saqlash\" ni bosing.",
                      )
                      return
                    }
                    setDialogError(null)
                    // Ro'yxatdan e'lon qilinganda Telegram sukut bo'yicha yoqiq
                    // (§3.3 N4); tasdiq oynasida uni o'chirib qo'yish mumkin.
                    setSendTelegram(true)
                    setPublishTarget(row)
                  }}
                  onUnpublish={() => {
                    setDialogError(null)
                    setUnpublishTarget(row)
                  }}
                  onDelete={() => {
                    setDialogError(null)
                    setDeleteTarget(row)
                  }}
                />
              ))}

              <div className="flex items-center justify-between px-1">
                <p className="text-xs text-slate-400">
                  {rows.length} / {total} ta
                </p>
                <div className="flex items-center gap-1">
                  <button
                    type="button"
                    disabled={page <= 1 || loading}
                    onClick={() => setPage((p) => Math.max(1, p - 1))}
                    className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:opacity-40"
                    aria-label="Oldingi sahifa"
                  >
                    <ChevronLeft className="h-4 w-4" />
                  </button>
                  <span className="min-w-[70px] text-center text-xs text-slate-500">
                    {page} / {lastPage}
                  </span>
                  <button
                    type="button"
                    disabled={page >= lastPage || loading}
                    onClick={() => setPage((p) => p + 1)}
                    className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:opacity-40"
                    aria-label="Keyingi sahifa"
                  >
                    <ChevronRight className="h-4 w-4" />
                  </button>
                </div>
              </div>
            </div>
          )}
        </div>

        {/* ---------------- Yozish maydoni ---------------- */}
        <div className="space-y-4">
          <Card className="space-y-4">
            <div className="flex items-center justify-between gap-2">
              <p className="font-semibold text-slate-800">
                {editing ? 'Yangilikni tahrirlash' : 'Yangi yangilik'}
              </p>
              {editing && <StatusPill item={editing} archived={isArchived} />}
            </div>

            {opening && <Loader label="Ochilmoqda..." className="py-4" />}

            {isArchived && (
              <p className="rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-500">
                Bu yangilik arxivda — uni faqat o'qish mumkin.
              </p>
            )}

            <Input
              label="Sarlavha"
              required
              value={form.title}
              maxLength={200}
              disabled={isArchived}
              placeholder="Ota-onalar yig'ilishi"
              onChange={(e) => setForm((p) => ({ ...p, title: e.target.value }))}
            />

            <div>
              <Textarea
                label="Matn"
                required
                value={form.body}
                disabled={isArchived}
                className="h-48"
                placeholder="25-sentabr, soat 15:00 da maktab yig'ilish zalida..."
                onChange={(e) => setForm((p) => ({ ...p, body: e.target.value }))}
              />
              <p className="mt-1 text-xs text-slate-400">
                Oddiy matn: qalin harf, rang va havola yo'q. Qator tashlaganingiz
                saqlanadi va o'quvchiga xuddi shunday ko'rinadi.
              </p>
            </div>

            {!isArchived && (
              <PhotoUpload
                label="Rasm (ixtiyoriy)"
                value={form.imageUrl}
                onChange={(url) => setForm((p) => ({ ...p, imageUrl: url }))}
              />
            )}

            <div>
              <span className="mb-1 block text-sm font-medium text-slate-600">
                Kimga ko'rinadi <span className="text-red-500">*</span>
              </span>
              <div className="flex flex-wrap gap-2">
                {AUDIENCE_ORDER.map((value) => {
                  const active = form.audience.includes(value)
                  return (
                    <button
                      key={value}
                      type="button"
                      disabled={isPublished || isArchived}
                      onClick={() => toggleAudience(value)}
                      className={cn(
                        'rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-60',
                        active
                          ? 'border-brand-500 bg-brand-50 text-brand-700'
                          : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                      )}
                    >
                      {audienceLabel(value)}
                    </button>
                  )
                })}
              </div>
              {isPublished && !isArchived && (
                <p className="mt-1 text-xs text-slate-400">
                  E'lon qilingan yangilikda qatnashuvchini o'zgartirib bo'lmaydi —
                  birinchi auditoriyaga ketgan nusxa joyida qolardi. Avval e'londan
                  qaytaring.
                </p>
              )}
            </div>

            {!isPublished && !isArchived && (
              <label className="flex cursor-pointer items-start gap-2 rounded-lg border border-slate-200 px-3 py-2">
                <input
                  type="checkbox"
                  checked={sendTelegram}
                  onChange={(e) => setSendTelegram(e.target.checked)}
                  className="mt-0.5 h-4 w-4 accent-brand-600"
                />
                <span className="text-sm text-slate-700">
                  Telegram orqali ham yuborilsin
                  <span className="mt-0.5 block text-xs text-slate-400">
                    {form.audience.length > 0
                      ? `E'lon qilinganda bot ${audienceListText(form.audience)}ga xabar yuboradi.`
                      : "E'lon qilinganda bot tanlangan qatnashuvchilarga xabar yuboradi."}{' '}
                    Faqat lentada chiqsin desangiz — belgini oling.
                  </span>
                </span>
              </label>
            )}

            {editing?.publishedAt && (
              <div className="rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-sm text-emerald-800">
                <p className="font-medium">
                  E'lon qilingan: {formatWhen(editing.publishedAt)}
                </p>
                <p className="mt-0.5 text-emerald-700">{telegramSummary(editing)}</p>
              </div>
            )}

            {formError && (
              <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
                {formError}
              </p>
            )}

            {!isArchived && (
              <div className="flex flex-wrap items-center justify-end gap-2">
                {!canSave && (
                  <p className="mr-auto text-xs text-slate-400">
                    Saqlash uchun: sarlavha, matn va kamida bitta qatnashuvchi.
                  </p>
                )}
                <Button variant="secondary" onClick={handleSaveDraft} disabled={!canSave || saving}>
                  <Save className="h-4 w-4" />
                  {saving ? 'Saqlanmoqda...' : isPublished ? 'Saqlash' : 'Qoralama saqlash'}
                </Button>
                {isPublished ? (
                  <Button
                    variant="secondary"
                    onClick={() => {
                      setDialogError(null)
                      setUnpublishTarget(editing)
                    }}
                  >
                    <ArrowLeftRight className="h-4 w-4" /> E'londan qaytarish
                  </Button>
                ) : (
                  <Button onClick={handlePublishClick} disabled={!canSave || saving}>
                    <Send className="h-4 w-4" /> E'lon qilish
                  </Button>
                )}
              </div>
            )}
          </Card>

          <NewsPreview
            title={form.title}
            body={form.body}
            imageUrl={form.imageUrl}
            audience={form.audience}
            when={editing?.publishedAt ?? null}
            authorName={editing?.authorName ?? user?.fullName ?? ''}
          />
        </div>
      </div>

      <NewsPublishDialog
        item={publishTarget}
        sendTelegram={sendTelegram}
        onSendTelegramChange={setSendTelegram}
        busy={dialogBusy}
        error={dialogError}
        onClose={() => setPublishTarget(null)}
        onConfirm={confirmPublish}
      />

      <NewsConfirmDialog
        open={unpublishTarget !== null}
        title="E'londan qaytarish"
        confirmLabel="E'londan qaytarish"
        busyLabel="Qaytarilmoqda..."
        busy={dialogBusy}
        error={dialogError}
        onClose={() => setUnpublishTarget(null)}
        onConfirm={confirmUnpublish}
      >
        <p className="font-medium text-slate-800">{unpublishTarget?.title}</p>
        <p>
          Yangilik lentadan yo'qoladi — ota-ona, o'quvchi va xodim uni boshqa
          ko'rmaydi.
        </p>
        <p className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-amber-800">
          Telegramda ALLAQACHON ketgan xabar qaytarilmaydi: u odamlarning
          telefonida qoladi.
        </p>
      </NewsConfirmDialog>

      <NewsConfirmDialog
        open={deleteTarget !== null}
        title="Yangilikni o'chirish"
        confirmLabel="O'chirish"
        busyLabel="O'chirilmoqda..."
        tone="danger"
        busy={dialogBusy}
        error={dialogError}
        onClose={() => setDeleteTarget(null)}
        onConfirm={confirmDelete}
      >
        <p className="font-medium text-slate-800">{deleteTarget?.title}</p>
        <p>
          Yangilik ro'yxatdan olinadi va lentada ko'rinmaydi, lekin butunlay
          yo'qolmaydi: matni "Arxiv" bo'limida qoladi.
        </p>
        {deleteTarget?.publishedAt && (
          <p className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-amber-800">
            Bu yangilik e'lon qilingan edi — Telegramda ketgan xabar qaytarilmaydi.
          </p>
        )}
      </NewsConfirmDialog>

      <NewsConfirmDialog
        open={pendingOpen !== null}
        title="Saqlanmagan o'zgarishlar"
        confirmLabel="Tashlab ketish"
        busyLabel="..."
        tone="danger"
        busy={false}
        error={null}
        onClose={() => setPendingOpen(null)}
        onConfirm={() => {
          const target = pendingOpen
          setPendingOpen(null)
          if (!target) return
          if (target.row) void openEdit(target.row, target.archived)
          else resetForm()
        }}
      >
        <p>
          Yozganingiz hali saqlanmagan. Boshqasiga o'tsangiz shu matn
          yo'qoladi — avval "Qoralama saqlash" ni bosing.
        </p>
      </NewsConfirmDialog>

      <Toast message={toast} duration={6000} onClose={() => setToast(null)} />
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Ro'yxat qatori                                                     */
/* ------------------------------------------------------------------ */

function StatusPill({ item, archived }: { item: NewsAdminDto; archived?: boolean }) {
  const published = item.publishedAt != null
  const label = archived ? 'Arxivda' : published ? "E'lon qilingan" : 'Qoralama'
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium',
        archived
          ? 'bg-slate-100 text-slate-500'
          : published
            ? 'bg-emerald-50 text-emerald-700'
            : 'bg-slate-100 text-slate-600',
      )}
    >
      {label}
    </span>
  )
}

interface RowProps {
  row: NewsAdminDto
  active: boolean
  /** Arxiv ro'yxatidagi qator — o'chirilgan, faqat o'qish uchun. */
  archived: boolean
  busy: boolean
  onEdit: () => void
  onPublish: () => void
  onUnpublish: () => void
  onDelete: () => void
}

function NewsRow({ row, active, archived, busy, onEdit, onPublish, onUnpublish, onDelete }: RowProps) {
  const published = row.publishedAt != null
  const telegram = telegramSummary(row)

  return (
    <Card className={cn('space-y-2', active && 'border-brand-300 ring-1 ring-brand-100')}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <p className="font-semibold text-slate-800">{row.title}</p>
        <StatusPill item={row} archived={archived} />
      </div>

      <p className="line-clamp-2 whitespace-pre-line break-words text-sm text-slate-500">
        {row.body}
      </p>

      <div className="flex flex-wrap items-center gap-1.5">
        {sortAudience(row.audience).map((a) => (
          <span
            key={a}
            className="rounded-md bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700"
          >
            {audienceLabel(a)}
          </span>
        ))}
        <span className="text-xs text-slate-400">
          {formatWhen(row.publishedAt ?? row.createdAt)} · {row.authorName}
        </span>
      </div>

      {telegram && <p className="text-xs text-slate-400">{telegram}</p>}

      <div className="flex flex-wrap justify-end gap-2 pt-1">
        <Button variant="ghost" onClick={onEdit} disabled={busy}>
          <Pencil className="h-4 w-4" /> {archived ? "Ko'rish" : 'Tahrirlash'}
        </Button>
        {!archived &&
          (published ? (
            <Button variant="secondary" onClick={onUnpublish}>
              <ArrowLeftRight className="h-4 w-4" /> E'londan qaytarish
            </Button>
          ) : (
            <Button variant="secondary" onClick={onPublish}>
              <Send className="h-4 w-4" /> E'lon qilish
            </Button>
          ))}
        {!archived && (
          <Button variant="ghost" onClick={onDelete} className="text-red-600 hover:bg-red-50">
            <Trash2 className="h-4 w-4" /> O'chirish
          </Button>
        )}
      </div>
    </Card>
  )
}

/* ------------------------------------------------------------------ */
/*  Ko'rinishi (jonli namuna)                                          */
/* ------------------------------------------------------------------ */

interface PreviewProps {
  title: string
  body: string
  imageUrl: string | null
  audience: NewsAudience[]
  /** E'lon qilingan bo'lsa — o'sha vaqt, aks holda hozirgi vaqt. */
  when: string | null
  authorName: string
}

/**
 * O'quvchi/ota-ona ko'radigan kartochka. Matn — `whitespace-pre-line`:
 * qator tashlash saqlanadi, boshqa hech narsa talqin qilinmaydi (§3.3 N2).
 */
function NewsPreview({ title, body, imageUrl, audience, when, authorName }: PreviewProps) {
  const empty = !title.trim() && !body.trim()

  return (
    <Card className="space-y-3">
      <div className="flex items-center justify-between gap-2">
        <p className="font-semibold text-slate-800">Ko'rinishi</p>
        <p className="text-xs text-slate-400">
          {audience.length > 0 ? `${audienceListText(audience)} shunday ko'radi` : 'Mini-ilovada'}
        </p>
      </div>

      {empty ? (
        <div className="rounded-xl border border-dashed border-slate-200 px-4 py-10 text-center">
          <p className="text-sm text-slate-400">
            Yozishni boshlang — ota-ona telefonida shu yerda ko'rinadi.
          </p>
        </div>
      ) : (
        <article className="overflow-hidden rounded-xl border border-slate-200 bg-white">
          {imageUrl && (
            <img src={imageUrl} alt="" className="h-40 w-full object-cover" />
          )}
          <div className="space-y-2 p-4">
            <h3 className="font-semibold text-slate-800">
              {title.trim() || 'Sarlavha yozilmagan'}
            </h3>
            <p className="text-xs text-slate-400">
              {formatWhen(when ?? new Date().toISOString())}
              {authorName ? ` · ${authorName}` : ''}
            </p>
            {body.trim() ? (
              <p className="whitespace-pre-line break-words text-sm text-slate-700">{body}</p>
            ) : (
              <p className="text-sm text-slate-300">Matn yozilmagan</p>
            )}
          </div>
        </article>
      )}
    </Card>
  )
}

/* ------------------------------------------------------------------ */
/*  E'lon natijasi                                                     */
/* ------------------------------------------------------------------ */

/**
 * E'londan keyingi bitta jumla.
 *
 * `telegramSentAt` bo'sh, lekin yuborish so'ralgan bo'lsa — bu bot
 * sozlanmaganini bildiradi: §3.3 N4 ga ko'ra e'lon baribir o'tadi,
 * hisoblagichlar 0 qoladi.
 */
function publishResultText(item: NewsAdminDto, requestedTelegram: boolean): string {
  if (!requestedTelegram) return "E'lon qilindi. Telegram xabari yuborilmadi."
  if (!item.telegramSentAt) {
    return "E'lon qilindi. Telegram bot sozlanmagan — faqat ilovada chiqdi."
  }
  return `E'lon qilindi · Telegram: ${item.telegramSentCount}/${item.telegramRecipientCount} yetkazildi.`
}
