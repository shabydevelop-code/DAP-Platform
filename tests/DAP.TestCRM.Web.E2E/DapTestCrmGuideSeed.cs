using DAP.Core.Guides;
using DAP.Core.Targets;

namespace DAP.TestCRM.Web.E2E;

// Test-only fixture: the Guide itself uses only production DAP contracts.
// The E2E runner acts as a learner; DAP.exe owns presentation and advancement.
public static class DapTestCrmGuideSeed
{
    public const string LegacyGuideId = "testcrm-create-case";
    public const string GuideId = "testcrm-web-canonical-workflow";
    public const string GuideName = "TestCRM Web Canonical Workflow";

    private static readonly FrameContext ContentFrame =
        new(new[] { new Locator("css", "#content-frame") });

    private static TargetDescriptor WebTarget(string css) =>
        TargetDescriptor.Create(
            TargetRuntime.Web,
            new Locator("css", css),
            frameContext: ContentFrame);

    private static GuideStep ClickStep(
        string id, int order, string css, string instruction, string contextCss,
        BubblePlacement placement = BubblePlacement.Bottom,
        string? completionCss = null) =>
        new(
            id, order, WebTarget(css),
            new BubbleDefinition(instruction, placement),
            new ValidationDefinition("clicked"),
            StepAdvanceMode.AutomaticOnValidation,
            new StepContextDefinition("css-exists", contextCss),
            CompletionConditions: completionCss is null
                ? null
                : new[] { new StepCompletionCondition("target-exists", WebTarget(completionCss)) });

    private static GuideStep ValueStep(
        string id, int order, string css, string instruction, string contextCss,
        BubblePlacement placement = BubblePlacement.Bottom,
        StepCompletionCondition? completionCondition = null) =>
        ValueStep(id, order, css, instruction, contextCss, "value-not-empty", null, placement, completionCondition);

    private static GuideStep ValueStep(
        string id, int order, string css, string instruction, string contextCss,
        string validationKind, string? expectedValue,
        BubblePlacement placement = BubblePlacement.Bottom,
        StepCompletionCondition? completionCondition = null) =>
        new(
            id, order, WebTarget(css),
            new BubbleDefinition(instruction, placement),
            new ValidationDefinition(validationKind, expectedValue),
            StepAdvanceMode.AutomaticOnValidation,
            new StepContextDefinition("css-exists", contextCss),
            CompletionConditions: completionCondition is null
                ? null
                : new[] { completionCondition });

    public static IReadOnlyList<GuideStep> CreateSteps() => new GuideStep[]
    {
        ValueStep(
            "testcrm-customer-name", 1,
            "[name='name']", "חפש את הלקוח: אלפא פתרונות בע\"מ", "#customer-search",
            "value-equals", "אלפא פתרונות בע\"מ"),

        ClickStep(
            "testcrm-customer-search-button", 2,
            "#customer-search button.primary", "לחץ על חיפוש", "#customer-search", completionCss: "#search-results"),

        ClickStep(
            "testcrm-customer-result", 3,
            "#search-results tbody tr.clickable:first-child",
            "פתח את הלקוח מתוצאות החיפוש", "#search-results", completionCss: "h2:has-text('אתרים')"),

        ClickStep(
            "testcrm-site-row", 4,
            "tbody tr.clickable:has-text('מטה תל אביב')",
            "פתח את האתר מטה תל אביב", "tbody tr.clickable", completionCss: "nav.tabs"),

        ClickStep(
            "testcrm-cases-tab", 5,
            "nav.tabs button:has-text('פניות')",
            "עבור ללשונית פניות", "nav.tabs", completionCss: "h2:has-text('פניות')"),

        ClickStep(
            "testcrm-sort-cases", 6,
            "th button[data-sort='status']",
            "מיין את הפניות לפי סטטוס", "h2:has-text('פניות')"),

        ClickStep(
            "testcrm-new-case", 7,
            "button.primary:has-text('פניה חדשה')",
            "צור פנייה חדשה", "h2:has-text('פניות')", completionCss: "[name='subject']"),

        ValueStep(
            "testcrm-case-subject", 8,
            "[name='subject']", "הקלד את נושא הפנייה", "[name='subject']"),

        ValueStep(
            "testcrm-case-description", 9,
            "[name='description']", "תאר את הפנייה", "[name='description']"),

        ClickStep(
            "testcrm-save-new-case", 10,
            "button.primary:has-text('שמור')",
            "שמור את הפנייה החדשה", "[name='subject']", completionCss: "#delete-case"),

        new GuideStep(
            "testcrm-back-to-cases", 11,
            WebTarget(".breadcrumb a:nth-of-type(3)"),
            new BubbleDefinition("חזור לרשימת הפניות", BubblePlacement.Bottom),
            new ValidationDefinition("clicked"),
            StepAdvanceMode.AutomaticOnValidation,
            new StepContextDefinition("css-exists", "h1:has-text('פניה')"),
            new StepCaptureDefinition(
                TargetRuntime.Web,
                new Locator("css", "html"),
                "frame-url-fragment"),
            new[] { new StepCompletionCondition("target-exists", WebTarget("h2:has-text('פניות')")) }),

        ClickStep(
            "testcrm-open-created-case", 12,
            "button.grid-open[data-go='{{step:testcrm-back-to-cases:capture}}']",
            "פתח את הפנייה שיצרת", "h2:has-text('פניות')", completionCss: "[name='status']"),

        ValueStep(
            "testcrm-case-in-progress", 13,
            "[name='status']", "שנה את סטטוס הפנייה לבטיפול", "[name='resolutionNotes']",
            "value-equals", "בטיפול",
            completionCondition: new StepCompletionCondition("target-enabled", WebTarget("[name='resolutionNotes']"))),

        ValueStep(
            "testcrm-resolution-notes", 14,
            "[name='resolutionNotes']", "הוסף הערות טיפול", "[name='resolutionNotes']"),

        ClickStep(
            "testcrm-activity-more", 15,
            "#activity-more", "הצג פעילויות נוספות", "#activity-more"),

        ValueStep(
            "testcrm-case-closed", 16,
            "[name='status']", "שנה את סטטוס הפנייה לסגורה", "[name='status']",
            "value-equals", "סגורה",
            completionCondition: new StepCompletionCondition("target-exists", WebTarget("[name='closeReason']"))),

        ValueStep(
            "testcrm-case-subject-after-close", 17,
            "[name='subject']", "הזן מחדש את נושא הפנייה", "[name='subject']"),

        ClickStep(
            "testcrm-attempt-close-save", 18,
            "button.primary:has-text('שמור')",
            "נסה לשמור את הפנייה", "[name='closeReason']", completionCss: "#ps-alert"),

        ClickStep(
            "testcrm-confirm-close-validation", 19,
            "#ps-alert button",
            "אשר את הודעת השגיאה", "#ps-alert", completionCss: "[name='closeReason']"),

        ValueStep(
            "testcrm-close-reason", 20,
            "[name='closeReason']", "בחר בסיבת הסגירה \"טופל\"", "[name='closeReason']",
            "value-equals", "טופל"),

        ClickStep("testcrm-save-closed-case", 21, "button.primary:has-text('שמור')", "שמור את הפנייה הסגורה", "[name='closeReason']", completionCss: "#delete-case"),
        ClickStep("testcrm-return-site", 22, ".breadcrumb a[data-go^='#/site/']", "חזור לאתר", "h1:has-text('פניה')", completionCss: "nav.tabs"),
        ClickStep("testcrm-open-leads-tab", 23, "nav.tabs button:has-text('לידים')", "עבור ללשונית לידים", "nav.tabs", completionCss: "h2:has-text('לידים')"),
        ClickStep("testcrm-return-cases-tab", 24, "nav.tabs button:has-text('פניות')", "חזור ללשונית פניות", "nav.tabs", completionCss: "h2:has-text('פניות')"),
        ClickStep("testcrm-open-leads-again", 25, "nav.tabs button:has-text('לידים')", "עבור שוב ללשונית לידים", "nav.tabs", completionCss: "h2:has-text('לידים')"),
        ClickStep("testcrm-new-lead", 26, "button.primary:has-text('ליד חדש')", "צור ליד חדש", "h2:has-text('לידים')", completionCss: "[name='contactName']"),
        ValueStep("testcrm-lead-contact", 27, "[name='contactName']", "הזן את שם איש הקשר", "[name='contactName']"),
        ClickStep("testcrm-save-new-lead", 28, "button.primary:has-text('שמור')", "שמור את הליד החדש", "[name='contactName']", completionCss: "#delete-lead"),
        ValueStep("testcrm-lead-close-success-1", 29, "[name='status']", "שנה את סטטוס הליד לנסגר בהצלחה", "[name='status']", "value-equals", "נסגר בהצלחה", completionCondition: new StepCompletionCondition("target-exists", WebTarget("[name='selectedService']"))),
        ValueStep("testcrm-lead-new", 30, "[name='status']", "החזר את סטטוס הליד לחדש", "[name='status']", "value-equals", "חדש", completionCondition: new StepCompletionCondition("target-not-exists", WebTarget("[name='selectedService']"))),
        ValueStep("testcrm-lead-close-success-2", 31, "[name='status']", "שנה שוב את סטטוס הליד לנסגר בהצלחה", "[name='status']", "value-equals", "נסגר בהצלחה", completionCondition: new StepCompletionCondition("target-exists", WebTarget("[name='selectedService']"))),
        ClickStep("testcrm-lead-invalid-save", 32, "button.primary:has-text('שמור')", "נסה לשמור את הליד", "[name='selectedService']", completionCss: "#ps-alert"),
        ClickStep("testcrm-lead-validation-ok", 33, "#ps-alert button", "אשר את הודעת השגיאה", "#ps-alert", completionCss: "[name='selectedService']"),
        ValueStep("testcrm-lead-service", 34, "[name='selectedService']", "בחר בשירות \"תמיכה מורחבת\"", "[name='selectedService']", "value-equals", "תמיכה מורחבת"),
        ClickStep("testcrm-save-lead", 35, "button.primary:has-text('שמור')", "שמור את הליד", "[name='selectedService']", completionCss: "#delete-lead"),
        ClickStep("testcrm-delete-lead", 36, "#delete-lead", "מחק את הליד", "#delete-lead", completionCss: "#ps-confirm"),
        ClickStep("testcrm-confirm-delete-lead", 37, "#ps-confirm [data-answer='yes']", "אשר את מחיקת הליד", "#ps-confirm", completionCss: "h2:has-text('לידים')"),
        ClickStep("testcrm-leads-to-customer", 38, ".breadcrumb a[data-go^='#/customer/']", "חזור ללקוח", "h2:has-text('לידים')", completionCss: "h2:has-text('אתרים')"),
        ClickStep("testcrm-customer-site", 39, "tbody tr.clickable:has-text('מטה תל אביב')", "פתח את האתר מטה תל אביב", "h2:has-text('אתרים')", completionCss: "nav.tabs"),
        ClickStep("testcrm-site-leads", 40, "nav.tabs button:has-text('לידים')", "עבור ללשונית לידים", "nav.tabs", completionCss: "h2:has-text('לידים')"),
        ClickStep("testcrm-open-lead", 41, "tbody tr.clickable:has-text('אבי כהן')", "פתח את הליד של אבי כהן", "h2:has-text('לידים')", completionCss: "#delete-lead"),
        ValueStep("testcrm-layout-status-new", 42, "[name='status']", "שנה את סטטוס הליד לחדש", "[name='status']", "value-equals", "חדש", completionCondition: new StepCompletionCondition("target-not-exists", WebTarget("[name='selectedService']"))),
        ValueStep("testcrm-layout-status-closed", 43, "[name='status']", "שנה את סטטוס הליד לנסגר בהצלחה", "[name='status']", "value-equals", "נסגר בהצלחה", completionCondition: new StepCompletionCondition("target-exists", WebTarget("[name='selectedService']"))),
        ValueStep("testcrm-race-status-new", 44, "[name='status']", "החזר את סטטוס הליד לחדש", "[name='status']", "value-equals", "חדש", completionCondition: new StepCompletionCondition("target-not-exists", WebTarget("[name='selectedService']"))),
        ValueStep("testcrm-race-status-closed", 45, "[name='status']", "שנה שוב את סטטוס הליד לנסגר בהצלחה", "[name='status']", "value-equals", "נסגר בהצלחה", completionCondition: new StepCompletionCondition("target-exists", WebTarget("[name='selectedService']"))),
        ClickStep("testcrm-lead-to-site", 46, ".breadcrumb a[data-go^='#/site/'][data-go$='/leads']", "חזור לאתר", "h1:has-text('ליד')", completionCss: "nav.tabs"),
        ClickStep("testcrm-site-cases-final", 47, "nav.tabs button:has-text('פניות')", "עבור ללשונית פניות", "nav.tabs", completionCss: "h2:has-text('פניות')"),
        ClickStep("testcrm-open-context-case", 48, "button.grid-open[data-go='{{step:testcrm-back-to-cases:capture}}']", "פתח שוב את הפנייה שיצרת", "h2:has-text('פניות')", completionCss: "#delete-case"),
        ClickStep("testcrm-context-back-site", 49, ".breadcrumb a[data-go^='#/site/']", "חזור לאתר", "h1:has-text('פניה')", completionCss: "h2:has-text('פניות')"),
        ClickStep("testcrm-open-created-case-final", 50, "button.grid-open[data-go='{{step:testcrm-back-to-cases:capture}}']", "פתח את הפנייה שיצרת", "h2:has-text('פניות')", completionCss: "#delete-case"),

        new GuideStep(
            "testcrm-before-delete-case-info", 51,
            Target: null,
            new BubbleDefinition(
                "שים לב: בשלב הבא נמחק את הפנייה שיצרת במהלך הלומדה.",
                BubblePlacement.Center),
            Validation: null,
            AdvanceMode: StepAdvanceMode.Manual),

        ClickStep("testcrm-delete-case", 52, "#delete-case", "מחק את הפנייה", "#delete-case", completionCss: "#ps-confirm"),
        ClickStep("testcrm-confirm-delete-case", 53, "#ps-confirm [data-answer='yes']", "אשר את מחיקת הפנייה", "#ps-confirm", completionCss: "h2:has-text('פניות')"),

        new GuideStep(
            "testcrm-header-home", 54,
            TargetDescriptor.Create(TargetRuntime.Web, new Locator("css", "#portal-header"),
                frameContext: new FrameContext(new[] { new Locator("css", "iframe[name='dap-header']") })),
            new BubbleDefinition("חזור למסך חיפוש הלקוח", BubblePlacement.Bottom),
            new ValidationDefinition("clicked"),
            StepAdvanceMode.AutomaticOnValidation,
            CompletionConditions: new[]
            {
                new StepCompletionCondition("target-exists", WebTarget("#customer-search"))
            })
    };


}
