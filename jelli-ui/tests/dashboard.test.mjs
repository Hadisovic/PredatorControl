import {test} from 'node:test'
import assert from 'node:assert/strict'
import {tabs, restoreTab, sectionTab, canPreviewZones, deviceBrand, powerProfileSummary} from '../src/bridge/dashboard.ts'

test('old tab preferences migrate without dropping keyboard navigation', () => {
  assert.equal(restoreTab('System'), 'Overview')
  assert.equal(restoreTab('Lighting'), 'Keyboard')
  assert.equal(restoreTab('unrecognised'), 'Overview')
  tabs.forEach(tab => assert.equal(restoreTab(tab), tab))
})
test('every backend section has a deliberate dashboard home', () => {
  for (const id of ['power', 'fans']) assert.equal(sectionTab(id), 'Cooling')
  for (const id of ['keyboard', 'rgb', 'keyboardProfile']) assert.equal(sectionTab(id), 'Keyboard')
  for (const id of ['battery', 'display', 'gpu']) assert.equal(sectionTab(id), 'Overview')
  for (const id of ['hardware', 'app']) assert.equal(sectionTab(id), 'Settings')
  assert.equal(sectionTab('games'), 'Games')
})
test('binary and unknown keyboards cannot show fake RGB zones', () => {
  const ids = ['rgb.zone0', 'rgb.zone1', 'rgb.zone2', 'rgb.zone3']
  assert.equal(canPreviewZones(false, ids), false)
  assert.equal(canPreviewZones(true, ids.slice(0, 3)), false)
  assert.equal(canPreviewZones(true, ids), true)
})

test('branding uses authoritative backend family, including unknown and non-Acer fallback', () => {
  assert.equal(deviceBrand('nitro'), 'NITRO')
  assert.equal(deviceBrand('predator'), 'PREDATOR')
  assert.equal(deviceBrand('unsupported'), 'ACER')
  assert.equal(deviceBrand('genericAcer'), 'ACER')
  assert.equal(deviceBrand(undefined), 'ACER')
  assert.equal(deviceBrand('Nitro AN515-58'), 'ACER')
})
test('RGB previews require actual supported zone controls and explicit zone count', () => {
  assert.equal(canPreviewZones(true, ['rgb.zone0', 'rgb.zone1'], 2), true)
  assert.equal(canPreviewZones(true, ['rgb.zone0'], 2), false)
  assert.equal(canPreviewZones(false, ['rgb.zone0', 'rgb.zone1'], 2), false)
  assert.equal(canPreviewZones(true, ['rgb.zone0'], 0), false)
  assert.equal(sectionTab('compatibility'), 'Settings')
})
test('Nitro performance selector tracks active profile and disabled AC-only modes', () => {
  const onAc = powerProfileSummary('Performance', [
    {id: 'power.quiet', label: 'Quiet', kind: 'button', enabled: true, active: false},
    {id: 'power.balanced', label: 'Default', kind: 'button', enabled: true, active: false},
    {id: 'power.performance', label: 'Performance', kind: 'button', enabled: true, active: true},
    {id: 'power.ac', label: 'When plugged in', kind: 'select', enabled: true},
  ])
  assert.equal(onAc.activeLabel, 'Performance')
  assert.deepEqual(onAc.enabledIds, ['power.quiet', 'power.balanced', 'power.performance'])
  assert.deepEqual(onAc.disabledIds, [])

  const onBattery = powerProfileSummary('Default', [
    {id: 'power.quiet', label: 'Quiet', kind: 'button', enabled: false, active: false},
    {id: 'power.balanced', label: 'Default', kind: 'button', enabled: true, active: true},
    {id: 'power.performance', label: 'Performance', kind: 'button', enabled: false, active: false},
  ])
  assert.equal(onBattery.activeLabel, 'Default')
  assert.deepEqual(onBattery.enabledIds, ['power.balanced'])
  assert.deepEqual(onBattery.disabledIds, ['power.quiet', 'power.performance'])
})
