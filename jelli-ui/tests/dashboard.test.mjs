import {test} from 'node:test'
import assert from 'node:assert/strict'
import {tabs, restoreTab, sectionTab, canPreviewZones} from '../src/bridge/dashboard.ts'

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
