namespace PredatorControlApp;

// Narrow internal test seam. Production retains the real Windows transports; tests never alter OS identity.
internal interface IHardwareTestIo
{
    bool IsOnAc { get; }
    HardwareOperationResult SetWindowsPowerMode(byte mode);
    HardwareOperationResult SetWindowsPowerPlan(Guid scheme);
    Guid? ReadActiveWindowsPowerPlan() => null;
    ulong? SendNitroSense(ushort command, ulong input, bool wide);
    ulong? ReadGamingMiscSetting(uint input) => null;
    (bool success, ulong output) SendWmi(string method, ulong input);
    bool SendLighting(byte[] payload);
    int? ReadSensor(ulong sensor);
    bool SetBatteryLimit(bool enabled);
    bool? ReadBatteryLimit();
    bool SetGpuMode(int mode);
    IKeyboardFirmware Keyboard { get; }
}
