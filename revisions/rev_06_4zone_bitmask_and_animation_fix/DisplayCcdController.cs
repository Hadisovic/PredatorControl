using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    [SupportedOSPlatform("windows")]
    public static class DisplayCcdController
    {
        #region Win32 CCD & GDI Interop

        private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        private const int ERROR_SUCCESS = 0;

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int CDS_UPDATEREGISTRY = 0x01;
        private const int CDS_TEST = 0x02;
        private const int DISP_CHANGE_SUCCESSFUL = 0;

        private const int DM_BITSPERPEL = 0x040000;
        private const int DM_PELSWIDTH = 0x080000;
        private const int DM_PELSHEIGHT = 0x100000;
        private const int DM_DISPLAYFREQUENCY = 0x400000;
        private const int DM_INTERLACED = 0x02;

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        public enum DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY : uint
        {
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER = 0xFFFFFFFF,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HD15 = 0,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SVIDEO = 1,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPOSITE_VIDEO = 2,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPONENT_VIDEO = 3,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI = 4,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI = 5,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS = 6,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_D_JPN = 8,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDI = 9,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL = 10,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED = 11,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EXTERNAL = 12,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED = 13,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDTVDONGLE = 14,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_MIRACAST = 15,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_WIRED = 16,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_VIRTUAL = 17,
            DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL = 0x80000000
        }

        public enum DISPLAYCONFIG_DEVICE_INFO_TYPE : uint
        {
            DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1,
            DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2,
            DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_PREFERRED_MODE = 3,
            DISPLAYCONFIG_DEVICE_INFO_GET_ADAPTER_NAME = 4,
            DISPLAYCONFIG_DEVICE_INFO_SET_TARGET_PERSISTENCE = 5,
            DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_BASE_TYPE = 6
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public DISPLAYCONFIG_DEVICE_INFO_TYPE type;
            public uint size;
            public LUID adapterId;
            public uint id;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string viewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_RATIONAL
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
            public uint rotation;
            public uint scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public uint scanLineOrdering;
            public bool targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_MODE_INFO
        {
            public uint infoType;
            public uint id;
            public LUID adapterId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
            public byte[] modeInfo;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
            public int dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(
            uint flags,
            out uint numPathArrayElements,
            out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(
            uint flags,
            ref uint numPathArrayElements,
            [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
            ref uint numModeInfoArrayElements,
            [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint flags;
            public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
            public ushort edidManufactureId;
            public ushort edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string monitorDevicePath;
        }

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);


        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern int ChangeDisplaySettingsEx(
            string? lpszDeviceName,
            ref DEVMODE lpDevMode,
            IntPtr hwnd,
            uint dwflags,
            IntPtr lParam);

        #endregion

        /// <summary>
        /// Finds the GDI device name (e.g. "\\.\DISPLAY1") of the internal laptop display (eDP/LVDS)
        /// using the Win32 CCD API.
        /// </summary>
        public static string? GetInternalDisplayGdiName()
        {
            try
            {
                int error = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
                if (error != ERROR_SUCCESS || pathCount == 0) return null;

                var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

                error = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (error != ERROR_SUCCESS) return null;

                for (int i = 0; i < pathCount; i++)
                {
                    var tech = paths[i].targetInfo.outputTechnology;
                    if (IsInternalTechnology(tech))
                    {
                        var sourceName = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                        sourceName.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                        sourceName.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                        sourceName.header.adapterId = paths[i].sourceInfo.adapterId;
                        sourceName.header.id = paths[i].sourceInfo.id;

                        if (DisplayConfigGetDeviceInfo(ref sourceName) == ERROR_SUCCESS &&
                            !string.IsNullOrEmpty(sourceName.viewGdiDeviceName))
                        {
                            return sourceName.viewGdiDeviceName;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Report(ex, false);
            }

            return null;
        }

        public static bool IsInternalTechnology(DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY tech)
        {
            return tech == DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL ||
                   tech == DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED ||
                   tech == DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED ||
                   tech == DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS;
        }

        /// <summary>
        /// Returns the current refresh rate (Hz) of the internal panel. Falls back to primary monitor if not detected.
        /// </summary>
        public static int GetCurrentRefreshRate(string? gdiDeviceName = null)
        {
            gdiDeviceName ??= GetInternalDisplayGdiName();
            DEVMODE dm = new();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (EnumDisplaySettings(gdiDeviceName, ENUM_CURRENT_SETTINGS, ref dm))
            {
                return dm.dmDisplayFrequency > 0 ? dm.dmDisplayFrequency : 60;
            }

            // Fallback to primary display if specific display query fails
            if (gdiDeviceName != null && EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
            {
                return dm.dmDisplayFrequency > 0 ? dm.dmDisplayFrequency : 60;
            }

            return 60;
        }

        /// <summary>
        /// Enumerates all supported refresh rates on the internal panel at current resolution geometry.
        /// </summary>
        public static List<int> GetSupportedRefreshRates(string? gdiDeviceName = null)
        {
            var rates = new SortedSet<int>();
            gdiDeviceName ??= GetInternalDisplayGdiName();

            DEVMODE cur = new();
            cur.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (!EnumDisplaySettings(gdiDeviceName, ENUM_CURRENT_SETTINGS, ref cur))
            {
                if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref cur))
                    return new List<int> { 60 };
                gdiDeviceName = null;
            }

            DEVMODE dm = new();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            for (int modeNum = 0; EnumDisplaySettings(gdiDeviceName, modeNum, ref dm); modeNum++)
            {
                if (IsSameGeometry(dm, cur) && dm.dmDisplayFrequency > 0)
                {
                    rates.Add(dm.dmDisplayFrequency);
                }
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            }

            if (rates.Count == 0) rates.Add(60);
            return rates.ToList();
        }

        /// <summary>
        /// Gets the maximum supported refresh rate of the internal display.
        /// </summary>
        public static int GetMaxRefreshRate(string? gdiDeviceName = null)
        {
            var rates = GetSupportedRefreshRates(gdiDeviceName);
            return rates.Count > 0 ? rates[^1] : 60;
        }

        private static bool IsSameGeometry(DEVMODE a, DEVMODE b) =>
            a.dmPelsWidth == b.dmPelsWidth &&
            a.dmPelsHeight == b.dmPelsHeight &&
            a.dmBitsPerPel == b.dmBitsPerPel &&
            (a.dmDisplayFlags & DM_INTERLACED) == 0;

        /// <summary>
        /// Switches the internal display's refresh rate specifically via ChangeDisplaySettingsEx.
        /// </summary>
        public static bool SetRefreshRate(int hz, string? gdiDeviceName = null)
        {
            if (hz <= 0) return false;

            gdiDeviceName ??= GetInternalDisplayGdiName();

            DEVMODE cur = new();
            cur.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (!EnumDisplaySettings(gdiDeviceName, ENUM_CURRENT_SETTINGS, ref cur))
            {
                if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref cur)) return false;
                gdiDeviceName = null;
            }

            if (cur.dmDisplayFrequency == hz) return true;

            DEVMODE dm = new();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            DEVMODE? match = null;

            for (int modeNum = 0; EnumDisplaySettings(gdiDeviceName, modeNum, ref dm); modeNum++)
            {
                if (IsSameGeometry(dm, cur) && dm.dmDisplayFrequency == hz)
                {
                    match = dm;
                    break;
                }
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            }

            if (match == null) return false;

            DEVMODE target = match.Value;
            target.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            target.dmFields = DM_BITSPERPEL | DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;

            if (ChangeDisplaySettingsEx(gdiDeviceName, ref target, IntPtr.Zero, CDS_TEST, IntPtr.Zero) != DISP_CHANGE_SUCCESSFUL)
                return false;

            return ChangeDisplaySettingsEx(gdiDeviceName, ref target, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero) == DISP_CHANGE_SUCCESSFUL;
        }

        #region External Monitor Support

        /// <summary>
        /// Information about a connected external monitor, including its supported refresh rates
        /// as reported by the OS driver — only rates the hardware actually supports are listed.
        /// </summary>
        public record ExternalMonitorInfo(
            string GdiName,
            string FriendlyName,
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY OutputTechnology,
            int CurrentHz,
            List<int> SupportedHz);

        /// <summary>
        /// Returns a list of all currently active external (non-internal) monitors.
        /// Uses the Win32 CCD API to enumerate paths and GDI to get supported refresh rates.
        /// Returns an empty list when no external monitor is connected.
        /// </summary>
        public static List<ExternalMonitorInfo> GetExternalMonitors()
        {
            var result = new List<ExternalMonitorInfo>();
            try
            {
                int error = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
                if (error != ERROR_SUCCESS || pathCount == 0) return result;

                var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
                error = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (error != ERROR_SUCCESS) return result;

                for (int i = 0; i < pathCount; i++)
                {
                    var tech = paths[i].targetInfo.outputTechnology;
                    if (IsInternalTechnology(tech)) continue; // Skip built-in panel

                    // Get the GDI device name (e.g. "\\.\DISPLAY2") via source name query
                    var sourceName = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                    sourceName.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                    sourceName.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                    sourceName.header.adapterId = paths[i].sourceInfo.adapterId;
                    sourceName.header.id = paths[i].sourceInfo.id;

                    if (DisplayConfigGetDeviceInfo(ref sourceName) != ERROR_SUCCESS) continue;
                    string gdiName = sourceName.viewGdiDeviceName;
                    if (string.IsNullOrEmpty(gdiName)) continue;

                    // Get the human-readable monitor name (e.g. "DELL U2723D") via target name query
                    var targetName = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                    targetName.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                    targetName.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
                    targetName.header.adapterId = paths[i].targetInfo.adapterId;
                    targetName.header.id = paths[i].targetInfo.id;

                    string friendlyName;
                    if (DisplayConfigGetDeviceInfo(ref targetName) == ERROR_SUCCESS &&
                        !string.IsNullOrWhiteSpace(targetName.monitorFriendlyDeviceName))
                    {
                        friendlyName = targetName.monitorFriendlyDeviceName.Trim();
                    }
                    else
                    {
                        // Fallback: describe by connection type
                        friendlyName = tech switch
                        {
                            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI => "HDMI Monitor",
                            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL => "DisplayPort Monitor",
                            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI => "DVI Monitor",
                            _ => "External Monitor"
                        };
                    }

                    // Enumerate the supported refresh rates from the OS driver at current resolution
                    int currentHz = GetCurrentRefreshRate(gdiName);
                    var supportedHz = GetSupportedRefreshRates(gdiName);

                    result.Add(new ExternalMonitorInfo(gdiName, friendlyName, tech, currentHz, supportedHz));
                }
            }
            catch (Exception ex)
            {
                Program.Report(ex, false);
            }
            return result;
        }

        /// <summary>
        /// Sets the refresh rate on a specific external monitor identified by its GDI device name.
        /// Only rates returned by GetExternalMonitors().SupportedHz are valid inputs.
        /// </summary>
        public static bool SetExternalRefreshRate(string gdiDeviceName, int hz)
        {
            // Delegates to the existing SetRefreshRate logic — works for any GDI device name
            return SetRefreshRate(hz, gdiDeviceName);
        }

        #endregion
    }
}
