$ErrorActionPreference = "Stop"

# The organization SCP denies iam:*OpenIDConnectProvider in this account, so GitHub
# Actions cannot federate. The workflows sign in with an access key of a dedicated
# IAM user instead, stored only as secrets of the aws-dev environment.

$Repo = "cvsckrause/cvsTest"
$EnvironmentName = "aws-dev"
$UserName = "erp-dev-github-deploy"
$DevCellAccount = "098981324459"

function Invoke-Aws {
  $output = & aws @args
  if ($LASTEXITCODE -ne 0) { throw "aws $($args[0]) $($args[1]) failed with exit code $LASTEXITCODE." }
  $output
}

function Invoke-Gh {
  $output = & gh @args
  if ($LASTEXITCODE -ne 0) { throw "gh $($args[0]) $($args[1]) failed with exit code $LASTEXITCODE." }
  $output
}

function Test-DeployUser {
  # Windows PowerShell turns redirected native stderr into a terminating error under Stop.
  $previous = $ErrorActionPreference
  $ErrorActionPreference = "Continue"
  try {
    & aws iam get-user --user-name $UserName 2>$null | Out-Null
    return $LASTEXITCODE -eq 0
  } finally {
    $ErrorActionPreference = $previous
  }
}

$account = ((Invoke-Aws sts get-caller-identity --query Account --output text) | Out-String).Trim()
if ($account -ne $DevCellAccount) {
  throw "Refusing to bootstrap account $account. The dev cell account is $DevCellAccount."
}

if (Test-DeployUser) {
  Write-Host "IAM user $UserName already exists."
} else {
  # Quoted: unquoted, PowerShell splits the shorthand at the comma into separate arguments.
  Invoke-Aws iam create-user --user-name $UserName --tags "Key=Project,Value=cvs-erp" "Key=Purpose,Value=github-deploy" | Out-Null
  Write-Host "Created IAM user $UserName."
}

# CDK creates and changes IAM roles for the tasks, so the deploy identity needs admin rights.
Invoke-Aws iam attach-user-policy --user-name $UserName --policy-arn "arn:aws:iam::aws:policy/AdministratorAccess" | Out-Null

$utf8 = New-Object System.Text.UTF8Encoding $false
$userId = ((Invoke-Gh api user --jq .id) | Out-String).Trim()
$environmentBody = @{
  wait_timer = 0
  prevent_self_review = $false
  can_admins_bypass = $false
  reviewers = @(@{ type = "User"; id = [int]$userId })
} | ConvertTo-Json -Depth 6
$environmentFile = Join-Path ([System.IO.Path]::GetTempPath()) "erp-dev-github-environment.json"
[System.IO.File]::WriteAllText($environmentFile, $environmentBody, $utf8)
Invoke-Gh api --method PUT -H "Accept: application/vnd.github+json" "/repos/$Repo/environments/$EnvironmentName" --input $environmentFile | Out-Null
Remove-Item $environmentFile
Write-Host "Environment '$EnvironmentName' requires your approval before a deploy or destroy runs."

# IAM allows two keys per user. Make room, create the new key, and retire the old ones only
# after the secrets point at the new key, so a running workflow keeps working until then.
$existingKeys = @((Invoke-Aws iam list-access-keys --user-name $UserName --output json | Out-String | ConvertFrom-Json).AccessKeyMetadata |
  Sort-Object CreateDate)
if ($existingKeys.Count -ge 2) {
  Invoke-Aws iam delete-access-key --user-name $UserName --access-key-id $existingKeys[0].AccessKeyId | Out-Null
  $existingKeys = @($existingKeys | Select-Object -Skip 1)
}

$created = (Invoke-Aws iam create-access-key --user-name $UserName --output json | Out-String | ConvertFrom-Json).AccessKey
Invoke-Gh secret set AWS_ACCESS_KEY_ID --repo $Repo --env $EnvironmentName --body $created.AccessKeyId | Out-Null
Invoke-Gh secret set AWS_SECRET_ACCESS_KEY --repo $Repo --env $EnvironmentName --body $created.SecretAccessKey | Out-Null
$created = $null
Write-Host "Stored a new access key for $UserName as secrets of '$EnvironmentName'."

foreach ($key in $existingKeys) {
  Invoke-Aws iam delete-access-key --user-name $UserName --access-key-id $key.AccessKeyId | Out-Null
  Write-Host "Deleted the previous access key $($key.AccessKeyId)."
}

Write-Host "Done. Run this script again to rotate the key."
