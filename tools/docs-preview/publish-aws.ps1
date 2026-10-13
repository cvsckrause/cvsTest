$ErrorActionPreference = "Stop"
$bucket = "erp-docs-098981324459"
$distributionId = "E2TFQ4QFVZ1QB"
$node = "C:\Program Files\nodejs\node.exe"
$dist = "tools/docs-preview/dist"

function Invoke-Aws {
  & aws @args
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& $node "tools/docs-preview/export-static.mjs"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Invoke-Aws s3 cp "$dist/index.html" "s3://$bucket/index.html" --content-type "text/html; charset=utf-8" --cache-control "public, max-age=300"
Invoke-Aws s3 cp "$dist/404.html" "s3://$bucket/404.html" --content-type "text/html; charset=utf-8" --cache-control "public, max-age=300"
Invoke-Aws s3 cp "$dist/health" "s3://$bucket/health" --content-type "application/json; charset=utf-8" --cache-control "public, max-age=60"
Invoke-Aws s3 cp "$dist/vendor/mermaid.min.js" "s3://$bucket/vendor/mermaid.min.js" --content-type "text/javascript; charset=utf-8" --cache-control "public, max-age=86400"
Get-ChildItem "$dist/doc" -File | ForEach-Object {
  Invoke-Aws s3 cp $_.FullName "s3://$bucket/doc/$($_.Name)" --content-type "text/html; charset=utf-8" --cache-control "public, max-age=300"
}
Invoke-Aws cloudfront create-invalidation --distribution-id $distributionId --paths "/*"
