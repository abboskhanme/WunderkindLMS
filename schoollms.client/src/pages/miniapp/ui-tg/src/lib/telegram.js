/**
 * Telegram WebApp bilan yagona aloqa nuqtasi.
 *
 * ILOVA TELEGRAMDAN TASHQARIDA HAM OCHILADI — brauzerda ishlab chiqish va
 * demo uchun. Shuning uchun bu yerdagi har bir funksiya `window.Telegram`
 * yo'qligiga chidaydi va zarur bo'lsa mazmunli "yo'q" qaytaradi. Ekranlar
 * hech qachon `window.Telegram` ga to'g'ridan-to'g'ri murojaat qilmaydi.
 */
import { useEffect, useState } from 'react'

const tg = typeof window !== 'undefined' ? window.Telegram?.WebApp : undefined

/** Telegram ichidamizmi. */
export const inTelegram = Boolean(tg?.initData)

/**
 * Serverga yuboriladigan XOM satr. Uni parse QILMAYMIZ: imzo aynan shu
 * satr ustida tekshiriladi, ya'ni bitta belgini o'zgartirsak ham imzo
 * buziladi. Foydalanuvchi kimligi haqidagi yagona haqiqat — serverning
 * javobi, sahifadagi `tg.initDataUnsafe` emas (nomining o'zi shuni aytadi).
 */
export const initData = tg?.initData ?? ''

/** Mijoz Bot API'ning shu versiyasini biladimi (eski mijozda metod chaqirish ogohlantirish beradi). */
function supports(version) {
  try {
    return Boolean(tg?.isVersionAtLeast?.(version))
  } catch {
    return false
  }
}

/** Ilova ochilganda: to'liq balandlik, brend rangidagi sarlavha. */
export function bootstrap() {
  if (!tg) return
  tg.ready()
  tg.expand()
  try {
    tg.setHeaderColor('#FFD006')
    tg.setBackgroundColor('#F0F0F0')
    // Pastdagi tizim paneli tab paneli bilan bir xil oq bo'lsin (7.10+).
    if (supports('7.10')) tg.setBottomBarColor('#FFFFFF')
  } catch {
    // Eski mijozlarda bu metodlar yo'q — rang o'zgarmaydi, ilova ishlayveradi.
  }
  // Telefonda ro'yxatni tepaga qaytarish uchun pastga surilganda Telegram buni
  // "ilovani yopish" deb tushunib, oynani pastga tortib ketardi (7.7+). Yopish
  // uchun Telegramning o'z tugmasi bor — sahifa aylantirish unga aralashmasin.
  if (supports('7.7')) {
    try {
      tg.disableVerticalSwipes()
    } catch {
      /* qo'llab-quvvatlanmasa — eski xatti-harakat qoladi */
    }
  }
}

/** Telegramning o'z "orqaga" tugmasi. Qaytarilgan funksiya uni yashiradi. */
export function showBackButton(onClick) {
  if (!tg?.BackButton) return () => {}
  tg.BackButton.onClick(onClick)
  tg.BackButton.show()
  return () => {
    tg.BackButton.offClick(onClick)
    tg.BackButton.hide()
  }
}

/** Yengil taktil javob — tugma bosilganda. */
export function haptic(kind = 'light') {
  try {
    tg?.HapticFeedback?.impactOccurred(kind)
  } catch {
    /* qo'llab-quvvatlanmasa — jim o'tamiz */
  }
}

/**
 * Natija haqida taktil javob — `success` | `warning` | `error`.
 * HR topshiriqlari ekranlari (eski xodim ilovasidagi `haptic('success')` va h.k.).
 */
export function hapticNotify(type = 'success') {
  try {
    tg?.HapticFeedback?.notificationOccurred(type)
  } catch {
    /* qo'llab-quvvatlanmasa — jim o'tamiz */
  }
}

/** Tanlov o'zgarganda (sana lentasi, filtr) — eng yengil "tiq". */
export function hapticSelect() {
  try {
    tg?.HapticFeedback?.selectionChanged()
  } catch {
    /* qo'llab-quvvatlanmasa — jim o'tamiz */
  }
}

/**
 * Klaviatura ochiqmi — matn maydoni fokusda turibdimi.
 *
 * Telefonda klaviatura ochilganda pastga qadalgan (`fixed bottom`) tab paneli
 * klaviatura ustiga chiqib, yozish maydonini yopib qo'yardi. Shu holatda tab
 * paneli yashiriladi va yozish paneli pastga tushadi. `visualViewport` ga
 * tayanmaymiz: Telegram iOS va Android uni har xil o'zgartiradi, fokus esa
 * ikkalasida bir xil ishlaydi.
 */
export function useKeyboardOpen() {
  const [open, setOpen] = useState(false)
  useEffect(() => {
    const typing = (el) =>
      el instanceof HTMLTextAreaElement ||
      (el instanceof HTMLInputElement && !['checkbox', 'radio', 'button', 'submit'].includes(el.type))
    // Panel bosish TUGAGANDAN keyin qaytadi: darrov qaytsa, maydondan chiqargan bosish (mousedown)
    // bilan uning oxiri (mouseup) orasida panel barmoq ostiga chiqib, bosish tugmaga yetmay qolardi —
    // yozib bo'lib bosilgan birinchi tugma (sinf, variant) "yo'qolardi" (2026-10-12).
    // Fokus maydondan TUGMAGA o'tganda ham (mousedown) shunday — faqat yozish maydoni darrov yashiradi.
    let timer = 0
    const later = () => {
      window.clearTimeout(timer)
      timer = window.setTimeout(() => setOpen(typing(document.activeElement)), 250)
    }
    const onIn = (e) => {
      if (!typing(e.target)) return later()
      window.clearTimeout(timer)
      setOpen(true)
    }
    const onOut = later
    document.addEventListener('focusin', onIn)
    document.addEventListener('focusout', onOut)
    return () => {
      window.clearTimeout(timer)
      document.removeEventListener('focusin', onIn)
      document.removeEventListener('focusout', onOut)
    }
  }, [])
  return open
}

/** Telegram bergan foydalanuvchi (FAQAT ko'rinish uchun: ism, avatar). */
export const tgUser = tg?.initDataUnsafe?.user ?? null
