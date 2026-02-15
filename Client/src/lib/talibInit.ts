/**
 * talib-web WASM 初始化
 * 使用 public/talib.wasm，确保 MIME 正确
 */
import { init as talibInit } from "talib-web";

let initPromise: Promise<unknown> | null = null;

/**
 * 初始化 talib-web，幂等
 * @param wasmUrl 可选，默认 /talib.wasm（需放在 public 目录）
 */
export async function ensureTalibReady(wasmUrl = "/talib.wasm"): Promise<void> {
  if (initPromise) {
    await initPromise;
    return;
  }
  initPromise = talibInit(wasmUrl);
  await initPromise;
}
