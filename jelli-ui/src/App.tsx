import { useEffect, useRef, useState } from 'react'
import { connect, request, send, subscribe, reportError } from './bridge/client'
import type { Layout, State } from './bridge/types'
import { BlobCanvas } from './components/jelli/BlobCanvas'
import { Metric, valueFor } from './components/metrics'
import { HardwareControl } from './components/controls/HardwareControl'
import { QuickMenu } from './components/context-menu/QuickMenu'
import { ClickArbiter } from './bridge/clicks'
import { Settings } from './components/gaming-settings/Settings'

const tabs = ['System', 'Lighting', 'Games', 'Settings'] as const
export function App() {
  const [state, setState] = useState<State | null>(null)
  const [layout, setLayout] = useState<Layout>({surface: 'compact', creatureX: 20, creatureY: 0, flyout: false, panelLeft: 180})
  const [tab, setTab] = useState<typeof tabs[number]>('System')
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
  const sections = state?.sections.filter(s => tab === 'System' ? ['power','fans','gpu','display','battery','hardware'].includes(s.id) : tab === 'Lighting' ? s.id === 'rgb' : tab === 'Games' ? s.id === 'games' : s.id === 'app') ?? []
  return <main className={`${layout.surface} ${layout.flyout ? 'flyout' : ''} ${suspended ? 'suspended' : ''}`} onPointerDown={e => { if (e.target === e.currentTarget) send('surface', 'compact') }}>
    <div className="creature" style={{left: layout.creatureX, top: layout.creatureY}}>
      <BlobCanvas onActivate={activate} onContext={context} onDrag={() => clicks.current.cancel()}/>
      <button className="creature-keyboard" aria-label="Open Predator dashboard" onClick={() => send('surface', 'dashboard')} onContextMenu={context}>Open controls</button>
      {(layout.surface === 'compact' || layout.flyout) && <div className="floating-metrics"><Metric state={state} name={state?.settings.metric1 ?? 'cpuTemp'}/><Metric state={state} name={state?.settings.metric2 ?? 'gpuTemp'}/></div>}
    </div>
    {layout.surface === 'compact' && !connected && <button className="reconnect-compact" onClick={retry}>Reconnect</button>}
    {layout.surface === 'compact' && connected && error && <button className="reconnect-compact" title={error} onClick={() => send('surface', 'summary')}>View error</button>}
    {layout.surface !== 'compact' && <div key={layout.surface} className={`glass ${closing ? 'closing' : ''}`} style={layout.flyout ? {left: layout.panelLeft, width: 'min(430px, 100vw)', top: 0, height: '100vh'} : undefined}>
      <header><div><span className="eyebrow"><i aria-hidden="true"/>PREDATOR / JELLI</span><h1>{layout.surface === 'summary' ? 'At a glance' : layout.surface === 'menu' ? 'Quick controls' : 'Make yourself at home.'}</h1><p className="panel-subtitle">{layout.surface === 'summary' ? 'A little pulse check.' : layout.surface === 'menu' ? 'The essentials, within reach.' : 'Your machine. A little more in tune.'}</p></div><button className="close" aria-label="Collapse to Jelli" onClick={() => setClosing(true)}>×</button></header>
      {(!connected || error) && <div role="alert" className="error"><span>{error || 'Backend unavailable'}</span><button onClick={retry}>Reconnect</button>{connected && <button aria-label="Dismiss error" onClick={() => setError('')}>×</button>}</div>}
      {layout.surface === 'menu' && <QuickMenu items={state?.menu ?? []}/>}
      {layout.surface === 'summary' && <div className="summary-content"><div className="metric-grid">{['cpuTemp','gpuTemp','cpuFanRpm','gpuFanRpm','gpuPowerW','batteryPercent','fps'].filter(k => ['cpuTemp','gpuTemp'].includes(k) || valueFor(state,k) !== null).map(k => <Metric key={k} state={state} name={k}/>)}</div><dl><dt>Performance</dt><dd>{state?.powerMode ?? '—'}</dd><dt>Cooling</dt><dd>{state?.fanMode ?? '—'}</dd><dt>GPU</dt><dd>{state?.gpuMode ?? '—'}</dd><dt>Power</dt><dd>{state?.telemetry?.powerLine === 1 ? 'AC connected' : 'Battery'}{state?.telemetry?.isCharging ? ' · charging' : ''}</dd></dl><button className="launch" onClick={() => send('surface', 'dashboard')}>Open full dashboard →</button></div>}
      {layout.surface === 'dashboard' && <><nav aria-label="Dashboard sections">{tabs.map((t, i) => <button key={t} className={tab === t ? 'active' : ''} aria-current={tab === t ? 'page' : undefined} onClick={() => setTab(t)}><span className="nav-index" aria-hidden="true" data-index={`0${i + 1}`}/>{t}</button>)}</nav><div key={tab} className="dashboard-content">
        {tab === 'System' && <div className="system-glance"><Metric state={state} name="cpuTemp"/><Metric state={state} name="gpuTemp"/><span className="status-pill">{state?.powerMode ?? 'Connecting'}</span></div>}
        {sections.map(s => <section key={s.id}><h2>{s.label}</h2>{s.id === 'rgb' && <div className="keyboard-preview" aria-label="Four keyboard lighting zones">{[0,1,2,3].map(z => <button aria-label={`Select zone ${z+1}`} key={z} onClick={() => send('control',{id: `rgb.zone${z}`,value:null})}>{Array.from({length:12},(_,i)=><i key={i}/>)}</button>)}</div>}<div className="controls">{s.controls.map(c => <HardwareControl key={c.id} control={c}/>)}</div>{s.id === 'gpu' && <p className="footnote">{state?.gpuNotice}</p>}{s.id === 'games' && <p className="footnote">{state?.gameSyncStatus}</p>}</section>)}
        {tab === 'Games' && <section><h2>Focus on the game</h2><p className="muted">The native, click-through HUD replaces Jelli entirely. Real ETW frame monitoring. No injection.</p><button className="launch" onClick={() => {void request('overlay',true).catch(reportError)}}>Enter gaming mode ↗</button><p className="footnote">Ctrl + Shift + O to return to Jelli.</p></section>}
        {tab === 'Settings' && state && <Settings state={state}/>}
      </div><footer><span className={`connection ${connected ? 'online' : ''}`}/>{connected ? 'Connected to Predator backend' : 'Disconnected'}<span>JELLI UI</span></footer></>}
    </div>}
  </main>
}
