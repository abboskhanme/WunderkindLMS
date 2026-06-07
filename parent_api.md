# O'quvchi / Ota-ona ilovasi — Avtobus GPS API

Maktab avtobusining **hozir qayerda yurganini** va bugungi yo'nalishini xaritada ko'rsatish uchun
o'quvchi/ota-ona ilovasiga tegishli endpointlar. `StudentPortalController` (`/api/student/*`).

**Base URL:** `https://intellectschool.uz`
Har bir so'rovda: `Authorization: Bearer <token>` sarlavhasi (login `POST /api/auth/login` orqali olinadi).
Rollar: `student`, `parent`. Barcha javoblar **JSON**.

---

> ## 🔒 XAVFSIZLIK — vaqt oynasi
> Avtobus joylashuvi **faqat ertalabki oynada** — **06:00–09:00 (Asia/Tashkent)** — ko'rinadi.
> Oynadan tashqarida hech narsa qaytmaydi (`available:false` / bo'sh iz) — qolgan vaqt avtobuslar
> joylashuvi **ko'rinmaydi**.
> Tracker IMEI'si (qurilma ID) ilovaga **berilmaydi**; faqat **faol** (`IsActive`) avtobuslar ko'rsatiladi.

---

## 1. Avtobuslar jonli joylashuvi

```http
GET /api/student/buses                 (student, parent)
Authorization: Bearer <token>
```

**Javob:**
```json
{
  "available": true,
  "fromHour": 6,
  "toHour": 9,
  "serverTime": "2026-06-07T07:30:00",
  "buses": [
    {
      "id": "...",
      "name": "1-avtobus",
      "plateNumber": "01 A 123 BC",
      "driverName": "Aliyev V.",
      "driverPhone": "+998 90 123 45 67",
      "route": "Chilonzor",
      "lat": 41.311,
      "lng": 69.240,
      "speed": 32.0,
      "lastSeen": "2026-06-07T07:29:50",
      "online": true
    }
  ]
}
```

| Maydon | Izoh |
|---|---|
| `available` | Oyna ochiqmi (06:00–09:00). `false` bo'lsa `buses` **bo'sh** — ilova xaritani yashirib, "avtobuslar faqat ertalab 06:00–09:00 da ko'rinadi" deb ko'rsatadi. |
| `fromHour` / `toHour` | Ko'rinish oynasi chegaralari (6 va 9). |
| `serverTime` | Server (Tashkent) vaqti — ilova oynagacha qancha qolganini ko'rsatishi mumkin. |
| `online` | So'nggi signal `GpsOnlineMinutes` (admin sozlamasi, default **5** daqiqa) ichida bo'lsa `true`. |
| `lat` / `lng` / `speed` / `lastSeen` | Hali signal kelmagan avtobusda `null`. |

- **Jonli kuzatish:** ilova bu endpointni har bir necha soniyada (masalan 5–10s) qayta so'rab,
  xaritadagi marker(lar)ni yangilab turadi.

---

## 2. Bitta avtobusning bugungi yo'nalishi (iz)

```http
GET /api/student/buses/{id}/track      (student, parent)
Authorization: Bearer <token>
```

**Javob:**
```json
{
  "date": "2026-06-07",
  "points": [
    { "lat": 41.311, "lng": 69.240, "speed": 30, "time": "2026-06-07T07:05:00" }
  ],
  "stops": [
    { "lat": 41.320, "lng": 69.250, "arrivedAt": "2026-06-07T07:12:00",
      "departedAt": "2026-06-07T07:14:00", "durationMin": 2 }
  ],
  "distanceKm": 8.4,
  "movingMin": 22,
  "stoppedMin": 4
}
```

- Faqat **bugungi** kun izi (tarixiy kunlarni ko'rib bo'lmaydi).
- `points` — yo'nalish chizig'i (xaritada poliliniya). `stops` — to'xtagan joylar (radius + daqiqa bo'yicha).
- `distanceKm` / `movingMin` / `stoppedMin` — bugungi jamlama.
- Oynadan tashqarida bo'sh qaytadi: `points:[]`, `stops:[]`, qiymatlar `0`.

---

## Xatolar

| Kod | Sabab |
|---|---|
| `401` | Token yo'q / noto'g'ri yoki akkaunt arxivlangan. |
| `404` | Avtobus topilmadi (`/buses/{id}/track`). |

## curl misol
```bash
B=https://intellectschool.uz
TOKEN=$(curl -s -X POST $B/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"LOGIN","password":"PAROL"}' | jq -r .token)

curl -s $B/api/student/buses -H "Authorization: Bearer $TOKEN"
# Oyna ochiq (06:00–09:00) bo'lsa: { "available": true, "buses": [ ... ] }
# Tashqarida:                      { "available": false, "buses": [] }
```
