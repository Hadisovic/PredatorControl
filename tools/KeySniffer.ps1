Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Runtime.InteropServices;
public class KSniff2 {
    const int WH_KB_LL=13,WM_KD=0x100,WM_SKD=0x104;
    [StructLayout(LayoutKind.Sequential)] struct KB{public uint vk,sc,fl,t;public UIntPtr ex;}
    [StructLayout(LayoutKind.Sequential)] struct MSG{IntPtr h;public uint msg;UIntPtr wp;IntPtr lp;uint t;int x,y;}
    delegate IntPtr LLKP(int n,IntPtr w,IntPtr l);
    [DllImport("user32")] static extern IntPtr SetWindowsHookEx(int i,LLKP f,IntPtr m,uint t);
    [DllImport("user32")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32")] static extern IntPtr CallNextHookEx(IntPtr h,int n,IntPtr w,IntPtr l);
    [DllImport("kernel32",CharSet=CharSet.Auto)] static extern IntPtr GetModuleHandle(string m);
    [DllImport("user32")] static extern int GetMessage(out MSG m,IntPtr h,uint a,uint b);
    [DllImport("user32")] static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32")] static extern IntPtr DispatchMessage(ref MSG m);
    [DllImport("user32")] static extern void PostQuitMessage(int c);
    static IntPtr _h; static LLKP _p; static StreamWriter _log;
    public static void Run(string lp){
        _log=new StreamWriter(lp,false){AutoFlush=true};
        _log.WriteLine("=== KeySniffer "+DateTime.Now+" ===");
        Console.WriteLine("=== KeySniffer running ===");
        Console.WriteLine(">> Press Predator key or Mode key. ESC to quit. <<");
        Console.WriteLine("VK(hex)  VK  SC(hex)  SC  Name");
        Console.WriteLine("--------------------------------------------");
        _p=Proc; _h=SetWindowsHookEx(WH_KB_LL,_p,GetModuleHandle(null),0);
        if(_h==IntPtr.Zero){Console.WriteLine("HOOK FAILED - Run as Admin");return;}
        Console.WriteLine("Hook OK! Waiting for keypresses...\n");
        MSG m; while(GetMessage(out m,IntPtr.Zero,0,0)!=0){TranslateMessage(ref m);DispatchMessage(ref m);}
        UnhookWindowsHookEx(_h); _log.Close();}
    static IntPtr Proc(int n,IntPtr w,IntPtr l){
        if(n>=0&&(w==(IntPtr)WM_KD||w==(IntPtr)WM_SKD)){
            var k=(KB)Marshal.PtrToStructure(l,typeof(KB));
            string nm=k.vk==0xB7?"LAUNCH_APP2(Predator)":k.vk==0xB6?"LAUNCH_APP1(Nitro)":
                k.vk==0x87?"F24":k.vk==0x86?"F23":k.vk==0x1B?"ESC":"VK_0x"+k.vk.ToString("X2");
            string ln=string.Format("VK=0x{0:X2}({0,-3}) SC=0x{1:X2}({1,-3}) [{2}]",k.vk,k.sc,nm);
            Console.WriteLine(ln); _log.WriteLine("["+DateTime.Now.ToString("HH:mm:ss.fff")+"] "+ln);
            bool hit=k.vk==0xB6||k.vk==0xB7||k.vk==0x86||k.vk==0x87||
                k.sc==0x71||k.sc==0x6C||k.sc==0x6E||k.sc==0x6D||k.sc==0x76||k.sc==0x54;
            if(hit){string s="*** PREDATOR MATCH VK=0x"+k.vk.ToString("X2")+" SC=0x"+k.sc.ToString("X2")+" ***";
                Console.ForegroundColor=ConsoleColor.Green;Console.WriteLine(s);Console.ResetColor();_log.WriteLine(s);}
            if(k.vk==0x1B)PostQuitMessage(0);}
        return CallNextHookEx(_h,n,w,l);}
}
"@ -Language CSharp

$log = Join-Path $PSScriptRoot "KeySniffer.log"
Write-Host "Logging to: $log" -ForegroundColor Cyan
[KSniff2]::Run($log)
Write-Host "Done. Log saved." -ForegroundColor Green
