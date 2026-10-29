import { useEffect, useRef, useState } from 'react'
import { connect, request, send, subscribe, reportError } from './bridge/client'
import type { Layout, State } from './bridge/types'
import { BlobCanvas } from './components/jelli/BlobCanvas'
import { Metric, valueFor } from './components/metrics'
import { HardwareControl } from './components/controls/HardwareControl'
import { QuickMenu } from './components/context-menu/QuickMenu'
import { ClickArbiter } from './bridge/clicks'
import { Settings } from './components/gaming-settings/Settings'

import { tabs, restoreTab, sectionTab, canPreviewZones } from './bridge/dashboard'
export function App() {
  const [state, setState] = useState<State | null>(null)
  const [layout, setLayout] = useState<Layout>({surface: 'compact', creatureX: 20, creatureY: 0, flyout: false, panelLeft: 180})
  const [tab, setTab] = useState<typeof tabs[number]>(() => {
    try { return restoreTab(localStorage.getItem('jelli-active-tab')) }
    catch { return 'Overview' }
  })
  const contentRef = useRef<HTMLDivElement>(null)
  const tabScrollMap = useRef<Record<string, number>>({})

  const switchTab = (t: typeof tabs[number]) => {
    setTab(t)
    try { localStorage.setItem('jelli-active-tab', t) } catch { /* Navigation works without local storage. */ }
  }

  const onContentScroll = () => {
    if (contentRef.current) {
      tabScrollMap.current[tab] = contentRef.current.scrollTop
    }
  }

  useEffect(() => {
    if (contentRef.current) {
      const targetY = tabScrollMap.current[tab] ?? 0
      contentRef.current.scrollTop = targetY
    }
  }, [tab])
  const [error, setError] = useState('')
  const [connected, setConnected] = useState(false)
  const [suspended, setSuspended] = useState(false)
  const [closing, setClosing] = useState(false)
  const clicks = useRef(new ClickArbiter())
  const lastState = useRef(Date.now())
  const retry = () => { void connect().then(s => {setState(s); setConnected(true); setError(''); lastState.current = Date.now()}).catch(e => {setConnected(false); reportError(e)}) }
  useEffect(() => {
    retry()
    const unsub = subscribe(m => {
      if (m.type === 'state') {setState(m.data as State); setConnected(true); lastState.current = Date.now()}
      if (m.type === 'layout') { setLayout(m.data as Layout); setClosing(false) }
      if (m.type === 'suspend') setSuspended(!!m.data)
      if (m.type === 'error') setError(m.error ?? 'Backend error')
    })
    const onError = (e: Event) => setError((e as CustomEvent<string>).detail)
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Tab') document.body.classList.add('keyboard-navigation'); if (e.key === 'Escape') send('surface', 'compact') }
    const onPointer = () => document.body.classList.remove('keyboard-navigation')
    const onContext = (e: MouseEvent) => e.preventDefault()
    window.addEventListener('pointerdown', onPointer); window.addEventListener('jelli-error', onError); window.addEventListener('keydown', onKey); window.addEventListener('contextmenu', onContext)
    const watchdog = setInterval(() => {if (!document.hidden && Date.now() - lastState.current > 6000) retry()}, 5000)
    return () => { unsub(); clearInterval(watchdog); clicks.current.cancel(); window.removeEventListener('pointerdown', onPointer); window.removeEventListener('jelli-error', onError); window.removeEventListener('keydown', onKey); window.removeEventListener('contextmenu', onContext) }
  }, [])
  useEffect(() => {
    if (!closing) return
    const delay = matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 150
    const timer = setTimeout(() => send('surface', 'compact'), delay)
    return () => clearTimeout(timer)
  }, [closing])
  const activate = () => clicks.current.click(state?.doubleClickMs ?? 500,
    () => send('surface', layout.surface === 'summary' ? 'compact' : 'summary'),
    () => send('surface', 'dashboard'))
  const context = () => { clicks.current.cancel(); send('surface', 'menu') }
  const sections = state?.sections.filter(s => sectionTab(s.id) === tab) ?? []
  const isNitro = state?.device?.toLowerCase().includes('nitro') || state?.device?.startsWith('AN')
  const brand = isNitro ? 'NITRO' : 'PREDATOR'
  const versionLabel = state ? `${state.version}${state.revision ? ` rev ${state.revision}` : ''}` : '1.4.0 rev 2'
  return <main className={`${state?.theme === 'NitroCrimson' ? 'theme-crimson' : state?.theme === 'OledBlack' ? 'theme-oled' : 'theme-teal'} ${layout.surface} ${layout.flyout ? 'flyout' : ''} ${suspended ? 'suspended' : ''}`} onPointerDown={e => { if (e.target === e.currentTarget) send('surface', 'compact') }}>
    <div className="creature" style={{left: layout.creatureX, top: layout.creatureY}}>
      <BlobCanvas onActivate={activate} onContext={context} onDrag={() => clicks.current.cancel()}/>
      <button className="creature-keyboard" aria-label="Open laptop dashboard" onClick={() => send('surface', 'dashboard')} onContextMenu={context}>Open controls</button>
      {(layout.surface === 'compact' || layout.flyout) && (
        <div className="floating-metrics">
          {state?.settings.metric1 && state.settings.metric1 !== 'off' && state.settings.metric1 !== 'none' && (
            <Metric state={state} name={state.settings.metric1} />
          )}
          {state?.settings.metric2 && state.settings.metric2 !== 'off' && state.settings.metric2 !== 'none' && (
            <Metric state={state} name={state.settings.metric2} />
          )}
        </div>
      )}
    </div>
    {layout.surface === 'compact' && !connected && <button className="reconnect-compact" onClick={retry}>Reconnect</button>}
    {layout.surface === 'compact' && connected && error && <button className="reconnect-compact" title={error} onClick={() => send('surface', 'summary')}>View error</button>}
    {layout.surface !== 'compact' && <div key={layout.surface} className={`glass ${closing ? 'closing' : ''}`} style={layout.flyout ? {left: layout.panelLeft, width: 'min(430px, calc(100vw - 180px))', top: 0, height: '100vh'} : undefined}>
      <header><div><span className="eyebrow"><i aria-hidden="true"/>{brand} CONTROL <span className="brand-divider">/</span> JELLI</span><h1>{layout.surface === 'summary' ? 'At a glance' : layout.surface === 'menu' ? 'Quick controls' : tab}</h1><p className="panel-subtitle">{state?.device && state.device !== 'Unknown' ? state.device : 'Your desktop companion'} <span className="version-label">v{versionLabel}</span></p></div><button className="close" aria-label="Collapse to Jelli" onClick={() => setClosing(true)}>×</button></header>
      {(!connected || error) && <div role="alert" className="error"><span>{error || 'Backend unavailable'}</span><button onClick={retry}>Reconnect</button>{connected && <button aria-label="Dismiss error" onClick={() => setError('')}>×</button>}</div>}
      {layout.surface === 'menu' && <QuickMenu items={state?.menu ?? []}/>}
      {layout.surface === 'summary' && <div className="summary-content"><div className="metric-grid">{['cpuTemp','gpuTemp','cpuFanRpm','gpuFanRpm','gpuPowerW','batteryPercent','fps'].filter(k => ['cpuTemp','gpuTemp'].includes(k) || valueFor(state,k) !== null).map(k => <Metric key={k} state={state} name={k}/>)}</div><dl><dt>Performance</dt><dd>{state?.powerMode ?? '—'}</dd><dt>Cooling</dt><dd>{state?.fanMode ?? '—'}</dd><dt>GPU</dt><dd>{state?.gpuMode ?? '—'}</dd><dt>Power</dt><dd>{state?.telemetry?.powerLine === 1 ? 'AC connected' : 'Battery'}{state?.telemetry?.isCharging ? ' · charging' : ''}</dd></dl><button className="launch" onClick={() => send('surface', 'dashboard')}>Open full dashboard →</button></div>}
      {layout.surface === 'dashboard' && <>
        <nav aria-label="Dashboard sections">{tabs.map((t, i) => <button key={t} className={tab === t ? 'active' : ''} aria-current={tab === t ? 'page' : undefined} onClick={() => switchTab(t)}><span className="tab-number" aria-hidden="true">0{i + 1}</span>{t}</button>)}</nav>
        <div ref={contentRef} onScroll={onContentScroll} className="dashboard-content" id="dashboard-panel" aria-label={tab}>
          {tab === 'Overview' && <>
            <div className="overview-heading"><span className="eyebrow">LIVE READINGS</span><span className="status-pill">{state?.powerMode || 'Connecting'}</span></div>
            <div className="telemetry-cards">{['cpuTemp', 'gpuTemp', 'cpuFanRpm', 'gpuFanRpm', 'batteryPercent', 'gpuPowerW'].map(k => <Metric key={k} state={state} name={k}/>)}</div>
            <div className="quick-actions"><button onClick={() => switchTab('Cooling')}><span>Cooling</span><strong>{state?.fanMode || 'Unavailable'}</strong><small aria-hidden="true">↗</small></button><button onClick={() => switchTab('Keyboard')}><span>Keyboard</span><strong>{state?.keyboard?.kind === 'binary' ? 'On/off backlight' : state?.keyboard?.supportsRgb ? 'RGB lighting' : 'Check compatibility'}</strong><small aria-hidden="true">↗</small></button></div>
          </>}
          {tab === 'Cooling' && <div className="section-intro"><span className="cooling-state">{state?.fanMode || 'Unavailable'}</span><div className="inline-metrics"><Metric state={state} name="cpuTemp"/><Metric state={state} name="gpuTemp"/></div></div>}
          {tab === 'Keyboard' && <div className="keyboard-intro"><span className="eyebrow">{state?.keyboard?.supportsRgb ? 'RGB KEYBOARD' : state?.keyboard?.kind === 'binary' ? (state.keyboard.fixedColor ? 'RED BACKLIGHT' : 'ON / OFF BACKLIGHT') : 'KEYBOARD UNVERIFIED'}</span><span className="compact-hint">{state?.keyboard?.kind === 'binary' ? 'No brightness adjustment' : 'Hardware controls'}</span></div>}
          <div className="section-grid">{sections.map(s => s.id === 'keyboardProfile'
            ? <section className="hardware-card card-keyboardProfile" key={s.id}><details><summary>Keyboard compatibility</summary><p className="footnote">{state?.keyboard?.evidence}</p><p className="footnote" role="status">{state?.keyboard?.notice}</p><div className="controls">{s.controls.map(c => <HardwareControl key={c.id} control={c}/>)}</div>{state?.keyboard?.kind === 'binary' && <p className="footnote">With idle sleep enabled, typing wakes the backlight. Its enabled preference stays on.</p>}</details></section>
            : <section className={`hardware-card card-${s.id}`} key={s.id}><div className="card-heading"><h2>{s.label}</h2><span aria-hidden="true" className="card-indicator"/></div>
            {s.id === 'rgb' && canPreviewZones(state?.keyboard?.supportsRgb ?? false, s.controls.map(c => c.id)) && <div className="keyboard-preview" aria-label="Four keyboard lighting zones">{[0,1,2,3].map(z => <button aria-label={`Select zone ${z+1}`} key={z} onClick={() => send('control',{id: `rgb.zone${z}`,value:null})}>{Array.from({length:12},(_,i)=><i key={i}/>)}</button>)}</div>}
            {s.id === 'keyboard' && state?.keyboard?.fixedColor && <div className="single-color-keyboard" style={{color: state.keyboard.fixedColor}} aria-label="Fixed red single-color keyboard backlight"><div>{Array.from({length:42},(_,i) => <i key={i}/>)}</div><span>RED · SINGLE COLOR</span></div>}
            <div className="controls">{s.controls.map(c => <HardwareControl key={c.id} control={c}/>)}</div>
            {s.id === 'gpu' && <p className="footnote">{state?.gpuNotice}</p>}{s.id === 'games' && <p className="footnote">{state?.gameSyncStatus}</p>}{s.id === 'battery' && state?.batteryNotice && <details className="card-details"><summary>Battery health</summary><p className="footnote">{state.batteryNotice}</p></details>}
            {s.id === 'keyboard' && s.controls.length === 0 && <p className="muted">Choose your keyboard type under compatibility below.</p>}
          </section>)}</div>
          {tab === 'Games' && <section className="hardware-card game-card"><h2>Gaming overlay</h2><p className="muted">Show live metrics while you play.</p><button className="launch" onClick={() => {void request('overlay',true).catch(reportError)}}>Open gaming overlay ↗</button><p className="footnote">Ctrl + Shift + O toggles the overlay.</p></section>}
          {tab === 'Settings' && state && <Settings state={state}/>}
        </div>
        <footer><span className={`connection ${connected ? 'online' : ''}`}/>{connected ? 'Connected to your laptop' : 'Disconnected'}<span>JELLI · {versionLabel}</span></footer>
      </>}

    </div>}
  </main>
}
