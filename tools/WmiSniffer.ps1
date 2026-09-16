Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Management;
using System.Text;

public class WmiSniffer {
    static StreamWriter _log;

    public static void Run(string logPath) {
        _log = new StreamWriter(logPath, false) { AutoFlush = true };
        string header = "=== WMI Event Sniffer " + DateTime.Now + " ===";
        Console.WriteLine(header); _log.WriteLine(header);
        Console.WriteLine("Listening to APGeEvent + AcerGenericEvent on root\\wmi");
        Console.WriteLine(">> Press Predator key or Mode key now. Ctrl+C to quit. <<\n");

        // Watcher 1: APGeEvent
        try {
            var scope1 = new ManagementScope(@"root\wmi");
            scope1.Connect();
            var w1 = new ManagementEventWatcher(scope1, new EventQuery("SELECT * FROM APGeEvent"));
            w1.EventArrived += (s, e) => OnEvent("APGeEvent", e);
            w1.Start();
            Console.WriteLine("[OK] APGeEvent watcher started");
            _log.WriteLine("[OK] APGeEvent watcher started");
        } catch(Exception ex) {
            Console.WriteLine("[FAIL] APGeEvent: " + ex.Message);
            _log.WriteLine("[FAIL] APGeEvent: " + ex.Message);
        }

        // Watcher 2: AcerGenericEvent
        try {
            var scope2 = new ManagementScope(@"root\wmi");
            scope2.Connect();
            var w2 = new ManagementEventWatcher(scope2, new EventQuery("SELECT * FROM AcerGenericEvent"));
            w2.EventArrived += (s, e) => OnEvent("AcerGenericEvent", e);
            w2.Start();
            Console.WriteLine("[OK] AcerGenericEvent watcher started");
            _log.WriteLine("[OK] AcerGenericEvent watcher started");
        } catch(Exception ex) {
            Console.WriteLine("[FAIL] AcerGenericEvent: " + ex.Message);
            _log.WriteLine("[FAIL] AcerGenericEvent: " + ex.Message);
        }

        Console.WriteLine("\nWaiting... (press Predator key / Mode key now)\n");
        Console.ReadLine(); // block until user presses Enter to quit
        _log.Close();
    }

    static void OnEvent(string source, ManagementEventArrivedEventArgs e) {
        try {
            var sb = new StringBuilder();
            string ts = DateTime.Now.ToString("HH:mm:ss.fff");
            sb.AppendLine("[" + ts + "] === " + source + " ===");

            // Dump all properties
            foreach (PropertyData p in e.NewEvent.Properties) {
                string val = "";
                if (p.Value is byte[] bytes) {
                    val = "byte[" + bytes.Length + "]: " + BitConverter.ToString(bytes);
                    // Also decode detail[0], detail[1], detail[2]
                    if (bytes.Length >= 2)
                        val += "  --> detail[0]=0x" + bytes[0].ToString("X2") + " detail[1]=0x" + bytes[1].ToString("X2");
                    if (bytes.Length >= 3)
                        val += " detail[2]=0x" + bytes[2].ToString("X2");
                } else {
                    val = (p.Value ?? "null").ToString();
                }
                sb.AppendLine("  " + p.Name + " = " + val);
            }

            string result = sb.ToString();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(result);
            Console.ResetColor();
            _log.Write(result);
        } catch(Exception ex) {
            Console.WriteLine("Error in handler: " + ex.Message);
        }
    }
}
"@ -Language CSharp -ReferencedAssemblies "System.Management"

$log = Join-Path $PSScriptRoot "WmiSniffer.log"
Write-Host "Logging to: $log" -ForegroundColor Cyan
Write-Host "Press Predator key / Mode key, then press ENTER here to quit." -ForegroundColor Yellow
[WmiSniffer]::Run($log)
Write-Host "Done. Log at: $log" -ForegroundColor Green
