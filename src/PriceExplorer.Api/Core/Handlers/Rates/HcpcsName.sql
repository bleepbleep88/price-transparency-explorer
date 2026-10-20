-- A HCPCS Level II code's long name from CMS's quarterly HCPCS list (reference/hcpcs_names.csv.gz); none for CPT codes.
SELECT description FROM hcpcs_names WHERE code = $code LIMIT 1
