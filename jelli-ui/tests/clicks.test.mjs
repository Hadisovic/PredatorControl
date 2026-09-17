import { test } from 'node:test'
import assert from 'node:assert/strict'
import { ClickArbiter } from '../src/bridge/clicks.ts'
test('a single click waits for the OS arbitration interval', t => {
  t.mock.timers.enable({apis:['setTimeout']})
  const arbiter = new ClickArbiter(); const actions = []
  arbiter.click(500,()=>actions.push('summary'),()=>actions.push('dashboard'))
  t.mock.timers.tick(499); assert.deepEqual(actions,[])
  t.mock.timers.tick(1); assert.deepEqual(actions,['summary'])
})
test('a double click opens only the dashboard, without summary flash', t => {
  t.mock.timers.enable({apis:['setTimeout']})
  const arbiter = new ClickArbiter(); const actions = []
  arbiter.click(500,()=>actions.push('summary'),()=>actions.push('dashboard'))
  t.mock.timers.tick(200)
  arbiter.click(500,()=>actions.push('summary'),()=>actions.push('dashboard'))
  t.mock.timers.tick(1000); assert.deepEqual(actions,['dashboard'])
})
test('context menu, drag, or unmount cancels a pending summary', t => {
  t.mock.timers.enable({apis:['setTimeout']})
  const arbiter = new ClickArbiter(); let opened = false
  arbiter.click(500,()=>opened=true,()=>opened=true); arbiter.cancel()
  t.mock.timers.tick(1000); assert.equal(opened,false)
})
