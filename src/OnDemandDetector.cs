using System;

namespace UUDisplayOnDemand {
 public sealed class DetectionSnapshot {
  public bool? Active;
  public int SessionCount;
  public bool EncodeActivity;
  public long NvmlQueries;
  public string Health="OK";
  public string State="Idle";
  public string Error;
 }
 // Windows counters are the idle gate. NVML is consulted only for a candidate
 // encoder or an already confirmed remote session, never for healthy idle polls.
 public sealed class OnDemandDetector : IDisposable {
  private readonly IEncoderActivitySource activity;
  private readonly Func<uint[]> getServerIds;
  private readonly Func<ProbeResult> probe;
  private readonly Action closeProbe;
  private bool connected;
  private int sessions;
  private double nextProbe;
  private string probeError;
  public long NvmlQueries {get;private set;}
  public OnDemandDetector() : this(new GpuEncoderActivity(),Native.GetServerProcessIds,Native.Probe,Native.ResetProbe) { }
  public OnDemandDetector(IEncoderActivitySource source,Func<uint[]> ids,Func<ProbeResult> vendorProbe,Action release) {
   if(source==null || ids==null || vendorProbe==null || release==null) throw new ArgumentNullException();
   activity=source;getServerIds=ids;probe=vendorProbe;closeProbe=release;
  }
  public DetectionSnapshot Poll(double seconds) {
   var result=new DetectionSnapshot();
   EncoderActivity sample=null;string counterError=null;
   try {sample=activity.Read(getServerIds());} catch(Exception error) {counterError=error.Message;}
   bool ready=sample!=null && sample.Ready;
   bool encoding=ready && sample.Encoding;
   result.EncodeActivity=encoding;
   bool shouldProbe=seconds>=nextProbe && (connected || encoding);
   if(shouldProbe) {
    NvmlQueries++;nextProbe=seconds+5;
    try {
     var current=probe();connected=current.Active;sessions=current.SessionCount;probeError=null;
     if(!connected) closeProbe(); // Release NVIDIA resources immediately on idle.
    } catch(Exception error) {
     probeError=error.Message;
     closeProbe();
    }
   }
   // A failed candidate does not cause repeated vendor calls after GPU work ends.
   if(!connected && !encoding && seconds>=nextProbe) probeError=null;
   result.NvmlQueries=NvmlQueries;result.SessionCount=sessions;
   if(probeError!=null) {
    result.Active=null;result.Health="VendorQueryError";result.State="Uncertain";result.Error=probeError;
   } else if(connected) {
    result.Active=true;result.State="Connected";
    if(counterError!=null) {result.Health="CounterWarning";result.Error=counterError;}
   } else if(!ready) {
    result.Active=null;result.State=counterError==null?"WarmingUp":"CounterUnavailable";
    result.Health=counterError==null?"Starting":"CounterError";result.Error=counterError;
   } else {
    result.Active=false;result.State="Idle";
   }
   return result;
  }
  public void Dispose() {activity.Dispose();closeProbe();}
 }
}
