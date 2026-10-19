import 'vuetify/styles'
import { createVuetify } from 'vuetify'
import { md3 } from 'vuetify/blueprints'
import { aliases, mdi } from 'vuetify/iconsets/mdi-svg'

// Compact Material 3 theme for data-heavy screens. Follows the OS light/dark setting until the user toggles it.
export const vuetify = createVuetify({
  blueprint: md3,
  icons: { defaultSet: 'mdi', aliases, sets: { mdi } },
  theme: {
    defaultTheme: 'system',
    themes: {
      light: {
        dark: false,
        colors: {
          primary: '#0B6E6E',
          secondary: '#46596B',
          background: '#F6F8F9',
          surface: '#FFFFFF',
          success: '#2E7D32',
          warning: '#A15C00',
          error: '#C62828',
          info: '#1565C0',
        },
      },
      dark: {
        dark: true,
        colors: {
          primary: '#5CC6C4',
          secondary: '#A7B7C6',
          background: '#0E1417',
          surface: '#151D21',
          success: '#66BB6A',
          warning: '#FFB74D',
          error: '#EF5350',
          info: '#64B5F6',
        },
      },
    },
  },
  defaults: {
    VCard: { variant: 'outlined', rounded: 'lg' },
    VTextField: { variant: 'outlined', density: 'compact', hideDetails: 'auto' },
    VAutocomplete: { variant: 'outlined', density: 'compact', hideDetails: 'auto' },
    VCombobox: { variant: 'outlined', density: 'compact', hideDetails: 'auto' },
    VNumberInput: { variant: 'outlined', density: 'compact' },
    VSelect: { variant: 'outlined', density: 'compact', hideDetails: 'auto' },
    VChip: { size: 'small' },
    VDataTableServer: { density: 'compact' },
  },
})
