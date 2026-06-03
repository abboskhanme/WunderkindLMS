# Teacher (o'qituvchi) API — mobil ilova uchun

O'qituvchi ilovasi `TeacherPortalController` (`/api/teacher/*`) bilan ishlaydi. Barcha endpoint'lar
**faqat `teacher` rolida**. Javoblar JSON.

**Base URL:** `https://intellectschool.uz`

---

## Ulanish (auth)

Ko'p maktabli (multi-tenant) — bitta backend, har maktab ma'lumoti ajratilgan. Login **faqat
login+parol** bilan (maktab kodi shart emas): loginlar butun baza bo'ylab unikal, server logindan
maktabni o'zi aniqlaydi. Token ichida maktab "yopilgan" (`tenant` claim) — keyingi so'rovlarda
qo'shimcha sarlavha kerak emas.

```http
POST /api/auth/login
Content-Type: application/json

{ "email": "<o'qituvchi-login>", "password": "<parol>" }
```
Javob:
```json
{ "token": "eyJ...", "user": { "id": "...", "fullName": "...", "role": "teacher", "permissions": ["journal","assignments"] } }
```
- Keyingi so'rovlar: `Authorization: Bearer <token>`.
- `permissions` — o'qituvchiga ochiq bo'limlar (ilovada menyuni shularga qarab ko'rsating).

---

## Endpoint'lar

### Profil / umumiy
| Metod | Yo'l | Tavsif |
|---|---|---|
| GET | `/api/teacher/me` | O'qituvchi profili (`photoUrl` bilan) |
| GET | `/api/teacher/meta` | Maktab meta (fanlar, choraklar, dars vaqtlari) |
| GET | `/api/teacher/classes` | O'qituvchi sinflari/fanlari |
| GET | `/api/teacher/schedule?quarter=&week=` | Dars jadvali |
| GET | `/api/teacher/holidays` | Bayram kunlari (dars yo'q) |
| GET | `/api/teacher/salary` | Maosh ma'lumoti |

### Jurnal (kundalik)
| Metod | Yo'l | Tavsif |
|---|---|---|
| GET | `/api/teacher/journal/students` | Sinf o'quvchilari |
| GET | `/api/teacher/journal/columns` | Jurnal ustunlari (sanalar) |
| GET | `/api/teacher/journal` | Baholar/davomat |
| PUT | `/api/teacher/journal` | Baho/davomat qo'yish |
| DELETE | `/api/teacher/journal` | Baho/davomat o'chirish |
| GET/PUT | `/api/teacher/journal/notes` | Dars mavzusi/uy vazifa |
| GET/PUT | `/api/teacher/journal/quarter-grades` | Choraklik baholar |

> Baho qo'yilganda yoki davomatda sabab bilan belgilanganda — o'quvchining oilasi ilovasiga
> avtomatik push boradi (sozlangan bo'lsa).

### Sinf rahbarligi (homeroom)
| Metod | Yo'l | Tavsif |
|---|---|---|
| GET | `/api/teacher/homeroom` | Sinf o'quvchilari + kim kutilmoqda |
| POST | `/api/teacher/homeroom/handover` | "Topshirish" — farzandni ota-onaga topshirish |
| GET | `/api/teacher/pickups` | Olib ketish so'rovlari |
| POST | `/api/teacher/pickups/{id}/accept` | "Qabul qildim" |

### Topshiriqlar
| Metod | Yo'l | Tavsif |
|---|---|---|
| GET | `/api/teacher/assignments` | Ro'yxat |
| POST | `/api/teacher/assignments` | Yangi topshiriq |
| PUT | `/api/teacher/assignments/{id}` | Tahrirlash |
| DELETE | `/api/teacher/assignments/{id}` | O'chirish |
| GET | `/api/teacher/assignments/{id}/results` | Natijalar |
| PUT | `/api/teacher/assignments/{id}/submissions/{studentId}` | Baholash |
| GET | `/api/teacher/assignment-types` | Topshiriq turlari |
| POST | `/api/teacher/uploads` | Fayl yuklash (multipart) |

### Chat
| Metod | Yo'l | Tavsif |
|---|---|---|
| GET | `/api/teacher/chat/classes` | Sinf chat ro'yxati |
| GET | `/api/teacher/chat/last-messages` | Oxirgi xabarlar |
| GET | `/api/teacher/chat/{className}` | Sinf chati |
| POST | `/api/teacher/chat/{className}` | Xabar yuborish |

### LMS / boshqa
| Metod | Yo'l | Tavsif |
|---|---|---|
| GET | `/api/teacher/progress` | Fan progressi (reja/bajarilgan) |
| POST | `/api/teacher/feedback` | Taklif/shikoyat (multipart) |
| GET | `/api/teacher/lms/subjects` | LMS fanlar |
| GET | `/api/teacher/lms/subjects/{subjectId}/topics` | Mavzular |
| GET | `/api/teacher/lms/subjects/{subjectId}/progress` | O'quvchilar progressi |
| POST | `/api/teacher/notifications/register` | Push qurilmani ro'yxatga olish |
| DELETE | `/api/teacher/notifications/register?token=...` | Qurilmani o'chirish (logout) |

---

## Profil (`GET /api/teacher/me`)
```json
{
  "id": "...",
  "fullName": "Karimova Dilnoza",
  "email": "karimovadilnoza",
  "homeroomClass": "9-A",
  "subjects": [{ "id": "...", "name": "Matematika" }],
  "permissions": ["journal", "assignments"],
  "photoUrl": "/uploads/abc.jpg"
}
```
- `photoUrl` — o'qituvchining rasmi (admin yuklaydi); `null` bo'lsa standart avatar. Nisbiy URL —
  to'liq manzil: `https://intellectschool.uz` + `photoUrl`.
- `homeroomClass` bo'sh bo'lmasa — o'qituvchi sinf rahbari (Sinf rahbarligi bo'limini ko'rsating).

---

## Dars jadvali + bayram kunlari

`GET /api/teacher/holidays` → `[{ "date": "2026-03-21", "name": "Navro'z" }, ...]` (butun maktab).

Jadvalni chizishda:
- **Bayram kunlari**: ro'yxatdagi sanaga to'g'ri keladigan kunda dars ko'rsatmang — "Bayram" deb belgilang.
- **Chorak oxiri**: chorak hafta o'rtasida tugasa, oxirgi haftada chorak tugash sanasidan keyingi
  kunlar ko'rsatilmaydi (hafta diapazoni `quarter.startDate/endDate` dan, chorak chegarasiga qisilgan).

---

## Sinf rahbarligi va farzandni topshirish

Sinf rahbari dashboardidagi **"Sinf rahbarligi"** bo'limi. Ota-ona ilovada "Farzandimni olishga keldim"
bossa, sinf rahbariga push keladi va o'quvchi ro'yxatda belgilanadi. Rahbar farzandni **"Topshirish"**
(yoki "Qabul qildim") bossa — ota-onaga ruxsat push'i boradi.

> **Kunlik:** pickup holati har **o'qish kuni** mustaqil — `hasPendingPickup`/`status` faqat **bugungi**
> so'rovga tegishli, har kuni avtomatik nolga tushadi (kechagi holat ko'rinmaydi). Sinf o'quvchilari
> ro'yxati esa har doim chiqadi.

### Sinf o'quvchilari — `GET /api/teacher/homeroom`
```json
{
  "className": "9-A",
  "students": [
    { "studentId": "...", "fullName": "Aliyev Ali", "hasPendingPickup": true, "status": "pending", "requestedAt": "2026-06-01T16:20:00" },
    { "studentId": "...", "fullName": "Valiyev Vali", "hasPendingPickup": false, "status": null, "requestedAt": null }
  ]
}
```
- Rahbarlik yo'q bo'lsa `className` bo'sh, `students` bo'sh.
- `hasPendingPickup: true` — ota-onasi kelgan (kutilmoqda); ro'yxatda ajratib ko'rsating.

### Topshirish — `POST /api/teacher/homeroom/handover`
```json
{ "studentId": "..." }
```
- O'quvchi shu rahbar sinfida bo'lsin (aks holda **403**).
- Kutilayotgan so'rov tasdiqlanadi; bo'lmasa yangi "accepted" yoziladi.
- **Ota-onaga push**: «Farzandingiz {ism}ni olib ketishingiz mumkin — {rahbar} topshirdi.»
- Javob: pickup DTO (`status: "accepted"`).

### So'rovlar ro'yxati — `GET /api/teacher/pickups`
```json
[
  { "id": "...", "studentId": "...", "studentName": "Aliyev Ali", "className": "9-A",
    "status": "pending", "createdAt": "...", "acceptedAt": null, "acceptedByName": null }
]
```
### Qabul qilish — `POST /api/teacher/pickups/{id}/accept`
- So'rov `accepted` bo'ladi va ota-onaga push yuboriladi. Boshqa sinf → **403**, topilmasa → **404**.

---

## Push bildirishnoma — qurilmani ro'yxatga olish

Ilova kirgach FCM tokenini yuboradi (`deviceName`/`appId` admin "Ilova → O'qituvchilar"da ko'rinadi).
```http
POST /api/teacher/notifications/register
Authorization: Bearer <token>
Content-Type: application/json

{ "token": "<fcm-token>", "platform": "android", "deviceName": "Samsung A52", "appId": "<firebase-app-id>" }
```
- Javob: `{ "ok": true }`. Logout: `DELETE /api/teacher/notifications/register?token=<fcm-token>`.

---

## curl misol
```bash
B=https://intellectschool.uz
TOKEN=$(curl -s -X POST $B/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"karimovadilnoza","password":"PAROL"}' | jq -r .token)
curl -s $B/api/teacher/classes -H "Authorization: Bearer $TOKEN"
```

## Xatolar
- **401** → login/parol noto'g'ri yoki token yo'q.
- **403** → rol `teacher` emas, maktab to'xtatilgan, yoki boshqa sinf/maktab so'raldi.
- **404** → resurs topilmadi.
