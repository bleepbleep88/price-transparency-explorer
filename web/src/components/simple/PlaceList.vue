<script setup lang="ts">
import { dollars } from '@/lib/plainNumbers'

// A short list of places and their prices: name, where, price. Low prices in green, high in red.
export interface PlaceItem {
  key: string
  name: string
  place?: string | null
  price: number
  /** Green when the price is lower than most, red when higher; plain ink otherwise. Overrides the list's tone. */
  band?: 'low' | 'high' | null
  /** The provider the reader searched for: marked in copper. */
  yours?: boolean
}

withDefaults(defineProps<{ title?: string | null; items: PlaceItem[]; tone?: 'low' | 'high' | 'neutral'; perUnit?: boolean }>(), {
  title: null,
  tone: 'neutral',
  perUnit: false,
})
const emit = defineEmits<{ open: [key: string] }>()
</script>

<template>
  <section class="place-list" data-testid="place-list">
    <h3 v-if="title" class="list-title">{{ title }}</h3>
    <ol class="list">
      <li v-for="item in items" :key="item.key" :class="{ yours: item.yours }">
        <button type="button" class="row" data-testid="place-row" @click="emit('open', item.key)">
          <span class="who">
            <span class="name">{{ item.name }}</span>
            <span v-if="item.place" class="place" data-testid="place-where">{{ item.place }}</span>
          </span>
          <span class="price" :class="item.band ?? tone">{{ dollars(item.price) }}<span v-if="perUnit" class="unit"> per unit</span></span>
        </button>
      </li>
    </ol>
    <slot />
  </section>
</template>

<style scoped>
.place-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
}

.list-title {
  font-family: var(--v-font-body);
  font-size: 1.125rem;
  font-weight: 600;
}

.list {
  list-style: none;
  padding: 0;
}

.row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 16px;
  width: 100%;
  padding: 8px 0;
  text-align: left;
  color: inherit;
  font: inherit;
  background: none;
  border: 0;
  border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  cursor: pointer;
}

.row:hover .name {
  text-decoration: underline;
}

.row:focus-visible {
  outline: 2px solid rgb(var(--v-theme-primary));
  outline-offset: 2px;
}

.who {
  display: flex;
  flex-wrap: wrap;
  column-gap: 8px;
  min-width: 0;
}

.name {
  font-weight: 500;
  overflow-wrap: anywhere;
}

.place {
  font-size: 0.875rem;
  color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
}

.price {
  font-weight: 600;
  white-space: nowrap;
}

.price.low {
  color: rgb(var(--v-theme-low));
}

.price.high {
  color: rgb(var(--v-theme-high));
}

.unit {
  font-weight: 400;
  font-size: 0.875rem;
}

.yours .row {
  border-left: 3px solid rgb(var(--v-theme-copper));
  padding-left: 8px;
}
</style>
