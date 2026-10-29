using Microsoft.Win32;

namespace PredatorControlApp;

public partial class WmiController
{
    private KeyboardController? _keyboardControl;
    public KeyboardCapabilities Keyboard => Capabilities.Keyboard;

    internal KeyboardController KeyboardControl
    {
        get { lock (HardwareWriteGate.CommandLock) return _keyboardControl ??= new(Keyboard, new AuthorizedKeyboardFirmware(this),
            result => WriteGate.RecordResult(new(result.Status, result.Message))); }
    }

    private sealed class AuthorizedKeyboardFirmware(WmiController owner) : IKeyboardFirmware
    {
        private readonly IKeyboardFirmware _transport = owner._testIo?.Keyboard ?? new KeyboardFirmwareTransport(owner.Capabilities.ProfileId == "nitro-an515-54-octavia");
        public bool TryRead(out uint configuration) => _transport.TryRead(out configuration);
        public bool TryWrite(uint configuration) => owner.Write(
            owner.Capabilities.CanWrite(HardwareFeature.KeyboardPower) ? HardwareFeature.KeyboardPower : HardwareFeature.KeyboardTimeout, true,
            () => Acknowledged(_transport.TryWrite(configuration))).Accepted;
    }
}
