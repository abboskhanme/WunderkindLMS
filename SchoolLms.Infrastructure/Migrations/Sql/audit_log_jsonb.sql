-- ===========================================================================
--  audit_log.before / audit_log.after: text -> jsonb (SPEC §4.6)
--  Migratsiya: 20260912052432_FinanceAnomalyFlags · Vazifa: P1-14
-- ===========================================================================
--
--  BU FAYLNI TAHRIRLAMANG.
--  Migratsiya bir marta qo'llanadi va shundan keyin muzlaydi. O'zgartirish
--  kerak bo'lsa — YANGI migratsiya va yangi .sql fayl (billing_guards.sql
--  boshidagi qoida bilan bir xil).
--
--  NEGA XOM SQL, `migrationBuilder.AlterColumn` EMAS
--  -------------------------------------------------
--  Autogenerate `ALTER TABLE audit_logs ALTER COLUMN before TYPE jsonb;`
--  chiqaradi va PostgreSQL uni RAD ETADI:
--      ERROR: column "before" cannot be cast automatically to type jsonb
--      HINT:  You might need to specify "USING before::jsonb".
--  Ya'ni migratsiya ishlab turgan bazada yiqilardi. `USING ...` ni
--  `AlterColumn` orqali berib bo'lmaydi.
--
--  NEGA JSONB
--  ----------
--  Faza 0 bu ikki ustunni `text` qilib ko'chirgan. Matnda saqlangan snapshot
--  bo'yicha "kim 500 000 dan ortiq summani o'zgartirgan" degan savolga javob
--  berib bo'lmaydi — `LIKE '%Amount%'` jiddiy tekshiruv emas. `jsonb` bilan bu
--  oddiy so'rov:
--      select * from audit_logs where (after->>'Amount')::numeric > 500000;
--  Bundan tashqari `jsonb` yaroqsiz JSON'ni INSERT paytida rad etadi, ya'ni
--  buzilgan snapshot bazaga umuman tushmaydi.
--
--  MA'LUMOT YO'QOLMAYDI
--  --------------------
--  Bugungi barcha yozuvchilar `JsonSerializer.Serialize` dan o'tadi
--  (`AuditService.Entry` / `Record`, `CashShiftService.WriteAuditTrail`), ya'ni
--  mavjud qiymatlar allaqachon haqiqiy JSON. Shunga qaramay quyida ikkita
--  himoya bor: bo'sh satr NULL ga aylanadi va JSON bo'lmagan har qanday eski
--  qiymat JSON SATRIGA o'raladi. Hech narsa o'chirilmaydi.
-- ===========================================================================


-- 1) Bo'sh satr JSON emas ('' ni jsonb qabul qilmaydi), ma'nosi esa "yo'q".
UPDATE audit_logs SET before = NULL WHERE before IS NOT NULL AND btrim(before) = '';
UPDATE audit_logs SET after  = NULL WHERE after  IS NOT NULL AND btrim(after)  = '';

-- 2) JSON bo'lmagan eski qiymatni JSON satriga o'raymiz — o'chirmaymiz.
--    `IS NOT JSON` — PostgreSQL 16+ predikati (stack: 17).
UPDATE audit_logs SET before = to_jsonb(before)::text
 WHERE before IS NOT NULL AND before IS NOT JSON;
UPDATE audit_logs SET after = to_jsonb(after)::text
 WHERE after IS NOT NULL AND after IS NOT JSON;

-- 3) Tur o'zgarishi.
ALTER TABLE audit_logs ALTER COLUMN before TYPE jsonb USING before::jsonb;
ALTER TABLE audit_logs ALTER COLUMN after  TYPE jsonb USING after::jsonb;
