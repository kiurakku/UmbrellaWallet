# Applies GitHub repository hardening (branch rulesets + security features).
# Run once after cloning or when resetting org policy. Requires `gh` auth as repo admin.
#
# Usage (Git Bash / PowerShell):
#   gh auth login
#   pwsh .github/scripts/apply-repo-hardening.ps1

$ErrorActionPreference = "Stop"
$repo = "kiurakku/umbrella-wallet"

Write-Host "Patching repository settings…" -ForegroundColor Cyan
gh api --method PATCH "repos/$repo" `
  -f delete_branch_on_merge=true `
  -f allow_update_branch=true `
  -f allow_squash_merge=true `
  -f allow_merge_commit=false `
  -f allow_rebase_merge=false `
  -f allow_forking=false `
  -f description="Privacy-first non-custodial wallet (Windows & Linux). Source-available for audit — no forks. Sponsor: github.com/sponsors/kiurakku" | Out-Null

Write-Host "Enabling security analysis features…" -ForegroundColor Cyan
$sec = @{
  security_and_analysis = @{
    secret_scanning = @{ status = "enabled" }
    secret_scanning_push_protection = @{ status = "enabled" }
    dependabot_security_updates = @{ status = "enabled" }
  }
} | ConvertTo-Json -Depth 5
$sec | gh api --method PATCH "repos/$repo" --input - 2>$null
if ($LASTEXITCODE -ne 0) {
  Write-Warning "Some security features may require GitHub Advanced Security or org policy."
}

# Remove legacy protection if present (rulesets supersede it).
gh api --method DELETE "repos/$repo/branches/main/protection" 2>$null | Out-Null

$globalRules = @'
{
  "name": "Global — no force-push or deletion",
  "target": "branch",
  "enforcement": "active",
  "bypass_actors": [],
  "conditions": {
    "ref_name": { "include": ["~ALL"], "exclude": [] }
  },
  "rules": [
    { "type": "deletion" },
    { "type": "non_fast_forward" }
  ]
}
'@

$mainRules = @'
{
  "name": "main — PR, reviews, CI gates",
  "target": "branch",
  "enforcement": "active",
  "bypass_actors": [],
  "conditions": {
    "ref_name": { "include": ["refs/heads/main"], "exclude": [] }
  },
  "rules": [
    {
      "type": "pull_request",
      "parameters": {
        "dismiss_stale_reviews_on_push": true,
        "require_code_owner_review": true,
        "require_last_push_approval": true,
        "required_approving_review_count": 1,
        "required_review_thread_resolution": true
      }
    },
    {
      "type": "required_status_checks",
      "parameters": {
        "strict_required_status_checks_policy": true,
        "required_status_checks": [
          { "context": "desktop" },
          { "context": "gitleaks" },
          { "context": "dotnet-vulnerable" },
          { "context": "analyze (csharp)" }
        ]
      }
    },
    { "type": "deletion" },
    { "type": "non_fast_forward" }
  ]
}
'@

function Upsert-Ruleset([string]$name, [string]$json) {
  $existing = gh api "repos/$repo/rulesets" --jq ".[] | select(.name==`"$name`") | .id" 2>$null
  if ($existing) {
    Write-Host "Updating ruleset: $name ($existing)" -ForegroundColor Yellow
    $json | gh api --method PUT "repos/$repo/rulesets/$existing" --input - | Out-Null
  } else {
    Write-Host "Creating ruleset: $name" -ForegroundColor Green
    $json | gh api --method POST "repos/$repo/rulesets" --input - | Out-Null
  }
}

Upsert-Ruleset "Global — no force-push or deletion" $globalRules
Upsert-Ruleset "main — PR, reviews, CI gates" $mainRules

Write-Host "Done. Verify: https://github.com/$repo/settings/rules" -ForegroundColor Cyan
