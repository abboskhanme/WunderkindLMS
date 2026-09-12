/**
 * Telegram WebApp bilan yagona aloqa nuqtasi.
 *
 * ILOVA TELEGRAMDAN TASHQARIDA HAM OCHILADI — brauzerda ishlab chiqish va
 * demo uchun. Shuning uchun bu yerdagi har bir funksiya `window.Telegram`
 * yo'qligiga chidaydi va zarur bo'lsa mazmunli "yo'q" qaytaradi. Ekranlar
 * hech qachon `window.Telegram` ga to'g'ridan-to'g'ri murojaat qilmaydi.
 */

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

/** Ilova ochilganda: to'liq balandlik, brend rangidagi sarlavha. */
export function bootstrap() {
  if (!tg) return
  tg.ready()
  tg.expand()
  try {
    tg.setHeaderColor('#FFD006')
    tg.setBackgroundColor('#F0F0F0')
  } catch {
    // Eski mijozlarda bu metodlar yo'q — rang o'zgarmaydi, ilova ishlayveradi.
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

/** Telegram bergan foydalanuvchi (FAQAT ko'rinish uchun: ism, avatar). */
export const tgUser = tg?.initDataUnsafe?.user ?? null
