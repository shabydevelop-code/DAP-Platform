using DAP.Core.Guides;

namespace DAP.TestCRM.Windows.E2E;

/// <summary>
/// Executes the persisted Windows Guide as a CRM-only learner.
/// The Guide owns ordering and learner semantics; this adapter supplies only
/// the synthetic actions/values needed by the E2E fixture.
/// </summary>
internal sealed class PersistedWindowsCrmGuideExecutor
{
    private readonly WindowsCrmScenarioDriver driver;

    public PersistedWindowsCrmGuideExecutor(WindowsCrmScenarioDriver driver)
    {
        this.driver = driver;
    }

    public async Task RunAsync(IReadOnlyList<GuideStep> steps)
    {
        var ordered = steps.OrderBy(step => step.Order).ToArray();
        ValidateSequence(ordered);

        foreach (var step in ordered)
        {
            Console.WriteLine($"Windows persisted CRM-only Step {step.Order}/{ordered.Length}: {step.Id}");
            await ExecuteAsync(step);
        }
    }

    private async Task ExecuteAsync(GuideStep step)
    {
        switch (step.Id)
        {
            case "testcrm-windows-customer-name":
                await driver.SetCustomerSearch(RequiredExpectedValue(step));
                return;
            case "testcrm-windows-customer-search-button":
                await driver.SubmitCustomerSearch();
                return;
            case "testcrm-windows-customer-result":
                await driver.OpenFirstCustomer();
                return;
            case "testcrm-windows-site-row":
                await driver.OpenFirstSite();
                return;
            case "testcrm-windows-cases-tab":
                await driver.OpenCases();
                return;
            case "testcrm-windows-sort-cases":
                await driver.SortCasesByStatus();
                return;
            case "testcrm-windows-new-case":
                await driver.CreateCase();
                return;
            case "testcrm-windows-case-subject":
                await driver.SetCaseSubject("תקלה בחיבור לאינטרנט");
                return;
            case "testcrm-windows-case-description":
                await driver.SetCaseDescription("הלקוח מדווח על חיבור לא יציב.");
                return;
            case "testcrm-windows-save-new-case":
                await driver.SaveCase();
                return;
            case "testcrm-windows-back-to-cases":
                await driver.OpenSiteFromBreadcrumb();
                return;
            case "testcrm-windows-open-created-case":
                await driver.OpenCreatedCase();
                return;
            case "testcrm-windows-case-in-progress":
                await driver.SetCaseStatus(RequiredExpectedValue(step));
                return;
            case "testcrm-windows-resolution-notes":
                await driver.SetResolutionNotes("נבדקה תשתית הלקוח");
                return;
            case "testcrm-windows-activity-more":
                await driver.ShowMoreActivity();
                return;
            case "testcrm-windows-case-closed":
                await driver.SetCaseStatus(RequiredExpectedValue(step));
                return;
            case "testcrm-windows-case-subject-after-close":
                await driver.SetCaseSubject("תקלה בחיבור לאינטרנט");
                return;
            case "testcrm-windows-attempt-close-save":
                await driver.SaveCase();
                return;
            case "testcrm-windows-confirm-close-validation":
                await driver.DismissValidation();
                return;
            case "testcrm-windows-close-reason":
                await driver.SetCloseReason(RequiredExpectedValue(step));
                return;
            case "testcrm-windows-save-closed-case":
                await driver.SaveCase();
                return;
            case "testcrm-windows-return-site":
                await driver.OpenSiteFromBreadcrumb();
                return;
            case "testcrm-windows-open-leads-tab":
                await driver.OpenLeads();
                return;
            case "testcrm-windows-return-cases-tab":
                await driver.OpenCases();
                return;
            case "testcrm-windows-open-leads-again":
                await driver.OpenLeads();
                return;
            case "testcrm-windows-new-lead":
                await driver.CreateLead();
                return;
            case "testcrm-windows-lead-contact":
                await driver.SetLeadContact("דנה כהן");
                return;
            case "testcrm-windows-save-new-lead":
                await driver.SaveLead();
                return;
            case "testcrm-windows-lead-close-success-1":
                await driver.SetLeadStatus(RequiredExpectedValue(step));
                return;
            case "testcrm-windows-lead-new":
                await driver.SetLeadStatus(RequiredExpectedValue(step));
                return;
            default:
                throw new NotSupportedException(
                    $"Persisted Windows CRM-only executor does not yet support Guide Step '{step.Id}' (order {step.Order}).");
        }
    }

    private static string RequiredExpectedValue(GuideStep step) =>
        !string.IsNullOrWhiteSpace(step.Validation?.ExpectedValue)
            ? step.Validation.ExpectedValue
            : throw new InvalidOperationException(
                $"Guide Step '{step.Id}' requires a persisted expected value for CRM-only execution.");

    private static void ValidateSequence(IReadOnlyList<GuideStep> steps)
    {
        if (steps.Count == 0)
            throw new InvalidOperationException("Persisted Windows Guide contains no Steps.");

        for (var index = 0; index < steps.Count; index++)
        {
            var expectedOrder = index + 1;
            if (steps[index].Order != expectedOrder)
                throw new InvalidOperationException(
                    $"Persisted Windows Guide must have contiguous Step order 1..N. " +
                    $"Expected {expectedOrder}, found {steps[index].Order}.");

            if (steps[index].Target?.Runtime != DAP.Core.Targets.TargetRuntime.Windows)
                throw new InvalidOperationException(
                    $"Guide Step '{steps[index].Id}' is not a Windows target.");
        }
    }
}
