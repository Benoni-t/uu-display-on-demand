using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace UUDisplayOnDemand {
 public sealed class EncoderActivity {
  public bool Ready;
  public bool Encoding;
  public int ValidCounters;
  public double Utilization;
 }
 public interface IEncoderActivitySource : IDisposable {
  EncoderActivity Read(uint[] serverProcessIds);
 }
 // PDH reads Windows' GPU accounting counters. It does not initialize NVML.
 public sealed class GpuEncoderActivity : IEncoderActivitySource {
  private const uint MoreData=0x800007D2;
  private const uint NoInstance=0x800007D1;
  private const uint NoData=0x800007D5;
  private IntPtr query,counter;
  private int collections;
  [StructLayout(LayoutKind.Sequential)]
  private struct CounterValue {public uint Status;public double Value;}
  [StructLayout(LayoutKind.Sequential)]
  private struct CounterItem {public IntPtr Name;public CounterValue Value;}
  [DllImport("pdh.dll",CharSet=CharSet.Unicode)]
  private static extern uint PdhOpenQueryW(string source,UIntPtr userData,out IntPtr query);
  [DllImport("pdh.dll",CharSet=CharSet.Unicode)]
  private static extern uint PdhAddEnglishCounterW(IntPtr query,string path,UIntPtr userData,out IntPtr counter);
  [DllImport("pdh.dll")]
  private static extern uint PdhCollectQueryData(IntPtr query);
  [DllImport("pdh.dll",CharSet=CharSet.Unicode)]
  private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter,uint format,ref uint size,out uint count,IntPtr buffer);
  [DllImport("pdh.dll")]
  private static extern uint PdhCloseQuery(IntPtr query);
  private static void Check(uint result,string operation) {
   if(result!=0) throw new InvalidOperationException(operation+" failed: 0x"+result.ToString("X8"));
  }
  public GpuEncoderActivity() {
   Check(PdhOpenQueryW(null,UIntPtr.Zero,out query),"PdhOpenQuery");
   try {
    Check(PdhAddEnglishCounterW(query,@"\GPU Engine(*)\Utilization Percentage",UIntPtr.Zero,out counter),"PdhAddEnglishCounter");
    uint code=PdhCollectQueryData(query);
    if(code!=0 && code!=NoData) Check(code,"PdhCollectQueryData");
    collections=1;
   } catch {Dispose();throw;}
  }
  public static bool IsTargetEncoder(string name,HashSet<uint> serverIds) {
   if(String.IsNullOrEmpty(name) || !name.StartsWith("pid_",StringComparison.OrdinalIgnoreCase) || name.IndexOf("_engtype_VideoEncode",StringComparison.OrdinalIgnoreCase)<0) return false;
   int end=name.IndexOf('_',4);uint pid;
   return end>4 && UInt32.TryParse(name.Substring(4,end-4),out pid) && serverIds.Contains(pid);
  }
  public EncoderActivity Read(uint[] serverProcessIds) {
   if(query==IntPtr.Zero) throw new ObjectDisposedException("GpuEncoderActivity");
   uint code=PdhCollectQueryData(query);collections++;
   if(code==NoData) return new EncoderActivity {Ready=false};
   Check(code,"PdhCollectQueryData");
   var ids=new HashSet<uint>(serverProcessIds);
   for(int attempt=0;attempt<4;attempt++) {
    uint bytes=0,count;
    code=PdhGetFormattedCounterArrayW(counter,0x8200,ref bytes,out count,IntPtr.Zero);
    if(code==NoInstance || code==NoData || (code==0 && bytes==0)) return new EncoderActivity {Ready=collections>1};
    if(code!=MoreData) Check(code,"PdhGetFormattedCounterArray size");
    if(bytes>64*1024*1024) throw new InvalidOperationException("Unexpected GPU counter buffer size.");
    IntPtr buffer=Marshal.AllocHGlobal(checked((int)bytes));
    try {
     code=PdhGetFormattedCounterArrayW(counter,0x8200,ref bytes,out count,buffer);
     if(code==MoreData) continue;
     Check(code,"PdhGetFormattedCounterArray");
     var result=new EncoderActivity();int itemSize=Marshal.SizeOf(typeof(CounterItem));
     for(int i=0;i<count;i++) {
      var item=(CounterItem)Marshal.PtrToStructure(IntPtr.Add(buffer,i*itemSize),typeof(CounterItem));
      if(item.Value.Status>1 || Double.IsNaN(item.Value.Value) || Double.IsInfinity(item.Value.Value)) continue;
      result.ValidCounters++;
      string name=Marshal.PtrToStringUni(item.Name);
      if(IsTargetEncoder(name,ids) && item.Value.Value>0) {
       result.Encoding=true;result.Utilization+=item.Value.Value;
      }
     }
     result.Ready=collections>1 && (result.ValidCounters>0 || count==0);
     return result;
    } finally {Marshal.FreeHGlobal(buffer);}
   }
   throw new InvalidOperationException("GPU counter instances kept changing.");
  }
  public void Dispose() {
   if(query!=IntPtr.Zero) {PdhCloseQuery(query);query=IntPtr.Zero;counter=IntPtr.Zero;}
  }
 }
}
