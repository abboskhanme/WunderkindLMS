# Parent / Student app — API

Ota-ona / o'quvchi ilovasi `StudentPortalController` (`/api/student/*`) bilan ishlaydi. Oila bitta
akkaunt ishlatadi (o'quvchi akkaunti). Javoblar JSON.

**Base URL:** `https://intellectschool.uz`

---

## Ulanish (auth)

Login **faqat login+parol** bilan (maktab kodi shart emas — login global unikal, server maktabni o'zi
aniqlaydi). Token ichida maktab "yopilgan".

```http
POST /api/auth/login
Content-Type: application/json

{ "email": "<login>", "password": "<parol>" }
```
Javob: `{ "token": "eyJ...", "user": { ... } }` — keyingi so'rovlar: `Authorization: Bearer <token>`.

> Bir nechta farzandli ota-ona: aksar GET endpointlar `?studentId=` qabul qiladi (berilmasa — birinchi
> farzand). `POST` larda esa egalik tekshiriladi.

---

## Endpoint'lar

| Metod | Yo'l | Tavsif |
|---|---|---|
| GET | `/api/student/me` | Profil (rasm bilan) |
| GET | `/api/student/dashboard` | Bosh sahifa (profil + bugungi darslar/baholar) |
| GET | `/api/student/schedule?quarter=&week=` | Dars jadvali |
| GET | `/api/student/grades` | Baholar/davomat hisoboti |
| GET | `/api/student/homework?quarter=` | Mavzu + uy vazifalari |
| GET | `/api/student/rating` | Reyting (sinf + maktab) |
| GET | `/api/student/finance` | To'lovlar/balans |
| GET | `/api/student/discipline` | Intizomiy ball (qoldi + tarix) |
| GET | `/api/student/holidays` | Bayram kunlari (dars yo'q) |
| GET/PUT | `/api/student/location` | Uy joylashuvi (GPS) |
| GET | `/api/student/telegram` | Telegram bot holati + ro'yxat statusi |
| POST | `/api/student/pickup` | "Farzandimni olishga keldim" |
| GET | `/api/student/pickup` | Pickup so'rovi holati |
| POST | `/api/student/feedback` | Taklif/shikoyat (multipart) |
| GET/PUT | `/api/student/settings` | Til/tema/bildirishnoma sozlamasi |
| POST | `/api/student/notifications/register` | Push qurilmani ro'yxatga olish |
| DELETE | `/api/student/notifications/register?token=...` | Qurilmani o'chirish (logout) |

---

## Profil (`GET /api/student/me`)
```json
{
  "id": "...",
  "fullName": "Aliyev Ali Aliyevich",
  "className": "9-A",
  "birthDate": "2011-05-01",
  "gender": "male",
  "parentFullName": "Aliyev Vali",
  "parentPhone": "+998 90 123 45 67",
  "enrollmentDate": "2025-09-01",
  "photoUrl": "/uploads/abc.jpg",
  "parentPhotoUrl": "/uploads/def.jpg"
}
```
- `photoUrl` — o'quvchining rasmi (admin yuklaydi); `parentPhotoUrl` — ota-ona rasmi (ixtiyoriy).
- `null` bo'lsa standart avatar. Nisbiy URL — to'liq manzil: `https://intellectschool.uz` + `photoUrl`.
- `/api/student/dashboard` javobining ichidagi `profile` ham xuddi shu maydonlarni beradi.

---

## Intizomiy ball (`GET /api/student/discipline`)

O'quvchi **100 balldan** boshlaydi; sabablar orqali o'zgaradi (manfiy = jazo, musbat = rag'bat).
```json
{
  "remaining": 93,
  "plus": 3,
  "minus": 10,
  "items": [
    { "id": "...", "reasonName": "Darsga kech qoldi", "points": -5, "note": "3-darsda",
      "createdAt": "2026-06-01T09:30:00", "createdBy": "Administrator", "source": "manual" },
    { "id": "...", "reasonName": "Sababsiz", "points": -2, "note": "Jurnal davomati",
      "createdAt": "2026-05-28", "createdBy": "", "source": "attendance" }
  ]
}
```
- `remaining` = 100 + `plus` − `minus` (asosiy ko'rsatkich — rang bilan ko'rsating).
- `items` — tarix (yangidan eskiga). `source`: `manual` (admin qo'lda) yoki `attendance` (jurnal davomati).

---

## Dars jadvali + bayram kunlari

`GET /api/student/holidays` → `[{ "date": "2026-03-21", "name": "Navro'z" }, ...]` (butun maktab).

Jadvalni chizishda:
- **Bayram kunlari**: ro'yxatdagi sanada dars ko'rsatmang — "Bayram" deb belgilang.
- **Chorak oxiri**: chorak hafta o'rtasida tugasa, oxirgi haftada tugash sanasidan keyingi kunlar
  ko'rsatilmaydi (hafta diapazoni chorak `startDate/endDate` dan, chegaraga qisilgan).
> Jurnal/baholar API'lari bayram kunlarini allaqachon o'tkazib yuboradi.

---

## Joylashuv (`/api/student/location`)
```http
PUT /api/student/location
Authorization: Bearer <token>
Content-Type: application/json

{ "latitude": 41.311081, "longitude": 69.240562, "address": "Toshkent, Chilonzor 12" }
```
- `latitude` (−90..90), `longitude` (−180..180) majburiy; `address` ixtiyoriy. Javob: `{ "ok": true }`.
- `GET /api/student/location` → `{ latitude, longitude, address, updatedAt }` (yo'q bo'lsa — `null`).
- Admin "Ilova → Joylashuv" (Leaflet xarita)da ko'radi.

---

## Telegram bot orqali ro'yxatdan o'tish

Admin ota-onalarga Telegram bot orqali e'lon yuboradi. Ota-ona e'lon olishi uchun **bir marta** botga
telefon raqamini ulashi kerak (raqam o'quvchining `ParentPhone`si bilan solishtiriladi).

```http
GET /api/student/telegram
Authorization: Bearer <token>
```
```json
{ "configured": true, "botUsername": "MaktabBot", "botName": "Maktab LMS Bot",
  "deepLink": "https://t.me/MaktabBot", "registered": false }
```
- **Tavsiya:** birinchi loginda chaqiring. `configured = true` va `registered = false` bo'lsa —
  taklif (banner) + `deepLink` ga tugma ko'rsating. `registered = true` bo'lgach ko'rsatmang.
- Bog'lash bot tomonida (raqam ulashilganda) avtomatik; bu API faqat holatni qaytaradi.

---

## Farzandni olib ketish (pickup)

Ota-ona "Farzandimni olishga keldim" bossa → sinf rahbariga push. Rahbar "Qabul qildim/Topshirish"
bossa → ota-onaga "Ruxsat berildi" push (holat `accepted`).

```http
POST /api/student/pickup
Authorization: Bearer <token>
Content-Type: application/json

{ "studentId": null }
```
- `studentId` ixtiyoriy (bir nechta farzand bo'lsa). `pending` so'rov bo'lsa — o'sha qaytadi.
- Javob: `{ id, studentId, studentName, className, status, createdAt, acceptedAt, acceptedByName }`.

```http
GET /api/student/pickup
Authorization: Bearer <token>
```
- **Bugungi** so'rov holati (yo'q bo'lsa `null`) — pickup kunlik, har kuni qaytadan boshlanadi.
  `status: "accepted"` + `acceptedByName` to'lsa — "Ruxsat berildi".

---

## Push bildirishnoma — qurilmani ro'yxatga olish

Ilova kirgach FCM tokenini yuboradi (`deviceName`/`appId` admin "Ilova → Ota-onalar"da ko'rinadi).
Push: yangi baho, davomat, e'lon, pickup javobi va h.k. shu token orqali keladi.
```http
POST /api/student/notifications/register
Authorization: Bearer <token>
Content-Type: application/json

{ "token": "<fcm-token>", "platform": "android", "deviceName": "Samsung A52", "appId": "<firebase-app-id>" }
```
- Javob: `{ "ok": true }`. `deviceName`/`appId` bo'sh yuborilsa — eskisi saqlanadi.
- Logout: `DELETE /api/student/notifications/register?token=<fcm-token>`.

---

## curl misol
```bash
B=https://intellectschool.uz
TOKEN=$(curl -s -X POST $B/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"LOGIN","password":"PAROL"}' | jq -r .token)

curl -s $B/api/student/me        -H "Authorization: Bearer $TOKEN"
curl -s $B/api/student/discipline -H "Authorization: Bearer $TOKEN"
curl -s $B/api/student/telegram  -H "Authorization: Bearer $TOKEN"
```

## Xatolar
- **401** → token yo'q/noto'g'ri (yoki login/parol xato).
- **400** → noto'g'ri ma'lumot (masalan koordinata −90..90 / −180..180 emas).
- **404** → akkauntga bog'langan o'quvchi topilmadi.
