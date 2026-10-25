# Third-party notices

This file lists the direct NuGet dependencies and the resolved runtime packages
used by the projects. Package metadata and local NuGet package license files
were checked during preparation. Package versions are pinned in the project
files or resolved dependency graph; no new package was added for this
documentation task.

## Application runtime

| Package | Version | Purpose | License | Official project / license URL | In Windows x64 publish | Notice |
|---|---:|---|---|---|---|---|
| Microsoft.Data.Sqlite | 8.0.11 | SQLite ADO.NET provider metapackage | MIT | [Project](https://learn.microsoft.com/dotnet/standard/data/sqlite/), [License](https://licenses.nuget.org/MIT) | Resolved in app publish; implementation assembly comes from `Microsoft.Data.Sqlite.Core` | Retain the MIT notice |
| Microsoft.Data.Sqlite.Core | 8.0.11 | SQLite ADO.NET provider implementation | MIT | [Project](https://learn.microsoft.com/dotnet/standard/data/sqlite/), [License](https://licenses.nuget.org/MIT) | Yes | Retain the MIT notice |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.6 | SQLite native provider bundle | Apache-2.0 | [Project](https://github.com/ericsink/SQLitePCL.raw), [License](https://licenses.nuget.org/Apache-2.0) | Yes | Retain Apache-2.0 notice |
| SQLitePCLRaw.core | 2.1.6 | SQLitePCLRaw core API | Apache-2.0 | [Project](https://github.com/ericsink/SQLitePCL.raw), [License](https://licenses.nuget.org/Apache-2.0) | Yes | Retain Apache-2.0 notice |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.6 | Bundled SQLite native library package | Apache-2.0 package metadata; SQLite engine is public domain | [Project](https://github.com/ericsink/SQLitePCL.raw), [Package license](https://licenses.nuget.org/Apache-2.0), [SQLite](https://www.sqlite.org/copyright.html) | Yes | Preserve package notice; review the package's included third-party notices when redistributing |
| SQLitePCLRaw.provider.e_sqlite3 | 2.1.6 | SQLitePCLRaw native provider | Apache-2.0 | [Project](https://github.com/ericsink/SQLitePCL.raw), [License](https://licenses.nuget.org/Apache-2.0) | Yes | Retain Apache-2.0 notice |
| System.Memory | 4.5.3 | Memory API compatibility dependency | MIT (package license file) | [Project](https://github.com/dotnet/corefx), [License](https://github.com/dotnet/corefx/blob/master/LICENSE.TXT) | Resolved runtime dependency; preserve notice if distributed | Retain MIT notice |

## Test-only direct dependencies

These packages are used to build and run `HyperDL.Tests`; they are not part of
the application's Windows x64 publish output.

| Package | Version | Purpose | License | Official project / license URL | In app publish | Notice |
|---|---:|---|---|---|---|---|
| Microsoft.NET.Test.Sdk | 17.8.0 | .NET test execution | MIT | [Project](https://github.com/microsoft/vstest), [License](https://github.com/microsoft/vstest) | No | Retain the MIT notice for redistributed test tooling |
| xunit | 2.5.3 | Unit testing framework | Apache-2.0 | [Project](https://xunit.net/), [License](https://licenses.nuget.org/Apache-2.0) | No | Retain Apache-2.0 notice |
| xunit.runner.visualstudio | 2.5.3 | Visual Studio test runner adapter | Apache-2.0 | [Project](https://github.com/xunit/visualstudio.xunit), [License](https://licenses.nuget.org/Apache-2.0) | No | Retain Apache-2.0 notice |

The test SDK also resolves Microsoft Test Platform components and xUnit
transitive packages. Their local package metadata identifies Microsoft
TestPlatform components as MIT and most xUnit components as Apache-2.0.
`xunit.abstractions` 2.0.3 has a license URL in its package metadata but no
embedded SPDX expression; verify and retain its upstream license notice if
redistributing test binaries.

## Notes

- The application project directly references `Microsoft.Data.Sqlite`
  8.0.11. Its resolved SQLitePCLRaw runtime packages are listed above.
- The test project directly references `Microsoft.NET.Test.Sdk` 17.8.0,
  `xunit` 2.5.3, and `xunit.runner.visualstudio` 2.5.3.
- No FluentFTP, BitTorrent engine, updater, or other native dependency was
  added.
- The MIT project license does not replace any third-party license or notice
  obligation. This inventory is not legal advice; complete a human review
  before redistributing binaries.

  Traditional Chinese: [第三方套件聲明](THIRD_PARTY_NOTICES.zh-TW.md).
