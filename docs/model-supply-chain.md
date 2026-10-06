# Local LMT model supply chain

ClipRay's Local LMT provider uses the pinned Q4_K_M GGUF through a local `llama-cli` process. It does not open a network listener or contact an external service. Input is supplied on standard input. A healthy local model session may be reused between translations; the default idle shutdown is fifteen minutes. Stopping the session or exiting the application releases the process.

## Bundled release model

- Model: `NiuTrans/LMT-60-0.6B`, Q4_K_M GGUF, Apache-2.0.
- Bundled Q4_K_M GGUF SHA-256: `743c6cdc13294b5c470ec0e89e79d64f96ef0cab94ce1285c78da4dc0f56be5c`.
- Quantized file publisher: `mradermacher/LMT-60-0.6B-GGUF`, revision `86942b9f925bfccd4196d4982578b46413a1f73f`, Apache-2.0. File: `LMT-60-0.6B.Q4_K_M.gguf` (484,220,000 bytes).
- The GGUF metadata names this quantized repository and `NiuTrans/LMT-60-0.6B` as its original source. On 2026-10-06 the local artifact's SHA-256 was checked against the file's publisher-provided Hugging Face LFS SHA-256; both match the bundled hash above.
- Pinned download: https://huggingface.co/mradermacher/LMT-60-0.6B-GGUF/resolve/86942b9f925bfccd4196d4982578b46413a1f73f/LMT-60-0.6B.Q4_K_M.gguf

The 1.7B lineage below is retained only for the optional development fallback; it is not copied into the release installer.

## Optional development fallback lineage

- Model: `NiuTrans/LMT-60-1.7B`, revision `c468e3f82f71d3f5d112ebbbfcec8f0ee0bb6cdb`, Apache-2.0.
- Source safetensors SHA-256: `f32e78140294b4db8d560ca2ff280b723a5989ec63e12c4dd55459647722150b`.
- llama.cpp: `3f7c29d318e317b63f54c558bc69803963d7d88c` / b8816.
- Q4_K_M GGUF SHA-256: `9e9326981457f09e6cd66ea57aa9d879dc142edb226dee0feebf6bef317cf6e0`.
- BF16 GGUF SHA-256: `9d4623d454899e59636635d885d5aaa8b439db327766e866516030205e7a357a`.

The app refuses to launch Local LMT when the configured/discovered model is absent or its SHA-256 differs. Its search order is the explicit setting, `CLIPRAY_LMT_MODEL_PATH`, repository-relative audited artifact, then app-relative package path; it does not hard-code a user profile. `CLIPRAY_LLAMA_CLI_PATH` can point to an audited CLI; otherwise the same repository/app-relative locations are searched.

Vulkan is attempted first when the audited Vulkan CLI is discovered. A process-start failure may retry once with the CPU CLI path, and the translation status explicitly identifies that fallback. It never switches backend after generation begins.

`scripts/Get-LmtModel.ps1` and `scripts/Convert-LmtToGguf.ps1` are intentionally inert unless passed `-Download` or `-Convert`. They pin revisions, refuse hash mismatches/overwrites, and never delete inputs. They were not run for this task: the existing evidence and artifacts remain the source of truth.

## Known limitation

The current offline evidence proves one official-prompt smoke translation and Vulkan availability, not the original 20–50 case BF16-versus-Q4 quality comparison. Local LMT remains a user-selected local option, not a claim of broad quality equivalence.
