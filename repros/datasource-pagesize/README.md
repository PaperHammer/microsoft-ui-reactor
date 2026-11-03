# UseDataSource PageSize reproduction

This branch adds only this headless reproduction project. Product source is
unchanged from upstream main at `778c71ea9b60e89d707a4559f7f0c4ed25eba244`.
The project references `src/Reactor/Reactor.csproj`, so it builds the actual
implementation in this checkout rather than relying on a prebuilt local DLL.

## Run

On Windows x64 with the .NET SDK required by the repository's `global.json`
and the Windows build prerequisites described in the root `CONTRIBUTING.md`:

```powershell
git clone --single-branch --branch repro/datasource-pagesize https://github.com/PaperHammer/microsoft-ui-reactor.git reactor-pagesize-repro
cd reactor-pagesize-repro

dotnet restore .\repros\datasource-pagesize\PageSizeRepro.csproj -p:Platform=x64
dotnet build .\repros\datasource-pagesize\PageSizeRepro.csproj --no-restore -c Release -p:Platform=x64 -p:SkipSignaturesGen=true -p:SkipReactorApiGen=true -m:1 -nodereuse:false
dotnet run --project .\repros\datasource-pagesize\PageSizeRepro.csproj --no-build --no-restore -c Release -p:Platform=x64
```

Run each command after the preceding command succeeds. In particular, a failed
build must not be followed by `--no-build`, which could execute an older binary.

The program opens no window and needs no HTTP service. It uses the repository's
`ListDataSource<Row>` containing 100 rows whose IDs equal their zero-based indexes.
Reflection is limited to `RenderContext`'s three internal hook-lifecycle methods;
data fetching, cache access through the hook, and row indexing use the real APIs.

## Cases

1. **Options omitted:** request size 10 produces missing and misplaced rows because
   resource indexing defaults to 50. Passing matching size-10 options is the control.
2. **Runtime size change:** one hook changes both settings from 10 to 20. This is
   checked with unchanged dependencies and with another dependency changed to force
   a restart. A fresh size-20 hook is the control.
3. **Shared cache prefix:** size-10 and size-20 hooks use the same source and cache.
   Different prefixes are the control; a shared prefix lets the size-20 hook reuse
   an incompatible cached page.

The runtime-change restart variant changes search from null to whitespace.
`ListDataSource` ignores whitespace-only searches, so the data set stays unchanged.

Every `PASS`/`FAIL` compares actual row IDs with their expected indexes; the program
does not hard-code the broken values as successful results. On the baseline above,
all three scenarios reproduce, with **7 passed and 4 failed checks**. See
[`expected-output.txt`](expected-output.txt) for the captured run.

Exit codes: **0** = every row expectation met; **1** = incorrect rows detected;
**2** = setup/runtime exception. Exit 1 is the expected reproduction result on this
unfixed branch. It is not a build failure.

This repro intentionally excludes the unrelated completion-order and `Select`
projection findings. It is not added to the main solution or CI.
