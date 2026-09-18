-- ===========================================================================
--  Tranzaksiya turi katalogi — boshlang'ich ma'lumot
--  Migratsiya: TransactionTypes
--  Manba: mijoz yuborgan EduSchool kassa kirim shakli (dropdown: Do'ppi
--  uchun, Kitob uchun to'lov, Mock uchun to'lov, O'quvchi oylik to'lovi,
--  Transport 5-10sinflar, Transport hizmati). Chiqim tomoni uchun mijoz
--  namuna bermagan — kassadan chiqadigan kichik, kundalik xarajatlar
--  (rasmiy `expenses`/`Accounts.ExpenseCategories` bilan bog'liq EMAS,
--  faqat kassa yorlig'i) asosida tanlangan, admin xohlagancha qo'shadi/
--  o'zgartiradi/o'chiradi.
--
--  BU FAYLNI TAHRIRLAMANG — `billing_seed.sql` boshidagi izohga qarang.
--
--  IDEMPOTENT: `ON CONFLICT (id) DO NOTHING`.
--
--  ID'lar BARQAROR (qo'lda yozilgan UUID, `billing_seed.sql` naqshi):
--  Kirim — ...a1..a6, Chiqim — ...e1..e6.
--
--  `is_seeded = true` — `TransactionTypeService.DeleteAsync` bu qatorlarni
--  o'chirishga yo'l qo'ymaydi (faqat nomini o'zgartirish mumkin) — mijoz
--  skrinshotidagi xatti-harakat ("bitta seed qatorda o'chirish yo'q, faqat
--  tahrirlash").
-- ===========================================================================

INSERT INTO transaction_types (id, kind, name, is_active, is_seeded, position) VALUES
    ('00000000-0000-0000-0000-0000000000a1', 'in', 'Do''ppi uchun',              true, true, 0),
    ('00000000-0000-0000-0000-0000000000a2', 'in', 'Kitob uchun to''lov',        true, true, 1),
    ('00000000-0000-0000-0000-0000000000a3', 'in', 'Mock uchun to''lov',         true, true, 2),
    ('00000000-0000-0000-0000-0000000000a4', 'in', 'O''quvchi oylik to''lovi',   true, true, 3),
    ('00000000-0000-0000-0000-0000000000a5', 'in', 'Transport 5-10sinflar',      true, true, 4),
    ('00000000-0000-0000-0000-0000000000a6', 'in', 'Transport hizmati',          true, true, 5),
    ('00000000-0000-0000-0000-0000000000e1', 'out', 'Kanselyariya xarajati',     true, true, 0),
    ('00000000-0000-0000-0000-0000000000e2', 'out', 'Kuryer xizmati',            true, true, 1),
    ('00000000-0000-0000-0000-0000000000e3', 'out', 'Mayda ta''mirlash',         true, true, 2),
    ('00000000-0000-0000-0000-0000000000e4', 'out', 'Mehmon kutish xarajati',    true, true, 3),
    ('00000000-0000-0000-0000-0000000000e5', 'out', 'Nazorat/hujjat xarajati',   true, true, 4),
    ('00000000-0000-0000-0000-0000000000e6', 'out', 'Boshqa chiqim',             true, true, 5)
ON CONFLICT (id) DO NOTHING;
