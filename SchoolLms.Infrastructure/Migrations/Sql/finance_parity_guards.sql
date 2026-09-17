-- ===========================================================================
--  Moliya pariteti, A to'plami — baza darajasidagi qulflar
--  Migratsiya: FinanceParityBatchA
--  Manba: docs/modules/finance-parity.md §3.1 (A2, A3, A4) va §3.4; SPEC §4.1
-- ===========================================================================
--
--  BU FAYLNI TAHRIRLAMANG.
--  Migratsiya bir marta qo'llanadi va shundan keyin muzlaydi. Bu yerni
--  o'zgartirsangiz, yangi bazalar eski bazalardan JIMGINA farq qila boshlaydi.
--  O'zgartirish kerak bo'lsa — YANGI migratsiya va yangi .sql fayl.
--  (Qoida `billing_guards.sql` dan aynan olingan — P1-05.)
--
--  NEGA MIGRATSIYADA, `deploy/init-roles.sql` KUTILMAYDI
--  ----------------------------------------------------
--  `init-roles.sql` dagi `ALTER DEFAULT PRIVILEGES` yangi yaratilgan HAR
--  jadvalga `app_rw` uchun to'liq CRUD beradi. Ya'ni `CREATE TABLE
--  cash_handovers` tugagan lahzada `app_rw` da unga DELETE huquqi BOR.
--  Uni qaytarib olish qo'lda bajariladigan, unutilishi mumkin bo'lgan qadam
--  bo'lmasligi kerak: unutilsa hech qanday xato chiqmaydi, ilova xuddi
--  shunday ishlayveradi, faqat pul himoyasi jimgina yo'q bo'ladi.
--  Shuning uchun REVOKE migratsiyaning O'ZI bilan keladi. `init-roles.sql`
--  §5 dagi ro'yxat ham YANGILANADI (ikki qulf) — busiz o'sha skriptning
--  4-qadamidagi umumiy `GRANT ... ON ALL TABLES` keyingi yurishda bu
--  REVOKE'ni bekor qilardi. Aynan shu F0.03 defekti edi.
--
--  ROL HALI YO'Q BO'LSA — jim o'tib ketamiz (test harness'i migratsiyani
--  `app_rw` siz ham qo'llay olishi kerak, docs/TESTING.md). Migratsiyani
--  yiqitish noto'g'ri bo'lardi: DDL allaqachon qo'llangan bo'lardi.
--
--  NIMA UCHUN XOM SQL
--  ------------------
--  Qolgan hamma narsa (check constraint, unikal indeks, FK) EF modelida —
--  `FinanceParityModel.cs` da — yozilgan, chunki modeldagi narsa snapshot'ga
--  tushadi va keyingi `--autogenerate` uni "ortiqcha" deb DROP qilmaydi.
--  Shu faylda faqat EF UMUMAN ifodalay olmaydigan ikki narsa qoldi:
--    1) qaytarim qulfi — trigger;
--    2) `app_rw` roli uchun GRANT/REVOKE.
-- ===========================================================================


-- ---------------------------------------------------------------------------
-- 1) QAYTARIM QULFI — qaror BIR MARTA yoziladi (§3.1 A3)
--
--    Pastdagi ustun darajasidagi GRANT `app_rw` ga to'rtta ustunni yozishga
--    ruxsat beradi — busiz tasdiqlash oqimi umuman ishlamasdi. Lekin "bir
--    marta yozish" grant bilan ifodalanmaydi: grant "yozsa bo'ladi" deydi,
--    "faqat bir marta" demaydi. Ya'ni faqat grantga tayansak, direktor
--    qaytarimni tasdiqlab, keyin `cash_shift_id` ni BOSHQA smenaga ko'chirib
--    qo'ya olardi — pul bir smenadan chiqib, boshqasidan chiqqan bo'lib
--    ko'rinardi va ikkala smena ham "to'g'ri" yopilardi.
--
--    CHECK constraint bu yerda YARAMAYDI: u faqat YANGI qatorni ko'radi,
--    bizga esa ESKI qator (OLD) kerak. Shuning uchun trigger.
--
--    `CREATE OR REPLACE` (PostgreSQL 14+) ATAYLAB `DROP ... IF EXISTS`
--    o'rniga: migratsiyaning `Up()` qismida birorta DROP bo'lmasligi kerak
--    (docs/ASSUMPTIONS.md, 2026-09-11). Jadval shu migratsiyada yaratiladi,
--    ya'ni trigger oldindan mavjud bo'la olmaydi.
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION student_refunds_lock_decided() RETURNS trigger AS $$
BEGIN
    -- Qaror = tasdiq YOKI rad. Ikkalasi ham yakuniy.
    IF OLD.approved_at IS NOT NULL OR OLD.rejected_reason IS NOT NULL THEN
        -- ERRCODE `check_violation` — `billing_guards.sql` dagi taqsimot
        -- trigger'i bilan bir xil: ilova uchun bu "baza qoidasi to'xtatdi",
        -- huquq muammosi (42501) emas, ya'ni xabar ham boshqacha bo'ladi.
        RAISE EXCEPTION 'Refund decision is final and cannot be changed'
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END
$$ LANGUAGE plpgsql;

-- BEFORE: qator hali yozilmagan, ya'ni o'zgarish UMUMAN amalga oshmaydi.
-- AFTER bo'lsa qator yozilib, keyin tranzaksiya qaytarilardi — natija bir xil,
-- lekin bekorga ish.
--
-- DIQQAT: trigger EGA rolga ham tegishli. `REVOKE` `schoollms_owner` ga
-- ta'sir qilmaydi (SPEC §4.1 dagi tuzatish), trigger esa qiladi — ya'ni bu
-- qulf grantdan KUCHLIROQ va migratsiya rolida ham ishlaydi.
CREATE OR REPLACE TRIGGER student_refunds_locked
    BEFORE UPDATE ON student_refunds
    FOR EACH ROW EXECUTE FUNCTION student_refunds_lock_decided();


-- ---------------------------------------------------------------------------
-- 2) GRANT / REVOKE (SPEC §4.1, finance-parity §3.1 va §3.4)
--
--    TARTIB MUHIM: avval jadval darajasidagi GRANT/REVOKE, keyin ustun
--    darajasidagi GRANT. Teskarisida `REVOKE UPDATE ON <jadval>` endigina
--    berilgan USTUN huquqini ham olib ketardi — PostgreSQL jadval darajasida
--    UPDATE ni qaytarib olganda o'sha jadvalning HAR ustunidagi mos huquqni
--    ham o'chiradi. Natija jimgina "hech kim tasdiqlay olmaydi" bo'lardi.
--    (`anomaly_guards.sql` dagi aynan shu izoh.)
-- ---------------------------------------------------------------------------
DO $guards$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        RAISE NOTICE '[finance_parity_guards] `app_rw` roli topilmadi — GRANT/REVOKE o''tkazib '
                     'yuborildi. Bu bazada kassa topshirig''i va qaytarim uchun moliyaviy '
                     'o''zgarmaslik YO''Q. deploy/init-roles.sql ni ishga tushiring.';
        RETURN;
    END IF;

    -- ---- A2, A3, A4: o'qish va yangi qator yozish ----
    EXECUTE 'GRANT SELECT, INSERT ON '
            'public.cash_handovers, public.student_refunds, public.expense_attachments '
            'TO app_rw';

    -- ---- Tahrirlash va o'chirish — YO'Q ----
    --
    -- `cash_handovers`: xato topshiriq `reversal_of` bilan qarshi qator
    --   qo'shib tuzatiladi, xuddi `payments` dagidek.
    --
    -- `student_refunds`: maktabdan PUL CHIQADI — `payments` bilan bir sinf
    --   himoya, yumshog'i emas (finance-parity §3.4).
    --
    -- `expense_attachments`: chiqimning DALILI. Uni o'chirish yoki
    --   almashtirish — SPEC §4 tasvirlagan firibgarlikning aynan o'zi:
    --   summa joyida qoladi, lekin uni tekshirib bo'lmaydigan bo'lib qoladi.
    --   Noto'g'ri fayl yuklansa, to'g'risi YANGI qator bo'lib qo'shiladi.
    --
    -- TRUNCATE ham — "hamma qatorni o'chirish" ning DELETE'siz yo'li.
    EXECUTE 'REVOKE UPDATE, DELETE, TRUNCATE ON '
            'public.cash_handovers, public.student_refunds, public.expense_attachments '
            'FROM app_rw';

    -- ---- A3: qaror oqimi uchun AYNAN to'rtta ustun ----
    --
    -- `payments` dagidek to'liq REVOKE bu yerda YARAMAYDI — u tasdiqlash
    -- oqimini ham o'ldirardi. Shuning uchun jadval darajasidagi UPDATE olib
    -- tashlandi (yuqorida) va AYNAN to'rtta ustunga qaytariladi: summa,
    -- usul, sabab, so'rovchi va so'rov vaqti keyinchalik "tuzatib" qo'yilishi
    -- MUMKIN EMAS.
    --
    -- Va bu to'rttasi ham faqat BIR MARTA yoziladi — yuqoridagi trigger.
    EXECUTE 'GRANT UPDATE (approved_by, approved_at, cash_shift_id, rejected_reason) '
            'ON public.student_refunds TO app_rw';

    -- ---- A1: `expenses` ga TEGILMAYDI ----
    -- PostgreSQL'da huquq JADVAL darajasida beriladi, ya'ni yangi
    -- `cash_shift_id` ustuni avtomatik ravishda `expenses` ning mavjud to'liq
    -- CRUD huquqi ostiga tushadi (`billing_guards.sql`). Bu to'g'ri: chiqim
    -- tarix emas, joriy holat — u tasdiqlanadi, tuzatiladi va bekor qilinadi.
END
$guards$;
