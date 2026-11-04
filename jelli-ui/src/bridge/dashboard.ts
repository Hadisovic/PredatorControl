export const tabs = ['Overview', 'Hardware', 'Keyboard', 'Games', 'Settings'] as const
export type DashboardTab = typeof tabs[number]

export function restoreTab(saved: string | null): DashboardTab {
  if (saved === 'System') return 'Overview'
  if (saved === 'Cooling') return 'Hardware'
  if (saved === 'Lighting') return 'Keyboard'
  return tabs.includes(saved as DashboardTab) ? saved as DashboardTab : 'Overview'
}

export function sectionTab(id: string): DashboardTab {
  if (['power', 'fans', 'hardware'].includes(id)) return 'Hardware'
  if (['rgb', 'keyboard', 'keyboardProfile'].includes(id)) return 'Keyboard'
  if (id === 'games') return 'Games'
  if (['app', 'compatibility'].includes(id)) return 'Settings'
  return 'Overview'
}

export function canPreviewZones(supportsRgb: boolean, controlIds: string[], zoneCount = 4): boolean {
  return supportsRgb && zoneCount >= 1 && zoneCount <= 4 && Array.from({length: zoneCount}, (_, i) => i).every(i => controlIds.includes(`rgb.zone${i}`))
}

// Branding is supplied by the backend. Model strings cannot enable features or select a brand.
export function deviceBrand(chassisFamily?: string): string {
  return chassisFamily === 'nitro' ? 'NITRO' : chassisFamily === 'predator' ? 'PREDATOR' : 'ACER'
}

export interface PowerControlView {
  id: string
  label: string
  kind?: string
  enabled: boolean
  active?: boolean
}

export function powerProfileSummary(powerMode: string | undefined, controls: PowerControlView[] = []) {
  const buttons = controls.filter(c => !c.kind || c.kind === 'button')
  const active = buttons.find(c => c.active)
  const validPowerMode = powerMode && buttons.some(b => b.label === powerMode) ? powerMode : undefined
  return {
    activeLabel: active?.label ?? validPowerMode ?? buttons.find(b => b.id === 'power.balanced')?.label ?? buttons[0]?.label ?? 'Balanced',
    enabledIds: buttons.filter(c => c.enabled).map(c => c.id),
    disabledIds: buttons.filter(c => !c.enabled).map(c => c.id),
  }
}

// Keep RGB selectors out of the color grid, retaining the backend control objects and IDs.
export function groupRgbControls<T extends {id: string}>(controls: T[], zoneCount: number) {
  const count = Number.isInteger(zoneCount) && zoneCount >= 1 && zoneCount <= 4 ? zoneCount : 0
  const zones = ['rgb.all', ...Array.from({length: count}, (_, i) => `rgb.zone${i}`)]
    .flatMap(id => count ? controls.filter(c => c.id === id) : [])
  return {
    mode: controls.filter(c => c.id === 'rgb.mode'),
    zones,
    presets: controls.filter(c => /^rgb\.preset\d+$/.test(c.id)),
    remaining: controls.filter(c => c.id !== 'rgb.mode' && c.id !== 'rgb.all' && !/^rgb\.zone\d+$/.test(c.id) && !/^rgb\.preset\d+$/.test(c.id))
  }
}
