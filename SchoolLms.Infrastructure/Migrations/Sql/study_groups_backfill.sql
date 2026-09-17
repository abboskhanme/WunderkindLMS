-- ===========================================================================
--  `class_memberships` ni `students.class_name` dan to'ldirish (backfill)
--  Migratsiya: StudyGroupsAndMemberships
--  Manba: docs/modules/students-parity.md §3.1 (5-band)
-- ===========================================================================
--
--  BU FAYLNI TAHRIRLAMANG.
--  Migratsiya bir marta qo'llanadi va shundan keyin muzlaydi. O'zgartirish
--  kerak bo'lsa — YANGI migratsiya va yangi .sql fayl.
--  (Qoida `billing_guards.sql` dan aynan olingan — P1-05.)
--
--  NIMA QILADI
--  -----------
--  Har bir o'quvchiga sinf a'zoligining BITTA qatorini yozadi:
--    · arxivlanmagan o'quvchi  → OCHIQ qator (`left_on is null`);
--    · arxivlangan o'quvchi    → YOPIQ qator (`left_on` = arxiv sanasi).
--  Sinfsiz (`class_name` bo'sh) yoki sinfi topilmagan o'quvchiga qator
--  YOZILMAYDI — §3.1 buni ataylab talab qiladi: "taxmin qilinmaydi,
--  MA'LUM QILINADI". Pastdagi birinchi DO bloki ularni NOTICE bilan sanab
--  chiqadi.
--
--  SANALAR ISO SATR — EHTIYOTKORONA O'QILADI
--  -----------------------------------------
--  `students.enrollment_date` va `students.archived_at` — `text` ustunlar
--  (eski model). Ularda bo'sh satr ham, "2025-9-1" ham, "31.12.2024" ham
--  uchrashi mumkin (import, qo'lda tahrir). Shuning uchun:
--    1) regexp bilan `YYYY-MM-DD` shakli tekshiriladi (yil 1900-2099);
--    2) kun oyning uzunligidan oshmasligi ALOHIDA tekshiriladi — aks holda
--       '2025-02-30'::date "date/time field value out of range" bilan BUTUN
--       MIGRATSIYANI yiqitardi;
--    3) o'qib bo'lmasa — bugungi sana (maktab mintaqasi, AppClock.Today bilan
--       bir xil: Asia/Tashkent).
--  `CASE` lar ICHMA-ICH — PostgreSQL faqat shunda shartlarni ketma-ket
--  bajarishni kafolatlaydi (`a AND b` da tartib kafolatlanmaydi).
--
--  `joined_on <= left_on` — KAFOLAT, TAXMIN EMAS
--  ---------------------------------------------
--  Arxivlangan o'quvchining arxiv sanasi qabul sanasidan OLDIN bo'lishi
--  (buzuq ma'lumot) mumkin. Bunday holatda `joined_on` = `left_on` qilinadi,
--  aks holda `ck_class_memberships_period` migratsiyani yiqitardi.
--
--  BIR XIL NOMLI SINFLAR
--  ---------------------
--  `classes.name` UNIKAL EMAS (bazada ham, `ClassesController` da ham). Bir
--  xil nomli bir nechta sinf bo'lsa, o'quvchining holatiga MOS KELADIGANI
--  (arxivlangan o'quvchi — arxivlangan sinf) va keyin eng kichik `id`
--  tanlanadi. Tanlov DETERMINISTIK va NOTICE bilan xabar qilinadi.
--
--  QAYTA YURGIZILSA
--  ----------------
--  `where not exists (... shu o'quvchi uchun qator ...)` — ya'ni fayl
--  IDEMPOTENT: ikkinchi yurishda hech narsa qo'shilmaydi. LEKIN u DRIFT'ni
--  TUZATMAYDI: migratsiyadan keyin sinfi o'zgargan o'quvchining a'zoligi eski
--  qatorda qoladi, chunki uni yangilaydigan xizmat hali yo'q. Shu sababli
--  a'zolik xizmati (1-slice) ishga tushgunga qadar HAQIQAT MANBAI
--  `students.class_name` bo'lib qoladi va xizmat o'z deploy'ida
--  solishtirishni (reconcile) o'zi bajarishi kerak.
-- ===========================================================================


-- ---------------------------------------------------------------------------
-- 1) Oldindan hisobot: kim tushib qoladi va qayerda nom ikkilanadi
-- ---------------------------------------------------------------------------
DO $backfill_report$
DECLARE
    with_class   bigint;
    unmatched    bigint;
    ambiguous    bigint;
    sample_names text;
BEGIN
    SELECT count(*) INTO with_class
    FROM students s
    WHERE btrim(coalesce(s.class_name, '')) <> '';

    SELECT count(*), string_agg(DISTINCT s.class_name, ', ')
      INTO unmatched, sample_names
    FROM students s
    WHERE btrim(coalesce(s.class_name, '')) <> ''
      AND NOT EXISTS (SELECT 1 FROM classes c WHERE c.name = s.class_name);

    SELECT count(*) INTO ambiguous
    FROM students s
    WHERE btrim(coalesce(s.class_name, '')) <> ''
      AND (SELECT count(*) FROM classes c WHERE c.name = s.class_name) > 1;

    RAISE NOTICE '[class_memberships backfill] sinfi bor o''quvchi: %, sinfi topilmadi: %, nomi ikkilangan: %',
                 with_class, unmatched, ambiguous;

    IF unmatched > 0 THEN
        RAISE NOTICE '[class_memberships backfill] TOPILMAGAN SINF NOMLARI (ularga qator YOZILMAYDI): %',
                     sample_names;
    END IF;
END
$backfill_report$;


-- ---------------------------------------------------------------------------
-- 2) Qatorlarni yozish
-- ---------------------------------------------------------------------------
WITH raw AS (
    SELECT s.id           AS student_id,
           s.class_name   AS class_name,
           s.is_archived  AS is_archived,
           substr(btrim(coalesce(s.enrollment_date, '')), 1, 10) AS enroll_txt,
           substr(btrim(coalesce(s.archived_at, '')),     1, 10) AS archive_txt
    FROM students s
    WHERE btrim(coalesce(s.class_name, '')) <> ''
),
-- Ikkala sana ham BITTA ifoda bilan o'qiladi: `lateral values` ikki satr
-- beradi, `filter` ularni yana bitta qatorga yig'adi. Parser bir nusxada
-- turadi — ikki nusxa bir kun ikkiga ajrab ketardi.
parsed AS (
    SELECT r.student_id,
           r.class_name,
           r.is_archived,
           max(w.parsed) FILTER (WHERE w.which = 'enroll')  AS enrolled_on,
           max(w.parsed) FILTER (WHERE w.which = 'archive') AS archived_on
    FROM raw r
    CROSS JOIN LATERAL (
        SELECT v.which,
               CASE
                   WHEN v.txt ~ '^(19|20)[0-9]{2}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])$' THEN
                       CASE
                           WHEN substr(v.txt, 9, 2)::int <= extract(day FROM (
                                    make_date(substr(v.txt, 1, 4)::int, substr(v.txt, 6, 2)::int, 1)
                                    + interval '1 month' - interval '1 day'))
                           THEN v.txt::date
                       END
               END AS parsed
        FROM (VALUES ('enroll', r.enroll_txt), ('archive', r.archive_txt)) AS v(which, txt)
    ) w
    GROUP BY r.student_id, r.class_name, r.is_archived
),
matched AS (
    SELECT p.student_id,
           p.is_archived,
           p.enrolled_on,
           p.archived_on,
           cls.id AS class_id
    FROM parsed p
    JOIN LATERAL (
        SELECT c.id
        FROM classes c
        WHERE c.name = p.class_name
        ORDER BY (c.is_archived = p.is_archived) DESC, c.id
        LIMIT 1
    ) cls ON true
)
INSERT INTO class_memberships (id, student_id, class_id, joined_on, left_on, created_by, created_at)
SELECT gen_random_uuid(),
       m.student_id,
       m.class_id,
       -- Arxivlangan: qabul sanasi, lekin hech qachon chiqish sanasidan keyin emas.
       CASE WHEN m.is_archived
            THEN least(coalesce(m.enrolled_on, coalesce(m.archived_on, (now() AT TIME ZONE 'Asia/Tashkent')::date)),
                       coalesce(m.archived_on, (now() AT TIME ZONE 'Asia/Tashkent')::date))
            ELSE coalesce(m.enrolled_on, (now() AT TIME ZONE 'Asia/Tashkent')::date)
       END,
       -- Faol o'quvchida a'zolik OCHIQ qoladi.
       CASE WHEN m.is_archived
            THEN coalesce(m.archived_on, (now() AT TIME ZONE 'Asia/Tashkent')::date)
       END,
       -- `created_by` = null: bu qatorni odam emas, migratsiya yozdi.
       NULL,
       now()
FROM matched m
WHERE NOT EXISTS (
    SELECT 1 FROM class_memberships cm WHERE cm.student_id = m.student_id
);


-- ---------------------------------------------------------------------------
-- 3) Natija hisoboti
-- ---------------------------------------------------------------------------
DO $backfill_result$
DECLARE
    total  bigint;
    open_n bigint;
BEGIN
    SELECT count(*), count(*) FILTER (WHERE left_on IS NULL) INTO total, open_n
    FROM class_memberships;

    RAISE NOTICE '[class_memberships backfill] yozildi: % qator (faol: %, yopiq: %)',
                 total, open_n, total - open_n;
END
$backfill_result$;
