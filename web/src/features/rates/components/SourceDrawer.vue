<script setup lang="ts">
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue'
import { useDisplay } from 'vuetify'
import { mdiCheck, mdiClose, mdiContentCopy, mdiOpenInNew } from '@mdi/js'
import { copyText } from '@/lib/clipboard'
import { megabytes, money, plural, shortDate } from '@/lib/format'
import { ApiError } from '@/lib/apiClient'
import { providerName as cleanName } from '@/lib/plainNames'
import type { RateSourceProviderGroup } from '../api/types'
import { useRateSource } from '../composables/useRateQueries'
import { useReturnFocus } from '../composables/useReturnFocus'

// Where a price comes from, every traceable field kept, under short plain headings: the file, the spot in it, the
// values as published, repeats, and the provider groups the price covers.

const open = defineModel<boolean>({ required: true })
useReturnFocus(open)
const props = defineProps<{
  rateId: number | null
  providerName?: string
  providerKey?: string | null
  /** The row's whole-tax-ID contract, when it's a different rate than `rateId`. */
  wholeTaxIdRateId?: number | null
}>()

const { smAndDown } = useDisplay()
const heading = useTemplateRef<HTMLElement>('heading')

// The rate on show: the row's own, or its whole-tax-ID contract after "Show that contract". Opening starts on the row's own.
const shownRateId = ref<number | null>(props.rateId)
watch([() => props.rateId, open], () => {
  shownRateId.value = props.rateId
})
const showingWholeTaxId = computed(() => props.wholeTaxIdRateId != null && shownRateId.value === props.wholeTaxIdRateId)

async function showRate(rateId: number | null) {
  shownRateId.value = rateId
  // The button just pressed goes away, so start again from the top of the drawer.
  await nextTick()
  heading.value?.focus()
}

// Which value was copied last; the check mark stays until another rate shows or the drawer closes (no timer).
const copied = ref<'JSON path' | 'SHA-256' | null>(null)
watch([shownRateId, open], () => {
  copied.value = null
})

async function copyValue(value: string, what: 'JSON path' | 'SHA-256') {
  // Empty, then filled, so a second copy is announced again.
  copied.value = null
  await nextTick()
  if (await copyText(value)) copied.value = what
}
// The row's provider is listed first, so it's visible even when a rate covers hundreds of provider groups.
const source = useRateSource(
  () => (open.value ? shownRateId.value : null),
  () => props.providerKey ?? null,
)

const data = computed(() => source.data.value)

// The plan's own name(s) for this provider when lists show its practice's name instead (a network's shared name, or a
// name from a file whose names are unreliable): kept here so the shown name can be traced to the file.
const listedAs = computed(() => {
  const names = (data.value?.providerGroups ?? [])
    .filter((group) => group.providerMatch && group.shownName && group.businessName)
    .map((group) => group.businessName!)
  return names.length ? [...new Set(names)].join('; ') : null
})

function groupLine(group: RateSourceProviderGroup): string {
  const npis = group.wholeTaxId ? 'whole tax ID, no NPIs' : plural(group.npiCount, 'NPI')
  const listed = group.shownName && group.businessName ? `Listed by the plan as ${group.businessName} · ` : ''
  return `${listed}Tax ID ${group.taxIdDisplay} · ${npis} · group ${group.providerGroupId}`
}

const errorMessage = computed(() => (source.error.value instanceof ApiError ? source.error.value.message : source.error.value ? "Couldn't load the source." : null))

const comparisons = computed(() => {
  const s = data.value
  if (!s) return []
  return [
    {
      label: 'Code',
      published: `${s.billingCodeType} ${s.billingCodePublished}`,
      cleaned: `${s.billingCodeType} ${s.billingCode}`,
    },
    {
      label: 'Searched as',
      published:
        s.codeSource === 'additional_information'
          ? s.additionalInformation
            ? `"${s.additionalInformation}" in additional_information`
            : 'a note in additional_information'
          : `${s.codeType} ${s.code}`,
      cleaned: `${s.codeType} ${s.code}`,
    },
    { label: 'Price', published: String(s.rate), cleaned: money(s.rate) },
    { label: 'Rate type', published: s.negotiatedType, cleaned: s.negotiatedType },
    { label: 'Claim form', published: s.billingClass, cleaned: s.billingClass === 'professional' ? "Doctor's (CMS-1500)" : 'Facility (UB-04)' },
    { label: 'Place', published: s.places.map((p) => p.pos).join(', ') || 'not stated', cleaned: s.places.map((p) => p.name).join(', ') || 'Not stated' },
    { label: 'Modifiers', published: s.modifiers.map((m) => m.code).join(', ') || 'none', cleaned: s.modifiers.map((m) => m.label).join('; ') || 'None' },
    { label: 'Ends', published: s.expirationDate ?? '9999-12-31', cleaned: s.expirationDate ? shortDate(s.expirationDate) : 'No end date' },
  ]
})
</script>

<template>
  <v-navigation-drawer v-model="open" location="right" temporary :width="smAndDown ? 340 : 520" data-testid="source-drawer">
    <div class="d-flex align-center pa-4 pb-2">
      <div class="flex-grow-1">
        <h2 ref="heading" tabindex="-1" class="drawer-title">Source of this price</h2>
        <p v-if="providerName" class="muted">{{ cleanName(providerName) }}</p>
        <p v-if="listedAs" class="muted" data-testid="source-listed-as">Listed by the plan as {{ listedAs }}</p>
      </div>
      <v-btn :icon="mdiClose" variant="text" aria-label="Close" @click="open = false" />
    </div>

    <div class="px-4 pb-6 d-flex flex-column ga-6">
      <div v-if="showingWholeTaxId" data-testid="whole-tax-id-shown">
        <p>Showing the whole-tax-ID contract.</p>
        <v-btn variant="text" size="small" class="px-0" @click="showRate(rateId)">Back to this price</v-btn>
      </div>
      <v-skeleton-loader v-if="source.isPending.value && shownRateId != null" type="article, table" />
      <v-alert v-else-if="errorMessage" type="error" variant="tonal">{{ errorMessage }}</v-alert>

      <template v-else-if="data">
        <section class="d-flex flex-column ga-2">
          <h3 class="section-title">File</h3>
          <p>
            {{ data.file.productLabel }}, from {{ data.file.reportingEntityName ?? 'the plan' }} · {{ megabytes(data.file.contentLength) }} · schema
            {{ data.file.schemaVersion ?? 'unknown' }}
          </p>
          <v-table density="compact">
            <tbody>
              <tr>
                <td>Plan's file list</td>
                <td class="text-end">{{ shortDate(data.snapshot.indexDate) }}</td>
              </tr>
              <tr>
                <td>File says updated</td>
                <td class="text-end">{{ shortDate(data.file.lastUpdatedOn) }}</td>
              </tr>
              <tr>
                <td>Server says updated</td>
                <td class="text-end">{{ shortDate(data.file.httpLastModified) }}</td>
              </tr>
            </tbody>
          </v-table>
          <div class="d-flex flex-wrap ga-4">
            <v-btn :href="data.file.url" target="_blank" rel="noopener" variant="text" size="small" :append-icon="mdiOpenInNew" class="px-0">
              Open the file
            </v-btn>
            <v-btn :href="data.snapshot.indexUrl" target="_blank" rel="noopener" variant="text" size="small" :append-icon="mdiOpenInNew" class="px-0">
              Open the file list
            </v-btn>
          </div>
        </section>

        <section class="d-flex flex-column ga-2">
          <h3 class="section-title">Spot in the file</h3>
          <div class="d-flex align-center ga-2">
            <code class="text-break flex-grow-1" data-testid="source-json-path">{{ data.jsonPath }}</code>
            <v-btn
              :icon="copied === 'JSON path' ? mdiCheck : mdiContentCopy"
              :color="copied === 'JSON path' ? 'success' : undefined"
              variant="text"
              size="small"
              :aria-label="copied === 'JSON path' ? 'Copied the JSON path' : 'Copy the JSON path'"
              @click="copyValue(data.jsonPath, 'JSON path')"
            />
          </div>
          <div v-if="data.file.sha256" class="d-flex align-center ga-2">
            <code class="muted text-break flex-grow-1">SHA-256 {{ data.file.sha256 }}</code>
            <v-btn
              :icon="copied === 'SHA-256' ? mdiCheck : mdiContentCopy"
              :color="copied === 'SHA-256' ? 'success' : undefined"
              variant="text"
              size="small"
              :aria-label="copied === 'SHA-256' ? 'Copied the SHA-256' : 'Copy the SHA-256'"
              @click="copyValue(data.file.sha256, 'SHA-256')"
            />
          </div>
          <p class="d-sr-only" role="status">{{ copied ? `Copied the ${copied}` : '' }}</p>
        </section>

        <section class="d-flex flex-column ga-2">
          <h3 class="section-title">As published</h3>
          <v-table density="compact" class="published">
            <thead>
              <tr>
                <th></th>
                <th>In the file</th>
                <th>Shown here</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="row in comparisons" :key="row.label">
                <td>{{ row.label }}</td>
                <td><code>{{ row.published }}</code></td>
                <td>{{ row.cleaned }}</td>
              </tr>
            </tbody>
          </v-table>
        </section>

        <section class="d-flex flex-column ga-2">
          <h3 class="section-title">Repeats</h3>
          <p data-testid="source-repeats">
            Listed {{ data.occurrences === 1 ? 'once' : plural(data.occurrences, 'time') }}<template v-if="data.itemLabelCount > 1">,
              under {{ plural(data.itemLabelCount, 'item name') }}</template>. Counted once.
          </p>
          <p v-if="data.codeSource === 'additional_information' && data.itemLabelSamples.length" class="muted">
            Item names here don't match the procedure:
            {{ data.itemLabelSamples.join('; ') }}{{ data.itemLabelCount > data.itemLabelSamples.length ? '…' : '' }}
          </p>
        </section>

        <section class="d-flex flex-column ga-2">
          <h3 class="section-title">Provider groups ({{ data.providerGroupCount }})</h3>
          <p v-if="data.providerGroupCount > data.providerGroups.length" class="muted">First {{ data.providerGroups.length }} shown.</p>
          <!-- Above the list, which can run to 200 groups; the row's own group is listed first, right below. -->
          <div v-if="wholeTaxIdRateId != null && !showingWholeTaxId" data-testid="whole-tax-id-contract">
            <p>Also a whole-tax-ID contract (no NPIs).</p>
            <v-btn variant="text" size="small" class="px-0" @click="showRate(wholeTaxIdRateId)">Show it</v-btn>
          </div>
          <v-list density="compact" class="py-0 groups">
            <v-list-item
              v-for="group in data.providerGroups"
              :key="`${group.providerGroupId}-${group.groupIndex}`"
              :title="group.shownName ?? group.businessName ?? 'Name not published'"
              :subtitle="groupLine(group)"
            >
              <template v-if="group.providerMatch" #append>
                <span class="match">This provider</span>
              </template>
            </v-list-item>
          </v-list>
        </section>
      </template>
    </div>
  </v-navigation-drawer>
</template>

<style scoped>
.drawer-title {
  font-family: var(--v-font-body);
  font-size: 1.125rem;
  font-weight: 600;
}

.section-title {
  font-family: var(--v-font-body);
  font-size: 1rem;
  font-weight: 600;
}

.muted {
  font-size: 0.875rem;
  color: rgba(var(--v-theme-on-surface), var(--v-medium-emphasis-opacity));
}

/* Long published values wrap inside their cells, so the drawer never scrolls sideways on a phone. */
.published :deep(table) {
  table-layout: fixed;
}

.published :deep(th),
.published :deep(td) {
  overflow-wrap: anywhere;
}

.published :deep(th:first-child) {
  width: 28%;
}

@media (max-width: 600px) {
  .published :deep(th),
  .published :deep(td) {
    padding: 0 8px !important;
  }
}

/* Names and IDs wrap rather than cut off: every group ID stays readable. */
.groups :deep(.v-list-item-title),
.groups :deep(.v-list-item-subtitle) {
  white-space: normal;
  -webkit-line-clamp: unset;
  overflow-wrap: anywhere;
}

.match {
  font-size: 0.875rem;
  font-weight: 600;
  color: rgb(var(--v-theme-primary));
}
</style>
