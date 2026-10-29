export const tabs = ['Overview', 'Cooling', 'Keyboard', 'Games', 'Settings'] as const
export type DashboardTab = typeof tabs[number]

export function restoreTab(saved: string | null): DashboardTab {
  if (saved === 'System') return 'Overview'
  if (saved === 'Lighting') return 'Keyboard'
  return tabs.includes(saved as DashboardTab) ? saved as DashboardTab : 'Overview'
}

export function sectionTab(id: string): DashboardTab {
  if (['power', 'fans'].includes(id)) return 'Cooling'
  if (['rgb', 'keyboard', 'keyboardProfile'].includes(id)) return 'Keyboard'
  if (id === 'games') return 'Games'
  if (['app', 'hardware', 'compatibility'].includes(id)) return 'Settings'
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
  return {
    activeLabel: active?.label ?? powerMode ?? 'Unavailable',
    enabledIds: buttons.filter(c => c.enabled).map(c => c.id),
    disabledIds: buttons.filter(c => !c.enabled).map(c => c.id),
  }
}
