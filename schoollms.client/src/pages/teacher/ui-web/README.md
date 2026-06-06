# Teacher App — UI Kit (React + Tailwind)

Flutter "Teacher App" (o'qituvchi portali) ning **butun UI qismi** React + Tailwind CSS ga
ko'chirilgan. Maqsad — shu kodni veb-saytga asos qilib olish: dizayn tokenlari, umumiy
komponentlar va barcha ekranlar bir joyda, light + dark rejim bilan.

## Ishga tushirish

```bash
cd ui-export
npm install
npm run dev      # http://localhost:5173
npm run build    # dist/ — production build
```

Chap tomondagi panel (sidebar) — barcha ekranlar galereyasi. Yuqori o'ngdagi 🌙/☀️ tugma
light/dark rejimni almashtiradi. Telefon ramkasi ichida har bir ekran haqiqiy ilovadagidek
ko'rinadi.

## Tuzilma

```
src/
  index.css            ← dizayn tokenlari (CSS o'zgaruvchilari, light + .dark)
  lib/colors.js        ← gradeColor / avatarColor / initials helperlari
  components/          ← umumiy widgetlar (Flutter shared/widgets ekvivalenti)
    Avatar, AppCard (+ TapScale), AppButton, SegmentedControl,
    ProgressRing, BottomNav, EmptyState, AppSheet, ui (Header/SectionLabel)
  data/mock.js         ← namuna ma'lumotlar (haqiqiy ilovada API'dan keladi)
  screens/            ← barcha ekranlar (quyida ro'yxat)
  App.jsx              ← galereya + telefon ramkasi + bottom nav + dark toggle
tailwind.config.js     ← rang palitrasi, shrift, radius, soyalar
```

## Komponentlar → Flutter mosligi

| React komponent      | Flutter manbasi                       |
| -------------------- | ------------------------------------- |
| `Avatar`             | `app_avatar.dart`                     |
| `AppCard` / `TapScale` | `app_card.dart`                     |
| `AppButton`          | `app_button.dart` (filled/ghost/soft/danger) |
| `SegmentedControl`   | `segmented_control.dart`              |
| `ProgressRing`       | `progress_ring.dart` (SVG versiyasi)  |
| `BottomNav`          | `app_bottom_nav.dart`                 |
| `EmptyState`         | `empty_state.dart`                    |
| `AppSheet`           | `app_sheet.dart` (bottom sheet)       |

## Ekranlar ro'yxati (hammasi ko'chirilgan)

- **Kirish:** Login
- **Asosiy (bottom nav):** Dashboard, Jurnal (tanlash), Vazifalar, Suhbat (kanallar), Profil
- **Jadval & Jurnal:** Dars jadvali, Jurnal — baholar/mavzu/chorak/feedback (muzlatilgan ustun + panjara)
- **Vazifalar:** Vazifa yaratish (4 qadamli sehrgar), Vazifa natijalari (ProgressRing + histogramma)
- **Suhbat:** Suhbat oynasi (xabar puffaklari), Bildirishnomalar
- **Sinf rahbarligi:** Homeroom (ota-ona kelgan o'quvchilar)
- **Bo'limlar:** Maosh (gradient hero + grafik), Dars o'tilishi (progress), Taklif va shikoyatlar
- **Ta'lim (LMS):** Fanlar → Mavzular/Progress → O'quvchi mavzulari → Mavzu tafsiloti

## Veb-saytga ko'chirish bo'yicha eslatma

- **Ranglar ikki turga bo'linadi.** Sobit palitra (teal shkalasi, baho ranglari, semantic)
  `tailwind.config.js` da; mavzuga bog'liq ranglar (surface, bg, border, text, primary…)
  `index.css` dagi CSS o'zgaruvchilari orqali — `.dark` klassi bilan almashadi. Bu Flutterdagi
  `AppColors.getXxx(context)` naqshining aynan o'zi.
- **Shrift:** Plus Jakarta Sans (sarlavhalar 700–800, tana 400–600), monospace raqamlar uchun.
- **Radius:** 14/16/18/20/22/24 — `rounded-xl … rounded-6xl`.
- To'liq token ro'yxati: **[DESIGN-TOKENS.md](./DESIGN-TOKENS.md)**.

Tokenlar til-agnostik, shuning uchun Tailwind o'rniga oddiy CSS, Vue, yoki boshqa stekda ham
xuddi shu qiymatlar bilan aynan shu ko'rinishni qurish mumkin.
