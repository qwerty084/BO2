# Release And Signing Notes

BO2 uses single-project MSIX packaging from `BO2.csproj`.

## Current Packaging Facts

- Target runtime: `win-x86`.
- Supported app platform: `x86` only.
- Native payloads copied into the app package:
  - `BO2Monitor.dll`
  - `BO2InjectorHelper.exe`
- Manifest capability: `runFullTrust` for desktop process access.
- No `systemAIModels` capability is declared.

## CI Test Signing

The build workflow creates a temporary test signing certificate, builds a sideload-only x86 MSIX, verifies native payloads are present, uploads the package artifact, and removes the temporary certificate material.

The test-signed MSIX is a validation artifact. External distribution needs a deliberate release process, reviewed package identity, publisher details, certificate handling, and install/update guidance.

## Release Checklist

- Confirm `Package.appxmanifest` identity, publisher, logos, and capabilities.
- Build with `Platform=x86` and `RuntimeIdentifier=win-x86`.
- Verify `BO2Monitor.dll` and `BO2InjectorHelper.exe` are present in the package.
- Run managed tests, native tests, snapshot contract check, formatter check, and the live native smoke test where release scope requires it.
- Use a non-temporary signing certificate appropriate for the distribution channel.
