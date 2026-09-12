-- ===========================================================================
--  finance_anomaly_flags — baza darajasidagi qulflar (SPEC §4.1 grantlari, §4.6)
--  Migratsiya: 20260912052432_FinanceAnomalyFlags · Vazifa: P1-14
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
--  jadvalga `app_rw` uchun to'liq CRUD beradi. Ya'ni yuqoridagi
--  `CREATE TABLE finance_anomaly_flags` tugagan lahzada `app_rw` da unga
--  DELETE huquqi BOR. Uni qaytarib olish qo'lda bajariladigan, unutilishi
--  mumkin bo'lgan qadam bo'lmasligi kerak: unutilsa hech qanday xato
--  chiqmaydi, ilova xuddi shunday ishlayveradi, faqat himoya jimgina yo'q
--  bo'ladi. Shuning uchun REVOKE migratsiyaning O'ZI bilan keladi.
--
--  ROL HALI YO'Q BO'LSA — jim o'tib ketamiz (test harness'i migratsiyani
--  `app_rw` siz qo'llaydi, docs/TESTING.md).
--
--  NEGA USTUN DARAJASIDAGI GRANT
--  -----------------------------
--  SPEC §4.6: bayroqni "bekor qilib bo'lmaydi, faqat YOZMA SABAB bilan
--  yopiladi". Bu ikkita talab:
--    * o'chirish MUMKIN EMAS  -> DELETE va TRUNCATE yo'q;
--    * yopish MUMKIN          -> resolved_at / resolved_by / resolved_reason
--                                ustunlariga UPDATE kerak.
--  `payments` dagidek to'liq REVOKE bu yerda YARAMAYDI — u yopish oqimini ham
--  o'ldirardi. Shuning uchun jadval darajasidagi UPDATE olib tashlanadi va
--  AYNAN uchta ustunga qaytariladi: bayroqning matni, summasi, turi yoki
--  topilgan vaqti keyinchalik "tuzatib" qo'yilishi mumkin emas.
--
--  TARTIB MUHIM: avval jadval darajasidagi REVOKE, keyin ustun darajasidagi
--  GRANT. Teskarisida REVOKE endigina berilgan ustun huquqini ham olib
--  ketishi mumkin, va natija jimgina "hech kim yopa olmaydi" bo'lardi.
-- ===========================================================================

DO $guards$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        RAISE NOTICE '[anomaly_guards] `app_rw` roli topilmadi — GRANT/REVOKE o''tkazib yuborildi. '
                     'Bu bazada bayroqlarni o''chirishdan himoya YO''Q. '
                     'deploy/init-roles.sql ni ishga tushiring.';
        RETURN;
    END IF;

    -- O'qish va yangi bayroq yozish (tungi tekshiruv).
    EXECUTE 'GRANT SELECT, INSERT ON public.finance_anomaly_flags TO app_rw';

    -- O'chirish va butun jadvalni tahrirlash — YO'Q.
    EXECUTE 'REVOKE UPDATE, DELETE, TRUNCATE ON public.finance_anomaly_flags FROM app_rw';

    -- Yopish oqimi uchun AYNAN uchta ustun, boshqa hech narsa.
    EXECUTE 'GRANT UPDATE (resolved_at, resolved_by, resolved_reason) '
            'ON public.finance_anomaly_flags TO app_rw';
END
$guards$;
