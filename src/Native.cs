using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace UUDisplayOnDemand {
 public sealed class ProbeResult {
  public bool Active;
  public int SessionCount;
  public int ServerProcessCount;
 }
 public static class Native {
  private static string DeviceId;
  private const string DeviceName = "GameViewer Virtual Display Adapter";
  private static string ServerPath;
  public static void Configure(string deviceId, string serverPath) {
   if(String.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("A virtual display instance ID is required.");
   if(String.IsNullOrWhiteSpace(serverPath) || !Path.IsPathRooted(serverPath) || !String.Equals(Path.GetFileName(serverPath),"GameViewerServer.exe",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("An absolute GameViewerServer.exe path is required.");
   DeviceId=deviceId;
   ServerPath=Path.GetFullPath(serverPath);
  }
  private static bool initialized;
  [StructLayout(LayoutKind.Sequential)]
  private struct EncoderSession {
   public uint sessionId, pid, vgpuInstance, codecType, hResolution, vResolution, averageFps, averageLatency;
  }
  [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)]
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  private static extern int nvmlInit_v2();
  [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)]
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  private static extern int nvmlShutdown();
  [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)]
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  private static extern int nvmlDeviceGetCount_v2(out uint count);
  [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)]
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
  [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)]
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  private static extern int nvmlDeviceGetEncoderSessions(IntPtr device, ref uint count, IntPtr sessions);
  [DllImport("cfgmgr32.dll", CharSet=CharSet.Unicode)]
  private static extern uint CM_Locate_DevNodeW(out uint node, string deviceId, uint flags);
  [DllImport("cfgmgr32.dll")]
  private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);
  [DllImport("cfgmgr32.dll", CharSet=CharSet.Unicode)]
  private static extern uint CM_Get_DevNode_Registry_PropertyW(uint node, uint property, out uint type, byte[] buffer, ref uint length, uint flags);
  [DllImport("cfgmgr32.dll")]
  private static extern uint CM_Enable_DevNode(uint node, uint flags);
  [DllImport("cfgmgr32.dll")]
  private static extern uint CM_Disable_DevNode(uint node, uint flags);
  [DllImport("kernel32.dll", SetLastError=true)]
  private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder path, ref uint length);
  [DllImport("kernel32.dll")]
  private static extern bool CloseHandle(IntPtr handle);
  private static string ReadProcessPath(uint pid) {
   IntPtr process=OpenProcess(0x1000,false,pid);
   if(process==IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
   try {
    var path=new StringBuilder(4096);uint length=(uint)path.Capacity;
    if(!QueryFullProcessImageNameW(process,0,path,ref length)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    return path.ToString();
   } finally {CloseHandle(process);}
  }
  private static void Check(int result, string operation) {
   if(result != 0) throw new InvalidOperationException(operation + " returned NVML error " + result);
  }
  public static void ResetProbe() {
   if(initialized) {nvmlShutdown(); initialized=false;}
  }
  public static ProbeResult Probe() {
   if(!initialized) {Check(nvmlInit_v2(), "nvmlInit"); initialized=true;}
   var serverIds = new HashSet<uint>();
   foreach(var process in Process.GetProcessesByName("GameViewerServer")) {
    using(process) {
     try {
      if(String.Equals(ReadProcessPath((uint)process.Id), ServerPath, StringComparison.OrdinalIgnoreCase)) serverIds.Add((uint)process.Id);
     } catch(InvalidOperationException) { } // Process exited during observation.
    }
   }
   uint deviceCount; Check(nvmlDeviceGetCount_v2(out deviceCount), "nvmlDeviceGetCount");
   int matches=0, supported=0;
   for(uint i=0;i<deviceCount;i++) {
    IntPtr device; Check(nvmlDeviceGetHandleByIndex_v2(i,out device), "nvmlDeviceGetHandle");
    uint count=0;
    int code=nvmlDeviceGetEncoderSessions(device,ref count,IntPtr.Zero);
    if(code==3) continue; // A GPU without support does not mask errors on the supported GPU.
    if(code!=0 && code!=7) Check(code,"nvmlDeviceGetEncoderSessions");
    supported++;
    if(count==0) continue;
    for(int attempt=0;attempt<4;attempt++) {
     if(count>4096) throw new InvalidOperationException("Unexpected encoder session count.");
     int size=Marshal.SizeOf(typeof(EncoderSession));
     IntPtr buffer=Marshal.AllocHGlobal(checked((int)count*size));
     try {
      code=nvmlDeviceGetEncoderSessions(device,ref count,buffer);
      if(code==7) continue;
      Check(code,"nvmlDeviceGetEncoderSessions");
      for(int j=0;j<count;j++) {
       var session=(EncoderSession)Marshal.PtrToStructure(IntPtr.Add(buffer,j*size),typeof(EncoderSession));
       if(serverIds.Contains(session.pid)) matches++;
      }
      break;
     } finally {Marshal.FreeHGlobal(buffer);}
    }
    if(code==7) throw new InvalidOperationException("Encoder session list kept changing.");
   }
   if(supported==0) throw new InvalidOperationException("No supported NVIDIA encoder session query is available.");
   return new ProbeResult {Active=matches>0,SessionCount=matches,ServerProcessCount=serverIds.Count};
  }
  private static uint LocateValidatedDevice() {
   uint node;
   uint code=CM_Locate_DevNodeW(out node,DeviceId,0);
   if(code!=0) throw new InvalidOperationException("UU virtual display was not found: " + code);
   var buffer=new byte[2048]; uint length=(uint)buffer.Length,type;
   code=CM_Get_DevNode_Registry_PropertyW(node,13,out type,buffer,ref length,0);
   if(code!=0) {length=(uint)buffer.Length;code=CM_Get_DevNode_Registry_PropertyW(node,1,out type,buffer,ref length,0);}
   if(code!=0) throw new InvalidOperationException("Cannot verify virtual display identity: " + code);
   string name=Encoding.Unicode.GetString(buffer,0,(int)length).TrimEnd('\0');
   if(!String.Equals(name,DeviceName,StringComparison.Ordinal)) throw new InvalidOperationException("Refusing to change an unexpected display device.");
   return node;
  }
  public static bool DeviceEnabled() {
   uint node=LocateValidatedDevice(),status,problem;
   uint code=CM_Get_DevNode_Status(out status,out problem,node,0);
   if(code!=0) throw new InvalidOperationException("Cannot query virtual display: " + code);
   if(problem!=0 && problem!=22) throw new InvalidOperationException("Virtual display has an unrelated device problem: " + problem);
   return problem!=22;
  }
  public static void SetDeviceEnabled(bool enabled) {
   uint node=LocateValidatedDevice();
   // A normal disable is intentionally nonpersistent: a reboot restores remote availability
   // if this local helper fails to start. The helper disables it again when idle.
   uint code=enabled ? CM_Enable_DevNode(node,0) : CM_Disable_DevNode(node,0);
   if(code!=0) throw new InvalidOperationException("Virtual display change failed: " + code);
  }
 }
}
