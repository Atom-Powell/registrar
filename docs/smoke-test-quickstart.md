# The Registrar menu - smoke test quickstart

A hands-on smoke test of the interactive operator menu (`registrar menu`). Written
for PowerShell 7 on Windows, run from the repo root. The engine writes its outputs
(validation report, DOCX) next to each AGR, so this guide works in a scratch folder
to keep the repo's `fixtures/` clean.

## 1. Set up a scratch folder

`$env:USERPROFILE` below is a built-in Windows variable that already points at your
profile (`C:\Users\<you>`) - use it as written, do not replace it with your username.

```powershell
$test = "$env:USERPROFILE\registrar-smoketest"
New-Item -ItemType Directory -Force $test | Out-Null
Copy-Item .\fixtures\agr-sample-ds-draft.json,
          .\fixtures\agr-sample-aup-draft.json,
          .\fixtures\agr-sample-ds-prohibited-char.json  $test
```

The folder is created at `C:\Users\<you>\registrar-smoketest`. When the menu later
asks for a working folder, give it that same path (or run `$test` to print it).

## 2. Launch the menu

Option A - from source (fast, uses the current branch code):

```powershell
$env:DOTNET_ROLL_FORWARD = "LatestMajor"   # this machine has no .NET 8 runtime; roll forward
dotnet run --project .\src\Registrar.Cli -- menu
```

Option B - the packaged go-live artifact (self-contained exe + launcher):

```powershell
pwsh .\scripts\publish.ps1                     # builds dist\registrar\ (about 2 minutes)
.\dist\registrar\runtime\Registrar.cmd         # or: .\dist\registrar\runtime\registrar.exe menu
```

The packaged exe needs no .NET install and no env var - that is the point of it.

## 3. Walk the scenarios

At each menu, type the number or letter and press Enter. `0` or Enter on the main
menu quits.

| Step | Do this | You should see |
|------|---------|----------------|
| 1 | `3` (change folder), then paste the scratch path from step 1 | Banner now shows your working folder |
| 2 | `1` (validate), pick `agr-sample-ds-draft.json`, Enter (mode: draft), Enter (no registry) | Green PASS banner and a tally line |
| 3 | `1`, pick `agr-sample-ds-prohibited-char.json`, Enter, Enter | Red FAIL, a `QA-ENG-05` block with message and remediation. Then press `c` (copy failures) and paste into Notepad to see the Governor paste-block |
| 4 | `2` (build), pick `agr-sample-ds-draft.json`, Enter, Enter | PASS, then press `d` to open the DOCX in Word, `r` to open the JSON report |
| 5 | `2`, pick `agr-sample-ds-draft.json`, type `promotion` (override the draft default), Enter | A red error line ("mode ... does not match"), NOT a stale PASS - this is the fail-closed guard |
| 6 | `0` to quit, then relaunch | Your folder and recent AGRs are remembered |

Also try `agr-sample-aup-draft.json` in step 4 - it is the second artifact type and
pulls in four clauses by reference.

## 4. What to check

- The DOCX files open in Word and look like governed documents: header table,
  Document Control Header, numbered body sections, footer.
- Outputs land next to the AGR in the scratch folder: `validation-report.json`
  always, the DOCX on a passing build, `promotion-payload.json` in promotion mode.
- If a clipboard copy fails (for example over RDP with clipboard redirection off),
  you will see "Could not copy to the clipboard" and the menu keeps going - that is
  the graceful-failure behavior, not a bug.

## 5. Clean up

```powershell
Remove-Item -Recurse -Force $test
Remove-Item -Recurse -Force .\dist   # if you ran Option B
```

## Notes

- For scripting or CI, the direct verbs remain: `registrar validate <agr.json>` and
  `registrar build <agr.json> --mode draft|promotion`. The menu is an additive
  convenience over them.
- The menu never edits AGRs and never contacts the Governor. Copying failures puts
  text on your clipboard for you to paste back into the Governor chat - the operator
  checkpoint stays a human decision.
