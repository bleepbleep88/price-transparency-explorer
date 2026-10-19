<script setup lang="ts">
const emit = defineEmits<{ search: [code: string] }>()

const examples = [
  { code: '45378', label: 'Colonoscopy', why: 'Facility fees vary 5x; doctor fees are mostly a fixed schedule' },
  { code: '29881', label: 'Knee arthroscopy', why: 'Only published as a facility rate under revenue code 0490' },
  { code: '99214', label: 'Office visit, level 4', why: 'Visit codes come in levels; compare like with like' },
  { code: '812', label: 'Colonoscopy anesthesia', why: 'Priced per anesthesia unit, published without its leading zeros' },
  { code: 'J0585', label: 'Botox injection', why: 'Drugs are priced per HCPCS unit' },
]
</script>

<template>
  <v-card variant="outlined">
    <v-card-item title="Try an example" subtitle="Each one shows something the raw files get wrong or hide." />
    <v-list density="compact" class="pt-0">
      <v-list-item
        v-for="example in examples"
        :key="example.code"
        :title="`${example.label} (${example.code})`"
        :subtitle="example.why"
        lines="two"
        link
        :data-testid="`example-${example.code}`"
        @click="emit('search', example.code)"
      />
    </v-list>
  </v-card>
</template>
