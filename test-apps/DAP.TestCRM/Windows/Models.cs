namespace DAP.TestCRM.Windows;

public sealed record Customer(int Id, string Name, string Phone, string Email);
public sealed record Site(int Id, int CustomerId, string Name, string Type, string Address);
public sealed record CaseItem(int Id, int SiteId, string Status, string Subject, string Description, string CloseReason);
public sealed record Lead(int Id, int SiteId, string Source, string ContactName, string Status, string Notes, string SelectedService, string LostReason);

public sealed record SiteInput(string Name, string Type, string Address);
public sealed record CaseInput(string Status, string Subject, string Description, string CloseReason);
public sealed record LeadInput(string Source, string ContactName, string Status, string Notes, string SelectedService, string LostReason);
