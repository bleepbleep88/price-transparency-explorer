import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { VueQueryPlugin } from '@tanstack/vue-query'

import App from './App.vue'
import router from './router'
import { vuetify } from './plugins/vuetify'
import { queryClient } from './lib/queryClient'

const app = createApp(App)

app.use(createPinia())
app.use(router)
app.use(vuetify)
app.use(VueQueryPlugin, { queryClient })

app.mount('#app')
