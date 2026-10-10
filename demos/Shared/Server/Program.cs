using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();



// Resolve the shared demo data directory from the server project, not from bin/Debug.
// Both launchers start the server with the project directory as its working directory.
var serverProjectDirectory = builder.Environment.ContentRootPath;
var sharedDirectory = Path.GetFullPath(Path.Combine(serverProjectDirectory, ".."));
var dataDirectory = Path.Combine(sharedDirectory, "data");
Directory.CreateDirectory(dataDirectory);
var dbPath = Path.Combine(dataDirectory, "sampleapp.db");

var connectionString = $"Data Source={dbPath}";
var seedGridBaseline = args.Contains("--seed-grid-baseline", StringComparer.OrdinalIgnoreCase);
InitializeDatabase(connectionString, preserveCases: seedGridBaseline);
if (seedGridBaseline)
{
    var count = SeedGridBaseline(connectionString);
    Console.WriteLine($"SampleApp grid baseline: {count} tagged cases available at SiteId=1.");
}

app.MapGet("/api/customers", (string? name, string? phone, string? email, string? sort, string? dir) =>
{
    var order = Sort(sort, dir, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    { ["id"]="Id", ["name"]="Name", ["phone"]="Phone", ["email"]="Email" }, "Id");
    var where = new List<string>();
    if(!string.IsNullOrWhiteSpace(name)) where.Add("Name LIKE $name COLLATE NOCASE");
    if(!string.IsNullOrWhiteSpace(phone)) where.Add("Phone LIKE $phone COLLATE NOCASE");
    if(!string.IsNullOrWhiteSpace(email)) where.Add("Email LIKE $email COLLATE NOCASE");
    var sql=$"SELECT Id,Name,Phone,Email FROM Customers{(where.Count>0?" WHERE "+string.Join(" AND ",where):"")} ORDER BY {order}";
    using var c=new SqliteConnection(connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;
    if(!string.IsNullOrWhiteSpace(name))cmd.Parameters.AddWithValue("$name","%"+name.Trim()+"%");
    if(!string.IsNullOrWhiteSpace(phone))cmd.Parameters.AddWithValue("$phone","%"+phone.Trim()+"%");
    if(!string.IsNullOrWhiteSpace(email))cmd.Parameters.AddWithValue("$email","%"+email.Trim()+"%");
    using var r=cmd.ExecuteReader();var list=new List<Customer>();while(r.Read())list.Add(new(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetString(3)));return list;
});
app.MapPost("/api/customers", (CustomerInput x) =>
{
    var errors=ValidateCustomer(x); if(errors.Count>0)return Results.ValidationProblem(errors);
    var id=InsertRoot("INSERT INTO Customers(Name,Phone,Email) VALUES($a,$b,$c)",x.Name.Trim(),x.Phone.Trim(),x.Email.Trim());
    return Results.Created($"/api/customers/{id}",new Customer(id,x.Name.Trim(),x.Phone.Trim(),x.Email.Trim()));
});
app.MapGet("/api/customers/{id:int}", (int id) => One("SELECT Id,Name,Phone,Email FROM Customers WHERE Id=$id", id, r => new Customer(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetString(3))));
app.MapGet("/api/customers/{id:int}/sites", (int id, string? sort, string? dir) =>
{
    var order=Sort(sort,dir,new(StringComparer.OrdinalIgnoreCase){{"id","Id"},{"name","Name"},{"type","Type"},{"address","Address"}},"Id");
    return Query<Site>($"SELECT Id,CustomerId,Name,Type,Address FROM Sites WHERE CustomerId=$id ORDER BY {order}",r=>new(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4)),id);
});
app.MapGet("/api/sites/{id:int}", (int id) => One("SELECT Id,CustomerId,Name,Type,Address FROM Sites WHERE Id=$id",id,r=>new Site(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4))));
app.MapPost("/api/customers/{customerId:int}/sites", (int customerId, SiteInput x) =>
{
    if (!Exists("SELECT 1 FROM Customers WHERE Id=$id", customerId)) return Results.NotFound();
    var errors=ValidateSite(x); if(errors.Count>0)return Results.ValidationProblem(errors);
    var id=Insert("INSERT INTO Sites(CustomerId,Name,Type,Address) VALUES($parent,$a,$b,$c)",customerId,x.Name,x.Type,x.Address);
    return Results.Created($"/api/sites/{id}",new Site(id,customerId,x.Name,x.Type,x.Address));
});
app.MapPut("/api/sites/{id:int}", async (HttpContext context, int id, SiteInput x) =>
{
    if (!Exists("SELECT 1 FROM Sites WHERE Id=$id", id)) return Results.NotFound();
    var errors=ValidateSite(x); if(errors.Count>0)return Results.ValidationProblem(errors);

    Exec("UPDATE Sites SET Name=$a,Type=$b,Address=$c WHERE Id=$id",id,x.Name,x.Type,x.Address);
    return Results.Ok(new Site(id,GetInt("SELECT CustomerId FROM Sites WHERE Id=$id",id),x.Name,x.Type,x.Address));
});
app.MapDelete("/api/sites/{id:int}", (int id) =>
{
    if (!Exists("SELECT 1 FROM Sites WHERE Id=$id", id)) return Results.NotFound();
    if (Exists("SELECT 1 FROM Cases WHERE SiteId=$id LIMIT 1", id) || Exists("SELECT 1 FROM Leads WHERE SiteId=$id LIMIT 1", id))
        return Results.Conflict(new { title = "לא ניתן למחוק אתר שיש בו פניות או לידים." });
    var customerId=GetInt("SELECT CustomerId FROM Sites WHERE Id=$id",id);
    Exec("DELETE FROM Sites WHERE Id=$id",id);
    return Results.Ok(new { customerId });
});

app.MapGet("/api/sites/{siteId:int}/cases", (int siteId,string? sort,string? dir) =>
{
    var order=Sort(sort,dir,new(StringComparer.OrdinalIgnoreCase){{"id","Id"},{"status","Status"},{"subject","Subject"}},"Id","DESC");
    return Query<Case>($"SELECT Id,SiteId,Status,Subject,Description,CloseReason FROM Cases WHERE SiteId=$id ORDER BY {order}",r=>new(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5)),siteId);
});
app.MapGet("/api/cases/{id:int}", (int id) => One("SELECT Id,SiteId,Status,Subject,Description,CloseReason FROM Cases WHERE Id=$id",id,r=>new Case(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5))));
app.MapPost("/api/cases/{id:int}/fieldchange/status", (int id, CaseStatusFieldChange x) => {if(!Exists("SELECT 1 FROM Cases WHERE Id=$id",id))return Results.NotFound();return Results.Ok(new { status=x.Status, subject="", closeReason="", resolutionEnabled=x.Status!="פתוחה" });});
app.MapPost("/api/sites/{siteId:int}/cases/fieldchange/status", (int siteId, CaseStatusFieldChange x) => {if(!Exists("SELECT 1 FROM Sites WHERE Id=$id",siteId))return Results.NotFound();return Results.Ok(new { status=x.Status, subject="", closeReason="", resolutionEnabled=x.Status!="פתוחה" });});
app.MapPost("/api/sites/{siteId:int}/cases", (int siteId,CaseInput x) => {if(!Exists("SELECT 1 FROM Sites WHERE Id=$id",siteId))return Results.NotFound();var errors=ValidateCase(x);if(errors.Count>0)return Results.ValidationProblem(errors);var id=Insert("INSERT INTO Cases(SiteId,Status,Subject,Description,CloseReason) VALUES($parent,$a,$b,$c,$d)",siteId,x.Status,x.Subject,x.Description,x.CloseReason);return Results.Created($"/api/cases/{id}",new Case(id,siteId,x.Status,x.Subject,x.Description,x.CloseReason));});
app.MapPut("/api/cases/{id:int}", (int id,CaseInput x) => {if(!Exists("SELECT 1 FROM Cases WHERE Id=$id",id))return Results.NotFound();var errors=ValidateCase(x);if(errors.Count>0)return Results.ValidationProblem(errors);Exec("UPDATE Cases SET Status=$a,Subject=$b,Description=$c,CloseReason=$d WHERE Id=$id",id,x.Status,x.Subject,x.Description,x.CloseReason);return Results.Ok(new Case(id,GetInt("SELECT SiteId FROM Cases WHERE Id=$id",id),x.Status,x.Subject,x.Description,x.CloseReason));});
app.MapDelete("/api/cases/{id:int}", (int id) =>
{
    if(!Exists("SELECT 1 FROM Cases WHERE Id=$id",id)) return Results.NotFound();
    Exec("DELETE FROM Cases WHERE Id=$id",id);
    return Results.NoContent();
});

app.MapGet("/api/sites/{siteId:int}/leads", (int siteId,string? sort,string? dir) =>
{
    var order=Sort(sort,dir,new(StringComparer.OrdinalIgnoreCase){{"id","Id"},{"source","Source"},{"contactName","ContactName"},{"status","Status"}},"Id","DESC");
    return Query<Lead>($"SELECT Id,SiteId,Source,ContactName,Status,Notes,SelectedService,LostReason FROM Leads WHERE SiteId=$id ORDER BY {order}",r=>new(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7)),siteId);
});
app.MapGet("/api/leads/{id:int}", (int id) => One("SELECT Id,SiteId,Source,ContactName,Status,Notes,SelectedService,LostReason FROM Leads WHERE Id=$id",id,r=>new Lead(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7))));
app.MapPost("/api/leads/{id:int}/fieldchange/status", (int id, LeadStatusFieldChange x) => {if(!Exists("SELECT 1 FROM Leads WHERE Id=$id",id))return Results.NotFound();return Results.Ok(new { status=x.Status, selectedService="", lostReason="" });});
app.MapPost("/api/sites/{siteId:int}/leads/fieldchange/status", (int siteId, LeadStatusFieldChange x) => {if(!Exists("SELECT 1 FROM Sites WHERE Id=$id",siteId))return Results.NotFound();return Results.Ok(new { status=x.Status, selectedService="", lostReason="" });});
app.MapPost("/api/sites/{siteId:int}/leads", (int siteId,LeadInput x) => {if(!Exists("SELECT 1 FROM Sites WHERE Id=$id",siteId))return Results.NotFound();var errors=ValidateLead(x);if(errors.Count>0)return Results.ValidationProblem(errors);var id=Insert("INSERT INTO Leads(SiteId,Source,ContactName,Status,Notes,SelectedService,LostReason) VALUES($parent,$a,$b,$c,$d,$e,$f)",siteId,x.Source,x.ContactName,x.Status,x.Notes,x.SelectedService,x.LostReason);return Results.Created($"/api/leads/{id}",new Lead(id,siteId,x.Source,x.ContactName,x.Status,x.Notes,x.SelectedService,x.LostReason));});
app.MapPut("/api/leads/{id:int}", (int id,LeadInput x) => {if(!Exists("SELECT 1 FROM Leads WHERE Id=$id",id))return Results.NotFound();var errors=ValidateLead(x);if(errors.Count>0)return Results.ValidationProblem(errors);Exec("UPDATE Leads SET Source=$a,ContactName=$b,Status=$c,Notes=$d,SelectedService=$e,LostReason=$f WHERE Id=$id",id,x.Source,x.ContactName,x.Status,x.Notes,x.SelectedService,x.LostReason);return Results.Ok(new Lead(id,GetInt("SELECT SiteId FROM Leads WHERE Id=$id",id),x.Source,x.ContactName,x.Status,x.Notes,x.SelectedService,x.LostReason));});

app.MapDelete("/api/leads/{id:int}", (int id) =>
{
    if(!Exists("SELECT 1 FROM Leads WHERE Id=$id",id)) return Results.NotFound();
    Exec("DELETE FROM Leads WHERE Id=$id",id);
    return Results.NoContent();
});

app.Run();

Dictionary<string,string[]> ValidateCustomer(CustomerInput x){var e=new Dictionary<string,string[]>();if(string.IsNullOrWhiteSpace(x.Name))e["name"]=["שם הלקוח הוא שדה חובה."];if(x.Name?.Trim().Length>100)e["name"]=["שם הלקוח מוגבל ל-100 תווים."];if(string.IsNullOrWhiteSpace(x.Phone))e["phone"]=["טלפון הוא שדה חובה."];if(string.IsNullOrWhiteSpace(x.Email))e["email"]=["דוא\"ל הוא שדה חובה."];else if(!System.Net.Mail.MailAddress.TryCreate(x.Email,out _))e["email"]=["כתובת הדוא\"ל אינה תקינה."];return e;}
Dictionary<string,string[]> ValidateSite(SiteInput x){var e=new Dictionary<string,string[]>();if(string.IsNullOrWhiteSpace(x.Name))e["name"]=["שם האתר הוא שדה חובה."];if(x.Name?.Trim().Length>80)e["name"]=["שם האתר מוגבל ל-80 תווים."];if(!new[]{"משרד","סניף","מחסן"}.Contains(x.Type))e["type"]=["סוג האתר אינו תקין."];if(string.IsNullOrWhiteSpace(x.Address))e["address"]=["כתובת היא שדה חובה."];return e;}
Dictionary<string,string[]> ValidateCase(CaseInput x){var e=new Dictionary<string,string[]>();if(!new[]{"פתוחה","בטיפול","סגורה"}.Contains(x.Status))e["status"]=["סטטוס הפנייה אינו תקין."];if(string.IsNullOrWhiteSpace(x.Subject))e["subject"]=["נושא הפנייה הוא שדה חובה."];if(x.Subject?.Trim().Length>120)e["subject"]=["נושא הפנייה מוגבל ל-120 תווים."];if(x.Status=="סגורה"&&string.IsNullOrWhiteSpace(x.CloseReason))e["closeReason"]=["סיבת סגירה היא שדה חובה כאשר הפנייה סגורה."];return e;}
Dictionary<string,string[]> ValidateLead(LeadInput x){var e=new Dictionary<string,string[]>();if(!new[]{"אתר אינטרנט","הפניה","קמפיין","טלפון"}.Contains(x.Source))e["source"]=["מקור הליד אינו תקין."];if(string.IsNullOrWhiteSpace(x.ContactName))e["contactName"]=["שם איש הקשר הוא שדה חובה."];if(x.ContactName?.Trim().Length>100)e["contactName"]=["שם איש הקשר מוגבל ל-100 תווים."];if(!new[]{"חדש","בתהליך","נסגר בהצלחה","נסגר ללא עסקה"}.Contains(x.Status))e["status"]=["סטטוס הליד אינו תקין."];if(x.Status=="נסגר בהצלחה"&&string.IsNullOrWhiteSpace(x.SelectedService))e["selectedService"]=["שירות שנבחר הוא שדה חובה כאשר הליד נסגר בהצלחה."];if(x.Status=="נסגר ללא עסקה"&&string.IsNullOrWhiteSpace(x.LostReason))e["lostReason"]=["סיבת אי-סגירה היא שדה חובה כאשר הליד נסגר ללא עסקה."];return e;}
bool Exists(string sql,int id){using var c=new SqliteConnection(connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("$id",id);return cmd.ExecuteScalar()!=null;}
string Sort(string? field,string? dir,Dictionary<string,string> allowed,string fallback,string fallbackDir="ASC"){var col=field!=null&&allowed.TryGetValue(field,out var c)?c:allowed[fallback];var d=string.Equals(dir,"desc",StringComparison.OrdinalIgnoreCase)?"DESC":string.Equals(dir,"asc",StringComparison.OrdinalIgnoreCase)?"ASC":fallbackDir;return $"{col} {d}";}
List<T> Query<T>(string sql,Func<SqliteDataReader,T> map,int? id=null){using var c=new SqliteConnection(connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;if(id.HasValue)cmd.Parameters.AddWithValue("$id",id.Value);using var r=cmd.ExecuteReader();var list=new List<T>();while(r.Read())list.Add(map(r));return list;}
IResult One<T>(string sql,int id,Func<SqliteDataReader,T> map){using var c=new SqliteConnection(connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("$id",id);using var r=cmd.ExecuteReader();return r.Read()?Results.Ok(map(r)):Results.NotFound();}
int GetInt(string sql,int id){using var c=new SqliteConnection(connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("$id",id);return Convert.ToInt32(cmd.ExecuteScalar());}
int InsertRoot(string sql,params string[] values){using var c=new SqliteConnection(connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql+"; SELECT last_insert_rowid();";for(var i=0;i<values.Length;i++)cmd.Parameters.AddWithValue("$"+(char)('a'+i),values[i]);return Convert.ToInt32((long)cmd.ExecuteScalar()!);}
int Insert(string sql,int parent,params string[] values){using var c=new SqliteConnection(connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql+"; SELECT last_insert_rowid();";cmd.Parameters.AddWithValue("$parent",parent);for(var i=0;i<values.Length;i++)cmd.Parameters.AddWithValue("$"+(char)('a'+i),values[i]);return Convert.ToInt32((long)cmd.ExecuteScalar()!);}
void Exec(string sql,int id,params string[] values){using var c=new SqliteConnection(connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("$id",id);for(var i=0;i<values.Length;i++)cmd.Parameters.AddWithValue("$"+(char)('a'+i),values[i]);cmd.ExecuteNonQuery();}
void InitializeDatabase(string cs, bool preserveCases = false){using var c=new SqliteConnection(cs);c.Open();using var cmd=c.CreateCommand();cmd.CommandText=@"CREATE TABLE IF NOT EXISTS Customers(Id INTEGER PRIMARY KEY,Name TEXT NOT NULL,Phone TEXT NOT NULL,Email TEXT NOT NULL);CREATE TABLE IF NOT EXISTS Sites(Id INTEGER PRIMARY KEY AUTOINCREMENT,CustomerId INTEGER NOT NULL,Name TEXT NOT NULL,Type TEXT NOT NULL,Address TEXT NOT NULL);CREATE TABLE IF NOT EXISTS Cases(Id INTEGER PRIMARY KEY AUTOINCREMENT,SiteId INTEGER NOT NULL,Status TEXT NOT NULL,Subject TEXT NOT NULL,Description TEXT NOT NULL);CREATE TABLE IF NOT EXISTS Leads(Id INTEGER PRIMARY KEY AUTOINCREMENT,SiteId INTEGER NOT NULL,Source TEXT NOT NULL,ContactName TEXT NOT NULL,Status TEXT NOT NULL,Notes TEXT NOT NULL);";cmd.ExecuteNonQuery();try{cmd.CommandText="ALTER TABLE Cases ADD COLUMN CloseReason TEXT NOT NULL DEFAULT ''";cmd.ExecuteNonQuery();}catch(SqliteException){}try{cmd.CommandText="ALTER TABLE Leads ADD COLUMN SelectedService TEXT NOT NULL DEFAULT ''";cmd.ExecuteNonQuery();}catch(SqliteException){}try{cmd.CommandText="ALTER TABLE Leads ADD COLUMN LostReason TEXT NOT NULL DEFAULT ''";cmd.ExecuteNonQuery();}catch(SqliteException){}cmd.CommandText="SELECT COUNT(*) FROM Customers";if(Convert.ToInt32(cmd.ExecuteScalar())>0){if (!preserveCases) TrimTestCases(cmd);return;}cmd.CommandText=@"INSERT INTO Customers VALUES(1,'אלפא פתרונות בע""מ','03-5550100','service@alpha.test'),(2,'אופק שירותים בע""מ','03-5550200','office@ofek.test');INSERT INTO Sites(CustomerId,Name,Type,Address) VALUES(1,'מטה תל אביב','משרד','הארבעה 10, תל אביב'),(1,'סניף חיפה','סניף','הנמל 20, חיפה'),(2,'משרד ירושלים','משרד','יפו 50, ירושלים');INSERT INTO Cases(SiteId,Status,Subject,Description) VALUES(1,'פתוחה','תקלה בחיבור לאינטרנט','החיבור אינו יציב'),(1,'סגורה','כרטיס כניסה','החלפת הכרטיס הושלמה'),(2,'פתוחה','תקלה במדפסת','המדפסת אינה זמינה');INSERT INTO Leads(SiteId,Source,ContactName,Status,Notes) VALUES(1,'אתר אינטרנט','דנה לוי','חדש','ביקשה מידע נוסף על המוצר'),(1,'הפניה','אבי כהן','בתהליך','נקבעה שיחת המשך'),(2,'קמפיין','נועה בר','חדש','מתעניינת בשדרוג');";cmd.ExecuteNonQuery();}
// Explicit demo-only fixture. No product code or Guide database is modified.
int SeedGridBaseline(string cs)
{
    using var connection = new SqliteConnection(cs);
    connection.Open();
    using var transaction = connection.BeginTransaction();
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    for (var index = 1; index <= 100; index++)
    {
        var subject = $"DAP-GRID-BASELINE-{index:0000}";
        command.Parameters.Clear();
        command.CommandText = "SELECT COUNT(*) FROM Cases WHERE SiteId=1 AND Subject=$subject";
        command.Parameters.AddWithValue("$subject", subject);
        if (Convert.ToInt32(command.ExecuteScalar()) > 0)
            continue;
        command.Parameters.Clear();
        command.CommandText = "INSERT INTO Cases(SiteId,Status,Subject,Description,CloseReason) VALUES(1,$status,$subject,$description,'')";
        command.Parameters.AddWithValue("$status", "פתוחה");
        command.Parameters.AddWithValue("$subject", subject);
        command.Parameters.AddWithValue("$description", "רשומת בדיקת ביצועים");
        command.ExecuteNonQuery();
    }
    command.Parameters.Clear();
    command.CommandText = "SELECT COUNT(*) FROM Cases WHERE SiteId=1 AND Subject LIKE 'DAP-GRID-BASELINE-%'";
    var count = Convert.ToInt32(command.ExecuteScalar());
    transaction.Commit();
    return count;
}
void TrimTestCases(SqliteCommand cmd){cmd.CommandText=@"DELETE FROM Cases WHERE SiteId=1 AND Subject NOT LIKE 'DAP-GRID-BASELINE-%' AND Id NOT IN (SELECT Id FROM Cases WHERE SiteId=1 AND Subject NOT LIKE 'DAP-GRID-BASELINE-%' ORDER BY Id DESC LIMIT 10);";cmd.ExecuteNonQuery();}

record Customer(int Id,string Name,string Phone,string Email);
record Site(int Id,int CustomerId,string Name,string Type,string Address);
record Case(int Id,int SiteId,string Status,string Subject,string Description,string CloseReason);
record Lead(int Id,int SiteId,string Source,string ContactName,string Status,string Notes,string SelectedService,string LostReason);
record CustomerInput(string Name,string Phone,string Email);
record SiteInput(string Name,string Type,string Address);
record CaseInput(string Status,string Subject,string Description,string CloseReason="");
record CaseStatusFieldChange(string Status);
record LeadInput(string Source,string ContactName,string Status,string Notes,string SelectedService="",string LostReason="");
record LeadStatusFieldChange(string Status);
