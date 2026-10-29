UsePersisted: clearing an optional assignee filter is lost on remount
==================================================================

This branch adds only the repro and evidence. Reactor's implementation is
unchanged from upstream main at 7f46468f3cb1307c7fbf108a6a575849a65084c9.
The separate fix branch is fix/persisted-null-roundtrip.

Requirements: Windows, .NET 10 SDK and x64.

Clone the repro branch (PowerShell):

  git clone --branch repro/persisted-null-roundtrip https://github.com/PaperHammer/microsoft-ui-reactor.git
  cd microsoft-ui-reactor

Then, from the repository root:

  $repoPath = (Get-Location).Path
  dotnet restore .\repro\persisted-null\Repro\PersistedNullRepro.csproj -p:Platform=x64 "-p:ReactorRoot=$repoPath"
  dotnet build .\repro\persisted-null\Repro\PersistedNullRepro.csproj --no-restore -c Release -p:Platform=x64 "-p:ReactorRoot=$repoPath" -p:SkipSignaturesGen=true -p:SkipReactorApiGen=true -m:1 -nodereuse:false
  dotnet run --project .\repro\persisted-null\Repro\PersistedNullRepro.csproj --no-build --no-restore -c Release -p:Platform=x64 "-p:ReactorRoot=$repoPath"

1. The optional assignee selector initially selects Alice; 2/4 tasks are visible.
2. Click "Clear filter". No assignee is selected, assigneeId is null, and all four
   tasks become visible. The ComboBox displays its "All assignees" placeholder.
3. Click "Navigate away" to unmount the task-filter component.
4. Click "Return to tasks" to mount it again in the same window.

Expected: the filter stays cleared and all four tasks remain visible.
Actual on this branch: Alice is selected again and only her two tasks are visible.

The four task records are local fixtures (two for Alice, two for Bob); no backend
is needed. The visible list is filtered directly from the hook's current value.
The clear button explicitly calls setAssigneeId(null).

The scope retains its single entry (Count=1) before remount. In the CACHE log,
hit is the public TryGet<string?> result, including type validation. With
hit=False, value=null is the default out value. The retained null entry should
instead produce hit=True, value=null. A truly missing or evicted entry may
correctly restore the initial value.

Headless mode (same build; no WinUI controls are mounted):

  dotnet run --project .\repro\persisted-null\Repro\PersistedNullRepro.csproj --no-build --no-restore -c Release -p:Platform=x64 "-p:ReactorRoot=$repoPath" -- --headless --log "$PWD\persisted-null-output.log"
  Get-Content .\persisted-null-output.log

Exit code 1 means the null-restoration failures reproduced; exit code 0 means
all null cases and non-null/missing-key controls passed. Reflection drives only
RenderContext's internal BeginRender/RunCleanups lifecycle in headless mode.
GUI mode uses a real ReactorApp/ReactorWindow and a real ComboBox.

To verify the fix, build the same repro with ReactorRoot pointing to a separate
checkout of fix/persisted-null-roundtrip. Run restore again when changing that
path, then build and run with the updated ReactorRoot value.

Evidence (captured against the base main commit and the uncommitted local fix):
- evidence/main-assignee-ui.png: original behavior after clear/leave/return.
- evidence/fixed-assignee-ui.png: the same sequence with the local fix.
- evidence/*-ui-output.log: complete GUI output, including the final RENDER line.
- evidence/*-headless-output.log: both scope implementations and hook lifecycle.

The screenshots are unedited compact window captures with no cursor. Logs are
also displayed in the window, refreshed after reconciliation. Logging does not
replace or modify the cache. GUI output is written beside the executable.