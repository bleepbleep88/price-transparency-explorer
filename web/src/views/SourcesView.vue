<script setup lang="ts">
import { computed } from 'vue'
import { mdiOpenInNew } from '@mdi/js'
import { ApiError } from '@/lib/apiClient'
import { plural, shortDate, wholeNumber } from '@/lib/format'
import DetailsPanel from '@/components/simple/DetailsPanel.vue'
import PageHeader from '@/components/simple/PageHeader.vue'
import LoadingPanel from '@/features/status/components/LoadingPanel.vue'
import { useDataStatus } from '@/features/status/useDataStatus'
import SourceFileDetails from '@/features/sources/components/SourceFileDetails.vue'
import { useSources } from '@/features/sources/composables/useSources'
import {
  CLEANING_RULES,
  leadSnapshot,
  planFiles,
  planProblems,
  priceTotals,
  snapshotProblem,
  sourcesSubtitle,
  totalsText,
} from '@/features/sources/lib/sourceText'

const sources = useSources()
const { status } = useDataStatus()

const snapshots = computed(() => sources.data.value ?? [])
const errorMessage = computed(() =>
  sources.error.value instanceof ApiError ? sources.error.value.message : sources.error.value ? 'Could not load the sources.' : null,
)
const stillLoading = computed(() => status.value != null && status.value.phase !== 'ready' && status.value.phase !== 'failed')
const subtitle = computed(() => sourcesSubtitle(snapshots.value))
// The one answer: the default month's published prices and what's left once exact copies count once.
const lead = computed(() => leadSnapshot(snapshots.value))
const totals = computed(() => (lead.value ? priceTotals(lead.value) : null))
</script>

<template>
  <v-container class="sources-page" max-width="960">
    <PageHeader title="Where the data comes from" :subtitle="subtitle" />

    <div v-if="sources.isPending.value" class="d-flex flex-column ga-4">
      <v-skeleton-loader type="heading" />
      <v-skeleton-loader type="list-item-two-line@3" />
    </div>
    <div v-else-if="errorMessage" class="d-flex flex-column ga-2 align-start" role="alert">
      <p class="problem">{{ errorMessage }}</p>
      <v-btn variant="tonal" color="primary" @click="sources.refetch()">Try again</v-btn>
    </div>
    <template v-else>
      <LoadingPanel v-if="stillLoading" />
      <p v-if="!snapshots.length && !stillLoading">Nothing loaded yet.</p>

      <section v-if="lead && totals" class="answer" data-testid="sources-total">
        <p class="big" :aria-label="totalsText(totals)">
          <span>{{ plural(totals.published, 'price') }}</span> <span class="nowrap">→ {{ wholeNumber(totals.unique) }}</span>
        </p>
        <p>{{ shortDate(lead.indexDate) }} files, exact copies counted once</p>
      </section>

      <section
        v-for="snapshot in snapshots"
        :key="snapshot.snapshotId"
        class="month"
        :aria-labelledby="`month-${snapshot.snapshotId}`"
        data-testid="source-snapshot"
      >
        <div class="month-head">
          <h2 :id="`month-${snapshot.snapshotId}`" class="month-title">{{ shortDate(snapshot.indexDate) }}</h2>
          <span v-if="snapshot.isLatest" class="secondary">Latest</span>
        </div>
        <p v-if="snapshotProblem(snapshot)" class="problem">{{ snapshotProblem(snapshot) }}</p>

        <ul class="plans">
          <li v-for="plan in planFiles(snapshot)" :key="plan.productKey" class="plan" data-testid="source-plan">
            <span class="plan-name">{{ plan.productLabel }}</span>
            <span class="plan-links">
              <a
                v-if="plan.prices"
                :href="plan.prices.url"
                target="_blank"
                rel="noopener"
                :aria-label="`${plan.productLabel} prices file, ${shortDate(snapshot.indexDate)}`"
                data-testid="source-link-prices"
              >
                Prices <v-icon :icon="mdiOpenInNew" size="16" aria-hidden="true" />
              </a>
              <a
                v-if="plan.outOfNetwork"
                :href="plan.outOfNetwork.url"
                target="_blank"
                rel="noopener"
                :aria-label="`${plan.productLabel} out of network file, ${shortDate(snapshot.indexDate)}`"
                data-testid="source-link-oon"
              >
                Out of network <v-icon :icon="mdiOpenInNew" size="16" aria-hidden="true" />
              </a>
            </span>
            <span v-for="problem in planProblems(plan)" :key="problem" class="problem plan-problem" data-testid="source-plan-problem">
              {{ problem }}
            </span>
          </li>
        </ul>

        <DetailsPanel>
          <p>
            Published by {{ snapshot.reportingEntityName ?? 'the plan'
            }}<template v-if="snapshot.reportingEntityType"> ({{ snapshot.reportingEntityType }})</template>. List of files updated
            {{ shortDate(snapshot.indexLastUpdatedOn) }}, schema {{ snapshot.schemaVersion ?? 'not stated' }}.
          </p>
          <p v-if="snapshot.error" class="problem">{{ snapshot.error }}</p>
          <code v-if="snapshot.indexSha256" class="trace">SHA-256 {{ snapshot.indexSha256 }}</code>
          <a :href="snapshot.indexUrl" target="_blank" rel="noopener" class="file-link">
            List of files <v-icon :icon="mdiOpenInNew" size="16" aria-hidden="true" />
          </a>
          <SourceFileDetails v-for="file in snapshot.files" :key="file.fileId" :file="file" />
        </DetailsPanel>
      </section>

      <DetailsPanel v-if="snapshots.length" label="How files are cleaned" testid="cleaning-rules">
        <ul class="lines">
          <li v-for="rule in CLEANING_RULES" :key="rule">{{ rule }}</li>
        </ul>
      </DetailsPanel>
    </template>
  </v-container>
</template>

<style scoped>
.sources-page {
  display: flex;
  flex-direction: column;
  gap: 24px;
  padding-top: 40px;
  padding-bottom: 40px;
}

.answer {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.big {
  font-family: var(--v-font-heading);
  font-size: 2.5rem;
  font-weight: 600;
  line-height: 1.1;
  overflow-wrap: anywhere;
}

.nowrap {
  white-space: nowrap;
}

.month {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 16px 16px 0;
  background: rgb(var(--v-theme-surface));
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-radius: 8px;
}

.month-head {
  display: flex;
  align-items: baseline;
  gap: 8px;
}

.month-title {
  font-family: var(--v-font-body);
  font-size: 1.125rem;
  font-weight: 600;
}

.secondary {
  font-size: 0.875rem;
  color: rgba(var(--v-theme-on-surface), var(--v-medium-emphasis-opacity));
}

.plans {
  list-style: none;
  padding: 0;
}

.plan {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  justify-content: space-between;
  gap: 4px 16px;
  padding: 8px 0;
  border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
}

.plan:last-child {
  border-bottom: 0;
}

.plan-name {
  font-weight: 500;
}

.plan-links {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
}

.plan-links a,
.file-link {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  color: rgb(var(--v-theme-primary));
}

.file-link {
  align-self: flex-start;
}

.plan-problem {
  flex-basis: 100%;
  font-size: 0.875rem;
}

.problem {
  color: rgb(var(--v-theme-error));
}

.lines {
  padding-left: 16px;
}

.trace {
  font-size: 0.875rem;
  overflow-wrap: anywhere;
}
</style>
