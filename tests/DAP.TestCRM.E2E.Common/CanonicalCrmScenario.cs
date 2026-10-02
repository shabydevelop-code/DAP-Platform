namespace DAP.TestCRM.E2E.Common;

public interface ICrmScenarioDriver
{
    Task SetCustomerSearch(string name);
    Task SubmitCustomerSearch();
    Task OpenFirstCustomer();
    Task OpenFirstSite();
    Task OpenCases();
    Task SortCasesByStatus();
    Task CreateCase();
    Task SetCaseSubject(string value);
    Task SetCaseDescription(string value);
    Task SaveCase();
    Task OpenSiteFromBreadcrumb();
    Task OpenCreatedCase();
    Task SetCaseStatus(string value);
    Task SetResolutionNotes(string value);
    Task ShowMoreActivity();
    Task DismissValidation();
    Task SetCloseReason(string value);
    Task OpenLeads();
    Task CreateLead();
    Task SetLeadContact(string value);
    Task SaveLead();
    Task SetLeadStatus(string value);
    Task SetLeadService(string value);
    Task DeleteLead();
    Task ConfirmDelete();
    Task OpenCustomerFromBreadcrumb();
    Task OpenFirstLead();
    Task OpenFirstCase();
    Task DeleteCase();
    Task GoPortal();
}

public static class CanonicalCrmScenario
{
    public static async Task RunCoreAsync(ICrmScenarioDriver d) => await Run53Async(d);

    public static async Task Run53Async(ICrmScenarioDriver d)
    {
        static async Task Step(int n, Func<Task> action)
        {
            Console.WriteLine($"Windows canonical Step {n}/53");
            await action();
        }

        await Step(1,()=>d.SetCustomerSearch("אלפא פתרונות בע\"מ"));
        await Step(2,d.SubmitCustomerSearch);
        await Step(3,d.OpenFirstCustomer);
        await Step(4,d.OpenFirstSite);
        await Step(5,d.OpenCases);
        await Step(6,d.SortCasesByStatus);
        await Step(7,d.CreateCase);
        await Step(8,()=>d.SetCaseSubject("תקלה בחיבור לאינטרנט"));
        await Step(9,()=>d.SetCaseDescription("הלקוח מדווח על חיבור לא יציב."));
        await Step(10,d.SaveCase);
        await Step(11,d.OpenSiteFromBreadcrumb);
        await Step(12,d.OpenCreatedCase);
        await Step(13,()=>d.SetCaseStatus("בטיפול"));
        await Step(14,()=>d.SetResolutionNotes("בוצעה בדיקת שירות מול הלקוח והתקלה טופלה."));
        await Step(15,d.ShowMoreActivity);
        await Step(16,()=>d.SetCaseStatus("סגורה"));
        await Step(17,()=>d.SetCaseSubject("תקלה בחיבור לאינטרנט"));
        await Step(18,d.SaveCase);
        await Step(19,d.DismissValidation);
        await Step(20,()=>d.SetCloseReason("טופל"));
        await Step(21,d.SaveCase);
        await Step(22,d.OpenSiteFromBreadcrumb);
        await Step(23,d.OpenLeads);
        await Step(24,d.OpenCases);
        await Step(25,d.OpenLeads);
        await Step(26,d.CreateLead);
        await Step(27,()=>d.SetLeadContact("לקוח בדיקת מערכת"));
        await Step(28,d.SaveLead);
        await Step(29,()=>d.SetLeadStatus("נסגר בהצלחה"));
        await Step(30,()=>d.SetLeadStatus("חדש"));
        await Step(31,()=>d.SetLeadStatus("נסגר בהצלחה"));
        await Step(32,d.SaveLead);
        await Step(33,d.DismissValidation);
        await Step(34,()=>d.SetLeadService("תמיכה מורחבת"));
        await Step(35,d.SaveLead);
        await Step(36,d.DeleteLead);
        await Step(37,d.ConfirmDelete);
        await Step(38,d.OpenCustomerFromBreadcrumb);
        await Step(39,d.OpenFirstSite);
        await Step(40,d.OpenLeads);
        await Step(41,d.OpenFirstLead);
        await Step(42,()=>d.SetLeadStatus("חדש"));
        await Step(43,()=>d.SetLeadStatus("נסגר בהצלחה"));
        await Step(44,()=>d.SetLeadStatus("חדש"));
        await Step(45,()=>d.SetLeadStatus("נסגר בהצלחה"));
        await Step(46,d.OpenSiteFromBreadcrumb);
        await Step(47,d.OpenCases);
        await Step(48,d.OpenFirstCase);
        await Step(49,d.OpenSiteFromBreadcrumb);
        await Step(50,d.OpenCreatedCase);
        await Step(51,d.DeleteCase);
        await Step(52,d.ConfirmDelete);
        await Step(53,d.GoPortal);
    }
}
