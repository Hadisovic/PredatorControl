// Browser-only fixture. No service, WMI, registry or hardware access.
const scenario = new URLSearchParams(location.search).get('keyboard') ?? 'binary'
const rgb = scenario === 'rgb'
const unknown = scenario === 'unknown'
const control = (id, label, kind, value, extra = {}) => ({id, label, kind, value, enabled: true, min: 0, max: 100, choices: null, ...extra})
const state = {
  device: rgb ? 'Predator Helios (simulated)' : 'Nitro AN515-54 (simulated)',
  theme: new URLSearchParams(location.search).get('theme') === 'teal' ? 'PredatorTeal' : 'NitroCrimson', version: '1.4.0', revision: 2,
  telemetry: {cpuTemp: 52, gpuTemp: 46, cpuFanRpm: 2180, gpuFanRpm: 1950, batteryPercent: 80, gpuPowerW: null, powerLine: 1},
  powerMode: 'Default', fanMode: 'Auto', gpuMode: 'Hybrid', doubleClickMs: 500, menu: [], monitors: [], overlay: false,
  settings: {presentation: 'expand', metric1: 'cpuTemp', metric2: 'gpuTemp', syncRgb: false, jelliEnabled: true, overlayGraphs: true, overlayPower: true, overlayUsage: true},
  keyboard: {kind: unknown ? 'unknown' : rgb ? 'rgb' : 'binary', supportsRgb: rgb, supportsBrightness: rgb, fixedColor: rgb || unknown ? null : '#ff394b',
    evidence: unknown ? 'Keyboard type is unverified. Select your keyboard type to enable its controls.' : rgb ? 'RGB firmware interface detected.' : 'Nitro keyboard: fixed red backlight with On/Off control. Brightness, colors and RGB effects are unavailable.',
    notice: 'Startup preserves your firmware lighting until you save a compatible preference.'},
  sections: [
    {id: 'power', label: 'Performance', controls: [control('power.quiet', 'Quiet', 'button', false), control('power.balanced', 'Default', 'button', true), control('power.performance', 'Performance', 'button', false)]},
    {id: 'fans', label: 'Cooling', controls: [control('fans.auto', 'Auto', 'button', true), control('fans.max', 'Maximum', 'button', false), control('fans.custom', 'Custom', 'button', false), control('fans.coolboost', 'Acer CoolBoost', 'toggle', false)]},
    {id: 'display', label: 'Display', controls: [control('display.60', '60 Hz', 'button', false), control('display.high', '144 Hz', 'button', true)]},
    {id: 'battery', label: 'Battery care', controls: [control('battery.limit', 'Limit charging to 80%', 'toggle', true)]},
    {id: rgb ? 'rgb' : 'keyboard', label: rgb ? 'Keyboard light' : 'Keyboard backlight', controls: unknown ? [] : rgb ? [
      ...[0,1,2,3].map(i => control(`rgb.zone${i}`, `Zone ${i + 1}`, 'button', i === 0)),
      control('rgb.brightness', 'Brightness', 'range', 80), control('rgb.speed', 'Effect speed', 'range', 50), control('keyboard.timeout', 'Turn off after 30 seconds of inactivity', 'toggle', true)
    ] : [control('keyboard.power', 'Red keyboard backlight', 'toggle', null), control('keyboard.timeout', 'Turn off after 30 seconds of inactivity', 'toggle', null)]},
    {id: 'keyboardProfile', label: 'Keyboard compatibility', controls: rgb || unknown ? [control('keyboard.profile', 'Keyboard type (restart required)', 'select', 0, {choices: ['Automatic / safe default', 'On/off backlight', 'RGB keyboard', 'No backlight'].map((label, value) => ({label, value, enabled: true}))})] : []},
    {id: 'games', label: 'Game Sync', controls: [control('games.enabled', 'Apply profiles when games launch', 'toggle', false), control('games.configure', 'Configure game profiles', 'button', false)]},
    {id: 'app', label: 'Application', controls: [control('app.startup', 'Start with Windows', 'toggle', false)]},
  ],
}
const listeners = new Set()
const emit = m => listeners.forEach(fn => fn({data: {version: 1, ...m}}))
window.chrome = {webview: {
  addEventListener: (_, fn) => listeners.add(fn),
  postMessage: message => setTimeout(() => {
    let data = null
    if (message.action === 'connect') data = state
    if (message.action === 'control') {
      const c = state.sections.flatMap(s => s.controls).find(c => c.id === message.data.id)
      if (c && c.id === 'keyboard.profile') data = {status: 'unverified', message: 'Restart required. No hardware command was sent.'}
      else if (new URLSearchParams(location.search).has('fail')) data = {status: 'failed', message: 'Simulated OEM service unavailable. No setting was saved.'}
      else if (c) { c.value = message.data.value; data = {status: 'confirmed', message: 'Simulated hardware readback confirmed.'} }
    }
    emit({type: 'response', id: message.id, data})
    emit({type: 'state', data: state})
    emit({type: 'layout', data: {surface: message.action === 'surface' ? message.data : 'dashboard', flyout: false, creatureX: (innerWidth - 140) / 2, creatureY: 0, panelLeft: 180}})
  }, 100),
}}
await import('../src/main.tsx')
