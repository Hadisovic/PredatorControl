export type Surface = 'compact' | 'summary' | 'dashboard' | 'menu'
export interface Preferences {
  presentation: 'expand' | 'flyout'; metric1: string; metric2: string; overlayCorner: string;
  overlayMonitor: string; overlayMode?: 'light' | 'default' | 'full' | 'complete'; overlayScale?: number;
  overlayGraphs: boolean; overlayPower: boolean; overlayUsage: boolean; syncRgb: boolean; jelliEnabled?: boolean; x: number; y: number
}
export interface Control {
  id: string; label: string; kind: 'button' | 'toggle' | 'range' | 'select' | 'color'; value: string | number | boolean | null;
  enabled: boolean; choices: { label: string; value: number; enabled: boolean }[] | null; min: number; max: number; accent?: string | null
}
export interface MenuItem { id: string; label: string; enabled: boolean; checked: boolean; children: MenuItem[] }
export interface OperationResult {status: 'confirmed' | 'failed' | 'unverified'; message: string}
export interface CapabilityEvidence {
  status: 'supported' | 'unsupported' | 'unknown' | 'unavailable'; backend: string; evidence: string;
  requiredService: string | null; writable: boolean; canVerify: boolean; canWrite: boolean
}
export interface DeviceIdentity {
  manufacturer: string; model: string; normalizedModel: string; family: string; sku: string;
  biosVersion: string; biosReleaseDate: string; boardProduct: string; boardVersion: string
}
export interface Compatibility {
  identity: DeviceIdentity; chassisFamily: 'predator' | 'nitro' | 'genericAcer' | 'unsupported';
  profileId: string; explanation: string; results: Record<string, CapabilityEvidence>;
  powerModes: {value: number; label: string; onAc: boolean; onBattery: boolean; windowsScheme?: string | null}[]; gpuModeMask: number
}
export interface State {
  identity?: DeviceIdentity; compatibility?: Compatibility; lastOperation?: OperationResult | null;
  device?: string; theme?: 'PredatorTeal' | 'NitroCrimson' | 'OledBlack';
  keyboard?: {kind: 'unknown' | 'binary' | 'rgb' | 'none'; supportsRgb: boolean; zoneCount?: number; effects?: number[]; fixedColor?: string | null; supportsBrightness: boolean; evidence: string; notice: string};
  telemetry: Record<string, number | boolean | null> | null; powerMode: string; fanMode: string; gpuMode: string;
  gpuNotice: string; fps: number | null; overlay: boolean; overlayMode?: 'light' | 'default' | 'full' | 'complete'; overlayScale?: number;
  powerPlanNotice?: string | null;
  sections: {id: string; label: string; controls: Control[]}[];
  menu: MenuItem[]; settings: Preferences; monitors: {id: string; label: string}[]; version: string; revision?: number; doubleClickMs: number; gameSyncStatus: string; batteryNotice?: string;
}
export interface Layout {surface: Surface; creatureX: number; creatureY: number; flyout: boolean; panelLeft: number}
