<script setup lang="ts">
import IncludeSwitch from './IncludeSwitch.vue'

// One switch on every insights page for facility-only charges (decision 89: left out by default; why is in Details).
defineProps<{ modelValue: boolean; count: number }>()
const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>()
</script>

<template>
  <IncludeSwitch
    :model-value="modelValue"
    :count="count"
    label="Include facility-only charges"
    testid="facility-charges"
    @update:model-value="(value) => emit('update:modelValue', value)"
  />
</template>
