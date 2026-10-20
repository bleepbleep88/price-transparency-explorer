<script setup lang="ts">
import { computed } from 'vue'
import { wholeNumber } from '@/lib/format'
import { FACILITY_CHARGE_REASON } from '../lib/plainWords'

// One switch on every insights page for facility charges with no procedure named (decision 89: left out by default).
// The count is what turning it on adds under the page's other filters; with nothing to add it can't be turned on.
const props = defineProps<{ modelValue: boolean; count: number }>()
const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>()

const label = computed(() => `Include facility charges with no procedure named${props.count ? ` (${wholeNumber(props.count)})` : ''}`)
</script>

<template>
  <div class="d-flex flex-column" data-testid="facility-charges">
    <v-switch
      :model-value="modelValue"
      :label="label"
      :disabled="!modelValue && count === 0"
      color="primary"
      density="compact"
      hide-details
      inset
      data-testid="facility-charges-switch"
      @update:model-value="(value) => emit('update:modelValue', Boolean(value))"
    />
    <p v-if="modelValue || count" class="text-body-small text-medium-emphasis">{{ FACILITY_CHARGE_REASON }}</p>
  </div>
</template>
