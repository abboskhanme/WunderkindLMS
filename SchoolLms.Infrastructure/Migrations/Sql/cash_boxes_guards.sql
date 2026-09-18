-- ===========================================================================
--  Kassalar (cash boxes) — baza darajasidagi GRANT/REVOKE
--  Migratsiya: CashBoxes
--  Manba: mijoz javobi (2026-09) — "smena" o'rnini kassalar bosadi; SPEC §4.1
-- ===========================================================================
--
--  BU FAYLNI TAHRIRLAMANG.
--  Migratsiya bir marta qo'llanadi va shundan keyin muzlaydi. Bu yerni
--  o'zgartirsangiz, yangi bazalar eski bazalardan JIMGINA farq qila boshlaydi.
--  O'zgartirish kerak bo'lsa — YANGI migratsiya va yangi .sql fayl.
--  (Qoida `billing_guards.sql` / `finance_parity_guards.sql` dan aynan olingan.)
--
--  NEGA MIGRATSIYADA, `deploy/init-roles.sql` KUTILMAYDI
--  ----------------------------------------------------
--  `init-roles.sql` dagi `ALTER DEFAULT PRIVILEGES` yangi yaratilgan HAR
--  jadvalga `app_rw` uchun to'liq CRUD beradi. Ya'ni `CREATE TABLE
--  cash_box_transactions` tugagan lahzada `app_rw` da unga DELETE huquqi
--  BOR. Uni qaytarib olish qo'lda bajariladigan, unutilishi mumkin bo'lgan
--  qadam bo'lmasligi kerak — shuning uchun REVOKE migratsiyaning O'ZI bilan
--  keladi.
--
--  ROL HALI YO'Q BO'LSA — jim o'tib ketamiz (test harness'i migratsiyani
--  `app_rw` siz ham qo'llay olishi kerak, docs/TESTING.md). Migratsiyani
--  yiqitish noto'g'ri bo'lardi: DDL allaqachon qo'llangan bo'lardi.
--
--  IKKI JADVAL — IKKI XIL HUQUQ
--  -----------------------------
--    * `cash_boxes`            — ODDIY kataloq: nom, mas'ul, sukut belgisi,
--                                 faollik. Rename/deactivate — UPDATE kerak.
--                                 Faqat DELETE yopiladi (kassa o'chirilmaydi,
--                                 faqat deaktivatsiya qilinadi — bu ilova
--                                 qatlamida ham amalga oshirilmaydi, lekin
--                                 himoya ikki qavatda bo'lishi kerak).
--    * `cash_box_transactions` — PUL YOZUVI: FAQAT QO'SHILADI (SPEC §4.1,
--                                 `payments`/`ledger_entries` bilan bir xil
--                                 qoida). Bekor qilingan amal — storno qatori
--                                 (`reversal_of`), hech qachon UPDATE/DELETE.
-- ===========================================================================

DO $guards$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        RAISE NOTICE '[cash_boxes_guards] `app_rw` roli topilmadi — GRANT/REVOKE o''tkazib '
                     'yuborildi. Bu bazada kassa harakatlari uchun moliyaviy o''zgarmaslik '
                     'YO''Q. deploy/init-roles.sql ni ishga tushiring.';
        RETURN;
    END IF;

    -- ---- cash_boxes: kataloq, faqat DELETE yopiladi ----
    EXECUTE 'REVOKE DELETE ON public.cash_boxes FROM app_rw';

    -- ---- cash_box_transactions: pul yozuvi, FAQAT QO'SHILADI ----
    EXECUTE 'REVOKE UPDATE, DELETE, TRUNCATE ON public.cash_box_transactions FROM app_rw';
END
$guards$;
