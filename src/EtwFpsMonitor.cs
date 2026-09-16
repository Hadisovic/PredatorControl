using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public sealed class EtwFpsMonitor : IDisposable
    {
        private const uint ERROR_SUCCESS = 0;
        private const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
        private const uint EVENT_CONTROL_CODE_DISABLE_PROVIDER = 0;
        private const uint EVENT_TRACE_CONTROL_FLUSH = 3; // ControlTrace code — deliver buffers now
        private const byte TRACE_LEVEL_INFORMATION = 4;
        private const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
        private const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
        private const uint PROCESS_TRACE_MODE_RAW_TIMESTAMP = 0x00001000; // EventHeader.TimeStamp = raw QPC ticks
        private const uint WNODE_FLAG_TRACED_GUID = 0x00020000;

        // Win10 dxgkrnl has no Present_Info — count DXGI presents + kernel flips instead
        private static readonly bool IsWin10 = Environment.OSVersion.Version.Build < 22000;

        // Microsoft-Windows-DxgKrnl provider — kernel present path shared by every graphics API
        private static readonly Guid DxgKrnlProviderId = IsWin10
            ? new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9")
            : new("802EC45A-1E99-4B83-9920-87C98277BA9D");
        private static readonly Guid FlipProviderId =
            new("802EC45A-1E99-4B83-9920-87C98277BA9D");

        // DxgKrnl Present_Info — kernel D3DKMTPresent, once per frame for every present path
        private static readonly ushort EVENT_DXGKRNL_PRESENT_INFO = (ushort)(IsWin10 ? 42 : 184);

        // Keyword bit carrying Present_Info — keeps high-volume kernel GPU events out
        private static readonly ulong DXGKRNL_KEYWORD_PRESENT = IsWin10 ? 0UL : 0x0000000008000000UL;

        // Kernel-side event-id filter — only Present_Info is delivered to the session.
        private const uint EVENT_FILTER_TYPE_EVENT_ID = 0x80000200;
        private const uint ENABLE_TRACE_PARAMETERS_VERSION_2 = 2;

        private const string SessionName = "PredatorControlFpsSession";

        [StructLayout(LayoutKind.Sequential)]
        private struct WNODE_HEADER
        {
            public uint BufferSize;
            public uint ProviderId;
            public ulong HistoricalContext;
            public ulong TimeStamp;
            public Guid Guid;
            public uint ClientContext;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct EVENT_TRACE_PROPERTIES
        {
            public WNODE_HEADER Wnode;
            public uint BufferSize;
            public uint MinimumBuffers;
            public uint MaximumBuffers;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint FlushTimer;
            public uint EnableFlags;
            public int AgeLimit;
            public uint NumberOfBuffers;
            public uint FreeBuffers;
            public uint EventsLost;
            public uint BuffersWritten;
            public uint LogBuffersLost;
            public uint RealTimeBuffersLost;
            public IntPtr LoggerThreadId;
            public uint LogFileNameOffset;
            public uint LoggerNameOffset;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
            public string LoggerName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
            public string LogFileName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_RECORD
        {
            public EVENT_HEADER EventHeader;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_HEADER
        {
            public ushort Size;
            public ushort HeaderType;
            public ushort Flags;
            public ushort EventProperty;
            public uint ThreadId;
            public uint ProcessId;
            public long TimeStamp; // raw QPC ticks when PROCESS_TRACE_MODE_RAW_TIMESTAMP is set
            public Guid ProviderId;
            public ushort Id;
            public byte Version;
            public byte Channel;
            public byte Level;
            public byte Opcode;
            public ushort Task;
            public ulong Keyword;
            public uint KernelTime;
            public uint UserTime;
            public Guid ActivityId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_FILTER_DESCRIPTOR
        {
            public ulong Ptr;
            public uint Size;
            public uint Type;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ENABLE_TRACE_PARAMETERS
        {
            public uint Version;
            public uint EnableProperty;
            public uint ControlFlags;
            public Guid SourceId;
            public IntPtr EnableFilterDesc;
            public uint FilterDescCount;
        }

        [StructLayout(LayoutKind.Explicit, Size = 448)]
        private struct EVENT_TRACE_LOGFILE
        {
            [FieldOffset(8)] public IntPtr LoggerName;
            [FieldOffset(28)] public uint ProcessTraceMode;
            [FieldOffset(400)] public IntPtr BufferCallback;
            [FieldOffset(424)] public IntPtr EventRecordCallback;
            [FieldOffset(440)] public IntPtr Context;
        }

        private delegate void EventRecordCallback([In] ref EVENT_RECORD eventRecord);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint StartTrace(out long sessionHandle,
            string sessionName, ref EVENT_TRACE_PROPERTIES properties);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint StopTrace(long sessionHandle,
            string sessionName, ref EVENT_TRACE_PROPERTIES properties);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ControlTrace(long sessionHandle,
            string? sessionName, ref EVENT_TRACE_PROPERTIES properties, uint controlCode);

        [DllImport("advapi32.dll")]
        private static extern uint EnableTraceEx2(long sessionHandle,
            in Guid providerId, uint controlCode, byte level,
            ulong matchAnyKeyword, ulong matchAllKeyword,
            uint timeout, IntPtr enableParameters);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern long OpenTrace(ref EVENT_TRACE_LOGFILE logfile);

        [DllImport("advapi32.dll")]
        private static extern uint ProcessTrace(long[] handles, uint count,
            IntPtr startTime, IntPtr endTime);

        [DllImport("advapi32.dll")]
        private static extern uint CloseTrace(long traceHandle);

        private long _sessionHandle;
        private long _traceHandle;

        private const int FlushIntervalMs = 200;
        private const int MinFlushFps = 10;
        private System.Threading.Timer? _flushTimer;
        private long _lastFlushTick;
        private volatile int _targetPid = -1;
        private volatile bool _dxgiActiveForCurrentPid;

        private const int RollingWindowSize = 360;
        private readonly long[] _frameTimes = new long[RollingWindowSize];
        private volatile int _frameHead = 0;
        private volatile int _framesFilled = 0;

        private EventRecordCallback? _callbackRef;

        public int TargetPid
        {
            get => _targetPid;
            set
            {
                if (_targetPid == value) return;
                bool wasPaused = _targetPid == 0;
                _targetPid = value;
                _frameHead = 0;
                _framesFilled = 0;
                _dxgiActiveForCurrentPid = false;
                if (_sessionHandle == 0) return;
                if (value == 0)
                    EnableTraceEx2(_sessionHandle, DxgKrnlProviderId, EVENT_CONTROL_CODE_DISABLE_PROVIDER, 0, 0, 0, 0, IntPtr.Zero);
                else if (wasPaused)
                    EnableProvider();
            }
        }

        private void EnableProvider()
        {
            if (IsWin10)
                EnableTraceEx2(_sessionHandle, FlipProviderId, EVENT_CONTROL_CODE_ENABLE_PROVIDER,
                    TRACE_LEVEL_INFORMATION, 0x0000040000000000UL, 0, 0, IntPtr.Zero);

            IntPtr desc = Marshal.AllocHGlobal(Marshal.SizeOf<EVENT_FILTER_DESCRIPTOR>());
            IntPtr eventIdBuf = Marshal.AllocHGlobal(8);
            IntPtr paramsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ENABLE_TRACE_PARAMETERS>());

            try
            {
                Marshal.WriteByte(eventIdBuf, 0, 1);
                Marshal.WriteByte(eventIdBuf, 1, 0);
                Marshal.WriteInt16(eventIdBuf, 2, 1);
                Marshal.WriteInt16(eventIdBuf, 4, (short)EVENT_DXGKRNL_PRESENT_INFO);

                Marshal.StructureToPtr(new EVENT_FILTER_DESCRIPTOR
                {
                    Ptr = (ulong)eventIdBuf.ToInt64(),
                    Size = 6,
                    Type = EVENT_FILTER_TYPE_EVENT_ID,
                }, desc, false);

                Marshal.StructureToPtr(new ENABLE_TRACE_PARAMETERS
                {
                    Version = ENABLE_TRACE_PARAMETERS_VERSION_2,
                    EnableFilterDesc = desc,
                    FilterDescCount = 1,
                }, paramsPtr, false);

                uint hr = EnableTraceEx2(_sessionHandle, DxgKrnlProviderId,
                    EVENT_CONTROL_CODE_ENABLE_PROVIDER,
                    TRACE_LEVEL_INFORMATION, DXGKRNL_KEYWORD_PRESENT, 0, 0, paramsPtr);
                if (hr != ERROR_SUCCESS)
                {
                    Debug.WriteLine($"EnableTraceEx2 filter failed: 0x{hr:X}, falling back to unfiltered");
                    EnableTraceEx2(_sessionHandle, DxgKrnlProviderId,
                        EVENT_CONTROL_CODE_ENABLE_PROVIDER,
                        TRACE_LEVEL_INFORMATION, DXGKRNL_KEYWORD_PRESENT, 0, 0, IntPtr.Zero);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(paramsPtr);
                Marshal.FreeHGlobal(eventIdBuf);
                Marshal.FreeHGlobal(desc);
            }
        }

        public void Start(int targetPid = -1)
        {
            _targetPid = targetPid;

            var stopProps = BuildSessionProperties();
            StopTrace(0, SessionName, ref stopProps);

            var props = BuildSessionProperties();
            uint hr = StartTrace(out _sessionHandle, SessionName, ref props);
            if (hr == 0xB7 /*ERROR_ALREADY_EXISTS*/)
            {
                StopTrace(0, SessionName, ref stopProps);
                hr = StartTrace(out _sessionHandle, SessionName, ref props);
            }
            if (hr != ERROR_SUCCESS)
                throw new InvalidOperationException($"StartTrace failed: 0x{hr:X}");

            EnableProvider();

            _callbackRef = OnEventRecord;
            IntPtr loggerNamePtr = Marshal.StringToHGlobalUni(SessionName);
            try
            {
                var logfile = new EVENT_TRACE_LOGFILE
                {
                    LoggerName = loggerNamePtr,
                    ProcessTraceMode = PROCESS_TRACE_MODE_REAL_TIME |
                                       PROCESS_TRACE_MODE_EVENT_RECORD |
                                       PROCESS_TRACE_MODE_RAW_TIMESTAMP,
                    EventRecordCallback = Marshal.GetFunctionPointerForDelegate(_callbackRef),
                };

                _traceHandle = OpenTrace(ref logfile);
            }
            finally
            {
                Marshal.FreeHGlobal(loggerNamePtr);
            }

            _flushTimer = new System.Threading.Timer(_ => FlushSession(), null, FlushIntervalMs, FlushIntervalMs);

            ProcessTrace(new[] { _traceHandle }, 1, IntPtr.Zero, IntPtr.Zero);
        }

        public void Stop()
        {
            _flushTimer?.Dispose();
            _flushTimer = null;
            long session = _sessionHandle;
            _sessionHandle = 0;
            var props = BuildSessionProperties();
            StopTrace(session, SessionName, ref props);
            CloseTrace(_traceHandle);
        }

        private void FlushSession()
        {
            if (_sessionHandle == 0) return;

            long now = Stopwatch.GetTimestamp();
            bool idleFlushDue = now - _lastFlushTick >= Stopwatch.Frequency;
            if (SampleFps() < MinFlushFps && !idleFlushDue) return;

            _lastFlushTick = now;
            var props = BuildSessionProperties();
            ControlTrace(_sessionHandle, null, ref props, EVENT_TRACE_CONTROL_FLUSH);
        }

        public void Dispose() => Stop();

        private void OnEventRecord(ref EVENT_RECORD record)
        {
            bool flip = IsWin10 && record.EventHeader.ProviderId == FlipProviderId
                && record.EventHeader.Task == 14 && record.EventHeader.Opcode == 1;
            if (!flip && (record.EventHeader.ProviderId != DxgKrnlProviderId
                || record.EventHeader.Id != EVENT_DXGKRNL_PRESENT_INFO)) return;

            int targetPid = _targetPid;
            if (targetPid <= 0 || (int)record.EventHeader.ProcessId != targetPid) return;

            if (flip && _dxgiActiveForCurrentPid) return;
            if (IsWin10 && !flip) _dxgiActiveForCurrentPid = true;

            _frameTimes[_frameHead] = record.EventHeader.TimeStamp;
            _frameHead = (_frameHead + 1) % RollingWindowSize;
            if (_framesFilled < RollingWindowSize) _framesFilled++;
        }

        public double SampleFps()
        {
            int filled = _framesFilled;
            if (filled < 2) return 0;

            long freq = Stopwatch.Frequency;
            int head = _frameHead;
            long newest = _frameTimes[(head - 1 + RollingWindowSize) % RollingWindowSize];

            if (Stopwatch.GetTimestamp() - newest > 4 * freq) return 0;

            long cutoff = newest - freq;
            int count = 1;
            long oldest = newest;
            for (int i = 2; i <= filled; i++)
            {
                long t = _frameTimes[(head - i + RollingWindowSize) % RollingWindowSize];
                if (t < cutoff) break;
                oldest = t;
                count++;
            }

            double elapsed = (double)(newest - oldest) / freq;
            if (elapsed <= 0) return 0;
            return (count - 1) / elapsed;
        }

        private static EVENT_TRACE_PROPERTIES BuildSessionProperties() => new()
        {
            Wnode = new WNODE_HEADER
            {
                BufferSize = (uint)Marshal.SizeOf<EVENT_TRACE_PROPERTIES>(),
                Flags = WNODE_FLAG_TRACED_GUID,
                ClientContext = 1,
            },
            LogFileMode = 0x00000100,
            LogFileNameOffset = 0,
            LoggerNameOffset = (uint)Marshal.OffsetOf<EVENT_TRACE_PROPERTIES>(
                nameof(EVENT_TRACE_PROPERTIES.LoggerName)),
            BufferSize = 8,
            MinimumBuffers = 8,
            MaximumBuffers = 16,
        };
    }
}
