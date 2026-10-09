using DAP.Core.Guides;
using DAP.Core.Targets;

namespace DAP.TestCRM.Windows.E2E;

internal static class DapTestCrmWindowsGuideSeed
{
    public const string GuideId = "testcrm-windows-canonical-workflow";
    public const string GuideName = "TestCRM Windows Canonical Workflow";
    public const string ApplicationContextKey = "crm-windows";

    public static IReadOnlyList<GuideApplicationContext> CreateApplicationContexts() =>
        new[] { new GuideApplicationContext(ApplicationContextKey, TargetRuntime.Windows,
            new[] { new ApplicationContextMatcher(1, "AutomationId", "TestCrmMainWindow") }) };

    private static TargetDescriptor ById(string automationId) =>
        TargetDescriptor.Create(TargetRuntime.Windows, new Locator("automation-id", automationId));

    private static TargetDescriptor ByIdAndName(string automationId, string name) =>
        TargetDescriptor.Create(
            TargetRuntime.Windows,
            new Locator("automation-id", automationId),
            new[]
            {
                new Anchor(new Locator("name", name), AnchorRelation.Self)
            });

    private static TargetDescriptor GridRow(
        string gridAutomationId,
        string? descendantName = null,
        bool descendantNameIsRegex = false,
        string? columnHeaderName = null)
    {
        var anchors = new List<Anchor>
        {
            new(
                new Locator("automation-id", gridAutomationId),
                AnchorRelation.Ancestor)
        };

        if (!string.IsNullOrWhiteSpace(descendantName))
        {
            anchors.Add(
                new Anchor(
                    new Locator(descendantNameIsRegex ? "name-regex" : "name", descendantName),
                    AnchorRelation.Descendant));
        }

        if (!string.IsNullOrWhiteSpace(columnHeaderName))
        {
            anchors.Add(
                new Anchor(
                    new Locator("name", columnHeaderName),
                    AnchorRelation.ColumnHeader));
        }

        return TargetDescriptor.Create(
            TargetRuntime.Windows,
            new Locator("control-type", "dataitem"),
            anchors);
    }

    private static GuideStep ClickStep(
        string id,
        int order,
        TargetDescriptor target,
        string instruction,
        BubblePlacement placement = BubblePlacement.Bottom,
        StepCompletionCondition? completionCondition = null,
        bool isEnabled = true) =>
        new(
            id,
            order,
            target,
            new BubbleDefinition(instruction, placement),
            new ValidationDefinition("clicked"),
            StepAdvanceMode.AutomaticOnValidation,
            CompletionConditions: completionCondition is null
                ? null
                : new[] { completionCondition },
            IsEnabled: isEnabled);

    private static GuideStep NavigationRowStep(
        string id,
        int order,
        string gridAutomationId,
        string instruction,
        string? descendantName = null,
        bool descendantNameIsRegex = false,
        string? columnHeaderName = null,
        StepCompletionCondition? completionCondition = null,
        bool isEnabled = true) =>
        new(
            id,
            order,
            GridRow(gridAutomationId, descendantName, descendantNameIsRegex, columnHeaderName),
            new BubbleDefinition(instruction, BubblePlacement.Bottom),
            new ValidationDefinition("target-disappeared"),
            StepAdvanceMode.AutomaticOnValidation,
            CompletionConditions: completionCondition is null
                ? null
                : new[] { completionCondition },
            IsEnabled: isEnabled);

    private static GuideStep ValueStep(
        string id,
        int order,
        string automationId,
        string instruction,
        string validationKind = "value-not-empty",
        string? expectedValue = null,
        StepCompletionCondition? completionCondition = null,
        string? automationValue = null,
        bool isEnabled = true) =>
        new(
            id,
            order,
            ById(automationId),
            new BubbleDefinition(instruction, BubblePlacement.Bottom),
            new ValidationDefinition(validationKind, expectedValue),
            StepAdvanceMode.AutomaticOnValidation,
            CompletionConditions: completionCondition is null
                ? null
                : new[] { completionCondition },
            IsEnabled: isEnabled,
            AutomationValue: automationValue);

    public static IReadOnlyList<GuideStep> CreateSteps() => new GuideStep[]
    {
        ValueStep(
            "testcrm-windows-customer-name", 1,
            "CustomerNameSearch", "חפש את הלקוח: אלפא פתרונות בע\"מ",
            "value-equals", "אלפא פתרונות בע\"מ", automationValue: "אלפא פתרונות בע\"מ"),

        ClickStep(
            "testcrm-windows-customer-search-button", 2,
            ById("SearchCustomersButton"), "לחץ על חיפוש", completionCondition: new StepCompletionCondition("target-exists", ById("CustomersGrid"))),

        NavigationRowStep(
            "testcrm-windows-customer-result", 3,
            "CustomersGrid", "פתח את הלקוח מתוצאות החיפוש", completionCondition: new StepCompletionCondition("target-exists", ById("SitesGrid"))),

        NavigationRowStep(
            "testcrm-windows-site-row", 4,
            "SitesGrid", "פתח את האתר מטה תל אביב",
            "מטה תל אביב",
            completionCondition: new StepCompletionCondition("target-exists", ById("CasesTab"))),

        ClickStep(
            "testcrm-windows-cases-tab", 5,
            ById("CasesTab"), "עבור ללשונית פניות", completionCondition: new StepCompletionCondition("target-exists", ById("NewCaseButton"))),

        ClickStep(
            "testcrm-windows-sort-cases", 6,
            ById("SortCasesByStatusButton"), "מיין את הפניות לפי סטטוס"),

        ClickStep(
            "testcrm-windows-new-case", 7,
            ById("NewCaseButton"), "צור פנייה חדשה", completionCondition: new StepCompletionCondition("target-exists", ById("CaseSubject"))),

        ValueStep(
            "testcrm-windows-case-subject", 8,
            "CaseSubject", "הקלד את נושא הפנייה", automationValue: "תקלה בחיבור לאינטרנט"),

        ValueStep(
            "testcrm-windows-case-description", 9,
            "CaseDescription", "תאר את הפנייה", automationValue: "הלקוח מדווח על חיבור לא יציב."),

        ClickStep(
            "testcrm-windows-save-new-case", 10,
            ById("SaveCaseButton"), "שמור את הפנייה החדשה", completionCondition: new StepCompletionCondition("target-exists", ById("DeleteCaseButton"))),

        new GuideStep(
            "testcrm-windows-back-to-cases", 11,
            ByIdAndName("Breadcrumb", "מטה תל אביב"),
            new BubbleDefinition("חזור לרשימת הפניות", BubblePlacement.Bottom),
            new ValidationDefinition("clicked"),
            StepAdvanceMode.AutomaticOnValidation,
            Capture: new StepCaptureDefinition(
                TargetRuntime.Windows,
                new Locator("name-regex", @"^פניה\s+\d+$"),
                "name",
                @"^פניה\s+(\d+)$"),
            CompletionConditions: new[]
            {
                new StepCompletionCondition("target-exists", ById("CasesGrid"))
            }),

        NavigationRowStep(
            "testcrm-windows-open-created-case", 12,
            "CasesGrid", "פתח את הפנייה שיצרת",
            @"^{{step:testcrm-windows-back-to-cases:capture}}$",
            descendantNameIsRegex: true,
            columnHeaderName: "מזהה",
            completionCondition: new StepCompletionCondition("target-exists", ById("DeleteCaseButton"))),

        ValueStep(
            "testcrm-windows-case-in-progress", 13,
            "CaseStatus", "שנה את סטטוס הפנייה לבטיפול",
            "value-equals", "בטיפול",
            completionCondition: new StepCompletionCondition("target-enabled", ById("CaseResolutionNotes")), automationValue: "בטיפול"),

        ValueStep(
            "testcrm-windows-resolution-notes", 14,
            "CaseResolutionNotes", "הוסף הערות טיפול", automationValue: "בוצעה בדיקת שירות מול הלקוח והתקלה טופלה."),

        ClickStep(
            "testcrm-windows-activity-more", 15,
            ById("ActivityMoreButton"), "הצג פעילויות נוספות"),

        ValueStep(
            "testcrm-windows-case-closed", 16,
            "CaseStatus", "שנה את סטטוס הפנייה לסגורה",
            "value-equals", "סגורה",
            completionCondition: new StepCompletionCondition("target-exists", ById("CaseCloseReason")), automationValue: "סגורה"),

        ValueStep(
            "testcrm-windows-case-subject-after-close", 17,
            "CaseSubject", "הזן מחדש את נושא הפנייה", automationValue: "תקלה בחיבור לאינטרנט"),

        ClickStep(
            "testcrm-windows-attempt-close-save", 18,
            ById("SaveCaseButton"), "נסה לשמור את הפנייה",
            completionCondition: new StepCompletionCondition(
                "target-exists",
                TargetDescriptor.Create(
                    TargetRuntime.Windows,
                    new Locator("name-regex", @"^(OK|אישור)$")))),

        new GuideStep(
            "testcrm-windows-confirm-close-validation", 19,
            TargetDescriptor.Create(
                TargetRuntime.Windows,
                new Locator("name-regex", @"^(OK|אישור)$")),
            new BubbleDefinition("אשר את הודעת השגיאה", BubblePlacement.Bottom),
            new ValidationDefinition("target-disappeared"),
            StepAdvanceMode.AutomaticOnValidation,
            CompletionConditions: new[]
            {
                new StepCompletionCondition("target-exists", ById("CaseCloseReason"))
            }),

        ValueStep(
            "testcrm-windows-close-reason", 20,
            "CaseCloseReason", "בחר בסיבת הסגירה \"טופל\"",
            "value-equals", "טופל", automationValue: "טופל"),

        ClickStep(
            "testcrm-windows-save-closed-case", 21,
            ById("SaveCaseButton"), "שמור את הפנייה הסגורה", completionCondition: new StepCompletionCondition("target-replaced", ById("DeleteCaseButton"))),

        ClickStep(
            "testcrm-windows-return-site", 22,
            ByIdAndName("Breadcrumb", "מטה תל אביב"), "חזור לאתר", completionCondition: new StepCompletionCondition("target-exists", ById("LeadsTab"))),

        ClickStep(
            "testcrm-windows-open-leads-tab", 23,
            ById("LeadsTab"), "עבור ללשונית לידים", completionCondition: new StepCompletionCondition("target-exists", ById("NewLeadButton"))),

        ClickStep(
            "testcrm-windows-return-cases-tab", 24,
            ById("CasesTab"), "חזור ללשונית פניות", completionCondition: new StepCompletionCondition("target-exists", ById("NewCaseButton")), isEnabled: false),

        ClickStep(
            "testcrm-windows-open-leads-again", 25,
            ById("LeadsTab"), "עבור שוב ללשונית לידים", completionCondition: new StepCompletionCondition("target-exists", ById("NewLeadButton")), isEnabled: false),

        ClickStep(
            "testcrm-windows-new-lead", 26,
            ById("NewLeadButton"), "צור ליד חדש", completionCondition: new StepCompletionCondition("target-exists", ById("LeadContactName"))),

        ValueStep(
            "testcrm-windows-lead-contact", 27,
            "LeadContactName", "הזן את שם איש הקשר", automationValue: "לקוח בדיקת מערכת"),

        ClickStep(
            "testcrm-windows-save-new-lead", 28,
            ById("SaveLeadButton"), "שמור את הליד החדש", completionCondition: new StepCompletionCondition("target-exists", ById("DeleteLeadButton"))),

        ValueStep(
            "testcrm-windows-lead-close-success-1", 29,
            "LeadStatus", "שנה את סטטוס הליד לנסגר בהצלחה",
            "value-equals", "נסגר בהצלחה",
            completionCondition: new StepCompletionCondition("target-exists", ById("LeadSelectedService")), automationValue: "נסגר בהצלחה"),

        ValueStep(
            "testcrm-windows-lead-new", 30,
            "LeadStatus", "החזר את סטטוס הליד לחדש",
            "value-equals", "חדש",
            completionCondition: new StepCompletionCondition("target-not-exists", ById("LeadSelectedService")), automationValue: "חדש", isEnabled: false),

        ValueStep(
            "testcrm-windows-lead-close-success-2", 31,
            "LeadStatus", "שנה שוב את סטטוס הליד לנסגר בהצלחה",
            "value-equals", "נסגר בהצלחה",
            completionCondition: new StepCompletionCondition("target-exists", ById("LeadSelectedService")), automationValue: "נסגר בהצלחה", isEnabled: false),

        ClickStep(
            "testcrm-windows-lead-invalid-save", 32,
            ById("SaveLeadButton"), "נסה לשמור את הליד",
            completionCondition: new StepCompletionCondition(
                "target-exists",
                TargetDescriptor.Create(
                    TargetRuntime.Windows,
                    new Locator("name-regex", @"^(OK|אישור)$")))),

        new GuideStep(
            "testcrm-windows-lead-validation-ok", 33,
            TargetDescriptor.Create(
                TargetRuntime.Windows,
                new Locator("name-regex", @"^(OK|אישור)$")),
            new BubbleDefinition("אשר את הודעת השגיאה", BubblePlacement.Bottom),
            new ValidationDefinition("target-disappeared"),
            StepAdvanceMode.AutomaticOnValidation,
            CompletionConditions: new[]
            {
                new StepCompletionCondition("target-exists", ById("LeadSelectedService"))
            }),

        ValueStep(
            "testcrm-windows-lead-service", 34,
            "LeadSelectedService", "בחר בשירות \"תמיכה מורחבת\"",
            "value-equals", "תמיכה מורחבת", automationValue: "תמיכה מורחבת"),

        ClickStep(
            "testcrm-windows-save-lead", 35,
            ById("SaveLeadButton"), "שמור את הליד",
            completionCondition: new StepCompletionCondition("target-replaced", ById("DeleteLeadButton"))),

        ClickStep(
            "testcrm-windows-delete-lead", 36,
            ById("DeleteLeadButton"), "מחק את הליד",
            completionCondition: new StepCompletionCondition(
                "target-exists",
                TargetDescriptor.Create(
                    TargetRuntime.Windows,
                    new Locator("name-regex", @"^(Yes|כן|אישור)$")))),

        new GuideStep(
            "testcrm-windows-confirm-delete-lead", 37,
            TargetDescriptor.Create(
                TargetRuntime.Windows,
                new Locator("name-regex", @"^(Yes|כן|אישור)$")),
            new BubbleDefinition("אשר את מחיקת הליד", BubblePlacement.Bottom),
            new ValidationDefinition("target-disappeared"),
            StepAdvanceMode.AutomaticOnValidation,
            CompletionConditions: new[]
            {
                new StepCompletionCondition("target-exists", ById("NewLeadButton"))
            }),

        ClickStep(
            "testcrm-windows-leads-to-customer", 38,
            ByIdAndName("Breadcrumb", "אלפא פתרונות בע\"מ"), "חזור ללקוח",
            completionCondition: new StepCompletionCondition("target-exists", ById("SitesGrid"))),

        NavigationRowStep(
            "testcrm-windows-customer-site", 39,
            "SitesGrid", "פתח את האתר מטה תל אביב",
            "מטה תל אביב",
            completionCondition: new StepCompletionCondition("target-exists", ById("LeadsTab"))),

        ClickStep(
            "testcrm-windows-site-leads", 40,
            ById("LeadsTab"), "עבור ללשונית לידים",
            completionCondition: new StepCompletionCondition("target-exists", ById("NewLeadButton"))),

        NavigationRowStep(
            "testcrm-windows-open-lead", 41,
            "LeadsGrid", "פתח את הליד של אבי כהן",
            "אבי כהן",
            completionCondition: new StepCompletionCondition("target-exists", ById("DeleteLeadButton"))),

        ValueStep(
            "testcrm-windows-layout-status-new", 42,
            "LeadStatus", "שנה את סטטוס הליד לחדש",
            "value-equals", "חדש",
            completionCondition: new StepCompletionCondition("target-not-exists", ById("LeadSelectedService")), automationValue: "חדש", isEnabled: false),

        ValueStep(
            "testcrm-windows-layout-status-closed", 43,
            "LeadStatus", "שנה את סטטוס הליד לנסגר בהצלחה",
            "value-equals", "נסגר בהצלחה",
            completionCondition: new StepCompletionCondition("target-exists", ById("LeadSelectedService")), automationValue: "נסגר בהצלחה", isEnabled: false),

        ValueStep(
            "testcrm-windows-race-status-new", 44,
            "LeadStatus", "החזר את סטטוס הליד לחדש",
            "value-equals", "חדש",
            completionCondition: new StepCompletionCondition("target-not-exists", ById("LeadSelectedService")), automationValue: "חדש", isEnabled: false),

        ValueStep(
            "testcrm-windows-race-status-closed", 45,
            "LeadStatus", "שנה שוב את סטטוס הליד לנסגר בהצלחה",
            "value-equals", "נסגר בהצלחה",
            completionCondition: new StepCompletionCondition("target-exists", ById("LeadSelectedService")), automationValue: "נסגר בהצלחה", isEnabled: false),

        ClickStep(
            "testcrm-windows-lead-to-site", 46,
            ByIdAndName("Breadcrumb", "מטה תל אביב"), "חזור לאתר",
            completionCondition: new StepCompletionCondition("target-exists", ById("CasesTab"))),

        ClickStep(
            "testcrm-windows-site-cases-final", 47,
            ById("CasesTab"), "עבור ללשונית פניות",
            completionCondition: new StepCompletionCondition("target-exists", ById("NewCaseButton"))),

        NavigationRowStep(
            "testcrm-windows-open-context-case", 48,
            "CasesGrid", "פתח שוב את הפנייה שיצרת",
            @"^{{step:testcrm-windows-back-to-cases:capture}}$",
            descendantNameIsRegex: true,
            columnHeaderName: "מזהה",
            completionCondition: new StepCompletionCondition("target-exists", ById("DeleteCaseButton"))),

        ClickStep(
            "testcrm-windows-context-back-site", 49,
            ByIdAndName("Breadcrumb", "מטה תל אביב"), "חזור לאתר",
            completionCondition: new StepCompletionCondition("target-exists", ById("CasesGrid"))),

        NavigationRowStep(
            "testcrm-windows-open-created-case-final", 50,
            "CasesGrid", "פתח את הפנייה שיצרת",
            @"^{{step:testcrm-windows-back-to-cases:capture}}$",
            descendantNameIsRegex: true,
            columnHeaderName: "מזהה",
            completionCondition: new StepCompletionCondition("target-exists", ById("DeleteCaseButton"))),

        new GuideStep(
            "testcrm-windows-before-delete-case-info", 51,
            Target: null,
            new BubbleDefinition(
                "שים לב: בשלב הבא נמחק את הפנייה שיצרת במהלך הלומדה.",
                BubblePlacement.Center),
            Validation: null,
            AdvanceMode: StepAdvanceMode.Manual),

        ClickStep(
            "testcrm-windows-delete-case", 52,
            ById("DeleteCaseButton"), "מחק את הפנייה",
            completionCondition: new StepCompletionCondition(
                "target-exists",
                TargetDescriptor.Create(
                    TargetRuntime.Windows,
                    new Locator("name-regex", @"^(Yes|כן|אישור)$")))),

        new GuideStep(
            "testcrm-windows-confirm-delete-case", 53,
            TargetDescriptor.Create(
                TargetRuntime.Windows,
                new Locator("name-regex", @"^(Yes|כן|אישור)$")),
            new BubbleDefinition("אשר את מחיקת הפנייה", BubblePlacement.Bottom),
            new ValidationDefinition("target-disappeared"),
            StepAdvanceMode.AutomaticOnValidation,
            CompletionConditions: new[]
            {
                new StepCompletionCondition("target-exists", ById("NewCaseButton"))
            }),

        ClickStep(
            "testcrm-windows-header-home", 54,
            ById("PortalHeader"), "חזור למסך חיפוש הלקוח",
            completionCondition: new StepCompletionCondition("target-exists", ById("CustomerNameSearch"))),

        new GuideStep(
            "testcrm-windows-guide-summary", 55,
            Target: null,
            new BubbleDefinition(
                "המדריך הושלם בהצלחה",
                BubblePlacement.Center),
            Validation: null,
            AdvanceMode: StepAdvanceMode.Manual)
    }.Select(step => step with { ApplicationContextKey = ApplicationContextKey }).ToArray();
}
