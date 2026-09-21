# Applies GitHub repository hardening (branch rulesets + security features).
# Run once after cloning or when resetting org policy. Requires `gh` auth as repo admin.
#
# Usage (Git Bash / PowerShell):
#   gh auth login
#   pwsh .github/scripts/apply-repo-hardening.ps1

$ErrorActionPreference = "Stop"
$repo = "thefear078/UmbrellaWallet"

Write-Host "Patching repository settings…" -ForegroundColor Cyan
gh api --method PATCH "repos/$repo" `
  -f delete_branch_on_merge=true `
  -f allow_update_branch=true `
  -f allow_squash_merge=true `
  -f allow_merge_commit=false `
  -f allow_rebase_merge=false `
  -f description="Privacy-first non-custodial wallet (Windows & Linux). Source-available for audit — no forks. Docs: docs/INDEX.md. By the fear." | Out-Null

# Note: allow_forking=false only works on org-owned private repos via API.
# Public repos rely on LICENSE + CONTRIBUTING policy instead.

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

# Private vulnerability reporting
gh api --method PUT "repos/$repo/private-vulnerability-reporting" 2>$null | Out-Null

# Remove legacy protection if present (rulesets supersede it).
gh api --method DELETE "repos/$repo/branches/main/protection" 2>$null | Out-Null

$globalRules = @'
{
  "name": "All branches — no force-push",
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

# Solo maintainer: require PR + CI, but approving_review_count=0 (self-approve is blocked
# when count>=1). Code owners file still documents ownership; review not hard-required.
$mainRules = @'
{
  "name": "main — CI gates, no force-push, no deletion",
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
        "require_code_owner_review": false,
        "require_last_push_approval": false,
        "required_approving_review_count": 0,
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
          { "context": "dependency-review" },
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

Upsert-Ruleset "All branches — no force-push" $globalRules
Upsert-Ruleset "main — CI gates, no force-push, no deletion" $mainRules

Write-Host "Done. Verify: https://github.com/$repo/settings/rules" -ForegroundColor Cyan
Write-Host "Status doc: docs/REPO_HARDENING.md" -ForegroundColor Cyan
