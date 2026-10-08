# Windows build notes

Required only for development/build machines:

- Windows x64
- .NET 8 SDK
- internet access for NuGet restore
- Inno Setup 6 if building the standalone installer locally

Publish command:

```powershell
dotnet publish .\src\Kassie.Makeover\Kassie.Makeover.csproj -c Release -r win-x64 --self-contained true -o .\publish -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The end user should not run this command. The normal build route is the included Windows GitHub Actions workflow.
