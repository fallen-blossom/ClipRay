# ClipRay TXT Acceptance Catalog

This offline tool validates test data. It does not invoke an OCR model, control a browser, or claim a foreground acceptance result.

Run from the repository root:

```powershell
dotnet run --project tests/ClipRay.Acceptance/ClipRay.Acceptance.csproj
```

Each UTF-8 `.txt` file contains one or more `[case]` sections with `key=value` entries. Every case requires unique `id` and `kind` values. Translation cases provide `source_language`, `target_language`, `source_text` and `expected_text`. A `scroll_capture` case uses numbered `content_line.01` fields and declares its `minimum_content_lines`.

`execution=manual_pending` explicitly records foreground behavior that needs human acceptance; catalog validation never upgrades it to PASS.
