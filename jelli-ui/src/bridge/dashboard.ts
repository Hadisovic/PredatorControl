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
  if (['app', 'hardware'].includes(id)) return 'Settings'
  return 'Overview'
}

export function canPreviewZones(supportsRgb: boolean, controlIds: string[]): boolean {
  return supportsRgb && [0, 1, 2, 3].every(i => controlIds.includes(`rgb.zone${i}`))
}
