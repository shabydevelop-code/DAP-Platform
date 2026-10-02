using DAP.Core.Guides;
using DAP.Core.Targets;

namespace DAP.TestCRM.Windows.E2E;

internal static class DapTestCrmWindowsGuideSeed
{
    public const string GuideId = "testcrm-windows-canonical-workflow";
    public const string GuideName = "TestCRM Windows Canonical Workflow";

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
        string instruction,
        string? descendantName = null,
        bool descendantNameIsRegex = false,
        string? columnHeaderName = null) =>
        new(
            id,
            order,
            GridRow(gridAutomationId, descendantName, descendantNameIsRegex, columnHeaderName),
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
            "SitesGrid", "פתח את האתר מטה תל אביב",
            "מטה תל אביב"),

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
            ById("SaveCaseButton"), "שמור את הפנייה החדשה"),

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
                @"^פניה\s+(\d+)$")),

        NavigationRowStep(
            "testcrm-windows-open-created-case", 12,
            "CasesGrid", "פתח את הפנייה שיצרת",
            @"^{{step:testcrm-windows-back-to-cases:capture}}$",
            descendantNameIsRegex: true,
            columnHeaderName: "מזהה")
    };
}
