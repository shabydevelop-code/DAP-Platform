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
            "tbody tr:first-child button.grid-open",
            "פתח את הפנייה שיצרת", "h2:has-text('פניות')"),

        ValueStep(
            "testcrm-case-in-progress", 13,
            "[name='status']", "שנה את סטטוס הפנייה לבטיפול", "[name='resolutionNotes']",
            "value-equals", "בטיפול"),

        ValueStep(
            "testcrm-resolution-notes", 14,
            "[name='resolutionNotes']", "הוסף הערות טיפול", "[name='resolutionNotes']")
    };


}
