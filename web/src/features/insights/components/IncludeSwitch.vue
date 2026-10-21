<script setup lang="ts">
import { computed } from 'vue'
import { wholeNumber } from '@/lib/format'

// A switch that adds rows left out by default because they don't compare like for like (why is in Details). The count
// is what turning it on adds under the page's other filters; with nothing to add it can't be turned on.
const props = defineProps<{ modelValue: boolean; count: number; label: string; testid: string }>()
const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>()

const text = computed(() => `${props.label}${props.count ? ` (${wholeNumber(props.count)})` : ''}`)
</script>

<template>
  <v-switch
    :model-value="modelValue"
    :label="text"
    :disabled="!modelValue && count === 0"
    color="primary"
    density="compact"
    hide-details
    inset
    class="flex-grow-0"
    :data-testid="testid"
    @update:model-value="(value) => emit('update:modelValue', Boolean(value))"
  />
</template>
