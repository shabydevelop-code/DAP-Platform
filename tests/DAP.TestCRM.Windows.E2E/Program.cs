using System.IO;
using System.Diagnostics;
using System.Windows.Automation;
using DAP.TestCRM.E2E.Common;
using DAP.TestCRM.Windows.E2E;

const string appTitle="DAP Test CRM - Windows";
var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..",".."));
var appProject=Path.Combine(root,"test-apps","DAP.TestCRM","Windows","DAP.TestCRM.Windows.csproj");

using var app=Process.Start(new ProcessStartInfo("dotnet",$"run --project \"{appProject}\" --no-launch-profile"){WorkingDirectory=root,UseShellExecute=false})
    ?? throw new Exception("Could not start Windows TestCRM.");
try
{
    var sw=Stopwatch.StartNew(); AutomationElement? window=null;
    while(sw.ElapsedMilliseconds<30000 && window is null){window=AutomationElement.RootElement.FindFirst(TreeScope.Children,new PropertyCondition(AutomationElement.NameProperty,appTitle));if(window is null)Thread.Sleep(100);}
    if(window is null)throw new TimeoutException("Timed out waiting for Windows TestCRM main window.");

    await CanonicalCrmScenario.RunCoreAsync(new WindowsCrmScenarioDriver(app,window));
    Console.WriteLine("PASS: Windows CRM-only canonical Customer -> Site -> Case -> Lead core scenario completed.");
}
finally
{
    try{if(!app.HasExited)app.Kill(true);}catch{}
}
