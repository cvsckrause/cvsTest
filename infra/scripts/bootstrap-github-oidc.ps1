$ErrorActionPreference = "Stop"

$Repo = "cvsckrause/cvsTest"
$EnvironmentName = "aws-dev"
$RoleName = "erp-dev-github-deploy"
$ProviderHost = "token.actions.githubusercontent.com"

function Invoke-Aws {
  & aws @args
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$account = (& aws sts get-caller-identity --query Account --output text).Trim()
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($account -ne "098981324459") {
  Write-Error "Refusing to bootstrap account $account. The dev cell account is 098981324459."
}

$providerArn = "arn:aws:iam::${account}:oidc-provider/${ProviderHost}"
& aws iam get-open-id-connect-provider --open-id-connect-provider-arn $providerArn | Out-Null
if ($LASTEXITCODE -ne 0) {
  Invoke-Aws iam create-open-id-connect-provider `
    --url "https://$ProviderHost" `
    --client-id-list sts.amazonaws.com `
    --thumbprint-list 6938fd4d98bab03faadb97b34396831e3780aea1 1c58a3a8518e8759bf075b76b750d4f2df264fcd
  Write-Host "Created GitHub OIDC provider."
} else {
  Write-Host "GitHub OIDC provider already exists."
}

$trust = @{
  Version = "2012-10-17"
  Statement = @(
    @{
      Effect = "Allow"
      Principal = @{ Federated = $providerArn }
      Action = "sts:AssumeRoleWithWebIdentity"
      Condition = @{
        StringEquals = @{
          "${ProviderHost}:aud" = "sts.amazonaws.com"
          "${ProviderHost}:sub" = "repo:${Repo}:environment:${EnvironmentName}"
        }
      }
    }
  )
} | ConvertTo-Json -Depth 8

$trustFile = Join-Path ([System.IO.Path]::GetTempPath()) "erp-dev-github-trust.json"
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($trustFile, $trust, $utf8)
$trustUri = ($trustFile -replace '\\', '/')

& aws iam get-role --role-name $RoleName | Out-Null
if ($LASTEXITCODE -ne 0) {
  Invoke-Aws iam create-role `
    --role-name $RoleName `
    --description "GitHub Actions deploy and destroy for the ERP dev cell." `
    --assume-role-policy-document "file://$trustUri"
  Write-Host "Created $RoleName."
} else {
  Invoke-Aws iam update-assume-role-policy --role-name $RoleName --policy-document "file://$trustUri"
  Write-Host "Updated trust policy on $RoleName."
}

Invoke-Aws iam attach-role-policy `
  --role-name $RoleName `
  --policy-arn "arn:aws:iam::aws:policy/AdministratorAccess"

Write-Host "Attached AdministratorAccess. CDK has to create IAM resources, and only the aws-dev GitHub environment can assume this role."

$userId = (& gh api user --jq .id).Trim()
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$environmentBody = @{
  wait_timer = 0
  prevent_self_review = $false
  can_admins_bypass = $false
  reviewers = @(@{ type = "User"; id = [int]$userId })
} | ConvertTo-Json -Depth 6
$environmentFile = Join-Path ([System.IO.Path]::GetTempPath()) "erp-dev-github-environment.json"
[System.IO.File]::WriteAllText($environmentFile, $environmentBody, $utf8)
& gh api --method PUT -H "Accept: application/vnd.github+json" "/repos/$Repo/environments/$EnvironmentName" --input $environmentFile
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Cost gate is the GitHub environment '$EnvironmentName'. A deploy or destroy waits for your approval, then assumes $RoleName."
Write-Host "Role ARN: arn:aws:iam::${account}:role/${RoleName}"
