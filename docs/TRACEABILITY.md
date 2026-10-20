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

---

## 6. Structural & MEP Geometric Algorithm Traceability

### 6.1 Spatial Voxel Hash Grid
- **Objective**: Reduce 10M+ point TLS scans to manageable density without introducing spatial bias.
- **Complexity**: $O(N)$ insertion and centroid accumulation.
- **Formulation**:
  $$\text{Key}(x, y, z) = (\lfloor x/v \rfloor \cdot 73856093) \oplus (\lfloor y/v \rfloor \cdot 19349663) \oplus (\lfloor z/v \rfloor \cdot 83492791)$$
  $$\bar{\mathbf{p}}_k = \frac{1}{M_k} \sum_{i=1}^{M_k} \mathbf{p}_i$$

### 6.2 3D k-d Tree & Dutch National Flag 3-Way QuickSelect
- **Problem**: Coplanar TLS scans (floors, flat walls) contain thousands of points with identical coordinates on one axis. Standard 2-way Lomuto partitioning degrades to $O(N^2)$ causing stack overflow.
- **Solution**: Implemented 3-way partition (Dutch National Flag) dividing intervals into `< pivot`, `== pivot`, and `> pivot`.
- **Recursion Guarantee**: Exact subtree lengths (`leftLength = mid - start`, `rightLength = length - leftLength - 1`) guarantee strict monotonic reduction with max recursion depth $\le \lceil \log_2 N \rceil \le 18$.

### 6.3 Local Covariance PCA & Jacobi Cyclic Eigendecomposition
- **Covariance Matrix**:
  $$\mathbf{C} = \frac{1}{k} \sum_{i=1}^k (\mathbf{p}_i - \bar{\mathbf{p}})(\mathbf{p}_i - \bar{\mathbf{p}})^T$$
- **Jacobi Sweep**: Diagonalizes symmetric $3 \times 3$ matrix through plane Givens rotations until off-diagonal sum $|\text{off}(\mathbf{A})| < 10^{-15}$.
- Smallest eigenvalue corresponds to surface normal $\mathbf{n}$.
- **Viewpoint Orientation**: If $\mathbf{n} \cdot (\mathbf{v}_{origin} - \mathbf{p}) < 0$, $\mathbf{n} \leftarrow -\mathbf{n}$.

### 6.4 Normal-Constrained Plane Extraction
- Enforces normal alignment constraint $|\mathbf{n}_i \cdot \mathbf{n}_{plane}| \ge \cos(15^\circ)$ in addition to Euclidean distance $|a x_i + b y_i + c z_i + d| \le \delta$.
- Eliminates cross-surface contamination between orthogonal walls and floor/ceiling boundaries.
- **Classification Rules**:
  - Horizontal ($|n_z| \ge 0.85$): Lowest elevation $\rightarrow$ **Floor**; highest elevation $\rightarrow$ **Ceiling**.
  - Vertical ($|n_z| \le 0.20$): $\rightarrow$ **Walls**.

### 6.5 Spatial Proximity Cylinder RANSAC & Perpendicular Bisector Intersection
- **Problem**: Uniform sampling over a large building cloud yields $P(\text{both samples on same small pipe}) < 0.1\%$.
- **Solution**: Spatial binning ($0.6\text{m}$ cells) draws 80% of candidate pairs from the same local neighborhood.
- **Perpendicular Bisector Formulation**:
  Given chord $\mathbf{d} = P_2 - P_1$ in the projection plane and surface normal $N_1$:
  $$t_1 = \frac{|\mathbf{d}|^2}{2 (N_1 \cdot \mathbf{d})}, \quad r = |t_1|, \quad C = P_1 + t_1 N_1$$
  Analytically guarantees $r_1 = r_2$ with zero matrix inversion overhead.
- **Classification Rules**:
  - **Column**: $|\text{axis}_z| \ge 0.75$, radius $r \ge 0.15\text{m}$ (diameter $\ge 0.30\text{m}$).
  - **MEP Pipe**: radius $r < 0.15\text{m}$ (diameter $< 0.30\text{m}$).

---

## 7. Automated Algorithm Verification Benchmark Log

**Test Suite**: `tests/AlgorithmVerification/Program.cs`
**Input Data**: 149,000 points synthetic indoor TLS scan ($12\text{m} \times 10\text{m} \times 3.2\text{m}$, Gaussian noise $\sigma = 2.5\text{mm}$).

```
=================================================
 3D Point Cloud Structural & MEP Verification
=================================================

[1] Generating 150,000 pt Synthetic TLS Benchmark Scan...
    Generated 149,000 points in 23 ms.
    Bounds: Min=<-6.011062, -5.009508, -0.01166872>, Max=<6.009639, 5.008382, 3.209672>

[2] Executing Classification Pipeline (Voxel -> PCA -> RANSAC Planes -> RANSAC Cylinders)...
    [ 10%] Downsampling point cloud with spatial voxel grid...
    [ 25%] Estimating normal vectors via PCA (113,171 points)...
    [ 45%] Fitting planar models with RANSAC...
    [ 65%] Extracting non-planar points for cylinder fitting...
    [ 75%] Fitting cylindrical models (Columns & Pipes) on 17,969 points...
    [ 90%] Creating clusters in SCENE Project Tree...
    [100%] Classification complete! 10 elements extracted in 1.11s.

=================================================
 Classification Results Summary
=================================================
Raw Points        : 149,000
Downsampled Points: 113,171
Processing Time   : 1.11 seconds
Total Elements    : 10
Floors Detected   : 1
Ceilings Detected : 1
Walls Detected    : 4
Columns Detected  : 2
MEP Pipes Detected: 2

Detected Elements:
  - [Floors] Floor (Ground Slab): Plane: 11.92m x 9.94m (Normal: [0.00, 0.00, 1.00]) (Center: 0.00, -0.01, 0.00) [Inliers: 28,429 pts]
  - [Ceilings] Ceiling / Upper Slab: Plane: 12.14m x 10.14m (Normal: [0.00, 0.00, 1.00]) (Center: 0.00, 0.02, 3.20) [Inliers: 24,859 pts]
  - [Walls] Wall 1: Plane: 11.89m x 3.18m (Normal: [0.00, -1.00, 0.00]) (Center: -0.03, 5.00, 1.60) [Inliers: 12,595 pts]
  - [Walls] Wall 2: Plane: 11.92m x 3.17m (Normal: [0.00, -1.00, 0.00]) (Center: -0.04, -5.00, 1.60) [Inliers: 11,199 pts]
  - [Walls] Wall 3: Plane: 9.90m x 3.17m (Normal: [1.00, 0.00, 0.00]) (Center: 6.00, -0.01, 1.59) [Inliers: 9,085 pts]
  - [Walls] Wall 4: Plane: 9.89m x 3.18m (Normal: [1.00, 0.00, 0.00]) (Center: -6.00, -0.01, 1.60) [Inliers: 9,035 pts]
  - [Columns] Column 1: Column: Radius=0.254m (Dia=0.51m), Length=3.20m (Center: 2.50, 1.50, 1.60) [Inliers: 4,017 pts]
  - [Columns] Column 2: Column: Radius=0.252m (Dia=0.50m), Length=3.20m (Center: -2.50, -1.50, 1.60) [Inliers: 3,841 pts]
  - [MEP Pipes] MEP Pipe 1: Pipe: Radius=77.0mm (Dia=154.1mm), Length=8.00m (Center: -1.00, 0.00, 2.50) [Inliers: 3,148 pts]
  - [MEP Pipes] MEP Pipe 2: Pipe: Radius=52.2mm (Dia=104.3mm), Length=9.94m (Center: 0.00, 2.00, 2.80) [Inliers: 2,456 pts]

>>> ALL CLASSIFICATION ALGORITHM TESTS PASSED! <<<
```

