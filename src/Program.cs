using System;
using System.Diagnostics;
using System.Configuration;
using System.IO;
using System.Security.Principal;
using System.Threading;

namespace UUDisplayOnDemand {
 internal static class Program {
  private const string TaskName="UU-VirtualDisplay-OnDemand";
  private const int DelaySeconds=20;
  private static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Codex","UUDisplayOnDemand");
  private static readonly string StatusFile=Path.Combine(Root,"status.json");
  private static readonly string LogFile=Path.Combine(Root,"events.log");
  private static string Quote(string value) {
   if(value==null) return "null";
   return "\""+value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n").Replace("\t","\\t")+"\"";
  }
  private static void Log(string message) {
   if(File.Exists(LogFile) && new FileInfo(LogFile).Length>1048576) File.Copy(LogFile,LogFile+".previous",true);
   if(File.Exists(LogFile) && new FileInfo(LogFile).Length>1048576) File.WriteAllText(LogFile,"");
   File.AppendAllText(LogFile,DateTime.UtcNow.ToString("o")+" "+message+Environment.NewLine);
  }
  private static void State(bool? active,bool? enabled,string health,string error,int sessions,DetectionSnapshot detection) {
   string json="{\"Version\":\"0.2.0\",\"TimeUtc\":"+Quote(DateTime.UtcNow.ToString("o"))+",\"ProcessId\":"+Process.GetCurrentProcess().Id+
    ",\"RemoteActive\":"+(active.HasValue?active.Value.ToString().ToLowerInvariant():"null")+
    ",\"VirtualDisplayEnabled\":"+(enabled.HasValue?enabled.Value.ToString().ToLowerInvariant():"null")+
    ",\"Health\":"+Quote(health)+",\"Error\":"+Quote(error)+",\"EncoderSessions\":"+sessions+
    ",\"DisconnectDelaySeconds\":"+DelaySeconds+
    ",\"DetectorState\":"+Quote(detection==null?"Stopped":detection.State)+
    ",\"EncodeActivity\":"+(detection!=null && detection.EncodeActivity?"true":"false")+
    ",\"NvmlQueries\":"+(detection==null?0:detection.NvmlQueries)+"}";
   string temporary=StatusFile+".new";
   File.WriteAllText(temporary,json);
   if(File.Exists(StatusFile)) File.Replace(temporary,StatusFile,null); else File.Move(temporary,StatusFile);
  }
  private static bool IsAdmin() {
   return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
  }
  private static void RunScheduler(string arguments) {
   var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"schtasks.exe"),arguments);
   start.UseShellExecute=false;start.CreateNoWindow=true;
   using(var process=Process.Start(start)) {process.WaitForExit(15000);}
  }
  private static int Uninstall() {
   if(!IsAdmin()) {
    var start=new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--uninstall");
    start.UseShellExecute=true;start.Verb="runas";
    Process.Start(start);return 0;
   }
   RunScheduler("/End /TN \""+TaskName+"\"");
   Thread.Sleep(700);
   RunScheduler("/Delete /TN \""+TaskName+"\" /F");
   Native.SetDeviceEnabled(true);
   Log("Automation removed; UU virtual display enabled. UU services and startup settings were not changed.");
   State(null,true,"Stopped",null,0,null);
   return 0;
  }
  [STAThread]
  public static int Main(string[] args) {
   try {
    Native.Configure(ConfigurationManager.AppSettings["DeviceInstanceId"],ConfigurationManager.AppSettings["ServerPath"]);
    if(args.Length==1 && args[0]=="--uninstall") return Uninstall();
    if(!IsAdmin()) return 5;
    Directory.CreateDirectory(Root);
    bool created;
    using(var mutex=new Mutex(true,@"Local\Codex.UUDisplayOnDemand",out created)) {
     if(!created) return 0;
     try {return Watch();} finally {Native.ResetProbe();mutex.ReleaseMutex();}
    }
   } catch(Exception error) {
    try {Log("Fatal: "+error.GetType().Name+": "+error.Message);} catch { }
    return 1;
   }
  }
  private static int Watch() {
   var clock=Stopwatch.StartNew();
   double lastActive=0,lastWritten=-10;
   string previousSignal=null;
   Log("Started v0.2.0. Windows counter gate=2s; active NVML checks=5s; idle NVML queries=0; disconnect grace=20s.");
   using(var detector=new OnDemandDetector()) {
    while(true) {
    bool? active=null,enabled=null;
    string health="OK",errorText=null;
    int sessions=0;
    DetectionSnapshot detection=null;
    try {
     try {
      detection=detector.Poll(clock.Elapsed.TotalSeconds);
      active=detection.Active;sessions=detection.SessionCount;health=detection.Health;errorText=detection.Error;
      if(!active.HasValue || active.Value) lastActive=clock.Elapsed.TotalSeconds;
     } catch(Exception error) {
      health="DetectionError";errorText=error.Message;lastActive=clock.Elapsed.TotalSeconds;
      Native.ResetProbe();
     }
     enabled=Native.DeviceEnabled();
     bool desired=enabled.Value;
     if(active.HasValue && active.Value) desired=true;
     else if(active.HasValue && clock.Elapsed.TotalSeconds-lastActive>=DelaySeconds) desired=false;
     // Unknown activity keeps the current device state and never closes a session.
     if(desired!=enabled.Value) {
      Native.SetDeviceEnabled(desired);
      Log(desired?"Enabled UU virtual display for a confirmed remote session.":"Disabled idle UU virtual display after the disconnect grace period.");
      Thread.Sleep(300);
      enabled=Native.DeviceEnabled();
     }
    } catch(Exception error) {health="DeviceError";errorText=error.Message;}
    string signal=active+"/"+enabled+"/"+health;
    if(signal!=previousSignal || clock.Elapsed.TotalSeconds-lastWritten>=10) {
     State(active,enabled,health,errorText,sessions,detection);
     if(signal!=previousSignal) Log("State "+signal+(errorText==null?"":"; "+errorText));
     previousSignal=signal;lastWritten=clock.Elapsed.TotalSeconds;
    }
    Thread.Sleep(2000);
    }
   }
  }
 }
}
