# Texnik Topshiriq (TZ) — SchoolLms / Intellect School

**Bulutli ko'p-maktabli ta'lim boshqaruv tizimi (School Management & LMS, SaaS)**

Hujjat versiyasi: 2026-06-04

---

## 1. Loyiha haqida umumiy ma'lumot

**SchoolLms** — xususiy maktablar, o'quv markazlari va ta'lim muassasalari uchun mo'ljallangan
zamonaviy, veb-asosli boshqaruv tizimi. Tizim bitta platformada **bir nechta maktabni** mustaqil
ravishda yuritish imkonini beradi (SaaS / ko'p-ijarachilik modeli): har bir maktab o'z subdomenida
(`maktab.intellectschool.uz`), o'z ma'lumotlari, foydalanuvchilari va sozlamalari bilan ishlaydi.

Tizim **o'quv jarayonini to'liq raqamlashtiradi**: o'quvchilar va o'qituvchilar bazasi, dars jadvali,
elektron jurnal (baho/davomat), uy vazifalari va topshiriqlar, mustaqil ta'lim (LMS), oylik to'lovlar
va moliya, intizom, hisobotlar va tahlil. O'quvchilar va ota-onalar **mobil/veb ilova** orqali o'z
ma'lumotlarini real vaqtda kuzatadilar; bildirishnomalar **Telegram bot** va **push** orqali yetkaziladi.

---

## 2. Maqsad va vazifalar

- Maktab ma'muriyatining qog'oz va qo'lda ishlarini **to'liq avtomatlashtirish**.
- O'qituvchiga jurnal, baho, davomat, uy vazifa va topshiriqlarni **bitta tizimda** boshqarish.
- Ota-ona va o'quvchiga farzandining **o'zlashtirishi, davomati, to'lovi, intizomi** haqida shaffof,
  real-vaqtli ma'lumot berish.
- Moliyani (oylik to'lov, chegirma, qarzdorlik, maosh) **avtomatik hisoblash va nazorat qilish**.
- Bitta platformada **ko'p maktabni** xavfsiz, izolyatsiyalangan holda yuritish (SaaS).
- Ma'lumot xavfsizligi, zaxira nusxa va uzluksiz ishlashni ta'minlash.

---

## 3. Foydalanuvchi rollari

| Rol | Tavsif | Asosiy imkoniyatlari |
|---|---|---|
| **Platforma egasi** (Control Plane) | Butun platforma boshlig'i (asosiy domen) | Maktablar (tenant) yaratish, obuna/modullarni boshqarish, statistika |
| **Superadmin** | Maktab bosh administratori | Maktabning barcha modullariga to'liq kirish, login/parollar |
| **Admin** | Maktab administratori | O'quvchi/o'qituvchi/jurnal/moliya boshqaruvi (rol ruxsatlariga ko'ra) |
| **O'qituvchi** | Fan o'qituvchisi / sinf rahbari | Jurnal, baho, davomat, uy vazifa, topshiriq, o'z hisobotlari |
| **O'quvchi** | Talaba | O'z baholari, davomati, to'lovi, topshiriqlari, LMS, reytingi |
| **Ota-ona** | O'quvchining vakili | Farzandi ma'lumotlari (o'quvchi bilan bir xil ko'rinish), pickup, taklif |
| **Xodim** (staff) | Maktab xodimi | Ruxsat matritsasiga ko'ra cheklangan kirish; shartnoma/maosh |

Ruxsatlar **rol matritsasi** orqali moslashuvchan boshqariladi (qaysi rol qaysi bo'limga kira oladi).

---

## 4. Texnologiyalar (Tech stack)

| Qatlam | Texnologiya |
|---|---|
| Backend | ASP.NET Core (.NET 8), C# |
| Frontend (admin/veb) | React + TypeScript, Vite, Tailwind CSS, Recharts (diagrammalar) |
| Ma'lumotlar bazasi | Microsoft SQL Server 2022 |
| Real-time | SignalR (chat, jonli yangilanish) |
| Autentifikatsiya | JWT (Bearer token), rol asosida |
| Hujjat generatsiyasi | OpenXML (Excel `.xlsx`, Word `.docx` shartnoma) |
| Bildirishnoma | Firebase Cloud Messaging (push), Telegram Bot API |
| Infratuzilma | Docker / Docker Compose |
| Tashqi kirish | Cloudflare Tunnel (har maktab — subdomen) |

---

## 5. Arxitektura

- **Clean Architecture** — 4 qatlam: `Domain` (entity/biznes qoidalar), `Application`
  (servislar, DTO, abstraksiyalar), `Infrastructure` (DB, auth, tashqi integratsiyalar), `Server`
  (API kontrollerlar). Bu — kengaytiriladigan, test qilinadigan, qatlamlar ajratilgan tuzilma.
- **Ko'p-ijarachilik (Multi-tenant):** yagona bazada har bir yozuv `TenantId` bilan ajratiladi; global
  query-filter har so'rovda faqat joriy maktab ma'lumotini ko'rsatadi. Joriy maktab subdomen yoki
  token orqali aniqlanadi (tenant middleware).
- **Control Plane:** asosiy domen — platforma boshlig'i (maktablarni provizyon qiladi); subdomenlar —
  alohida maktablar.
- **Vaqt zonasi:** barcha sana/vaqt yagona `AppClock` (Asia/Tashkent, UTC+5) orqali — server
  joylashuvidan qat'i nazar to'g'ri vaqt.

---

## 6. Amalga oshirilgan modullar (bajarilgan ishlar)

### 6.1. Autentifikatsiya va xavfsizlik
- Login **faqat login+parol** bilan (maktab kodi shart emas — login global unikal, server maktabni
  o'zi aniqlaydi). JWT token, rol asosida himoya.
- Login (username) F.I.SH dan avtomatik generatsiya (kirill→lotin translit), takrorlanmas; tasodifiy
  xavfsiz parol. Parol DB'da faqat **hash** ko'rinishida.
- **OWASP / Zero-Trust** mustahkamlash: ochiq parol saqlanmaydi, JWT kaliti muhit o'zgaruvchisida,
  rate-limiting, fayl yuklash allowlist (xavfsiz kengaytmalar), xavfsizlik sarlavhalari (CSP,
  nosniff, HSTS), `AppUser.Email` unikal indeksi.
- **JWT bekor qilish (revocation):** arxivlangan/o'chirilgan foydalanuvchi tokeni darhol rad etiladi.
- **DataProtection** kalitlari doimiy volume'da (deploylar orasida saqlanadi).

### 6.2. Ko'p-maktablilik va obuna (Control Plane)
- Platforma egasi yangi maktab (tenant) yaratadi (provisioning).
- Maktab **obunasi**: yoqilgan modullar (bo'sh = cheklovsiz), obuna muddati (kalendar oraliq), narx.
- Obuna muddati tugasa yoki modul yopiq bo'lsa — tegishli admin yo'llari **403** qaytaradi, navigatsiya
  frontendda yashiriladi.

### 6.3. O'quvchilar
- To'liq CRUD: F.I.SH (familiya/ism/sharif alohida), tug'ilgan sana, jinsi, manzil, ota-ona ma'lumoti.
- Metrika/passport **fayl yuklash** (rasm/hujjat).
- **Chegirma** (foiz + summa + izoh) — oylik to'lovni avtomatik kamaytiradi.
- **Sinf ichida kichik guruh** (1/2 guruh) — bo'lingan darslar uchun.
- **Arxivlash** (soft-delete): tarixiy ma'lumot saqlanadi, login bloklanadi, oylik to'lov to'xtaydi;
  arxivdan qaytarish mavjud.
- **GPS joylashuvi:** mobil ilovadan uy koordinatasi; admin Leaflet xaritada ko'radi.
- **Excel'dan ommaviy import:** bo'sh shablon (`.xlsx`, yo'riqnoma + mavjud sinflar ro'yxati) yuklab
  olish + to'ldirilgan faylni yuklash; qatorma-qator tekshiruv va qisman import (xato qatorlar
  raqami/sababi bilan).
- **Shaxsiy daftar (detal sahifa):** bitta o'quvchi haqida BARCHA ma'lumot — profil, fan×chorak
  baholari, davomat, intizom, topshiriqlar, oylik baholash, uy vazifa/xulq — **diagrammalar** bilan
  (radar, maydon/area, ustun grafiklar).

### 6.4. O'qituvchilar va xodimlar
- O'qituvchilar bazasi, fanlarga biriktirish, sinf rahbarligi, maosh.
- O'qituvchi **arxivlash** (tarixiy nusxa saqlanadi).
- **Xodimlar** (role=staff) — rol ruxsat matritsasiga ko'ra; shartnoma va bot ro'yxati.

### 6.5. Sinflar, fanlar, jadval
- Sinflar (nom, oylik narx), fanlar, dars jadvali (kun/dars raqami/vaqt, guruh bo'yicha).
- Dars vaqtlari, choraklar, bayram kunlari (bu sanalarda dars hisoblanmaydi).

### 6.6. Elektron jurnal
- Har dars uchun: o'tildi belgisi, mavzu, uy vazifa, **baho**, **davomat** (sabab bilan: sababsiz/
  kasal/kech).
- Har o'quvchiga **uy vazifa holati** (qildi/qilmadi) va **xulq** (yaxshi/yomon).
- Admin va o'qituvchi uchun yagona jurnal katak modali.

### 6.7. Oylik baholash (baholash turlari)
- Sozlanadigan **baholash turlari** (masalan: faollik, intizom, ...).
- Har o'quvchiga oy bo'yicha turlar kesimida baho; o'rtacha hisob va dinamika diagrammasi.

### 6.8. Intizom
- O'quvchi 100 balldan boshlaydi; sabablar orqali o'zgaradi (manfiy = jazo, musbat = rag'bat).
- Qo'lda kiritilgan va jurnal davomatidan kelgan ballar; to'liq tarix.

### 6.9. Topshiriqlar (assignments)
- Formatlar: **yozma, fayl, test, video**. Material biriktirish, muddat, kech topshirish jarimasi,
  maksimal ball.
- Test — avtomatik baholanadi; yozma/fayl — o'qituvchi baholaydi.
- O'quvchi javob/fayl yuklaydi; **ballar jadvali (scoreboard)** admin uchun; ota-ona ko'rinishi.

### 6.10. LMS (mustaqil ta'lim)
- Fan → mavzu → material (video/matn/fayl) ierarxiyasi.
- **Qulflanish mantig'i:** ketma-ket (sequential), partiyali (batch), barchasi ochiq (all).
- O'quvchi mavzuni "tugatdim" deb belgilaydi → keyingisi ochiladi; progress kuzatiladi.

### 6.11. Fan progresi
- Har fan bo'yicha "rejada nechta dars / nechta o'tildi / qoldi / foiz", bugungi kunga kutilgan,
  keyingi dars sanasi. O'qituvchi va o'quvchi ko'radi.

### 6.12. Reyting
- O'quvchi reytingi (o'rtacha baho bo'yicha): o'z sinfi to'liq + maktab bo'yicha TOP 15. Admin va
  o'quvchi/ota-ona uchun bir xil hisob.

### 6.13. Moliya
- Sinf oylik narxi asosida har oy uchun **avtomatik hisob (qarz)**; chegirma (foiz/summa) qo'llanadi.
- To'lov qabul qilish (oy ko'rsatib), balans, **to'lov tarixi (ledger)** oylar kesimida (to'langan/
  qisman/to'lanmagan).
- Kirim/chiqim moliyaviy tranzaksiyalari; chegirma o'zgarishi audit yoziladi.
- Login/parollarni (faqat birinchi kirishgacha ko'rinadigan parol) Excel'ga eksport.

### 6.14. Shartnomalar
- Word andoza (`@`-o'rinbosarlar) → OpenXML orqali to'ldirilgan `.docx`; ota-ona/xodimga **Telegram
  bot** orqali yuboriladi.

### 6.15. Boshqaruv
- **Filiallar** (xarita + radius), **rollar** (xodim ruxsat matritsasi), **xodimlar**, **taklif/
  shikoyatlar** (rasm bilan).

### 6.16. Oshxona
- Kunlik/oraliq menyu (taom, tarkib, rasm); ota-ona/o'quvchi ilovasida ko'rinadi.

### 6.17. O'quvchi / ota-ona ilovasi (portal + API)
- To'liq `/api/student/*` API (47+ endpoint) — `student_api.md` da hujjatlangan.
- **`/api/student/notebook`** — o'quvchi o'zining BARCHA ma'lumotini (admin detal sahifasi bilan
  aynan bir xil) bitta so'rovda ko'radi.
- Bosh sahifa, jadval, baholar, davomat, intizom, reyting, moliya, topshiriqlar, LMS, oshxona,
  bayramlar, chat, sozlamalar, joylashuv, pickup, taklif/shikoyat.
- **Pickup** ("farzandimni olishga keldim") — sinf rahbariga push, javob ota-onaga.

### 6.18. O'qituvchi portali va hisobotlar
- `/api/teacher/*` API (`teacher_api.md`).
- **Faollik hisoboti:** jadvalga nisbatan bajarilish foizi (baho/mavzu/uy vazifa kiritilganligi).

### 6.19. Bildirishnomalar va real-time
- **Telegram bot:** e'lonlar, shartnoma yetkazish (ota-ona/xodim raqami orqali bog'lanish).
- **Push (FCM):** yangi baho, davomat, e'lon, pickup javobi va h.k.
- **Chat (SignalR):** sinf guruh chati, jonli yangilanish (tenant-izolyatsiyalangan).

### 6.20. Hisobot va tahlil
- Diagrammalar (Recharts): o'zlashtirish, davomat, oylik baholash dinamikasi, uy vazifa/xulq,
  fan progresi — admin panel va o'quvchi daftarida.

### 6.21. Import / Eksport
- Excel: o'quvchi import shabloni + ommaviy yuklash; login/parol eksporti; CSV eksport.

---

## 7. Funksional bo'lmagan talablar (sifat)

| Yo'nalish | Yechim |
|---|---|
| **Xavfsizlik** | OWASP/Zero-Trust, JWT revocation, rate-limit, upload allowlist, CSP/HSTS, parol hash |
| **Ma'lumot izolyatsiyasi** | Tenant query-filter — maktablar bir-birining ma'lumotini ko'rmaydi |
| **Ishonchlilik** | Kunlik avtomatik DB backup (7 kun saqlash), DB qayta-urinish (retry) |
| **Unumdorlik** | Kesh (qisqa-TTL), so'rovlarni ajratish (split query), batch hisoblar |
| **Uzluksizlik** | Docker `restart: unless-stopped`, Cloudflare tunnel barqaror (HTTP/2) |
| **Til** | To'liq o'zbek tili (interfeys + API izohlari) |
| **Vaqt** | Yagona AppClock (Asia/Tashkent) |

---

## 8. Integratsiyalar
- **Telegram Bot API** — e'lon, shartnoma, ro'yxatdan o'tish (telefon orqali bog'lash).
- **Firebase Cloud Messaging** — mobil push.
- **Cloudflare Tunnel** — xavfsiz tashqi kirish (port ochilmaydi; har maktab subdomen).
- **OpenXML** — Excel/Word generatsiyasi.

---

## 9. Infratuzilma va deploy
Docker Compose 4 ta servis:
- `app` — API + frontend (SPA), 8080-port (faqat tunnel orqali).
- `sqlserver` — SQL Server 2022 (ma'lumot va backup volume'lari).
- `cloudflared` — Cloudflare tunnel (HTTP/2).
- `backup` — har kuni 02:00 (Toshkent) barcha bazalarni `.bak` qiladi, 7 kun saqlaydi.

Har o'zgarishdan keyin avtomatik `build → up -d` (CI-ga tayyor jarayon).

---

## 10. API hujjatlari
- `student_api.md` — o'quvchi/ota-ona ilovasi (47+ endpoint).
- `parent_api.md` — ota-ona nuqtai nazaridan.
- `teacher_api.md` — o'qituvchi ilovasi.
- Admin API — veb-panel uchun (`/api/admin/*`).

---

## 11. Kelajakda amalga oshirish mumkin (rivojlantirish imkoniyatlari)

Tizim arxitekturasi quyidagilarni qo'shishga tayyor:

**To'lov va moliya**
- **Onlayn to'lov** integratsiyasi (Payme, Click, Uzum) — ota-ona ilovadan to'g'ridan-to'g'ri to'laydi.
- Avtomatik kvitansiya/cheklar, SMS to'lov eslatmalari.

**Bildirishnoma va aloqa**
- **SMS-shlyuz** (Eskiz/Play Mobile) — parol, qarzdorlik, davomat SMS.
- Ota-ona ↔ o'qituvchi shaxsiy yozishmasi (1:1 chat).

**O'quv jarayoni**
- **Onlayn imtihon/test** moduli (vaqt chegarasi, savol banki, avtomatik baholash) kengaytmasi.
- **Onlayn dars** (video-konferensiya) integratsiyasi.
- Kutubxona (kitob berish/qaytarish), uy vazifasini onlayn topshirish kengaytmasi.

**Tahlil va AI**
- Kengaytirilgan **analitika dashboard** (maktab/sinf/o'qituvchi kesimida tendensiyalar).
- AI yordamchi: o'zlashtirish prognozi, xavf ostidagi o'quvchini aniqlash, avtomatik tavsiya.

**Infratuzilma va miqyoslash**
- **DB-per-tenant** (har maktabga alohida baza) + Azure SQL — yuqori izolyatsiya/miqyos.
- **Ofsayt backup** (bulutga avtomatik nusxa: S3/Object Storage).
- Yuqori yuklamada gorizontal masshtablash (bir nechta app instansiyasi).

**Mobil va boshqalar**
- Mahalliy **mobil ilovalar** (Android/iOS) — mavjud `student_api.md`/`teacher_api.md` ustiga.
- **Transport/avtobus** kuzatuvi (o'quvchi joylashuvi allaqachon bor).
- **Ko'p tillilik** (o'zbek/rus/ingliz) interfeysi.
- Yagona **identifikatsiya** (yuz/QR davomat, turniket integratsiyasi).

---

> **Xulosa:** SchoolLms — o'quv jarayonining barcha bosqichini (ta'lim, baho, davomat, moliya, aloqa,
> tahlil) qamragan, ko'p-maktabli, xavfsiz va kengaytiriladigan to'liq tayyor platforma. Yuqoridagi
> "kelajak" bo'limidagi imkoniyatlar mavjud arxitektura ustiga bosqichma-bosqich qo'shilishi mumkin.
