import { init, TAFuncs } from 'talib-web';

const wasmPath = 'D:/UGit/DWQuant/Client/public/talib.wasm';
const originalFetch = globalThis.fetch;
if (typeof originalFetch === 'function') {
  globalThis.fetch = undefined;
}
await init(wasmPath);
if (typeof originalFetch === 'function') {
  globalThis.fetch = originalFetch;
}

const len = 2000;
const close = Array.from({ length: len }, (_, i) => 100000 + Math.sin(i * 0.01) * 500 + i * 0.3);
const out = TAFuncs.MA({ inReal: close, timePeriod: 30, MAType: 0 });
const arr = out.output;
let finiteCount = 0;
for (const v of arr) {
  if (Number.isFinite(v)) finiteCount += 1;
}
console.log('length', arr.length);
console.log('finiteCount', finiteCount);
console.log('head', arr.slice(0, 35));
