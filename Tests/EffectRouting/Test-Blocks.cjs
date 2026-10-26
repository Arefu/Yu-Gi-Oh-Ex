const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const assert = require('node:assert/strict');
const build = path.resolve(process.argv[2]);
const source = fs.readFileSync(path.join(build, 'Blocks/effect-blocks.js'), 'utf8');
const context = vm.createContext({});
// Run the production conversion functions without the WebView/Blockly UI startup.
vm.runInContext(source.slice(0, source.indexOf('let workspace;')) + '\nthis.api = {treeToState, generate};', context);
const api = context.api;
function mockBlock(state) {
  return {
    type: state.type,
    getFieldValue: key => typeof state.fields?.[key] === 'boolean' ? String(state.fields[key]).toUpperCase() : state.fields?.[key] ?? '',
    getNextBlock: () => state.next?.block ? mockBlock(state.next.block) : null,
    getInputTargetBlock: key => state.inputs?.[key]?.block ? mockBlock(state.inputs[key].block) : null,
  };
}
const trees = JSON.parse(fs.readFileSync(path.join(build, 'multi-effect-test-trees.json'), 'utf8').replace(/^\uFEFF/, ''));
const output = [];
for (const test of trees) {
  const states = api.treeToState(test.tree);
  const script = api.generate({ getTopBlocks: () => states.map(mockBlock) });
  // one top block per effect: an as(...) block or an effect hat (ys_on_summon, ys_activate ...); locks sit beside them
  const count = states.filter((s) => s.type !== 'ys_lock' && s.type !== 'ys_hand_limit' && s.type !== 'ys_aura' && s.type !== 'ys_protect' && s.type !== 'ys_name_as' && s.type !== 'ys_only_one' && s.type !== 'ys_attack_directly' && s.type !== 'ys_xyz_also').length;
  // a script of only continuous statements (lock / hand_limit / aura / indestructible) has no effect hat; a raw row (no donor, no "from") is one
  const hasEffect = test.compiled.from !== undefined || test.compiled.rawRow === true;
  assert.equal(count, hasEffect ? 1 + (test.compiled.parts?.length ?? 0) : 0, 'Script-to-blocks lost an effect');
  assert.equal((script.match(/as\(/g) ?? []).length, (test.script.match(/as\(/g) ?? []).length, 'Blocks-to-script lost a clone');
  output.push({script, expected: test.compiled});
  console.log('PASS blocks round trip:', test.script);
}
fs.writeFileSync(path.join(build, 'multi-effect-block-roundtrips.json'), JSON.stringify(output));
