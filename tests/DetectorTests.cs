using System;
using System.Collections.Generic;
using UUDisplayOnDemand;

public static class DetectorTests {
 sealed class FakeActivity : IEncoderActivitySource {
  public bool Ready=true, Encoding, Fail, Disposed;
  public EncoderActivity Read(uint[] ids) {
   if(Fail) throw new InvalidOperationException("counter unavailable");
   return new EncoderActivity {Ready=Ready,Encoding=Encoding};
  }
  public void Dispose() {Disposed=true;}
 }
 static int assertions;
 static void Check(bool condition,string message) {
  assertions++;if(!condition) throw new Exception(message);
 }
 public static int Main() {
  var source=new FakeActivity();int queries=0,releases=0;bool remote=false,failProbe=false;
  using(var detector=new OnDemandDetector(source,()=>new uint[]{42},()=> {
   queries++;if(failProbe) throw new InvalidOperationException("vendor unavailable");
   return new ProbeResult {Active=remote,SessionCount=remote?1:0};
  },()=>{releases++;})) {
   for(int i=0;i<10000;i++) Check(detector.Poll(i*2).Active==false,"idle state");
   Check(queries==0 && detector.NvmlQueries==0,"healthy idle must never query NVML");
   source.Ready=false;Check(detector.Poll(20000).Active==null,"warm-up is unknown");
   Check(queries==0,"warm-up must not query NVML");source.Ready=true;
   source.Fail=true;Check(detector.Poll(20002).Health=="CounterError","counter failure is unknown");
   Check(queries==0,"counter failure must not query NVML");source.Fail=false;
   source.Encoding=true;remote=true;
   Check(detector.Poll(20004).Active==true && queries==1,"encoder candidate confirms a connection");
   source.Encoding=false;
   Check(detector.Poll(20006).Active==true && queries==1,"static remote desktop remains connected");
   Check(detector.Poll(20010).Active==true && queries==2,"confirmed session rechecked after five seconds");
   source.Fail=true;
   Check(detector.Poll(20016).Active==true,"working NVML preserves a session during counter failure");source.Fail=false;
   failProbe=true;
   Check(detector.Poll(20022).Active==null,"vendor error must not be treated as disconnected");
   failProbe=false;remote=false;
   Check(detector.Poll(20028).Active==false,"disconnect confirmed even with no encoder activity");
   int afterDisconnect=queries;
   for(int i=0;i<100;i++) detector.Poll(20030+i*2);
   Check(queries==afterDisconnect && releases>=2,"idle releases NVML and stops vendor queries");
   source.Encoding=true;
   Check(detector.Poll(20230).Active==false,"unrelated/unsupported encoder is not a connection");
   int negative=queries;detector.Poll(20232);
   Check(queries==negative,"negative candidates are rate limited");
   failProbe=true;
   Check(detector.Poll(20236).Active==null,"candidate query error is unknown");
   source.Encoding=false;
   Check(detector.Poll(20242).Active==false,"ended candidate returns to idle without a new vendor query");
  }
  Check(source.Disposed,"counter disposed");
  var pids=new HashSet<uint>{42};
  Check(GpuEncoderActivity.IsTargetEncoder("pid_42_luid_0x0_phys_0_eng_1_engtype_VideoEncode",pids),"matching encode engine");
  Check(!GpuEncoderActivity.IsTargetEncoder("pid_420_luid_0x0_engtype_VideoEncode",pids),"PID matching must be exact");
  Check(!GpuEncoderActivity.IsTargetEncoder("pid_42_luid_0x0_engtype_3D",pids),"3D engine is not encoding");
  Check(!GpuEncoderActivity.IsTargetEncoder("pid_7_luid_0x0_engtype_VideoEncode",pids),"other process is ignored");
  Console.WriteLine("PASS: "+assertions+" assertions; no native device or NVIDIA calls.");return 0;
 }
}
