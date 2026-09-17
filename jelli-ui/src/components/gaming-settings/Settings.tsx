import type { State, Preferences } from '../../bridge/types'
import { send } from '../../bridge/client'
import { metrics, valueFor } from '../metrics'
export function Settings({state: s}: {state: State}) {
  const update = (values: Partial<Preferences>) => send('settings', {...s.settings, ...values})
  const toggle = (label: string, key: 'syncRgb' | 'overlayGraphs' | 'overlayPower' | 'overlayUsage') => <button className="toggle-row" role="switch" aria-checked={s.settings[key]} onClick={() => update({[key]: !s.settings[key]})}><span>{label}</span><span className={`switch ${s.settings[key] ? 'on' : ''}`}><i /></span></button>
  return <>
    <section><h2>Your desktop companion</h2><p className="muted">Choose how the controls bloom from Jelli.</p><div className="segments">{(['expand','flyout'] as const).map(mode => <button key={mode} className={s.settings.presentation === mode ? 'selected' : ''} aria-pressed={s.settings.presentation === mode} onClick={() => update({presentation: mode})}>{mode === 'expand' ? 'Expand Jelli' : 'Attached flyout'}</button>)}</div>
    {(['metric1','metric2'] as const).map((slot, i) => <label className="field" key={slot}><span>Floating metric {i + 1}</span><select value={s.settings[slot]} onChange={e => update({[slot]: e.target.value})}>{Object.entries(metrics).filter(([key]) => key === s.settings[slot] || key === 'fps' || valueFor(s, key) !== null).map(([key, m]) => <option key={key} value={key}>{m.label}{key === 'fps' ? ' (when available)' : ''}</option>)}</select></label>)}
    {toggle('Sync keyboard RGB with Jelli', 'syncRgb')}<p className="footnote">Temporary color sync. Manual lighting is restored when disabled. Game Sync keeps priority.</p></section>
    <section><h2>Gaming HUD</h2><p className="muted">Jelli sleeps off-screen while the native benchmark HUD is active. Ctrl + Shift + O brings Jelli back.</p><label className="field"><span>Corner</span><select value={s.settings.overlayCorner} onChange={e => update({overlayCorner: e.target.value})}>{[['topLeft','Top left'],['topRight','Top right'],['bottomLeft','Bottom left'],['bottomRight','Bottom right']].map(([v,l]) => <option value={v} key={v}>{l}</option>)}</select></label>
    <label className="field"><span>Monitor</span><select value={s.settings.overlayMonitor} onChange={e => update({overlayMonitor: e.target.value})}><option value="">Primary display</option>{s.monitors.map(m => <option key={m.id} value={m.id}>{m.label}</option>)}</select></label>
    {toggle('Temperature graphs', 'overlayGraphs')}{toggle('GPU power & battery', 'overlayPower')}{toggle('Utilization', 'overlayUsage')}
    <button className="launch" onClick={() => send('overlay', true)}>Enter gaming mode ↗</button></section>
    <section><h2>About Predator Control</h2><p className="muted">Version {s.version} · Jelli UI<br/>For compatible Acer laptops using the PredatorSense hardware/service stack. Features vary by model.</p><p className="footnote">¹ GPU utilization follows the existing backend estimate. Sensor values are never synthesized by Jelli.</p><button onClick={() => send('legacy')}>Open native fallback controls ↗</button></section>
  </>
}
