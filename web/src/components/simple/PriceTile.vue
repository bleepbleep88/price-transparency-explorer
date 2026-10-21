<script setup lang="ts">
import { money } from '@/lib/format'
import { dollars } from '@/lib/plainNumbers'
import TrendChip from './TrendChip.vue'

// One bill's answer: what it's for, the typical price big, one line under it, the history chip and how many places.
// Under 5 places there is no typical price: the few prices are listed instead.
const props = withDefaults(
  defineProps<{
    label: string
    where?: string | null
    price: number | null
    perUnit?: boolean
    line?: string | null
    chip?: string | null
    count?: string | null
    listed?: number[]
    selected?: boolean
    selectable?: boolean
  }>(),
  { where: null, perUnit: false, line: null, chip: null, count: null, listed: () => [], selected: false, selectable: false },
)
const emit = defineEmits<{ select: [] }>()

function onClick() {
  if (props.selectable) emit('select')
}

function fewText(listed: number[]): string {
  if (!listed.length) return 'Not enough prices'
  const places = listed.length === 1 ? '1 place' : `${listed.length} places`
  return `Only ${places}: ${listed.map((rate) => money(rate)).join(' · ')}`
}
</script>

<template>
  <component
    :is="selectable ? 'button' : 'div'"
    :type="selectable ? 'button' : undefined"
    class="price-tile"
    :class="{ selected, selectable }"
    :aria-pressed="selectable ? selected : undefined"
    data-testid="price-tile"
    @click="onClick"
  >
    <span class="tile-label">
      {{ label }}<span v-if="where" class="tile-where"> {{ where }}</span>
    </span>
    <template v-if="price != null">
      <span class="tile-price" data-testid="tile-price">
        {{ dollars(price) }}<span class="tile-unit">{{ perUnit ? 'per unit' : 'typical' }}</span>
      </span>
      <span v-if="line" class="tile-line" data-testid="tile-line">{{ line }}</span>
    </template>
    <span v-else class="tile-line" data-testid="tile-few">{{ fewText(listed) }}</span>
    <span v-if="chip || count" class="tile-foot">
      <TrendChip :text="chip" />
      <span v-if="count" class="tile-count">{{ count }}</span>
    </span>
  </component>
</template>

<style scoped>
.price-tile {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
  padding: 16px;
  text-align: left;
  color: inherit;
  font: inherit;
  background: rgb(var(--v-theme-surface));
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-radius: 8px;
}

.price-tile.selectable {
  cursor: pointer;
}

.price-tile.selectable:hover {
  border-color: rgba(var(--v-theme-primary), 0.6);
}

.price-tile.selected {
  border-color: rgb(var(--v-theme-primary));
  box-shadow: inset 0 0 0 1px rgb(var(--v-theme-primary));
}

.price-tile:focus-visible {
  outline: 2px solid rgb(var(--v-theme-primary));
  outline-offset: 2px;
}

.tile-label {
  font-size: 0.875rem;
  font-weight: 600;
}

.tile-where {
  font-weight: 400;
  color: rgba(var(--v-theme-on-surface), var(--v-medium-emphasis-opacity));
}

.tile-price {
  font-family: var(--v-font-heading);
  font-size: 2.5rem;
  font-weight: 600;
  line-height: 1.1;
  color: rgb(var(--v-theme-primary));
}

.tile-unit {
  margin-left: 8px;
  font-family: var(--v-font-body);
  font-size: 0.875rem;
  font-weight: 500;
  color: rgba(var(--v-theme-on-surface), var(--v-medium-emphasis-opacity));
}

.tile-line {
  font-size: 1rem;
}

.tile-foot {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-top: 4px;
}

.tile-count {
  font-size: 0.8125rem;
  color: rgba(var(--v-theme-on-surface), var(--v-medium-emphasis-opacity));
}
</style>
