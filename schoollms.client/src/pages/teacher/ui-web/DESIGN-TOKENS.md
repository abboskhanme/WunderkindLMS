# Dizayn tokenlari — to'liq ma'lumotnoma

Bu hujjat ilovaning butun dizayn tilini bitta joyda jamlaydi. Til-agnostik: istalgan
stekda (Tailwind, oddiy CSS, Vue, SCSS) shu qiymatlar bilan aynan shu ko'rinishni qurasiz.

## 1. Ranglar

### 1.1. Teal shkalasi (sobit — light/dark da bir xil)

| Token | HEX |
| --- | --- |
| teal50 | `#F0FDFA` |
| teal100 | `#CCFBF1` |
| teal300 | `#5EEAD4` |
| teal400 | `#2DD4BF` |
| teal500 | `#14B8A6` |
| teal600 | `#0D9488` ← asosiy (light primary) |
| teal700 | `#0F766E` |
| teal800 | `#115E59` |
| teal900 | `#134E4A` |
| teal950 | `#042F2E` |

### 1.2. Baho ranglari

| Baho | HEX |
| --- | --- |
| 5 | `#059669` |
| 4 | `#0D9488` |
| 3 | `#D97706` |
| 2 | `#EA580C` |
| 1 | `#E11D48` |

### 1.3. Semantic (sobit)

| Token | HEX |
| --- | --- |
| success | `#10B981` |
| warning | `#F59E0B` |
| danger | `#EF4444` |
| info | `#3B82F6` |

### 1.4. Mavzuga bog'liq tokenlar (CSS o'zgaruvchilari)

`.dark` klassi bilan almashadi (Flutterdagi `AppColors.getXxx(context)` ekvivalenti).

| Token | Light | Dark |
| --- | --- | --- |
| `--bg` | `#FBFCFC` | `#07120F` |
| `--bg-alt` | `#F4F6F5` | `#0C1A17` |
| `--surface` | `#FFFFFF` | `#11201C` |
| `--surface2` | `#F8FAFA` | `#162822` |
| `--surface3` | `#EFF3F2` | `#1C322B` |
| `--border` | `#E7ECEB` | `#1F352E` |
| `--border-soft` | `#F0F3F2` | `#1F352E` |
| `--text` | `#0F1A17` | `#ECF5F2` |
| `--muted` | `#5A6360` | `#95A8A2` |
| `--faint` | `#94A09B` | `#5E726C` |
| `--primary` | `#0D9488` | `#2DD4BF` |
| `--primary-soft` | `#DBF5F0` | `#0F2E29` |
| `--chip` | `#F0F4F3` | `#162822` |

### 1.5. Avatar palitrasi (ismdan hash bilan tanlanadi)

`#0D9488`, `#3B82F6`, `#7C3AED`, `#DB2777`, `#F59E0B`, `#10B981`, `#2563EB`

## 2. Tipografiya

- **Asosiy shrift:** Plus Jakarta Sans
- **Monospace** (raqamlar, sana, ball, vaqt): JetBrains Mono / `monospace`
- Og'irliklar: sarlavhalar **800** (`font-extrabold`), kichik sarlavha **700**, tana **400–600**
- Sarlavhalarda `letter-spacing`: `-0.025em` … `-0.03em`

| Rol | O'lcham | Og'irlik |
| --- | --- | --- |
| Ekran katta sarlavha | 22px | 800 |
| Hero raqam (maosh/progress) | 32–40px | 800, mono |
| Karta sarlavha | 15px | 700 |
| Tana | 13–15px | 400–600 |
| Yorliq (label) | 12px upper, `letter-spacing 0.04` | 700 |
| Mayda izoh | 11px | 500–600 |

## 3. Radius

| Nomi | px | Ishlatilishi |
| --- | --- | --- |
| `rounded-xl` | 14 | tugmalar, kichik chip, input |
| `rounded-2xl` | 16 | qatorlar, kichik kartalar |
| `rounded-3xl` | 18 | kanal/dars qatorlari |
| `rounded-4xl` | 20 | asosiy kartalar |
| `rounded-5xl` | 22 | hero kartalar |
| `rounded-6xl` | 24 | bottom sheet, katta hero |
| `rounded-full` | ∞ | avatar, filter chip, FAB nuqta |

## 4. Soyalar

| Nomi | Qiymat |
| --- | --- |
| card | `0 2px 8px rgba(15,26,23,0.04)` |
| soft | `0 1px 2px rgba(15,26,23,0.04)` |
| sheet | `0 -8px 32px rgba(0,0,0,0.12)` |
| glow (teal hero) | `0 12px 28px rgba(13,148,136,0.30)` |
| fab | `0 8px 24px rgba(13,148,136,0.40)` |

## 5. Gradientlar (asosiy)

- **Hero karta (dars/profil/sinf):** `linear-gradient(135deg, #14B8A6, #0F766E)` yoki `#0D9488 → #115E59`
- **Maosh/Progress hero:** `linear-gradient(135deg, #134E4A, #0F766E, #0D9488)`
- **Xodimlar kanali:** `linear-gradient(135deg, #7C3AED, #C026D3)`
- **Login dekorativ blob:** `radial-gradient(circle, rgba(94,234,212,0.45), transparent 70%)`

## 6. Interaksiya naqshlari

- **TapScale:** bosilganda `transform: scale(0.97)`, `transition 0.1s ease-out` — barcha bosiladigan kartalarda.
- **SegmentedControl:** faol segment `surface` foni + mayda soya; transition 150ms.
- **BottomNav faol tab:** ikon ostida `primary-soft` pill (56×28, radius 14), matn `primary` + 700.
- **Tugma loading:** uchta pulslanuvchi nuqta (1.2s, har biri 0.2s kechikish bilan).

## 7. Asosiy o'lchamlar

- Bottom nav balandligi: **60px**
- Avatar: 32 / 34 / 36 / 40 / 44 / 80 (ring bilan)
- Jurnal panjarasi: ism ustuni **150px**, katak **50×56px**
- FAB: **60×60**, radius 18
- Input balandligi: **48px**, radius 14
- Asosiy tugma: balandlik **48–54px**
