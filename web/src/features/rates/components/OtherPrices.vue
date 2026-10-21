<script setup lang="ts">
import { computed } from 'vue'
import DetailsPanel from '@/components/simple/DetailsPanel.vue'
import type { RateSearchResult } from '../api/types'
import { otherPrices } from '../lib/answer'

// Every other price for the service (other settings, plans and modifiers), one short line each. Closed until asked.
const props = defineProps<{ result: RateSearchResult; shown: string[] }>()
const emit = defineEmits<{ pick: [groupKey: string, plan: string] }>()

const lines = computed(() => otherPrices(props.result, props.shown))
const groups = computed(() => new Map(props.result.groups.map((group) => [group.key, group])))
</script>

<template>
  <DetailsPanel v-if="lines.length" :label="`Other prices (${lines.length})`" testid="other-prices">
    <ul class="others">
      <li v-for="line in lines" :key="line.groupKey">
        <button type="button" class="other" data-testid="other-price" @click="emit('pick', line.groupKey, groups.get(line.groupKey)!.productKey)">
          <span class="bill">{{ line.bill }} <span class="plan">· {{ line.plan }}</span></span>
          <span class="price">{{ line.price }}</span>
          <span class="count">{{ line.count }}</span>
        </button>
      </li>
    </ul>
  </DetailsPanel>
</template>

<style scoped>
.others {
  list-style: none;
  padding: 0;
}

.other {
  display: grid;
  grid-template-columns: 1fr auto auto;
  gap: 16px;
  align-items: baseline;
  width: 100%;
  padding: 8px 0;
  text-align: left;
  font: inherit;
  font-size: 1rem;
  color: inherit;
  background: none;
  border: 0;
  border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  cursor: pointer;
}

.other:hover .bill {
  text-decoration: underline;
}

.other:focus-visible {
  outline: 2px solid rgb(var(--v-theme-primary));
  outline-offset: 2px;
}

.bill {
  min-width: 0;
  overflow-wrap: anywhere;
}

.plan,
.count {
  color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
}

.count {
  font-size: 0.875rem;
  white-space: nowrap;
}

.price {
  font-weight: 600;
  white-space: nowrap;
}

@media (max-width: 480px) {
  .other {
    grid-template-columns: 1fr auto;
  }

  .count {
    display: none;
  }
}
</style>
