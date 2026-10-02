namespace DAP.TestCRM.E2E.Common;

public interface ICrmScenarioDriver
{
    Task SearchCustomer(string name);
    Task OpenFirstCustomer();
    Task OpenFirstSite();
    Task OpenCases();
    Task SortCasesByStatus();
    Task CreateCase();
    Task SetCaseSubject(string value);
    Task SetCaseDescription(string value);
    Task SaveCase();
    Task SetCaseStatus(string value);
    Task SetResolutionNotes(string value);
    Task ShowMoreActivity();
    Task DismissValidation();
    Task SetCloseReason(string value);
    Task OpenSiteFromBreadcrumb();
    Task OpenLeads();
    Task CreateLead();
    Task SetLeadContact(string value);
    Task SaveLead();
    Task SetLeadStatus(string value);
    Task SetLeadService(string value);
    Task DeleteLead();
    Task ConfirmDelete();
    Task GoPortal();
}

public static class CanonicalCrmScenario
{
    public static async Task RunCoreAsync(ICrmScenarioDriver d)
    {
        await d.SearchCustomer("אלפא פתרונות בע\"מ");
        await d.OpenFirstCustomer();
        await d.OpenFirstSite();
        await d.OpenCases();
        await d.SortCasesByStatus();
        await d.CreateCase();
        await d.SetCaseSubject("תקלה בחיבור לאינטרנט");
        await d.SetCaseDescription("הלקוח מדווח על חיבור לא יציב.");
        await d.SaveCase();
        await d.SetCaseStatus("בטיפול");
        await d.SetResolutionNotes("בוצעה בדיקת שירות מול הלקוח והתקלה טופלה.");
        await d.ShowMoreActivity();
        await d.DismissValidation();
        await d.SetCaseStatus("סגורה");
        await d.SetCaseSubject("תקלה בחיבור לאינטרנט");
        await d.SaveCase();
        await d.DismissValidation();
        await d.SetCloseReason("טופל");
        await d.SaveCase();

        await d.OpenSiteFromBreadcrumb();
        await d.OpenLeads();
        await d.OpenCases();
        await d.OpenLeads();
        await d.CreateLead();
        await d.SetLeadContact("לקוח בדיקת מערכת");
        await d.SaveLead();
        await d.SetLeadStatus("נסגר בהצלחה");
        await d.SetLeadStatus("חדש");
        await d.SetLeadStatus("נסגר בהצלחה");
        await d.SaveLead();
        await d.DismissValidation();
        await d.SetLeadService("תמיכה מורחבת");
        await d.SaveLead();
        await d.DeleteLead();
        await d.ConfirmDelete();
        await d.GoPortal();
    }
}
