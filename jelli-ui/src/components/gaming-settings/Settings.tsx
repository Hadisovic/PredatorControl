import { useState, useEffect } from 'react'
import type { State, Preferences } from '../../bridge/types'
import { send, request } from '../../bridge/client'
import { metrics, valueFor } from '../metrics'

export function Settings({state: s}: {state: State}) {
  const [jelliActive, setJelliActive] = useState(s.settings.jelliEnabled !== false)
  const [isSwitching, setIsSwitching] = useState(false)

  useEffect(() => {
    // Once the backend confirms Jelli is off, clear the switching flag
    if (isSwitching && s.settings.jelliEnabled === false) {
      setIsSwitching(false)
    }
    if (!isSwitching) {
      setJelliActive(s.settings.jelliEnabled !== false)
    }
  }, [s.settings.jelliEnabled, isSwitching])

  const handleToggleJelli = (enable: boolean) => {
    if (isSwitching) return
    setJelliActive(enable)
    if (!enable) {
      setIsSwitching(true)
      // Use request('legacy') — bypasses settings validation entirely, directly calls SetJelliEnabled(false)
      request('legacy').catch(() => setIsSwitching(false))
    } else {
      update({ jelliEnabled: true })
    }
  }

  const currentMode = (s.settings.overlayMode ?? s.overlayMode ?? 'default')
  const currentScale = (s.settings.overlayScale ?? s.overlayScale ?? 100)

  const update = (values: Partial<Preferences>) => send('settings', {...s.settings, ...values})

  const toggle = (label: string, key: 'syncRgb' | 'overlayGraphs' | 'overlayPower' | 'overlayUsage') => (
    <button className="toggle-row" role="switch" aria-checked={s.settings[key]} onClick={() => update({[key]: !s.settings[key]})}>
      <span>{label}</span>
      <span className={`switch ${s.settings[key] ? 'on' : ''}`}><i /></span>
    </button>
  )

  const setOverlayMode = (mode: 'light' | 'default' | 'full' | 'complete') => {
    update({ overlayMode: mode })
  }

  const setOverlayScale = (scale: number) => {
    update({ overlayScale: scale })
  }

  return <>
    <section>
      <h2>Your desktop companion</h2>
      <p className="muted">Choose how the controls bloom from Jelli, or toggle off to use classic controls.</p>

      <button
        className="toggle-row"
        role="switch"
        aria-checked={jelliActive}
        disabled={isSwitching}
        onClick={() => handleToggleJelli(!jelliActive)}
      >
        <span>Jelli Desktop Companion</span>
        <span className={`switch ${jelliActive ? 'on' : ''}`}><i /></span>
      </button>
      <p className="footnote">
        {isSwitching
          ? 'Switching to classic control panel...'
          : jelliActive
          ? 'Turn off to hide Jelli completely and switch back to the classic Predator Control panel.'
          : 'Turn on to display the Jelli companion creature on your desktop.'}
      </p>

      {/* Presentation mode — always accessible so user can switch between Expand/Flyout even while Jelli is off */}
      <div className="segments">
        {(['expand','flyout'] as const).map(mode => (
          <button
            key={mode}
            className={s.settings.presentation === mode ? 'selected' : ''}
            aria-pressed={s.settings.presentation === mode}
            onClick={() => update({presentation: mode})}
          >
            {mode === 'expand' ? 'Expand Jelli' : 'Attached flyout'}
          </button>
        ))}
      </div>
      {(['metric1','metric2'] as const).map((slot, i) => (
        <label className="field" key={slot}>
          <span>Floating metric {i + 1}</span>
          <select value={s.settings[slot] ?? 'off'} onChange={e => update({[slot]: e.target.value})}>
            <option value="off">Off (None)</option>
            {Object.entries(metrics).filter(([key]) => key === s.settings[slot] || key === 'fps' || valueFor(s, key) !== null).map(([key, m]) => (
              <option key={key} value={key}>{m.label}{key === 'fps' ? ' (when available)' : ''}</option>
            ))}
          </select>
        </label>
      ))}
      {toggle('Sync keyboard RGB with Jelli', 'syncRgb')}
      <p className="footnote">Temporary color sync. Manual lighting is restored when disabled. Game Sync keeps priority.</p>
    </section>

    <section>
      <h2>Gaming HUD</h2>
      <p className="muted">Jelli sleeps off-screen while the native benchmark HUD is active. Ctrl + Shift + O toggles the overlay.</p>

      <label className="field"><span>HUD Mode</span></label>
      <div className="segments">
        {(['light', 'default', 'full', 'complete'] as const).map(m => (
          <button
            key={m}
            className={currentMode === m ? 'selected' : ''}
            aria-pressed={currentMode === m}
            onClick={() => setOverlayMode(m)}
          >
            {m === 'light' ? 'Light' : m === 'default' ? 'Default' : m === 'full' ? 'Full' : 'Complete'}
          </button>
        ))}
      </div>

      <label className="field">
        <span>HUD Scale ({currentScale}%)</span>
        <input
          type="range"
          min={50}
          max={200}
          step={5}
          value={currentScale}
          onChange={e => setOverlayScale(Number(e.target.value))}
        />
      </label>

      <label className="field">
        <span>Position preset</span>
        <select value={s.settings.overlayCorner} onChange={e => update({overlayCorner: e.target.value})}>
          {[['topLeft','Top left'],['topRight','Top right'],['bottomLeft','Bottom left'],['bottomRight','Bottom right']].map(([v,l]) => (
            <option value={v} key={v}>{l}</option>
          ))}
        </select>
      </label>

      <label className="field">
        <span>Target display</span>
        <select value={s.settings.overlayMonitor} onChange={e => update({overlayMonitor: e.target.value})}>
          <option value="">Primary display</option>
          {s.monitors.map(m => (
            <option key={m.id} value={m.id}>{m.label}</option>
          ))}
        </select>
      </label>

      <p className="footnote">Tip: Hold Ctrl + Shift + Alt in-game to drag and freely reposition the HUD anywhere on your screen.</p>

      <button className="launch" onClick={() => send('overlay', true)}>
        {s.overlay ? 'Gaming mode active (Ctrl+Shift+O to close)' : 'Enter gaming mode'}
      </button>
    </section>

    <section>
      <h2>About Predator Control</h2>
      <p className="muted">Version {s.version} - Jelli UI<br/>For compatible Acer laptops using the PredatorSense hardware/service stack. Features vary by model.</p>
      <p className="footnote">All telemetry queried locally on-device without internet access.</p>
      <button
        disabled={isSwitching}
        onClick={() => handleToggleJelli(false)}
      >
        {isSwitching ? 'Switching...' : 'Switch to classic control panel'}
      </button>
    </section>
  </>
}
