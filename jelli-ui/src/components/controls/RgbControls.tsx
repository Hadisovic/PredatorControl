import type { Control } from '../../bridge/types'
import { groupRgbControls } from '../../bridge/dashboard'
import { HardwareControl } from './HardwareControl'

export function RgbControls({controls, zoneCount}: {controls: Control[]; zoneCount: number}) {
  const groups = groupRgbControls(controls, zoneCount)
  return <div className="rgb-controls">
    <div className="controls">{groups.mode.map(c => <HardwareControl key={c.id} control={c}/>)}</div>
    {groups.zones.length > 0 && <div className="rgb-zones" role="group" aria-label="Select keyboard zone" style={{gridTemplateColumns: `repeat(${groups.zones.length}, minmax(0, 1fr))`}}>
      {groups.zones.map(c => <HardwareControl key={c.id} control={c}/>)}
    </div>}
    {groups.presets.length > 0 && <div className="rgb-presets" role="group" aria-label="Preset colors">
      {groups.presets.map(c => <HardwareControl key={c.id} control={c}/>)}
    </div>}
    <div className="controls">{groups.remaining.map(c => <HardwareControl key={c.id} control={c}/>)}</div>
  </div>
}
