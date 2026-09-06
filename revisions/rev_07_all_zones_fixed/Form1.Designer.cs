namespace PredatorControlApp
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _telemetryService?.Dispose();
                _predatorKeyHook?.Dispose();
                _wmi?.Dispose();
                _gameSync?.Dispose();
                _trayIcon?.Dispose();
                _colorPicker?.Dispose();
                _fanCurveForm?.Dispose();
                _ipc?.Dispose();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            AutoScaleMode = AutoScaleMode.Dpi;
        }
    }
}
