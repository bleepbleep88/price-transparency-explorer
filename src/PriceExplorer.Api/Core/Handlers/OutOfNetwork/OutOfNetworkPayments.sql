-- The searched code's out-of-network payments in one snapshot's saved allowed-amount files: one row per NPI on each
-- provider entry (an entry without NPIs, or a payment without providers, gives one row with nulls there). Codes are
-- matched in their stored form. Grouping and statistics happen in OutOfNetworkSummary.
WITH files AS (
    SELECT file_id, product_key, product_label, structure_index
    FROM source_files
    WHERE snapshot_id = $snapshotId AND kind = 'allowed_amount' AND status IN ('loaded', 'partial')
)
SELECT
    p.payment_id        AS PaymentId,
    f.product_key       AS ProductKey,
    f.product_label     AS ProductLabel,
    f.structure_index   AS StructureIndex,
    p.billing_code_type AS BillingCodeType,
    p.billing_code      AS BillingCode,
    p.billing_class     AS BillingClass,
    p.service_codes     AS ServiceCodes,
    p.modifiers         AS Modifiers,
    p.allowed_amount    AS AllowedAmount,
    p.provider_key      AS ProviderKey,
    pr.provider_idx     AS ProviderIndex,
    pr.billed_charge    AS BilledCharge,
    pr.npi              AS Npi
FROM oon_payments p
JOIN files f ON f.file_id = p.file_id
LEFT JOIN oon_providers pr ON pr.payment_id = p.payment_id
WHERE p.billing_code IN $codes
ORDER BY p.payment_id, pr.provider_idx, pr.npi
