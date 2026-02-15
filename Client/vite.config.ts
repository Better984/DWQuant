import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import svgr from 'vite-plugin-svgr'
import path from 'node:path'

// https://vite.dev/config/
export default defineConfig({
  resolve: {
    alias: {
      klinecharts: path.resolve(__dirname, './vendors/klinecharts_local/dist/index.esm.js'),
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
