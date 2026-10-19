-- Plans per product. A plan belongs to every rate file its reporting structure lists (structures may share files).
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
    SELECT DISTINCT sf.snapshot_id, sf.structure_index, sf_file.product_key, sf_file.product_label
    FROM structure_files sf
    JOIN source_files sf_file
      ON sf_file.snapshot_id = sf.snapshot_id AND sf_file.url = sf.url AND sf_file.kind = 'in_network'
) f ON f.snapshot_id = p.snapshot_id AND f.structure_index = p.structure_index
WHERE p.snapshot_id = $snapshotId
ORDER BY f.product_label, p.plan_id, p.plan_name
