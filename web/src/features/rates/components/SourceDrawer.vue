<script setup lang="ts">
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue'
import { useDisplay } from 'vuetify'
import { mdiCheck, mdiClose, mdiContentCopy, mdiOpenInNew } from '@mdi/js'
import { copyText } from '@/lib/clipboard'
import { megabytes, money, plural, shortDate } from '@/lib/format'
import { ApiError } from '@/lib/apiClient'
import { useRateSource } from '../composables/useRateQueries'

const open = defineModel<boolean>({ required: true })
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
const errorMessage = computed(() => (source.error.value instanceof ApiError ? source.error.value.message : source.error.value ? 'Could not load the source details.' : null))

const comparisons = computed(() => {
  const s = data.value
  if (!s) return []
  return [
    {
      label: 'Billing code',
      published: `${s.billingCodeType} ${s.billingCodePublished}`,
      cleaned: `${s.billingCodeType} ${s.billingCode}`,
    },
    {
      label: 'Procedure searched as',
      published: s.codeSource === 'additional_information' ? `"${s.additionalInformation}" in additional_information` : `${s.codeType} ${s.code}`,
      cleaned: `${s.codeType} ${s.code}`,
    },
    { label: 'Rate', published: String(s.rate), cleaned: money(s.rate) },
    { label: 'Rate type', published: s.negotiatedType, cleaned: s.negotiatedType },
    { label: 'Billed on', published: s.billingClass, cleaned: s.billingClass === 'professional' ? 'Professional claim (CMS-1500)' : 'Facility claim (UB-04)' },
    { label: 'Setting', published: s.places.map((p) => p.pos).join(', ') || 'not stated', cleaned: s.places.map((p) => p.name).join(', ') || 'Not stated' },
    { label: 'Modifiers', published: s.modifiers.map((m) => m.code).join(', ') || 'none', cleaned: s.modifiers.map((m) => m.label).join('; ') || 'None' },
    { label: 'Ends', published: s.expirationDate ?? '9999-12-31', cleaned: s.expirationDate ? shortDate(s.expirationDate) : 'No end date' },
  ]
})
</script>

<template>
  <v-navigation-drawer v-model="open" location="right" temporary :width="smAndDown ? 340 : 540" data-testid="source-drawer">
    <div class="d-flex align-center pa-4 pb-2">
      <div class="flex-grow-1">
        <h2 ref="heading" tabindex="-1" class="text-title-large">Where this rate comes from</h2>
        <p v-if="providerName" class="text-body-medium text-medium-emphasis">{{ providerName }}</p>
      </div>
      <v-btn :icon="mdiClose" variant="text" aria-label="Close" @click="open = false" />
    </div>

    <div class="px-4 pb-6 d-flex flex-column ga-4">
      <div v-if="showingWholeTaxId" data-testid="whole-tax-id-shown">
        <p class="text-body-medium">Showing the row's whole-tax-ID contract.</p>
        <v-btn variant="text" size="small" class="px-0" @click="showRate(rateId)">Back to the row's rate</v-btn>
      </div>
      <v-skeleton-loader v-if="source.isPending.value && shownRateId != null" type="article, table" />
      <v-alert v-else-if="errorMessage" type="error" variant="tonal">{{ errorMessage }}</v-alert>

      <template v-else-if="data">
        <section>
          <h3 class="text-title-small mb-1">File</h3>
          <p class="text-body-medium">
            {{ data.file.productLabel }} in-network rates from {{ data.file.reportingEntityName ?? 'the plan' }}
            ({{ megabytes(data.file.contentLength) }}, schema {{ data.file.schemaVersion ?? 'unknown' }})
          </p>
          <v-btn :href="data.file.url" target="_blank" rel="noopener" variant="text" size="small" :append-icon="mdiOpenInNew" class="px-0">
            Open the file
          </v-btn>
          <v-table density="compact" class="mt-1">
            <tbody>
              <tr>
                <td>Index (snapshot) date</td>
                <td>{{ shortDate(data.snapshot.indexDate) }}</td>
              </tr>
              <tr>
                <td>File's own "last updated"</td>
                <td>{{ shortDate(data.file.lastUpdatedOn) }}</td>
              </tr>
              <tr>
                <td>Server's last modified</td>
                <td>{{ shortDate(data.file.httpLastModified) }}</td>
              </tr>
            </tbody>
          </v-table>
          <v-btn :href="data.snapshot.indexUrl" target="_blank" rel="noopener" variant="text" size="small" :append-icon="mdiOpenInNew" class="px-0">
            Open the index file
          </v-btn>
        </section>

        <section>
          <h3 class="text-title-small mb-1">Exact spot in the file</h3>
          <div class="d-flex align-center ga-1">
            <code class="text-body-medium text-break flex-grow-1" data-testid="source-json-path">{{ data.jsonPath }}</code>
            <v-btn
              :icon="copied === 'JSON path' ? mdiCheck : mdiContentCopy"
              :color="copied === 'JSON path' ? 'success' : undefined"
              variant="text"
              size="small"
              :aria-label="copied === 'JSON path' ? 'Copied the JSON path' : 'Copy the JSON path'"
              @click="copyValue(data.jsonPath, 'JSON path')"
            />
          </div>
          <div v-if="data.file.sha256" class="d-flex align-center ga-1 mt-1">
            <code class="text-body-small text-break flex-grow-1">SHA-256 {{ data.file.sha256 }}</code>
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

        <section>
          <h3 class="text-title-small mb-1">As published vs. cleaned up</h3>
          <v-table density="compact">
            <thead>
              <tr>
                <th></th>
                <th>In the file</th>
                <th>Shown here</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="row in comparisons" :key="row.label">
                <td class="text-body-small">{{ row.label }}</td>
                <td class="text-body-medium"><code>{{ row.published }}</code></td>
                <td class="text-body-medium">{{ row.cleaned }}</td>
              </tr>
            </tbody>
          </v-table>
        </section>

        <section>
          <h3 class="text-title-small mb-1">Duplicates</h3>
          <p class="text-body-medium">
            This exact rate is published {{ plural(data.occurrences, 'time') }}<template v-if="data.itemLabelCount > 1">,
              under {{ plural(data.itemLabelCount, 'different item name') }}</template>. It's counted once here.
          </p>
          <p v-if="data.codeSource === 'additional_information'" class="text-body-medium text-medium-emphasis mt-1">
            The plan files facility rates under revenue codes and names the procedure in a notes field. The item names
            attached to these rows don't match the procedure, so they're shown only for tracing:
            {{ data.itemLabelSamples.join('; ') }}{{ data.itemLabelCount > data.itemLabelSamples.length ? '…' : '' }}
          </p>
        </section>

        <section>
          <h3 class="text-title-small mb-1">Provider groups covered ({{ data.providerGroupCount }})</h3>
          <p v-if="data.providerGroupCount > data.providerGroups.length" class="text-body-small text-medium-emphasis mb-1">
            Showing the first {{ data.providerGroups.length }}.
          </p>
          <v-list density="compact" class="py-0">
            <v-list-item
              v-for="group in data.providerGroups"
              :key="`${group.providerGroupId}-${group.groupIndex}`"
              :title="group.businessName ?? 'Name not published'"
              :subtitle="`Tax ID ${group.taxIdDisplay} · ${group.wholeTaxId ? 'whole tax ID (no NPIs listed)' : plural(group.npiCount, 'NPI')} · provider_group_id ${group.providerGroupId}`"
            >
              <template v-if="group.providerMatch" #append>
                <v-chip size="x-small" color="primary" variant="tonal">This provider</v-chip>
              </template>
            </v-list-item>
          </v-list>
          <div v-if="wholeTaxIdRateId != null && !showingWholeTaxId" class="mt-2" data-testid="whole-tax-id-contract">
            <p class="text-body-medium">This row also includes a whole-tax-ID contract (no NPIs listed).</p>
            <v-btn variant="text" size="small" class="px-0" @click="showRate(wholeTaxIdRateId)">Show that contract</v-btn>
          </div>
        </section>
      </template>
    </div>
  </v-navigation-drawer>
</template>
