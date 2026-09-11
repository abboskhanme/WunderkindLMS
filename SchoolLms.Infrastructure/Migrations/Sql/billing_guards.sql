-- ===========================================================================
--  BillingCore — baza darajasidagi qulflar (SPEC §3.7 invariant + §4.1 grantlar)
--  Migratsiya: 20260911061113_BillingCore · Vazifa: P1-05
-- ===========================================================================
--
--  BU FAYLNI TAHRIRLAMANG.
--  Migratsiya bir marta qo'llanadi va shundan keyin muzlaydi. Bu yerni
--  o'zgartirsangiz, yangi bazalar eski bazalardan JIMGINA farq qila boshlaydi.
--  O'zgartirish kerak bo'lsa — YANGI migratsiya va yangi .sql fayl yozing.
--
--  NEGA XOM SQL
--  ------------
--  Qolgan hamma narsa (check constraint, unikal indeks, generated column)
--  EF modelida — BillingModel.cs da — yozilgan, chunki modeldagi narsa
--  snapshot'ga tushadi va keyingi `--autogenerate` uni DROP qilmaydi.
--  Shu faylda faqat EF UMUMAN ifodalay olmaydigan ikki narsa qoldi:
--    1) taqsimot (allocation) invarianti — trigger;
--    2) `app_rw` roli uchun GRANT/REVOKE.
--
--  Fayl EMBEDDED RESOURCE sifatida assembly ichiga kiradi (csproj), ya'ni
--  chop etilgan konteynerda ham mavjud bo'ladi — diskdagi yo'lga tayanmaydi.
-- ===========================================================================


-- ---------------------------------------------------------------------------
-- 1) TAQSIMOT INVARIANTI (SPEC §3.7)
--
--    "Bitta to'lovga tegishli taqsimotlar yig'indisi to'lov summasidan
--     OSHMASIN." Bu tekshiruv ILOVADA emas, bazada: ilovadagi tekshiruvni
--     xatolik ham, chetlab o'tish urinishi ham yengib o'tadi.
--
--    CHECK constraint bu yerda ishlamaydi — u faqat BITTA qatorni ko'radi,
--    bizga esa butun `payment_id` guruhining yig'indisi kerak. Shuning uchun
--    trigger.
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION check_allocation_total() RETURNS trigger AS $$
BEGIN
    -- MAXSUS QULF. Ikkita parallel tranzaksiya bir xil to'lovga taqsimot
    -- qo'shsa, ularning HECH BIRI ikkinchisining hali commit qilinmagan
    -- qatorini ko'rmaydi — ya'ni ikkalasi ham "yig'indi yetarli" deb o'tib
    -- ketardi va to'lov ikki barobar taqsimlanardi.
    --
    -- `SELECT ... FOR UPDATE` bu yerda YARAMAYDI: u `payments` jadvalida
    -- UPDATE huquqini talab qiladi, `app_rw` da esa u ataylab yo'q (pastga
    -- qarang). Advisory lock hech qanday jadval huquqini talab qilmaydi va
    -- tranzaksiya tugashi bilan o'zi bo'shaydi.
    PERFORM pg_advisory_xact_lock(hashtextextended(NEW.payment_id::text, 0));

    IF (SELECT coalesce(sum(amount), 0)
          FROM payment_allocations
         WHERE payment_id = NEW.payment_id)
       > (SELECT amount FROM payments WHERE id = NEW.payment_id)
    THEN
        -- Xabar matni SPEC §3.7 dan AYNAN olingan — P1-11 va testlar shunga tayanadi.
        RAISE EXCEPTION 'Allocation exceeds payment amount'
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END
$$ LANGUAGE plpgsql;

-- AFTER: qator allaqachon yozilgan, shuning uchun yig'indi YANGI qatorni ham
-- hisoblaydi. BEFORE bo'lsa o'zini hisobga olmay, oxirgi taqsimot doim
-- o'tib ketardi.
-- `CREATE OR REPLACE` (PostgreSQL 14+) ATAYLAB `DROP ... IF EXISTS` o'rniga:
-- migratsiyaning `Up()` qismida birorta DROP bo'lmasligi kerak (P1-05 qabul
-- mezoni) — autogenerate chiqargan "ortiqcha" DROP'larni ko'zdan qochirmaslik
-- uchun qoida qat'iy: `Up()` da DROP ko'rsangiz — to'xtang va o'qing.
CREATE OR REPLACE TRIGGER payment_allocations_total
    AFTER INSERT OR UPDATE ON payment_allocations
    FOR EACH ROW EXECUTE FUNCTION check_allocation_total();


-- ---------------------------------------------------------------------------
-- 2) MOLIYAVIY O'ZGARMASLIK — GRANT / REVOKE (SPEC §4.1)
--
--    NEGA MIGRATSIYADA, `deploy/init-roles.sql` KUTILMAYDI
--    ----------------------------------------------------
--    `init-roles.sql` dagi `ALTER DEFAULT PRIVILEGES` yangi yaratilgan HAR
--    jadvalga `app_rw` uchun to'liq CRUD beradi. Ya'ni yuqoridagi
--    `CREATE TABLE payments` tugagan lahzada `app_rw` da unga DELETE huquqi
--    BOR. Himoya faqat `init-roles.sql` QAYTA ishga tushirilgandan keyin
--    paydo bo'ladi — bu esa qo'lda bajariladigan, unutilishi mumkin bo'lgan
--    qadam. Unutilsa hech qanday xato chiqmaydi: ilova xuddi shunday
--    ishlayveradi, faqat pul himoyasi jimgina yo'q bo'ladi.
--
--    Shuning uchun REVOKE migratsiyaning O'ZI bilan keladi. `init-roles.sql`
--    dagi ro'yxat ham qoladi (ikki qulf) — u boshqa jadvallarni
--    (`access_events`, `point_transactions`) va parol yangilashni bajaradi.
--
--    ROL HALI YO'Q BO'LSA — jim o'tib ketamiz. Bu haqiqiy holat: test
--    harness'i (docs/TESTING.md) migratsiyani `app_rw` siz qo'llaydi, va
--    P1-02 dan oldingi bazalarda ham rol bo'lmasligi mumkin. Migratsiyani
--    yiqitish noto'g'ri bo'lardi — DDL allaqachon qo'llangan bo'lardi.
-- ---------------------------------------------------------------------------
DO $guards$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        RAISE NOTICE '[billing_guards] `app_rw` roli topilmadi — GRANT/REVOKE o''tkazib yuborildi. '
                     'Bu bazada moliyaviy o''zgarmaslik YO''Q. deploy/init-roles.sql ni ishga tushiring.';
        RETURN;
    END IF;

    -- Oddiy moliya jadvallari — to'liq CRUD. Ular tarix emas, joriy holat:
    -- narx o'zgaradi, chegirma tasdiqlanadi, smena yopiladi, hisob-faktura
    -- statusi `open` dan `paid` ga o'tadi.
    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON '
            'public.fee_categories, public.student_subscriptions, public.discounts, '
            'public.invoices, public.cash_shifts, public.expenses, public.billing_settings '
            'TO app_rw';

    -- O'ZGARMAS JADVALLAR. Faqat o'qish va qo'shish.
    -- Xato to'lov `reversal_of` bilan YANGI qator qo'shib tuzatiladi.
    EXECUTE 'GRANT SELECT, INSERT ON '
            'public.payments, public.payment_allocations, public.ledger_entries '
            'TO app_rw';
    EXECUTE 'REVOKE UPDATE, DELETE, TRUNCATE ON '
            'public.payments, public.payment_allocations, public.ledger_entries '
            'FROM app_rw';
END
$guards$;
