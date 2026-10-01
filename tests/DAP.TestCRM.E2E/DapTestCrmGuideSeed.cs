using DAP.Core.Guides;
using DAP.Core.Targets;

namespace DAP.TestCRM.E2E;

// Test-only fixture consumed by the top-level E2E runner.

public static class DapTestCrmGuideSeed
{
    public const string GuideId = "testcrm-customer-search";

    public static GuideStep CustomerNameStep => new(
        "testcrm-customer-name", 1,
        TargetDescriptor.Create(TargetRuntime.Web,
            new Locator("css", "[name='name']"),
            frameContext: new FrameContext(new[] { new Locator("css", "#content-frame") })),
        new BubbleDefinition("הקלד את שם הלקוח", BubblePlacement.Bottom),
        new ValidationDefinition("value-not-empty"),
        StepAdvanceMode.AutomaticOnValidation,
        new StepContextDefinition("css-exists", "#customer-search"));

    public static GuideStep CustomerSearchButtonStep => new(
        "testcrm-customer-search-button", 2,
        TargetDescriptor.Create(TargetRuntime.Web,
            new Locator("css", "#customer-search button.primary"),
            frameContext: new FrameContext(new[] { new Locator("css", "#content-frame") })),
        new BubbleDefinition("לחץ על חיפוש", BubblePlacement.Bottom),
        new ValidationDefinition("clicked"),
        StepAdvanceMode.AutomaticOnValidation,
        new StepContextDefinition("css-exists", "#customer-search"));
}
