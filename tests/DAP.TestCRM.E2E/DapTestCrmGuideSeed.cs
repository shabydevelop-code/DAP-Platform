using DAP.Core.Guides;
using DAP.Core.Targets;

namespace DAP.TestCRM.E2E;

// Test-only fixture: the Guide itself uses only production DAP contracts.
// The E2E runner acts as a learner; DAP.exe owns presentation and advancement.
public static class DapTestCrmGuideSeed
{
    public const string GuideId = "testcrm-create-case";

    private static readonly FrameContext ContentFrame =
        new(new[] { new Locator("css", "#content-frame") });

    private static TargetDescriptor WebTarget(string css) =>
        TargetDescriptor.Create(
            TargetRuntime.Web,
            new Locator("css", css),
            frameContext: ContentFrame);

    private static GuideStep ClickStep(
        string id, int order, string css, string instruction, string contextCss,
        BubblePlacement placement = BubblePlacement.Bottom) =>
        new(
            id, order, WebTarget(css),
            new BubbleDefinition(instruction, placement),
            new ValidationDefinition("clicked"),
            StepAdvanceMode.AutomaticOnValidation,
            new StepContextDefinition("css-exists", contextCss));

    private static GuideStep ValueStep(
        string id, int order, string css, string instruction, string contextCss,
        BubblePlacement placement = BubblePlacement.Bottom) =>
        ValueStep(id, order, css, instruction, contextCss, "value-not-empty", null, placement);

    private static GuideStep ValueStep(
        string id, int order, string css, string instruction, string contextCss,
        string validationKind, string? expectedValue,
        BubblePlacement placement = BubblePlacement.Bottom) =>
        new(
            id, order, WebTarget(css),
            new BubbleDefinition(instruction, placement),
            new ValidationDefinition(validationKind, expectedValue),
            StepAdvanceMode.AutomaticOnValidation,
            new StepContextDefinition("css-exists", contextCss));

    public static IReadOnlyList<GuideStep> CreateSteps() => new GuideStep[]
    {
        ValueStep(
            "testcrm-customer-name", 1,
            "[name='name']", "הקלד את שם הלקוח", "#customer-search"),

        ClickStep(
            "testcrm-customer-search-button", 2,
            "#customer-search button.primary", "לחץ על חיפוש", "#customer-search"),

        ClickStep(
            "testcrm-customer-result", 3,
            "#search-results tbody tr.clickable:first-child",
            "פתח את הלקוח מתוצאות החיפוש", "#search-results"),

        ClickStep(
            "testcrm-site-row", 4,
            "tbody tr.clickable:first-child",
            "פתח את האתר הראשון של הלקוח", "tbody tr.clickable"),

        ClickStep(
            "testcrm-cases-tab", 5,
            "nav.tabs button:has-text('פניות')",
            "עבור ללשונית פניות", "nav.tabs"),

        ClickStep(
            "testcrm-sort-cases", 6,
            "th button[data-sort='status']",
            "מיין את הפניות לפי סטטוס", "h2:has-text('פניות')"),

        ClickStep(
            "testcrm-new-case", 7,
            "button.primary:has-text('פניה חדשה')",
            "צור פנייה חדשה", "h2:has-text('פניות')"),

        ValueStep(
            "testcrm-case-subject", 8,
            "[name='subject']", "הקלד את נושא הפנייה", "[name='subject']"),

        ValueStep(
            "testcrm-case-description", 9,
            "[name='description']", "תאר את הפנייה", "[name='description']"),

        ClickStep(
            "testcrm-save-new-case", 10,
            "button.primary:has-text('שמור')",
            "שמור את הפנייה החדשה", "[name='subject']"),

        ClickStep(
            "testcrm-back-to-cases", 11,
            ".breadcrumb a:nth-of-type(3)",
            "חזור לרשימת הפניות", "h1:has-text('פניה')"),

        ClickStep(
            "testcrm-open-created-case", 12,
            "button.grid-open[data-go='{{step:testcrm-back-to-cases:frame-url-fragment}}']",
            "פתח את הפנייה שיצרת", "h2:has-text('פניות')"),

        ValueStep(
            "testcrm-case-in-progress", 13,
            "[name='status']", "שנה את סטטוס הפנייה לבטיפול", "[name='resolutionNotes']",
            "value-equals", "בטיפול"),

        ValueStep(
            "testcrm-resolution-notes", 14,
            "[name='resolutionNotes']", "הוסף הערות טיפול", "[name='resolutionNotes']"),

        ClickStep(
            "testcrm-activity-more", 15,
            "#activity-more", "הצג פעילויות נוספות", "#activity-more"),

        ValueStep(
            "testcrm-case-closed", 16,
            "[name='status']", "שנה את סטטוס הפנייה לסגורה", "[name='status']",
            "value-equals", "סגורה"),

        ValueStep(
            "testcrm-case-subject-after-close", 17,
            "[name='subject']", "הזן מחדש את נושא הפנייה", "[name='subject']"),

        ClickStep(
            "testcrm-attempt-close-save", 18,
            "button.primary:has-text('שמור')",
            "נסה לשמור את הפנייה", "[name='closeReason']"),

        ClickStep(
            "testcrm-confirm-close-validation", 19,
            "#ps-alert button",
            "אשר את הודעת השגיאה", "#ps-alert"),

        ValueStep(
            "testcrm-close-reason", 20,
            "[name='closeReason']", "בחר סיבת סגירה", "[name='closeReason']",
            "value-equals", "טופל"),

        ClickStep("testcrm-save-closed-case", 21, "button.primary:has-text('שמור')", "שמור את הפנייה הסגורה", "[name='closeReason']"),
        ClickStep("testcrm-return-site", 22, ".breadcrumb a[data-go^='#/site/']", "חזור לאתר", "h1:has-text('פניה')"),
        ClickStep("testcrm-open-leads-tab", 23, "nav.tabs button:has-text('לידים')", "עבור ללשונית לידים", "nav.tabs"),
        ClickStep("testcrm-return-cases-tab", 24, "nav.tabs button:has-text('פניות')", "חזור ללשונית פניות", "nav.tabs"),
        ClickStep("testcrm-open-leads-again", 25, "nav.tabs button:has-text('לידים')", "עבור שוב ללשונית לידים", "nav.tabs"),
        ClickStep("testcrm-new-lead", 26, "button.primary:has-text('ליד חדש')", "צור ליד חדש", "h2:has-text('לידים')"),
        ValueStep("testcrm-lead-contact", 27, "[name='contactName']", "הזן את שם איש הקשר", "[name='contactName']"),
        ClickStep("testcrm-save-new-lead", 28, "button.primary:has-text('שמור')", "שמור את הליד החדש", "[name='contactName']"),
        ValueStep("testcrm-lead-close-success-1", 29, "[name='status']", "שנה את סטטוס הליד לנסגר בהצלחה", "[name='status']", "value-equals", "נסגר בהצלחה"),
        ValueStep("testcrm-lead-new", 30, "[name='status']", "החזר את סטטוס הליד לחדש", "[name='status']", "value-equals", "חדש"),
        ValueStep("testcrm-lead-close-success-2", 31, "[name='status']", "שנה שוב את סטטוס הליד לנסגר בהצלחה", "[name='status']", "value-equals", "נסגר בהצלחה"),
        ClickStep("testcrm-lead-invalid-save", 32, "button.primary:has-text('שמור')", "נסה לשמור את הליד", "[name='selectedService']"),
        ClickStep("testcrm-lead-validation-ok", 33, "#ps-alert button", "אשר את הודעת השגיאה", "#ps-alert"),
        ValueStep("testcrm-lead-service", 34, "[name='selectedService']", "בחר שירות", "[name='selectedService']", "value-equals", "תמיכה מורחבת"),
        ClickStep("testcrm-save-lead", 35, "button.primary:has-text('שמור')", "שמור את הליד", "[name='selectedService']"),
        ClickStep("testcrm-delete-lead", 36, "#delete-lead", "מחק את הליד", "#delete-lead"),
        ClickStep("testcrm-confirm-delete-lead", 37, "#ps-confirm [data-answer='yes']", "אשר את מחיקת הליד", "#ps-confirm"),
        ClickStep("testcrm-leads-to-customer", 38, ".breadcrumb a[data-go^='#/customer/']", "חזור ללקוח", "h2:has-text('לידים')"),
        ClickStep("testcrm-customer-site", 39, "tbody tr.clickable:first-child", "פתח את האתר הראשון", "h2:has-text('אתרים')"),
        ClickStep("testcrm-site-leads", 40, "nav.tabs button:has-text('לידים')", "עבור ללשונית לידים", "nav.tabs"),
        ClickStep("testcrm-open-lead", 41, "tbody tr.clickable:first-child", "פתח את הליד הראשון", "h2:has-text('לידים')"),
        ValueStep("testcrm-layout-status-new", 42, "[name='status']", "שנה את סטטוס הליד לחדש", "[name='status']", "value-equals", "חדש"),
        ValueStep("testcrm-layout-status-closed", 43, "[name='status']", "שנה את סטטוס הליד לנסגר בהצלחה", "[name='status']", "value-equals", "נסגר בהצלחה"),
        ValueStep("testcrm-race-status-new", 44, "[name='status']", "החזר את סטטוס הליד לחדש", "[name='status']", "value-equals", "חדש"),
        ValueStep("testcrm-race-status-closed", 45, "[name='status']", "שנה שוב את סטטוס הליד לנסגר בהצלחה", "[name='status']", "value-equals", "נסגר בהצלחה"),
        ClickStep("testcrm-lead-to-site", 46, ".breadcrumb a[data-go^='#/site/'][data-go$='/leads']", "חזור לאתר", "h1:has-text('ליד')"),
        ClickStep("testcrm-site-cases-final", 47, "nav.tabs button:has-text('פניות')", "עבור ללשונית פניות", "nav.tabs"),
        ClickStep("testcrm-open-context-case", 48, "tbody tr:first-child button.grid-open", "פתח את הפנייה הראשונה", "h2:has-text('פניות')"),
        ClickStep("testcrm-context-back-site", 49, ".breadcrumb a[data-go^='#/site/']", "חזור לאתר", "h1:has-text('פניה')"),
        ClickStep("testcrm-open-created-case-final", 50, "button.grid-open[data-go='{{step:testcrm-back-to-cases:frame-url-fragment}}']", "פתח את הפנייה שיצרת", "h2:has-text('פניות')"),
        ClickStep("testcrm-delete-case", 51, "#delete-case", "מחק את הפנייה", "#delete-case"),
        ClickStep("testcrm-confirm-delete-case", 52, "#ps-confirm [data-answer='yes']", "אשר את מחיקת הפנייה", "#ps-confirm"),

        new GuideStep(
            "testcrm-header-home", 53,
            TargetDescriptor.Create(TargetRuntime.Web, new Locator("css", "#portal-header"),
                frameContext: new FrameContext(new[] { new Locator("css", "iframe[name='dap-header']") })),
            new BubbleDefinition("חזור למסך חיפוש הלקוח", BubblePlacement.Bottom),
            new ValidationDefinition("clicked"),
            StepAdvanceMode.AutomaticOnValidation)
    };


}
