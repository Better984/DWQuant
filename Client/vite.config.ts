import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import svgr from 'vite-plugin-svgr'
import path from 'node:path'
import fs from 'node:fs'

const klinechartsDistEntry = path.resolve(__dirname, './vendors/klinecharts_local/dist/index.esm.js')
const klinechartsSrcEntry = path.resolve(__dirname, './vendors/klinecharts_local/src/index.ts')
const klinechartsAlias = fs.existsSync(klinechartsDistEntry) ? klinechartsDistEntry : klinechartsSrcEntry

// https://vite.dev/config/
export default defineConfig({
  resolve: {
    alias: {
      klinecharts: klinechartsAlias,
    },
  },
  plugins: [
    react(),
    svgr({
      include: "**/*.svg?react",
    }),
  ],
  server: {
    port: 3000,
  },
})
