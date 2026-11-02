import test from 'node:test';
import assert from 'node:assert/strict';
import { Minimatch } from 'minimatch';

test('glob preprocessing respects a small expansion budget', () => {
  // Small fixture: exercise amplification without stressing the machine.
  const matcher = new Minimatch(Array(6).fill('**/../folder/file').join('/'), {
    optimizationLevel: 2, braceExpandMax: 8,
  });
  assert.ok(matcher.set.length <= 8);
});
