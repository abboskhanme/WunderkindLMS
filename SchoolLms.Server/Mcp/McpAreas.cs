namespace SchoolLms.Server.Mcp;

/// <summary>
/// What a tool reads, expressed the way the web panel grants it: menu PAGES (keys from
/// schoollms.client/src/config/navigation.ts) for roles that use page grants, and server
/// SECTION keys for legacy roles. Reading any one of the pages (or sections) is enough.
/// </summary>
public sealed record McpArea(string Title, string[] Pages, string[] Sections);

/// <summary>
/// The area of every tool. Page keys MUST match navigation.ts (`to`) — a page that is renamed
/// there and not here would silently deny staff with page grants (never widen access).
/// </summary>
public static class McpAreas
{
    public static readonly McpArea Dashboard = new("Bosh sahifa", ["/admin"], ["dashboard"]);

    public static readonly McpArea Students = new("O'quvchilar",
        ["/admin/students", "/admin/students/arxiv"], ["students"]);

    public static readonly McpArea Classes = new("Sinflar / Guruhlar",
        ["/admin/classes", "/admin/groups", "/admin/students"], ["classes", "students"]);

    public static readonly McpArea Groups = new("Guruhlar", ["/admin/groups", "/admin/classes"], ["classes"]);

    public static readonly McpArea ClassPerformance = new("Sinflar reytingi / Analitika",
        ["/admin/classes/rating", "/admin/grades-report/class", "/admin/grades-report/school"],
        ["classes", "gradesReport"]);

    public static readonly McpArea Schedule = new("Dars jadvali",
        ["/admin/schedule", "/admin/schedule/teachers"], ["schedule"]);

    public static readonly McpArea AttendanceDaily = new("Kunlik davomat",
        ["/admin/attendance", "/admin/attendance/mark"], ["attendance"]);

    public static readonly McpArea AttendanceAnalytics = new("Davomat analitikasi",
        ["/admin/attendance/analytics"], ["attendance"]);

    public static readonly McpArea BoardingEvening = new("Kechki dars",
        ["/admin/attendance/boarding?session=evening"], ["attendanceEvening"]);

    public static readonly McpArea BoardingDorm = new("Yotoqxona",
        ["/admin/attendance/boarding?session=dorm"], ["attendanceDorm"]);

    public static readonly McpArea Journal = new("Jurnal", ["/admin/journal"], ["journal"]);

    public static readonly McpArea SeasonalMarks = new("Mavsumiy baholash",
        ["/admin/seasonal-marks", "/admin/seasonal-marks/by-subjects", "/admin/seasonal-marks/report"], ["seasonalMarks"]);

    public static readonly McpArea Exams = new("Imtihonlar (blok test)",
        ["/admin/exams/results", "/admin/exams/list"], ["exams"]);

    public static readonly McpArea Admission = new("Qabul nomzodlari",
        ["/admin/admission/candidates"], ["admission"]);

    public static readonly McpArea Debtors = new("Qarzdorlar bilan ishlash", ["/admin/finance/debtors"], ["finance"]);

    public static readonly McpArea Arrears = new("Qarzdorlik oyma-oy", ["/admin/finance/arrears"], ["finance"]);

    public static readonly McpArea Transactions = new("Tranzaksiyalar", ["/admin/finance/transactions"], ["finance"]);

    public static readonly McpArea Invoices = new("Abonement tranzaksiyalari", ["/admin/billing/invoices"], ["finance"]);

    public static readonly McpArea FinanceReports = new("Moliya hisobotlari / P&L / Pul oqimi",
        ["/admin/finance/pnl", "/admin/finance/pnl-2", "/admin/finance/reports", "/admin/finance/cashflow"], ["finance"]);

    public static readonly McpArea Salary = new("Ish haqi", ["/admin/teachers/salary"], ["finance"]);

    public static readonly McpArea Discounts = new("Moliya hisobotlari (chegirmalar)",
        ["/admin/finance/reports", "/admin/billing/invoices"], ["finance"]);

    public static readonly McpArea Expenses = new("Tranzaksiyalar / Moliya hisobotlari (chiqimlar)",
        ["/admin/finance/transactions", "/admin/finance/reports", "/admin/finance/pnl"], ["finance"]);

    public static readonly McpArea Employees = new("Xodimlar", ["/admin/boshqaruv/staff"], ["staff", "teachers"]);

    public static readonly McpArea Leads = new("Lidlar", ["/admin/leads"], ["leads"]);

    public static readonly McpArea Marketing = new("Sotuv va marketing", ["/admin/marketing/arizalar"], ["marketing"]);

    public static readonly McpArea Discipline = new("Xulq-atvor",
        ["/admin/discipline", "/admin/discipline/incidents"], ["discipline"]);

    public static readonly McpArea Messages = new("Xabarlar", ["/admin/messages"], ["messages"]);

    public static readonly McpArea Certificates = new("Sertifikatlar", ["/admin/certificates"], ["students"]);

    public static readonly McpArea Contracts = new("Shartnomalar", ["/admin/contracts"], ["contracts"]);
}
