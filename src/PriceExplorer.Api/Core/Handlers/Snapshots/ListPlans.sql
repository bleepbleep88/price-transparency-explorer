SELECT
    f.product_key       AS ProductKey,
    f.product_label     AS ProductLabel,
    p.plan_id           AS PlanId,
    p.plan_id_type      AS PlanIdType,
    p.plan_market_type  AS MarketType,
    p.issuer_name       AS IssuerName,
    p.plan_sponsor_name AS SponsorName,
    p.plan_name         AS PlanName
FROM plans p
JOIN (
    SELECT DISTINCT snapshot_id, structure_index, product_key, product_label
    FROM source_files
    WHERE kind = 'in_network'
) f ON f.snapshot_id = p.snapshot_id AND f.structure_index = p.structure_index
WHERE p.snapshot_id = $snapshotId
ORDER BY f.product_label, p.plan_id, p.plan_name
