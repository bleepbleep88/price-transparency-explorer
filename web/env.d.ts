/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** API origin. Leave empty to call the same origin (Vite proxy in dev, nginx in Docker). */
  readonly VITE_API_BASE_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
