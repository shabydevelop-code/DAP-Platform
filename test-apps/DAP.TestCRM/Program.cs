using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var dbPath = Path.Combine(builder.Environment.ContentRootPath, "testcrm.db");
var connectionString = $"Data Source={dbPath}";
InitializeDatabase(connectionString);

app.MapGet("/api/customers", (string? sort, string? dir) =>
{
    var order = Sort(sort, dir, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    { ["id"]="Id", ["name"]="Name", ["phone"]="Phone", ["email"]="Email" }, "Id");
    return Query<Customer>($"SELECT Id,Name,Phone,Email FROM Customers ORDER BY {order}", r => new(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetString(3)));
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
    var id=Insert("INSERT INTO Sites(CustomerId,Name,Type,Address) VALUES($parent,$a,$b,$c)",customerId,x.Name,x.Type,x.Address);
    return Results.Created($"/api/sites/{id}",new Site(id,customerId,x.Name,x.Type,x.Address));
});
app.MapPut("/api/sites/{id:int}", async (int id, SiteInput x) =>
{
    await Task.Delay(450);
    Exec("UPDATE Sites SET Name=$a,Type=$b,Address=$c WHERE Id=$id",id,x.Name,x.Type,x.Address);
    return Results.Ok(new Site(id,GetInt("SELECT CustomerId FROM Sites WHERE Id=$id",id),x.Name,x.Type,x.Address));
});

app.MapGet("/api/sites/{siteId:int}/cases", (int siteId,string? sort,string? dir) =>
{
    var order=Sort(sort,dir,new(StringComparer.OrdinalIgnoreCase){{"id","Id"},{"status","Status"},{"subject","Subject"}},"Id","DESC");
    return Query<Case>($"SELECT Id,SiteId,Status,Subject,Description FROM Cases WHERE SiteId=$id ORDER BY {order}",r=>new(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4)),siteId);
});
app.MapGet("/api/cases/{id:int}", (int id) => One("SELECT Id,SiteId,Status,Subject,Description FROM Cases WHERE Id=$id",id,r=>new Case(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4))));
app.MapPost("/api/sites/{siteId:int}/cases", (int siteId,CaseInput x) => {var id=Insert("INSERT INTO Cases(SiteId,Status,Subject,Description) VALUES($parent,$a,$b,$c)",siteId,x.Status,x.Subject,x.Description);return Results.Created($"/api/cases/{id}",new Case(id,siteId,x.Status,x.Subject,x.Description));});
app.MapPut("/api/cases/{id:int}", (int id,CaseInput x) => {Exec("UPDATE Cases SET Status=$a,Subject=$b,Description=$c WHERE Id=$id",id,x.Status,x.Subject,x.Description);return Results.Ok(new Case(id,GetInt("SELECT SiteId FROM Cases WHERE Id=$id",id),x.Status,x.Subject,x.Description));});

app.MapGet("/api/sites/{siteId:int}/leads", (int siteId,string? sort,string? dir) =>
{
    var order=Sort(sort,dir,new(StringComparer.OrdinalIgnoreCase){{"id","Id"},{"source","Source"},{"contactName","ContactName"},{"status","Status"}},"Id","DESC");
    return Query<Lead>($"SELECT Id,SiteId,Source,ContactName,Status,Notes FROM Leads WHERE SiteId=$id ORDER BY {order}",r=>new(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5)),siteId);
});
app.MapGet("/api/leads/{id:int}", (int id) => One("SELECT Id,SiteId,Source,ContactName,Status,Notes FROM Leads WHERE Id=$id",id,r=>new Lead(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5))));
app.MapPost("/api/sites/{siteId:int}/leads", (int siteId,LeadInput x) => {var id=Insert("INSERT INTO Leads(SiteId,Source,ContactName,Status,Notes) VALUES($parent,$a,$b,$c,$d)",siteId,x.Source,x.ContactName,x.Status,x.Notes);return Results.Created($"/api/leads/{id}",new Lead(id,siteId,x.Source,x.ContactName,x.Status,x.Notes));});
app.MapPut("/api/leads/{id:int}", (int id,LeadInput x) => {Exec("UPDATE Leads SET Source=$a,ContactName=$b,Status=$c,Notes=$d WHERE Id=$id",id,x.Source,x.ContactName,x.Status,x.Notes);return Results.Ok(new Lead(id,GetInt("SELECT SiteId FROM Leads WHERE Id=$id",id),x.Source,x.ContactName,x.Status,x.Notes));});

app.Run();

string Sort(string? field,string? dir,Dictionary<string,string> allowed,string fallback,string fallbackDir="ASC"){var col=field!=null&&allowed.TryGetValue(field,out var c)?c:allowed[fallback];var d=string.Equals(dir,"desc",StringComparison.OrdinalIgnoreCase)?"DESC":string.Equals(dir,"asc",StringComparison.OrdinalIgnoreCase)?"ASC":fallbackDir;return $"{col} {d}";}
List<T> Query<T>(string sql,Func<SqliteDataReader,T> map,int? id=null){using var c=new SqliteConnection(connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;if(id.HasValue)cmd.Parameters.AddWithValue("$id",id.Value);using var r=cmd.ExecuteReader();var list=new List<T>();while(r.Read())list.Add(map(r));return list;}
IResult One<T>(string sql,int id,Func<SqliteDataReader,T> map){using var c=new SqliteConnection(connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("$id",id);using var r=cmd.ExecuteReader();return r.Read()?Results.Ok(map(r)):Results.NotFound();}
int GetInt(string sql,int id){using var c=new SqliteConnection(connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("$id",id);return Convert.ToInt32(cmd.ExecuteScalar());}
int Insert(string sql,int parent,params string[] values){using var c=new SqliteConnection(connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql+"; SELECT last_insert_rowid();";cmd.Parameters.AddWithValue("$parent",parent);for(var i=0;i<values.Length;i++)cmd.Parameters.AddWithValue("$"+(char)('a'+i),values[i]);return Convert.ToInt32((long)cmd.ExecuteScalar()!);}
void Exec(string sql,int id,params string[] values){using var c=new SqliteConnection(connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("$id",id);for(var i=0;i<values.Length;i++)cmd.Parameters.AddWithValue("$"+(char)('a'+i),values[i]);cmd.ExecuteNonQuery();}
void InitializeDatabase(string cs){using var c=new SqliteConnection(cs);c.Open();using var cmd=c.CreateCommand();cmd.CommandText=@"CREATE TABLE IF NOT EXISTS Customers(Id INTEGER PRIMARY KEY,Name TEXT NOT NULL,Phone TEXT NOT NULL,Email TEXT NOT NULL);CREATE TABLE IF NOT EXISTS Sites(Id INTEGER PRIMARY KEY AUTOINCREMENT,CustomerId INTEGER NOT NULL,Name TEXT NOT NULL,Type TEXT NOT NULL,Address TEXT NOT NULL);CREATE TABLE IF NOT EXISTS Cases(Id INTEGER PRIMARY KEY AUTOINCREMENT,SiteId INTEGER NOT NULL,Status TEXT NOT NULL,Subject TEXT NOT NULL,Description TEXT NOT NULL);CREATE TABLE IF NOT EXISTS Leads(Id INTEGER PRIMARY KEY AUTOINCREMENT,SiteId INTEGER NOT NULL,Source TEXT NOT NULL,ContactName TEXT NOT NULL,Status TEXT NOT NULL,Notes TEXT NOT NULL);";cmd.ExecuteNonQuery();cmd.CommandText="SELECT COUNT(*) FROM Customers";if(Convert.ToInt32(cmd.ExecuteScalar())>0)return;cmd.CommandText=@"INSERT INTO Customers VALUES(1,'אלפא פתרונות בע""מ','03-5550100','service@alpha.test'),(2,'אופק שירותים בע""מ','03-5550200','office@ofek.test');INSERT INTO Sites(CustomerId,Name,Type,Address) VALUES(1,'מטה תל אביב','משרד','הארבעה 10, תל אביב'),(1,'סניף חיפה','סניף','הנמל 20, חיפה'),(2,'משרד ירושלים','משרד','יפו 50, ירושלים');INSERT INTO Cases(SiteId,Status,Subject,Description) VALUES(1,'פתוחה','תקלה בחיבור לאינטרנט','החיבור אינו יציב'),(1,'סגורה','כרטיס כניסה','החלפת הכרטיס הושלמה'),(2,'פתוחה','תקלה במדפסת','המדפסת אינה זמינה');INSERT INTO Leads(SiteId,Source,ContactName,Status,Notes) VALUES(1,'אתר אינטרנט','דנה לוי','חדש','ביקשה מידע נוסף על המוצר'),(1,'הפניה','אבי כהן','בתהליך','נקבעה שיחת המשך'),(2,'קמפיין','נועה בר','חדש','מתעניינת בשדרוג');";cmd.ExecuteNonQuery();}

record Customer(int Id,string Name,string Phone,string Email);
record Site(int Id,int CustomerId,string Name,string Type,string Address);
record Case(int Id,int SiteId,string Status,string Subject,string Description);
record Lead(int Id,int SiteId,string Source,string ContactName,string Status,string Notes);
record SiteInput(string Name,string Type,string Address);
record CaseInput(string Status,string Subject,string Description);
record LeadInput(string Source,string ContactName,string Status,string Notes);
