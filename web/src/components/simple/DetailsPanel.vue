<script setup lang="ts">
import { ref, useId } from 'vue'
import { mdiChevronDown } from '@mdi/js'

// Everything that isn't the answer: codes, modifiers, tax IDs, counts, methods. Closed until asked for.
withDefaults(defineProps<{ label?: string; testid?: string }>(), { label: 'Details', testid: 'details' })
const open = ref(false)
const id = useId()
</script>

<template>
  <section class="details" :data-testid="testid">
    <button
      type="button"
      class="details-toggle"
      :aria-expanded="open"
      :aria-controls="id"
      :data-testid="`${testid}-toggle`"
      @click="open = !open"
    >
      <span>{{ label }}</span>
      <v-icon :icon="mdiChevronDown" size="20" :class="{ turned: open }" aria-hidden="true" />
    </button>
    <div v-show="open" :id="id" class="details-body">
      <slot />
    </div>
  </section>
</template>

<style scoped>
.details {
  border-top: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
}

.details-toggle {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 12px 0;
  font: inherit;
  font-weight: 600;
  color: rgb(var(--v-theme-primary));
  background: none;
  border: 0;
  cursor: pointer;
}

.details-toggle:focus-visible {
  outline: 2px solid rgb(var(--v-theme-primary));
  outline-offset: 2px;
}

.turned {
  transform: rotate(180deg);
}

.details-body {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding-bottom: 16px;
  font-size: 0.875rem;
}
</style>
