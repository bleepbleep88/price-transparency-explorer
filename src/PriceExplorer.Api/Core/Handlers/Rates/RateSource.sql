SELECT
    r.rate_id                 AS RateId,
    r.billing_code_type       AS BillingCodeType,
    r.billing_code            AS BillingCode,
    r.billing_code_raw        AS BillingCodePublished,
    r.code_type               AS CodeType,
    r.code                    AS Code,
    r.code_source             AS CodeSource,
    -- Only a bare code: a free-text note could carry a CPT description (Core/Domain/PublishedText.cs).
    CASE WHEN regexp_matches(r.additional_information, '^[0-9A-Za-z]{3,5}$') THEN r.additional_information END AS AdditionalInformation,
    r.negotiation_arrangement AS NegotiationArrangement,
    r.negotiated_type         AS NegotiatedType,
    r.rate                    AS Rate,
    r.expiration_date         AS ExpirationDate,
    r.billing_class           AS BillingClass,
    r.setting                 AS Setting,
    r.modifiers               AS Modifiers,
    r.occurrences             AS Occurrences,
    r.item_label_count        AS ItemLabelCount,
    -- Item names only for a HCPCS Level II code; a CPT, CDT or revenue-code item's names are never shown.
    CASE WHEN regexp_matches(r.billing_code, '^[A-CE-V][0-9]{4}$') AND regexp_matches(r.code, '^[A-CE-V][0-9]{4}$')
         THEN r.item_labels END AS ItemLabels,
    r.item_index              AS ItemIndex,
    r.rate_index              AS RateIndex,
    r.price_index             AS PriceIndex,
    (SELECT string_agg(rp.pos, ',' ORDER BY rp.pos) FROM rate_places rp WHERE rp.rate_id = r.rate_id) AS Places,
    f.file_id                 AS FileId,
    f.product_key             AS ProductKey,
    f.product_label           AS ProductLabel,
    f.url                     AS FileUrl,
    f.sha256                  AS Sha256,
    f.file_last_updated_on    AS FileLastUpdatedOn,
    f.http_last_modified      AS HttpLastModified,
    f.content_length          AS ContentLength,
    f.reporting_entity_name   AS ReportingEntityName,
    f.file_schema_version     AS SchemaVersion,
    s.snapshot_id             AS SnapshotId,
    s.index_url               AS IndexUrl,
    s.index_date              AS IndexDate,
    s.index_last_updated_on   AS IndexLastUpdatedOn
FROM rates r
JOIN source_files f ON f.file_id = r.file_id
JOIN snapshots s ON s.snapshot_id = f.snapshot_id
WHERE r.rate_id = $rateId
