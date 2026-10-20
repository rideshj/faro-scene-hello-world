# FARO SCENE Open API - Technical Traceability & Architecture Guide

This document provides a comprehensive traceability matrix, reverse-engineering findings, and runtime verification records for developing, compiling, and running .NET add-ins inside **FARO SCENE** and **FARO SCENE LT**.

---

## 1. Traceability & Verification Matrix

| Requirement / Component | Architecture / Contract | SCENE Host Implementation Details | Verification Evidence | Status |
| :--- | :--- | :--- | :--- | :--- |
| **Manifest Format** | Native SCENE XML (`AttrContainer`) | Parsed by `Scene_Interfaces.dll!CBVAppManifest` via `AppInstaller.exe`. Requires `AppManifest.xml` at bundle root. | Previously failed with *"Error: The app manifast could not be found"* when only `App.xml` was present. Resolved by adding `AppManifest.xml`. | **Verified Working** |
| **PE Unmanaged Exports** | Win32 C ABI Exports: `lsAppStartup`, `lsAppShutdown` | `Scene_Interfaces.dll` (RVA `0x2434`, `0x244C`) invokes `LoadLibraryExW(..., LOAD_WITH_ALTERED_SEARCH_PATH)` and `GetProcAddress(h, "lsAppStartup")`. | `GetProcAddress` probe confirmed valid function pointers (`0x7FFD57DE628A`). Native loading succeeded. | **Verified Working** |
| **Startup Return Code** | C/COM status convention: `0` (`LS_OK`) = Success, `!= 0` = Error | `Scene_Interfaces.dll` (RVA `0x2641`–`0x2643`): `test eax, eax; jnz ERROR_APP_DLL_LOAD`. If `eax != 0`, throws `0x8803` (*"Error: The app DLL could not be loaded"*). | Changed return type from `bool` (`1`) to `int` (`0`). Activation succeeded and marked active (`mov byte ptr [r15+30h], 1`). | **Verified Working** |
| **SDK Assembly Isolation** | `Private=False` / `CopyLocal=False` | SCENE's running process space provides `FARO.LS.SDK.*.dll`. Shipping them in local bin causes assembly collisions. | `build.ps1` actively cleans `FARO.LS.SDK.*` from bin before packaging. Reference assemblies isolated to `lib/`. | **Verified Working** |
| **Dependency Resolution** | Early `AppDomain.AssemblyResolve` hook | When loaded standalone or in SCENE LT, JIT compiler resolves assemblies alongside the app before referencing SDK types. | Packaged bundle loads local reference assemblies dynamically without `FileNotFoundException`. | **Verified Working** |
| **Interactive UI & Threading** | Modeless WinForms on dedicated STA Thread | SCENE's host message loop must not be blocked by modal dialogs during startup or background processing. | `InteractiveToolboxWindow` runs independently on an STA thread with full live button clicks, event logs, and background worker progress. | **Verified Working** |
| **Packaging & Deployment** | `.fpp` (native) and `.faroapp` (modern) | SCENE `AppInstaller.exe` expects `.fpp` archive containing manifests, binaries, and icons at root. | Verified drag-and-drop installation in SCENE LT App Manager. | **Verified Working** |

---

## 2. Reverse-Engineering Findings: SCENE Native Loader

### 2.1 The Native Loader Mechanism (`Scene_Interfaces.dll`)
FARO SCENE and SCENE LT utilize a native C++ plugin host architecture rather than pure managed CLR hosting. When an add-in is activated through the App Manager or upon startup:

1. **Path Resolution & Loading**:
   SCENE locates the directory `C:\Program Files\FARO\SCENE\Apps\<AppName>\` and calls:
   ```c
   LoadLibraryExW(appDllPath, NULL, LOAD_WITH_ALTERED_SEARCH_PATH /* 0x00000008 */);
   ```

2. **Entry Point Inspection**:
   SCENE checks for native exported C symbols:
   ```c
   FARPROC pStartup  = GetProcAddress(hModule, "lsAppStartup");
   FARPROC pShutdown = GetProcAddress(hModule, "lsAppShutdown");
   ```
   If either function pointer is `NULL`, SCENE aborts with error code `0x8803`.

3. **Execution & Status Verification** (Disassembly at RVA `0x263D`):
   ```x86asm
   00002638: lea rcx, [rsp+20h]          ; sceneContext pointer
   0000263D: call qword ptr [rsp+50h]    ; Invoke lsAppStartup(sceneContext, reserved)
   00002641: test eax, eax               ; Test return code
   00002643: jnz  000026FE               ; Jump to failure if eax != 0
   ; Success Fall-Through:
   0000265C: mov  [r15+58h], rax         ; Cache interface handles
   000026F3: mov  byte ptr [r15+30h], 1  ; Set App State = ACTIVE
   000026FD: jmp  exit_success           ; Return 0 (Success)
   ```

### 2.2 Why Pure C# Assemblies Fail Without IL Weaving
- Standard C# compilers (`csc.exe` / Roslyn) emit assemblies with the COMIMAGE flag `0x00000001` (`ILONLY`).
- A pure IL assembly does not contain an export directory in the PE header.
- As a consequence, `GetProcAddress(hModule, "lsAppStartup")` returns `NULL`, causing SCENE to immediately throw:
  > *"Error: The app DLL could not be loaded."*

---

## 3. Automated IL Export Weaving Pipeline

To allow a standard C# .NET Framework 4.8 assembly to export native C-style functions, `build.ps1` executes an automated post-compilation IL post-processing step:

```
[MSBuild (net48 x64)] ──> FAROSceneHelloWorld.dll (ILONLY)
                                  │
                                  ▼
[ildasm.exe]          ──> FAROSceneHelloWorld.il
                                  │
                                  ▼
[Regex Weaving]       ──> 1. Set .corflags 0x00000000 (Allow native exports)
                          2. Inject .vtfixup [1] int64 fromunmanaged at VT_01 / VT_02
                          3. Add .vtentry 1 : 1 and .export [1] as lsAppStartup
                          4. Add .vtentry 2 : 1 and .export [2] as lsAppShutdown
                                  │
                                  ▼
[ilasm.exe /pe64]     ──> FAROSceneHelloWorld.dll (Dual Native/Managed PE)
```

### Injected IL Directives:
```il
// Module Header
.vtfixup [1] int64 fromunmanaged at VT_01
.data VT_01 = int64(0)
.vtfixup [1] int64 fromunmanaged at VT_02
.data VT_02 = int64(0)

// Method lsAppStartup
.method public hidebysig static int32 lsAppStartup(native int sceneContext, native int reserved) cil managed noinlining
{
    .vtentry 1 : 1
    .export [1] as lsAppStartup
    ...
}

// Method lsAppShutdown
.method public hidebysig static void lsAppShutdown(native int sceneContext, native int reserved) cil managed noinlining
{
    .vtentry 2 : 1
    .export [2] as lsAppShutdown
    ...
}
```

---

## 4. Manifest Specifications

SCENE requires two manifests to support both native and managed discovery:

### 4.1 Native Manifest (`AppManifest.xml`)
Required by SCENE's native `CBVAppManifest` parser during package installation:
```xml
<?xml version="1.0" encoding="UTF-8"?>
<FARO>
  <AttrContainer>
    <Attr type="String">
      <Name value="'Name'" />
      <Value value="'FARO SCENE Hello World'" />
    </Attr>
    <Attr type="String">
      <Name value="'version'" />
      <Value value="'1.0.0.0'" />
    </Attr>
    <Attr type="String">
      <Name value="'Description'" />
      <Value value="'Interactive Hello World Add-in for FARO SCENE'" />
    </Attr>
    <Attr type="String">
      <Name value="'app_dll'" />
      <Value value="'FAROSceneHelloWorld.dll'" />
    </Attr>
    <Attr type="String">
      <Name value="'minimum_required_api'" />
      <Value value="'2.0.25.7594'" />
    </Attr>
  </AttrContainer>
</FARO>
```

### 4.2 Managed Open API Manifest (`App.xml`)
Used by SCENE Open API host for declarative Ribbon mapping:
```xml
<?xml version="1.0" encoding="utf-8"?>
<App>
  <Id>com.faro.scene.helloworld</Id>
  <Name>FARO SCENE Hello World</Name>
  <Version>1.0.0.0</Version>
  <EntryPoint>FAROSceneHelloWorld.EntryPoint</EntryPoint>
  <Ribbon>
    <Tab Id="FARO_APP_TAB" Title="Apps">
      <Group Id="FARO_HELLOWORLD_GROUP" Title="Workspace Tools">
        <Button Id="FARO_HELLOWORLD_BTN_COUNT"
                Title="Scan Stats"
                ToolTip="Count and display active scan points and open scans"
                Size="Large"
                Command="CountPoints"
                SmallImage="Resources/icon16.png"
                LargeImage="Resources/icon32.png" />
      </Group>
    </Tab>
  </Ribbon>
</App>
```

---

## 5. UI Architecture: Modeless STA Controller

To guarantee responsiveness and prevent freezing the FARO SCENE UI thread:
- `ShowInteractiveWindow` initializes a dedicated thread configured with `ApartmentState.STA`.
- It invokes `Application.Run(window)` on this thread, giving the diagnostic window an independent message pump.
- The window provides real-time button click tracking, memory profiling, workspace scan querying, and background worker progress bars.
- When SCENE shuts down, `lsAppShutdown` gracefully dispatches a `Close()` message to the STA thread.
