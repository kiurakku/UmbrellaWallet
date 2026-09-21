<#
.SYNOPSIS
    Shared supply-chain checks for the bundled third-party binaries (roadmap P0.2).

.DESCRIPTION
    Umbrella ships two programs it did not build: Tor and monero-wallet-rpc. Both are downloaded at
    build time and pinned by SHA-256 — but a hash written into this repository only ever agrees with
    itself. It proves the download matches what these scripts expect; it does not prove that is what
    the Tor Project or the Monero project actually published.

    What closes that gap is the signature. Both projects publish a sums file signed with a known key:
    Tor's `sha256sums-unsigned-build.txt` with a detached `.asc`, Monero's `hashes.txt` clearsigned.
    Verifying the signature, then finding the pinned hash inside the file it signs, turns the pin into
    a statement about the upstream project rather than about this repository.

    The checks are honest about what they managed to do. Without gpg, or without the key, the run says
    so out loud and falls back to the pin alone — and `-Required` (which release builds pass) turns
    that into a failure instead, because a release is exactly where "we could not check" must not
    quietly become "checked".
#>

Set-StrictMode -Version Latest

function Get-GpgCommand {
    foreach ($name in @('gpg', 'gpg2')) {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    }

    return $null
}

<#
.SYNOPSIS
    Proves a pinned SHA-256 is the one the upstream project signed for that exact file name.

.PARAMETER SumsUrl
    The project's own sums file (Tor: sha256sums-unsigned-build.txt, Monero: hashes.txt).

.PARAMETER SignatureUrl
    A detached signature for it, or empty when the sums file is clearsigned (Monero).

.PARAMETER FileName
    The archive name as it appears in the sums file — matched exactly, so a hash cannot be accepted
    because it happens to appear on some other platform's line.
#>
function Assert-PinnedBySignedSums {
    param(
        [Parameter(Mandatory)][string]$SumsUrl,
        [string]$SignatureUrl = '',
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string]$ExpectedSha256,
        [Parameter(Mandatory)][string]$KeyFingerprint,
        # Where to look for the signing key, tried in order. Callers put the project's own copy
        # first where one exists: keys.openpgp.org serves keys with their user IDs stripped unless
        # the owner verified an address there, and gpg cannot verify with what it hands back.
        [string[]]$KeyUrls = @(),
        [Parameter(Mandatory)][string]$WorkDir,
        [switch]$Required,
        [string]$What = 'bundle'
    )

    $expected = $ExpectedSha256.Trim().ToLowerInvariant()
    $fingerprint = ($KeyFingerprint -replace '\s', '').ToUpperInvariant()

    New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
    $sumsPath = Join-Path $WorkDir 'upstream-sums.txt'

    try {
        Invoke-WebRequest -Uri $SumsUrl -OutFile $sumsPath -UseBasicParsing
    }
    catch {
        $message = "Could not download the upstream sums file for ${What}: $SumsUrl`n  $($_.Exception.Message)"
        if ($Required) { throw "$message`nRefusing to continue: -RequireSignature was passed." }
        Write-Warning "$message`nFalling back to the pinned hash alone."
        return
    }

    # --- The signature ---------------------------------------------------------------------------
    $gpg = Get-GpgCommand
    if (-not $gpg) {
        $message = "gpg was not found, so the upstream sums file for $What could not be checked against key $fingerprint."
        if ($Required) { throw "$message`nInstall gpg (or drop -RequireSignature for a throwaway local build)." }
        Write-Warning "$message`nFalling back to the pinned hash alone — do NOT ship this build."
    }
    else {
        $keyring = Join-Path $WorkDir 'gnupg'
        New-Item -ItemType Directory -Force -Path $keyring | Out-Null
        $env:GNUPGHOME = $keyring

        # An isolated keyring, so this never depends on — or writes to — whatever the developer
        # happens to trust locally. The key is fetched by FINGERPRINT, so a keyserver can serve the
        # wrong key but not a key with the right fingerprint.
        $keyPath = Join-Path $WorkDir 'signing-key.asc'
        $fetched = $false
        $sources = @($KeyUrls) + @(
            "https://keys.openpgp.org/vks/v1/by-fingerprint/$fingerprint",
            "https://keyserver.ubuntu.com/pks/lookup?op=get&search=0x$fingerprint")
        foreach ($source in $sources) {
            if (-not $source) { continue }
            try {
                # A key kept in the repository is read from disk; anything else is downloaded.
                if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $keyPath -Force }
                else { Invoke-WebRequest -Uri $source -OutFile $keyPath -UseBasicParsing }
                & $gpg --batch --quiet --import $keyPath 2>&1 | Out-Null

                # Imported is not the same as usable. keys.openpgp.org returns key material with no
                # user ID unless the owner verified an address there, and gpg imports that happily
                # and then refuses to verify with it ("no public key"). Ask for the key by
                # fingerprint and only accept a source gpg can actually use.
                & $gpg --batch --list-keys $fingerprint 2>&1 | Out-Null
                if ($LASTEXITCODE -eq 0) { $fetched = $true; Write-Host "  signing key from: $source"; break }
                Write-Host "  key source gave no usable key with fingerprint ${fingerprint}: $source"
            }
            catch {
                # Say why, then try the next source: a release that fails here must be diagnosable
                # from its log, and 4.8.0/4.8.1 were not.
                Write-Host "  key source failed: $source - $($_.Exception.Message)"
            }
        }

        if (-not $fetched) {
            $message = "Could not fetch the signing key $fingerprint for $What."
            if ($Required) { throw "$message`nRefusing to continue: -RequireSignature was passed." }
            Write-Warning "$message`nFalling back to the pinned hash alone — do NOT ship this build."
        }
        else {
            if ($SignatureUrl) {
                $sigPath = Join-Path $WorkDir 'upstream-sums.asc'
                Invoke-WebRequest -Uri $SignatureUrl -OutFile $sigPath -UseBasicParsing
                $output = & $gpg --batch --status-fd 1 --verify $sigPath $sumsPath 2>&1
            }
            else {
                # Clearsigned: the signature is inside the file itself.
                $output = & $gpg --batch --status-fd 1 --verify $sumsPath 2>&1
            }

            $text = ($output | Out-String)
            # VALIDSIG carries the primary key fingerprint, so a good signature from the WRONG key is
            # rejected — which is the attack a plain "Good signature" check would sail straight past.
            if ($text -notmatch 'VALIDSIG' -or $text -notmatch [regex]::Escape($fingerprint)) {
                throw "Signature check FAILED for the $What sums file.`nExpected a valid signature from $fingerprint.`n$text"
            }

            Write-Host "  signature verified: $What sums signed by $fingerprint"
        }
    }

    # --- The pin, inside the file that was just verified -------------------------------------------
    $line = Select-String -Path $sumsPath -SimpleMatch $FileName -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $line) {
        throw "The upstream sums file does not mention $FileName at all — the pinned version is probably wrong or has been withdrawn.`n  $SumsUrl"
    }

    if ($line.Line -notmatch $expected) {
        throw "PINNED HASH DOES NOT MATCH UPSTREAM for ${FileName}:`n  pinned   $expected`n  upstream $($line.Line.Trim())`nRefusing to continue."
    }

    Write-Host "  pin matches upstream: $FileName"
}
