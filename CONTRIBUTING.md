# Contributing to HyperDL

## Development environment

- Windows 10 or Windows 11 x64.
- .NET 8 SDK x64.
- Visual Studio with the .NET Desktop Development workload, or another
  compatible editor.
- PowerShell.

## Build and test

From the repository root:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" restore HyperDL.sln
& "C:\Program Files\dotnet\dotnet.exe" build HyperDL.sln -c Release
& "C:\Program Files\dotnet\dotnet.exe" test HyperDL.sln -c Release --no-build
```

## Pull requests

1. Fork the repository.
2. Create a focused feature branch.
3. Keep changes consistent with the existing C# and XAML style.
4. Add or update tests for behavior changes.
5. Run Release Build and tests, and include the results in the Pull Request.
6. Describe user-visible changes and known limitations.

Do not include `bin`, `obj`, `artifacts`, runtime databases, `.part` files,
credentials, certificates, or private keys in a Pull Request.

## Safety and scope

Contributions must not add DRM bypass, malicious code, arbitrary command
execution, remote script execution, or copyright-infringing functionality.
Downloads must not be automatically executed.

Traditional Chinese: [貢獻指南](CONTRIBUTING.zh-TW.md).
