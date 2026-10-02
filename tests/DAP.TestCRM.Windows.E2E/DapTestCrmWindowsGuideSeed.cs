using DAP.Core.Guides;
using DAP.Core.Targets;

namespace DAP.TestCRM.Windows.E2E;

internal static class DapTestCrmWindowsGuideSeed
{
    public const string GuideId = "testcrm-windows-canonical-workflow";
    public const string GuideName = "TestCRM Windows Canonical Workflow";

    public static IReadOnlyList<GuideStep> CreateFirstTwoSteps() => new GuideStep[]
    {
        new(
            "testcrm-windows-customer-name",
            1,
            TargetDescriptor.Create(
                TargetRuntime.Windows,
                new Locator("automation-id", "CustomerNameSearch")),
            new BubbleDefinition(
                "חפש את הלקוח: אלפא פתרונות בע\"מ",
                BubblePlacement.Bottom),
            new ValidationDefinition(
                "value-equals",
                "אלפא פתרונות בע\"מ"),
            StepAdvanceMode.AutomaticOnValidation),

        new(
            "testcrm-windows-customer-search-button",
            2,
            TargetDescriptor.Create(
                TargetRuntime.Windows,
                new Locator("automation-id", "SearchCustomersButton")),
            new BubbleDefinition(
                "לחץ על חיפוש",
                BubblePlacement.Bottom),
            new ValidationDefinition("clicked"),
            StepAdvanceMode.AutomaticOnValidation)
    };
}
