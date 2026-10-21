import 'vuetify/styles'
import { createVuetify } from 'vuetify'
import { md3 } from 'vuetify/blueprints'
import { aliases, mdi } from 'vuetify/iconsets/mdi-svg'

// "Ledger": ink on warm paper, receipt green for actions, copper only for the provider you searched.
// Every text color passes WCAG AA on its background in both themes (4.5:1 body, 3:1 UI parts).
// Copper (#C0662B) is 3.6:1 on paper, so it marks fills, borders and markers; small copper text uses copper-ink.
export const vuetify = createVuetify({
  blueprint: md3,
  icons: { defaultSet: 'mdi', aliases, sets: { mdi } },
  theme: {
    defaultTheme: 'system',
    themes: {
      light: {
        dark: false,
        colors: {
          background: '#F6F2EA',
          surface: '#FCFAF5',
          'surface-bright': '#FFFFFF',
          'surface-light': '#EFE9DD',
          'surface-variant': '#1C2321',
          'on-surface-variant': '#F6F2EA',
          'on-background': '#1C2321',
          'on-surface': '#1C2321',
          primary: '#0E6B4E',
          'on-primary': '#FFFFFF',
          secondary: '#3D4A45',
          copper: '#C0662B',
          'copper-ink': '#9A4A17',
          low: '#2F7D5B',
          high: '#B4432F',
          // Darker than low: tonal status chips (e.g. "Data ready" on the paper app bar) need 4.5:1 (4.83:1 here).
          success: '#276B4E',
          warning: '#8F4A12',
          error: '#A8381F',
          info: '#25587F',
        },
        variables: {
          'border-color': '#1C2321',
          'border-opacity': 0.12,
          'medium-emphasis-opacity': 0.72,
        },
      },
      dark: {
        dark: true,
        colors: {
          background: '#121614',
          surface: '#1A1F1C',
          'surface-bright': '#242A26',
          'surface-light': '#232925',
          'surface-variant': '#ECE6DA',
          'on-surface-variant': '#121614',
          'on-background': '#ECE6DA',
          'on-surface': '#ECE6DA',
          primary: '#5BC79A',
          'on-primary': '#0B1F16',
          secondary: '#A9B5AF',
          copper: '#E08A4F',
          'copper-ink': '#E8975F',
          low: '#6DC79A',
          high: '#F08C74',
          success: '#6DC79A',
          warning: '#E9A867',
          error: '#F28B76',
          info: '#8EC1EA',
        },
        variables: {
          'border-color': '#ECE6DA',
          'border-opacity': 0.14,
          'medium-emphasis-opacity': 0.74,
        },
      },
    },
  },
  defaults: {
    // Paper app bar with a ruled bottom edge; flat cards with hairline borders and a small radius.
    VAppBar: { flat: true, color: 'background' },
    VCard: { variant: 'flat', border: true, rounded: 'sm' },
    VBtn: { rounded: 'sm' },
    VAlert: { rounded: 'sm' },
    VExpansionPanels: { rounded: 'sm' },
    VTextField: { variant: 'outlined', density: 'compact', hideDetails: 'auto', rounded: 'sm' },
    VAutocomplete: { variant: 'outlined', density: 'compact', hideDetails: 'auto', rounded: 'sm' },
    VSelect: { variant: 'outlined', density: 'compact', hideDetails: 'auto', rounded: 'sm' },
    VCombobox: { variant: 'outlined', density: 'compact', hideDetails: 'auto', rounded: 'sm' },
    VNumberInput: { variant: 'outlined', density: 'compact', rounded: 'sm' },
    // Chips and toggles share one shape: round ends (plan tabs, roles, filters, ranking).
    VChip: { rounded: 'pill' },
    VBtnToggle: { rounded: 'pill' },
    VDataTableServer: { density: 'compact' },
  },
})
