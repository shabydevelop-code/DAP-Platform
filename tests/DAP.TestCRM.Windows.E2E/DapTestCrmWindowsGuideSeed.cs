using DAP.Core.Guides;
using DAP.Core.Targets;

namespace DAP.TestCRM.Windows.E2E;

internal static class DapTestCrmWindowsGuideSeed
{
    public const string GuideId = "testcrm-windows-canonical-workflow";
    public const string GuideName = "TestCRM Windows Canonical Workflow";

    private static TargetDescriptor ById(string automationId) =>
        TargetDescriptor.Create(TargetRuntime.Windows, new Locator("automation-id", automationId));

    private static TargetDescriptor FirstGridRow(string gridAutomationId) =>
        TargetDescriptor.Create(
            TargetRuntime.Windows,
            new Locator("control-type", "dataitem"),
            new[]
            {
                new Anchor(
                    new Locator("automation-id", gridAutomationId),
                    AnchorRelation.Ancestor)
            });

    private static GuideStep ClickStep(
        string id,
        int order,
        TargetDescriptor target,
        string instruction,
        BubblePlacement placement = BubblePlacement.Bottom) =>
        new(
            id,
            order,
            target,
            new BubbleDefinition(instruction, placement),
            new ValidationDefinition("clicked"),
            StepAdvanceMode.AutomaticOnValidation);

    private static GuideStep NavigationRowStep(
        string id,
        int order,
        string gridAutomationId,
        string instruction) =>
        new(
            id,
            order,
            FirstGridRow(gridAutomationId),
            new BubbleDefinition(instruction, BubblePlacement.Bottom),
            new ValidationDefinition("target-disappeared"),
            StepAdvanceMode.AutomaticOnValidation);

    private static GuideStep ValueStep(
        string id,
        int order,
        string automationId,
        string instruction,
        string validationKind = "value-not-empty",
        string? expectedValue = null) =>
        new(
            id,
            order,
            ById(automationId),
            new BubbleDefinition(instruction, BubblePlacement.Bottom),
            new ValidationDefinition(validationKind, expectedValue),
            StepAdvanceMode.AutomaticOnValidation);

    public static IReadOnlyList<GuideStep> CreateSteps() => new GuideStep[]
    {
        ValueStep(
            "testcrm-windows-customer-name", 1,
            "CustomerNameSearch", "חפש את הלקוח: אלפא פתרונות בע\"מ",
            "value-equals", "אלפא פתרונות בע\"מ"),

        ClickStep(
            "testcrm-windows-customer-search-button", 2,
            ById("SearchCustomersButton"), "לחץ על חיפוש"),

        NavigationRowStep(
            "testcrm-windows-customer-result", 3,
            "CustomersGrid", "פתח את הלקוח מתוצאות החיפוש"),

        NavigationRowStep(
            "testcrm-windows-site-row", 4,
            "SitesGrid", "פתח את האתר הראשון של הלקוח"),

        ClickStep(
            "testcrm-windows-cases-tab", 5,
            ById("CasesTab"), "עבור ללשונית פניות"),

        ClickStep(
            "testcrm-windows-sort-cases", 6,
            ById("SortCasesByStatusButton"), "מיין את הפניות לפי סטטוס"),

        ClickStep(
            "testcrm-windows-new-case", 7,
            ById("NewCaseButton"), "צור פנייה חדשה"),

        ValueStep(
            "testcrm-windows-case-subject", 8,
            "CaseSubject", "הקלד את נושא הפנייה"),

        ValueStep(
            "testcrm-windows-case-description", 9,
            "CaseDescription", "תאר את הפנייה"),

        ClickStep(
            "testcrm-windows-save-new-case", 10,
            ById("SaveCaseButton"), "שמור את הפנייה החדשה")
    };
}
