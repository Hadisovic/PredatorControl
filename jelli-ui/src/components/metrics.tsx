import type { State } from '../bridge/types'
export const metrics: Record<string, {label: string; unit: string}> = {
  cpuTemp: {label: 'CPU', unit: '°C'}, gpuTemp: {label: 'GPU', unit: '°C'},
  cpuFanRpm: {label: 'CPU fan', unit: 'RPM'}, gpuFanRpm: {label: 'GPU fan', unit: 'RPM'},
  cpuUsage: {label: 'CPU use', unit: '%'}, gpuUsage: {label: 'GPU use¹', unit: '%'},
  gpuPowerW: {label: 'GPU power', unit: 'W'}, batteryPercent: {label: 'Battery', unit: '%'},
  powerMode: {label: 'Mode', unit: ''}, fps: {label: 'FPS', unit: ''},
}
export function valueFor(s: State | null, key: string) {
  if (!s) return null
  if (key === 'powerMode') return s.powerMode
  if (key === 'fps') return s.fps
  const v = s.telemetry?.[key]
  return typeof v === 'number' && v >= 0 ? Math.round(v) : null
}
export function Metric({state, name}: {state: State | null; name: string}) {
  const meta = metrics[name] ?? metrics.cpuTemp
  const value = valueFor(state, name)
  return <div className={`metric${typeof value === 'string' ? ' metric-text' : ''}`}><span>{meta.label}</span><strong>{value ?? '—'}<small>{value === null ? '' : meta.unit}</small></strong></div>
}
