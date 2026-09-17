import { useEffect, useState } from 'react'
import type { Control } from '../../bridge/types'
import { request, reportError } from '../../bridge/client'
export function HardwareControl({control: c}: {control: Control}) {
  const [busy, setBusy] = useState(false)
  const [value, setValue] = useState(c.value)
  useEffect(() => setValue(c.value), [c.value])
  const commit = async (v: unknown) => {
    setBusy(true)
    try { await request('control', {id: c.id, value: v}) } catch (e) { reportError(e); setValue(c.value) }
    finally { setBusy(false) }
  }
  const disabled = !c.enabled || busy
  if (c.kind === 'button') return <button className={`mode ${c.value ? 'selected' : ''}`} disabled={disabled} aria-pressed={!!c.value} onClick={() => void commit(null)}>{c.accent && <i className="swatch" style={{backgroundColor:c.accent}}/>}{c.label}</button>
  if (c.kind === 'toggle') return <button className="toggle-row" role="switch" aria-checked={!!c.value} disabled={disabled} onClick={() => void commit(!c.value)}><span>{c.label}</span><span className={`switch ${c.value ? 'on' : ''}`}><i /></span></button>
  if (c.kind === 'select') return <label className="field"><span>{c.label}</span><select disabled={disabled} value={Number(c.value)} onChange={e => void commit(Number(e.target.value))}>{c.choices?.map(o => <option key={o.value} value={o.value} disabled={!o.enabled}>{o.label}</option>)}</select></label>
  if (c.kind === 'color') return <label className="field"><span>{c.label}</span><input aria-label={c.label} type="color" value={String(value)} disabled={disabled} onChange={e => {setValue(e.target.value); void commit(e.target.value)}} /></label>
  return <label className={`dial-field ${disabled ? 'disabled' : ''}`}><span>{c.label}<strong>{Number(value)}<small>%</small></strong></span><input type="range" min={c.min} max={c.max} value={Number(value)} disabled={disabled} onChange={e => setValue(Number(e.target.value))} onPointerUp={e => void commit(Number(e.currentTarget.value))} onKeyUp={e => {if (['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End','PageUp','PageDown'].includes(e.key)) void commit(Number(e.currentTarget.value))}} /></label>
}
