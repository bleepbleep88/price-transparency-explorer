<script setup lang="ts">
import { computed } from 'vue'
import { wholeNumber } from '@/lib/format'

// One switch on every insights page for facility-only charges (decision 89: left out by default; why is in Details).
// The count is what turning it on adds under the page's other filters; with nothing to add it can't be turned on.
const props = defineProps<{ modelValue: boolean; count: number }>()
const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>()

const label = computed(() => `Include facility-only charges${props.count ? ` (${wholeNumber(props.count)})` : ''}`)
</script>

<template>
  <v-switch
    :model-value="modelValue"
    :label="label"
    :disabled="!modelValue && count === 0"
    color="primary"
    density="compact"
    hide-details
    inset
    class="flex-grow-0"
    data-testid="facility-charges"
    @update:model-value="(value) => emit('update:modelValue', Boolean(value))"
  />
</template>
