
# Import-RiftboundArtwork.ps1
# Copies and renames the Spiritforged/Unleashed card art from the two downloaded
# folders into the Unity project, named exactly "<cardId>.png" so that
# TCG Collector > Auto-Assign Card Artwork (CardArtworkAssigner.cs) picks them up
# automatically. Originals are left untouched (this COPIES, does not move).
#
# Usage: just run this script (right-click > "Run with PowerShell", or from a
# PowerShell terminal: .\Import-RiftboundArtwork.ps1). Edit the 3 paths below
# first if your folders are located somewhere else.

$sfdSource = "E:\Dev\claude\riftbound_sets_2_3_telechargeurs\riftbound_spiritforged_images_complet"
$unlSource = "E:\Dev\claude\riftbound_sets_2_3_telechargeurs\riftbound_unleashed_images_complet"
$projectRoot = "E:\Dev\claude\unity\TCGCollector"

$sfdDest = Join-Path $projectRoot "Assets\Data\Riftbound\Cards\RiftboundSpiritforged\Artwork"
$unlDest = Join-Path $projectRoot "Assets\Data\Riftbound\Cards\RiftboundUnleashed\Artwork"

New-Item -ItemType Directory -Force -Path $sfdDest | Out-Null
New-Item -ItemType Directory -Force -Path $unlDest | Out-Null

function Import-Set {
    param(
        [string]$SourceDir,
        [string]$DestDir,
        [string]$Label
    )

    $copied = 0
    $unmatched = @()

    Get-ChildItem -Path $SourceDir -Filter "*.png" | ForEach-Object {
        # Expected filename shape: "<CODE>-<num[letter]>_<English name>.png"
        # e.g. "SFD-001_Against the Odds.png" -> cardId "sfd_001"
        #      "SFD-020a_Draven, Vanquisher.png" -> cardId "sfd_020a"
        #      "SFD-T03_Gold.png" -> cardId "sfd_t03"
        if ($_.Name -match '^([A-Za-z]+)-([A-Za-z0-9]+)_') {
            $code = $Matches[1].ToLowerInvariant()
            $num = $Matches[2].ToLowerInvariant()
            $cardId = "${code}_${num}"
            $destPath = Join-Path $DestDir "$cardId.png"
            Copy-Item -Path $_.FullName -Destination $destPath -Force
            $copied++
        } else {
            $unmatched += $_.Name
        }
    }

    Write-Host "[$Label] Copied $copied image(s) into $DestDir"
    if ($unmatched.Count -gt 0) {
        Write-Host "[$Label] Could not parse a card id from $($unmatched.Count) file(s):"
        $unmatched | ForEach-Object { Write-Host "    $_" }
    }
}

Import-Set -SourceDir $sfdSource -DestDir $sfdDest -Label "Spiritforged"
Import-Set -SourceDir $unlSource -DestDir $unlDest -Label "Unleashed"

Write-Host ""
Write-Host "Done. Go back to Unity, let it import the new PNGs, then run:"
Write-Host "  Tools > TCG Collector > Auto-Assign Card Artwork"
Write-Host "(then Tools > TCG Collector > Rebuild Card Database if it wasn't already refreshed)"
