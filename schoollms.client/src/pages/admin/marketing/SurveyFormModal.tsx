/**
 * Ariza tahriri — ro'yxat ustidagi oyna (`docs/modules/sales-marketing.md`
 * §6.2, task SM-8).
 *
 * Ikki ustun: chapda maydonlar, o'ngda ommaviy kartaning JONLI ko'rinishi.
 * Ko'rinish har bir bosishda yangilanadi, chunki bu ekranning butun ma'nosi
 * shu: qaysi maydonni so'rash kerakligini ota-ona ko'radigan formaga qarab
 * hal qilish, saqlab, havolani ochib ko'rib emas.
 *
 * RULE T (§2.4) SHU YERDA KO'RINADI. "Jinsi" va "Sinf" kalitlari yoqilgan va
 * QULFLANGAN, yonida bir qatorlik sababi bilan. Ular YASHIRILMAYDI: yashirin
 * kalit "bunday imkoniyat yo'q" degan ma'noni beradi, sababi yozilgan qulf
 * esa "bu — qaror" deydi. Server ham shuni talab qiladi (400
 * `survey_fields_required`, `ck_surveys_required_toggles`) — shunday javob
 * kelsa, oyna ikkala kalitni qaytarib yoqadi va serverning jumlasini
 * ko'rsatadi.
 *
 * Har ochilganda QAYTA ULANADI (ota-komponent shartli render qiladi), shuning
 * uchun boshlang'ich qiymatlar `useState` da.
 */
import { useEffect, useRef, useState } from 'react'
import type { ChangeEvent, FormEvent } from 'react'
import { Check, FileUp, Loader2, Lock, Paperclip, X } from 'lucide-react'
import type { Stage } from '@/types'
import {
  DEFAULT_TOGGLES,
  PINNED_TOGGLES,
  createSurvey,
  isSlugAvailable,
  proposeSlug,
  slugProblem,
  surveyError,
  toggleLabels,
  updateSurvey,
  type Survey,
  type SurveyErrorInfo,
  type SurveyFieldToggles,
  type SurveySaveRequest,
  type SurveyToggleKey,
} from '@/api/services/surveys'
import { uploadAdminFile } from '@/api/services/students'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { PhotoUpload } from '@/components/ui/PhotoUpload'
import { cn } from '@/lib/utils'
import { SurveyPreview } from './SurveyPreview'

interface Props {
  /** `null` — yangi ariza. */
  editing: Survey | null
  stages: Stage[]
  /**
   * Ommaviy havolaning asosi ("https://wunderkindschool.uz"), mavjud
   * arizaning `publicUrl` idan ajratib olingan. Yangi arizada — `null`:
   * manzilni SERVER yig'adi (§5.2), klient uni taxmin qilmaydi.
   */
  publicOrigin: string | null
  onClose: () => void
  onSaved: (saved: Survey, created: boolean) => void
}

/** Slug tekshiruvining holati — yozayotganda yonida ko'rinadi. */
type SlugState = 'idle' | 'checking' | 'free' | 'taken' | 'error'

/** Kalitlar bloki uchun bir qator. */
interface ToggleRowProps {
  label: string
  hint: string
  checked: boolean
  locked?: boolean
  onChange: (value: boolean) => void
}

function ToggleRow({ label, hint, checked, locked, onChange }: ToggleRowProps) {
  return (
    <label className={cn('flex items-start gap-2.5', locked ? 'cursor-default' : 'cursor-pointer')}>
      <input
        type="checkbox"
        checked={checked}
        disabled={locked}
        onChange={(e) => onChange(e.target.checked)}
        className="mt-0.5 h-4 w-4 rounded border-slate-300 accent-brand-600 disabled:opacity-60"
      />
      <span className="text-sm">
        <span className="flex items-center gap-1.5 font-medium text-slate-700">
          {label}
          {locked && <Lock className="h-3.5 w-3.5 text-slate-400" />}
        </span>
        <span className="mt-0.5 block text-slate-400">{hint}</span>
      </span>
    </label>
  )
}

export function SurveyFormModal({ editing, stages, publicOrigin, onClose, onSaved }: Props) {
  const [name, setName] = useState(editing?.name ?? '')
  const [slug, setSlug] = useState(editing?.slug ?? '')
  /** Tahrirda slug allaqachon "o'ziniki" — nomdan qayta yasalmaydi. */
  const [slugTouched, setSlugTouched] = useState(Boolean(editing))
  const [subtitle, setSubtitle] = useState(editing?.subtitle ?? '')
  const [imageUrl, setImageUrl] = useState<string | null>(editing?.imageUrl ?? null)
  const [offerUrl, setOfferUrl] = useState<string | null>(editing?.offerUrl ?? null)
  const [thankYouText, setThankYouText] = useState(editing?.thankYouText ?? '')
  const [stageId, setStageId] = useState(editing?.stageId ?? '')
  const [toggles, setToggles] = useState<SurveyFieldToggles>(
    editing
      ? {
          showStudentFirstNameInput: editing.showStudentFirstNameInput,
          showStudentLastNameInput: editing.showStudentLastNameInput,
          showStudentPhoneNumberInput: editing.showStudentPhoneNumberInput,
          showStudentGradeInput: editing.showStudentGradeInput,
          showStudentGenderInput: editing.showStudentGenderInput,
        }
      : DEFAULT_TOGGLES,
  )

  /**
   * FAQAT server javobi saqlanadi — u tekshirilgan slug bilan birga, chunki
   * foydalanuvchi javobni kutayotganda yozishda davom etishi mumkin. Qolgan
   * holatlar (`idle` / `free` / `checking`) `slug` dan hisoblanadi: effekt
   * ichida sinxron `setState` kaskadli render keltiradi.
   */
  const [checked, setChecked] = useState<{ slug: string; state: SlugState } | null>(null)
  const [uploading, setUploading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [failure, setFailure] = useState<SurveyErrorInfo | null>(null)
  const offerRef = useRef<HTMLInputElement>(null)

  const shapeProblem = slugProblem(slug)

  /**
   * Slug band emasmi — yozayotganda, 400 ms kutib. Haqiqiy hakam baribir baza
   * (`ux_surveys_slug`): poygada saqlash 409 `slug_taken` qaytaradi va uni
   * quyidagi `submit` ushlaydi.
   */
  /** Shakli buzuq slug ham, tahrirdagi o'z slug'i ham serverga bormaydi. */
  const needsCheck = !shapeProblem && !(editing && slug === editing.slug)

  const slugState: SlugState = shapeProblem
    ? 'idle'
    : !needsCheck
      ? 'free'
      : checked?.slug === slug
        ? checked.state
        : 'checking'

  useEffect(() => {
    if (!needsCheck) return
    let active = true
    const timer = setTimeout(() => {
      isSlugAvailable(slug, editing?.id)
        .then((ok) => active && setChecked({ slug, state: ok ? 'free' : 'taken' }))
        .catch(() => active && setChecked({ slug, state: 'error' }))
    }, 400)
    return () => {
      active = false
      clearTimeout(timer)
    }
  }, [slug, needsCheck, editing])

  const changeName = (value: string) => {
    setName(value)
    if (!slugTouched) setSlug(proposeSlug(value))
  }

  const changeSlug = (value: string) => {
    setSlugTouched(true)
    // Nusxa-ko'chirilgan to'liq havoladan ham faqat oxirgi bo'lagini olamiz.
    const tail = value.split('/').pop() ?? ''
    setSlug(tail.trim().toLowerCase())
  }

  const setToggle = (key: SurveyToggleKey, value: boolean) => {
    // Rule T: qulflangan kalit hech qachon o'chmaydi — bosish ham, xato javob ham.
    if (PINNED_TOGGLES.includes(key)) return
    setToggles((prev) => ({ ...prev, [key]: value }))
  }

  const pickOffer = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file) return
    setUploading(true)
    setFailure(null)
    try {
      const uploaded = await uploadAdminFile(file)
      setOfferUrl(uploaded.url)
    } catch (err) {
      setFailure(surveyError(err))
    } finally {
      setUploading(false)
    }
  }

  const canSave =
    Boolean(name.trim()) && !shapeProblem && slugState !== 'taken' && !saving && !uploading

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!canSave) return
    setSaving(true)
    setFailure(null)
    try {
      const payload: SurveySaveRequest = {
        name: name.trim(),
        slug,
        subtitle: subtitle.trim() || null,
        imageUrl,
        offerUrl,
        thankYouText: thankYouText.trim() || null,
        stageId: stageId || null,
        ...toggles,
      }
      const saved = editing
        ? await updateSurvey(editing.id, payload)
        : await createSurvey(payload)
      onSaved(saved, !editing)
    } catch (err) {
      const info = surveyError(err)
      setFailure(info)
      if (info.code === 'slug_taken') setChecked({ slug, state: 'taken' })
      if (info.code === 'survey_fields_required') {
        // Server biz bilan rozi emas — ikkala kalitni qaytarib yoqamiz.
        setToggles((prev) => ({
          ...prev,
          showStudentGenderInput: true,
          showStudentGradeInput: true,
        }))
      }
    } finally {
      setSaving(false)
    }
  }

  const origin = editing?.publicUrl.replace(/\/ariza\/[^/]*$/, '') || publicOrigin
  const fullUrl = slug && !shapeProblem ? `${origin ?? ''}/ariza/${slug}` : null

  return (
    <Modal
      open
      onClose={onClose}
      title={editing ? 'Arizani tahrirlash' : 'Yangi ariza'}
      size="xl"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="survey-form" disabled={!canSave}>
            {saving && <Loader2 className="h-4 w-4 animate-spin" />}
            Saqlash
          </Button>
        </>
      }
    >
      <div className="grid gap-6 lg:grid-cols-2">
        <form id="survey-form" onSubmit={(e) => void submit(e)} className="space-y-4">
          <Input
            label="Ariza nomi"
            required
            placeholder="masalan: 2027 o'quv yiliga qabul"
            value={name}
            onChange={(e) => changeName(e.target.value)}
          />

          <div>
            <Input
              label="Havola manzili (slug)"
              required
              placeholder="qabul-2027"
              value={slug}
              onChange={(e) => changeSlug(e.target.value)}
              className={cn(slugState === 'taken' && 'border-red-300')}
            />
            <div className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs">
              {shapeProblem ? (
                <span className="text-red-600">{shapeProblem}</span>
              ) : slugState === 'checking' ? (
                <span className="inline-flex items-center gap-1 text-slate-400">
                  <Loader2 className="h-3 w-3 animate-spin" /> tekshirilmoqda…
                </span>
              ) : slugState === 'taken' ? (
                <span className="font-medium text-red-600">band — boshqasini tanlang</span>
              ) : slugState === 'free' ? (
                <span className="inline-flex items-center gap-1 font-medium text-emerald-600">
                  <Check className="h-3 w-3" /> bo'sh
                </span>
              ) : slugState === 'error' ? (
                <span className="text-amber-600">
                  tekshirib bo'lmadi — saqlashda aniqlanadi
                </span>
              ) : null}
            </div>
            {fullUrl && (
              <p className="mt-1 break-all text-xs text-slate-400">
                {origin ? fullUrl : `…${fullUrl}`}
                {!origin && ' — to‘liq havola saqlangandan keyin ko‘rinadi'}
              </p>
            )}
          </div>

          <Input
            label="Qo'shimcha sarlavha"
            placeholder="masalan: 1–11-sinflar uchun"
            value={subtitle}
            onChange={(e) => setSubtitle(e.target.value)}
          />

          <PhotoUpload label="Banner rasmi" value={imageUrl} onChange={setImageUrl} />

          <div>
            <span className="mb-1 block text-sm font-medium text-slate-600">Taklif hujjati</span>
            <div className="flex flex-wrap items-center gap-2">
              <Button
                type="button"
                variant="secondary"
                disabled={uploading}
                onClick={() => offerRef.current?.click()}
              >
                <FileUp className="h-4 w-4" /> {uploading ? 'Yuklanmoqda…' : 'Hujjat yuklash'}
              </Button>
              <input
                ref={offerRef}
                type="file"
                accept=".pdf,.doc,.docx"
                className="hidden"
                onChange={(e) => void pickOffer(e)}
              />
              {offerUrl && (
                <span className="inline-flex items-center gap-2 rounded-lg bg-slate-50 px-2.5 py-1.5 text-sm text-slate-600">
                  <Paperclip className="h-4 w-4 text-slate-400" />
                  <a
                    href={offerUrl}
                    target="_blank"
                    rel="noreferrer"
                    className="font-medium text-brand-600 hover:text-brand-700"
                  >
                    Hujjatni ochish
                  </a>
                  <button
                    type="button"
                    title="Hujjatni olib tashlash"
                    onClick={() => setOfferUrl(null)}
                    className="rounded p-0.5 text-slate-400 hover:text-red-600"
                  >
                    <X className="h-3.5 w-3.5" />
                  </button>
                </span>
              )}
            </div>
            <p className="mt-1 text-xs text-slate-400">
              PDF yoki Word. Ota-ona uni formaning ostidan yuklab oladi.
            </p>
          </div>

          <Select
            label="Lid qaysi ustunga tushsin"
            value={stageId}
            onChange={(e) => setStageId(e.target.value)}
          >
            <option value="">— birinchi ustunga (avtomatik) —</option>
            {stages.map((stage) => (
              <option key={stage.id} value={stage.id}>
                {stage.title}
              </option>
            ))}
          </Select>

          <Textarea
            label="Rahmat matni"
            rows={2}
            placeholder="Arizangiz qabul qilindi. Tez orada siz bilan bog'lanamiz."
            value={thankYouText}
            onChange={(e) => setThankYouText(e.target.value)}
          />

          <div className="space-y-3 rounded-xl border border-slate-200 p-4">
            <div>
              <h4 className="text-sm font-semibold text-slate-700">Ariza maydonlari</h4>
              <p className="mt-0.5 text-xs text-slate-400">
                Ota-onaning ismi, familiyasi va telefoni har doim so'raladi. Quyidagilar —
                o'quvchi haqida.
              </p>
            </div>

            <ToggleRow
              label={toggleLabels.showStudentFirstNameInput}
              hint="So'ralmasa, lid «ota-ona F.I.SH — farzandi» nomi bilan yaratiladi."
              checked={toggles.showStudentFirstNameInput}
              onChange={(v) => setToggle('showStudentFirstNameInput', v)}
            />
            <ToggleRow
              label={toggleLabels.showStudentLastNameInput}
              hint="Majburiy emas: kiritilsa ismga qo'shib yoziladi."
              checked={toggles.showStudentLastNameInput}
              onChange={(v) => setToggle('showStudentLastNameInput', v)}
            />
            <ToggleRow
              label={toggleLabels.showStudentGenderInput}
              hint="Lid kartasi jinsni har doim ko'rsatadi — «noma'lum» qiymati yo'q, shuning uchun bu maydon o'chmaydi."
              checked={toggles.showStudentGenderInput}
              locked
              onChange={() => {}}
            />
            <ToggleRow
              label={toggleLabels.showStudentGradeInput}
              hint="Lid kartasi sinfni har doim ko'rsatadi, «0» esa nol sinf degani — «noma'lum» o'rniga ishlatib bo'lmaydi."
              checked={toggles.showStudentGradeInput}
              locked
              onChange={() => {}}
            />
            <ToggleRow
              label={toggleLabels.showStudentPhoneNumberInput}
              hint="Majburiy emas: kelsa lid izohiga yoziladi."
              checked={toggles.showStudentPhoneNumberInput}
              onChange={(v) => setToggle('showStudentPhoneNumberInput', v)}
            />

            {!toggles.showStudentFirstNameInput && (
              <p className="rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-700">
                O'quvchining ismi so'ralmaydi — lid doskasida kartalar «... — farzandi» nomi
                bilan turadi. Qo'ng'iroq qilish uchun ota-onaning telefoni yetarli.
              </p>
            )}
          </div>

          {failure && (
            <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">
              {failure.message}
              {failure.fields.length > 0 && (
                <span className="mt-1 block text-red-500">
                  {failure.fields.map((f) => toggleLabels[f]).join(', ')} — qaytarib yoqildi.
                </span>
              )}
            </p>
          )}
        </form>

        <SurveyPreview
          name={name}
          subtitle={subtitle}
          imageUrl={imageUrl}
          offerUrl={offerUrl}
          thankYouText={thankYouText}
          fields={{
            studentFirstName: toggles.showStudentFirstNameInput,
            studentLastName: toggles.showStudentLastNameInput,
            studentPhone: toggles.showStudentPhoneNumberInput,
            studentGrade: toggles.showStudentGradeInput,
            studentGender: toggles.showStudentGenderInput,
          }}
        />
      </div>
    </Modal>
  )
}
