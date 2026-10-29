using Microsoft.Win32;

namespace PredatorControlApp;

public partial class WmiController
{
    private KeyboardCapabilities? _keyboard;
    private KeyboardController? _keyboardControl;
    public KeyboardCapabilities Keyboard
    {
        get
        {
            lock (_lock)
            {
                if (_keyboard != null) return _keyboard;
                var identity = GetSystemIdentity();
                bool rgbInterface = false;
                try
                {
                    // Method metadata is read-only; no firmware probing writes.
                    var obj = GetWmiObject();
                    using var parameters = obj?.GetMethodParameters("SetGamingRgbKb");
                    rgbInterface = parameters != null;
                }
                catch { /* Unknown is a safe, explicit capability state. */ }
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
                return _keyboard = KeyboardCapabilities.Detect(identity.Manufacturer, identity.Model, rgbInterface,
                    key?.GetValue("KeyboardProfile") as string);
            }
        }
    }

    internal KeyboardController KeyboardControl
    {
        get { lock (_lock) return _keyboardControl ??= new(Keyboard, new KeyboardFirmwareTransport()); }
    }
}
