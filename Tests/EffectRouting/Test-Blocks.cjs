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
  let count = 0;
  for (const top of states) for (let s = top; s; s = s.next?.block) if (s.type === 'ys_as' || s.type === 'ys_effect') count++;
  assert.equal(count, 1 + (test.compiled.parts?.length ?? 0), 'Script-to-blocks lost an effect');
  assert.equal((script.match(/as\(/g) ?? []).length, (test.script.match(/as\(/g) ?? []).length, 'Blocks-to-script lost a clone');
  output.push({script, expected: test.compiled});
  console.log('PASS blocks round trip:', test.script);
}
fs.writeFileSync(path.join(build, 'multi-effect-block-roundtrips.json'), JSON.stringify(output));
