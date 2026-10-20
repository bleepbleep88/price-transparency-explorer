import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vuetify from 'vite-plugin-vuetify'
import vueDevTools from 'vite-plugin-vue-devtools'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue(), vuetify({ autoImport: true }), vueDevTools()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  // The Vuetify plugin adds component imports while transforming .vue files, so Vite's dependency scan can't see them.
  // Pre-bundling Vuetify would then re-bundle and reload the page mid-navigation on a cold start.
  optimizeDeps: { exclude: ['vuetify'] },
  // ECharts is one ~520 kB chunk, loaded only when the plan comparison or a group card's trend draws a chart; the rest
  // are far smaller.
  build: { chunkSizeWarningLimit: 600 },
  server: {
    port: 5173,
    // Same-origin in dev: the .NET API runs on :5080 (see src/PriceExplorer.Api/Properties/launchSettings.json).
    // The API also serves its reference (OpenAPI document and Scalar UI); nginx forwards the same paths in the container.
    proxy: Object.fromEntries(
      ['/api', '/openapi', '/scalar'].map((path) => [
        path,
        { target: process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:5080', changeOrigin: true },
      ]),
    ),
  },
})
