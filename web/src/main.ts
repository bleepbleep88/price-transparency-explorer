// First: Vuetify's styles declare its CSS layer order (reset < components < utilities). Any component stylesheet that
// loads before them (App.vue's, say) flips that order, and the reset's `button { font: inherit }` beats every button.
import { vuetify } from './plugins/vuetify'
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { VueQueryPlugin } from '@tanstack/vue-query'

import App from './App.vue'
import router from './router'
// Self-hosted fonts (bundled by Vite): the app runs locally and offline, so no font CDN.
import '@fontsource-variable/fraunces/opsz.css'
import '@fontsource-variable/ibm-plex-sans'
import './styles/base.css'
import { queryClient } from './lib/queryClient'

const app = createApp(App)

app.use(createPinia())
app.use(router)
app.use(vuetify)
app.use(VueQueryPlugin, { queryClient })

app.mount('#app')
