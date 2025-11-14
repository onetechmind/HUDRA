# ADLX_3DSettings Wrapper Build Instructions

This directory contains the C++ source code for the ADLX_3DSettings.dll wrapper that provides C#-compatible exports for AMD ADLX SDK features.

## Features Supported

- **RSR (Radeon Super Resolution)**: Enable/disable and adjust sharpness (0-100)
- **AFMF (AMD Fluid Motion Frames)**: Enable/disable frame generation
- **Anti-Lag**: Enable/disable latency reduction

## Prerequisites

1. **Visual Studio 2019 or later** with C++ development tools
2. **AMD ADLX SDK** - Download from: https://github.com/GPUOpen-LibrariesAndSDKs/ADLX

## Setup

1. Clone or download the AMD ADLX SDK:
   ```bash
   cd ADLX_Wrapper_Source/ADLX_3DSettings
   git clone https://github.com/GPUOpen-LibrariesAndSDKs/ADLX.git SDK
   ```

2. Verify the SDK structure - you should have:
   ```
   ADLX_3DSettings/
   ├── SDK/
   │   ├── Include/
   │   │   ├── ADLX.h
   │   │   ├── I3DSettings.h
   │   │   └── ... (other interface headers)
   │   └── ADLXHelper/
   │       └── Windows/
   │           └── Cpp/
   │               ├── ADLXHelper.h
   │               └── ADLXHelper.cpp
   └── ADLX_3DSettings.cpp
   ```

## Build Instructions

### Option 1: Visual Studio Command Prompt (Recommended)

1. Open "x64 Native Tools Command Prompt for VS 2019" (or your VS version)

2. Navigate to the source directory:
   ```cmd
   cd HUDRA\ADLX_Wrapper_Source\ADLX_3DSettings
   ```

3. Compile the DLL:
   ```cmd
   cl.exe /LD /O2 /EHsc /MD ^
      /I"SDK\Include" ^
      /I"SDK\ADLXHelper\Windows\Cpp" ^
      ADLX_3DSettings.cpp SDK\ADLXHelper\Windows\Cpp\ADLXHelper.cpp ^
      /link /OUT:ADLX_3DSettings.dll
   ```

4. Copy the built DLL to HUDRA:
   ```cmd
   copy ADLX_3DSettings.dll ..\..\HUDRA\"External Resources"\AMD\ADLX\
   ```

### Option 2: MSBuild Project File

Create `ADLX_3DSettings.vcxproj` with the following content:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <ItemGroup>
    <ProjectConfiguration Include="Release|x64">
      <Configuration>Release</Configuration>
      <Platform>x64</Platform>
    </ProjectConfiguration>
  </ItemGroup>
  <PropertyGroup>
    <ConfigurationType>DynamicLibrary</ConfigurationType>
    <PlatformToolset>v142</PlatformToolset>
    <CharacterSet>Unicode</CharacterSet>
  </PropertyGroup>
  <PropertyGroup>
    <OutDir>$(SolutionDir)HUDRA\External Resources\AMD\ADLX\</OutDir>
    <IntDir>obj\$(Configuration)\</IntDir>
  </PropertyGroup>
  <ItemDefinitionGroup>
    <ClCompile>
      <AdditionalIncludeDirectories>SDK\Include;SDK\ADLXHelper\Windows\Cpp;%(AdditionalIncludeDirectories)</AdditionalIncludeDirectories>
      <Optimization>MaxSpeed</Optimization>
      <RuntimeLibrary>MultiThreadedDLL</RuntimeLibrary>
    </ClCompile>
  </ItemDefinitionGroup>
  <ItemGroup>
    <ClCompile Include="ADLX_3DSettings.cpp" />
    <ClCompile Include="SDK\ADLXHelper\Windows\Cpp\ADLXHelper.cpp" />
  </ItemGroup>
  <Import Project="$(VCTargetsPath)\Microsoft.Cpp.targets" />
</Project>
```

Then build:
```cmd
msbuild ADLX_3DSettings.vcxproj /p:Configuration=Release /p:Platform=x64
```

## Exported Functions

All functions use `extern "C" __declspec(dllexport)` and `CallingConvention.Cdecl`:

### RSR Functions
- `bool HasRSRSupport()` - Check if GPU supports RSR
- `bool GetRSRState()` - Get current RSR enabled state
- `bool SetRSR(bool isEnabled)` - Enable/disable RSR
- `int GetRSRSharpness()` - Get sharpness level (0-100, -1 on error)
- `bool SetRSRSharpness(int sharpness)` - Set sharpness level

### AFMF Functions
- `bool HasAFMFSupport()` - Check if GPU supports AFMF
- `bool GetAFMFState()` - Get current AFMF enabled state
- `bool SetAFMFState(bool isEnabled)` - Enable/disable AFMF

### Anti-Lag Functions
- `bool HasAntiLagSupport()` - Check if GPU supports Anti-Lag
- `bool GetAntiLagState()` - Get current Anti-Lag enabled state
- `bool SetAntiLagState(bool isEnabled)` - Enable/disable Anti-Lag

## Troubleshooting

### Build Errors

**"Cannot open include file 'ADLXHelper.h'"**
- Ensure the ADLX SDK is cloned in the correct location (`SDK/` subdirectory)
- Verify both include paths are set: `/I"SDK\Include"` and `/I"SDK\ADLXHelper\Windows\Cpp"`
- Make sure you're compiling both ADLX_3DSettings.cpp and SDK\ADLXHelper\Windows\Cpp\ADLXHelper.cpp

**Linker errors**
- Make sure you're using x64 tools (not x86)
- HUDRA is a 64-bit application and requires a 64-bit DLL

### Runtime Errors

**DllNotFoundException**
- Ensure the DLL is in `HUDRA\External Resources\AMD\ADLX\`
- Check that the DLL is 64-bit: `dumpbin /headers ADLX_3DSettings.dll | find "machine"`

**EntryPointNotFoundException**
- Verify exports: `dumpbin /exports ADLX_3DSettings.dll`
- Function names are case-sensitive

## Testing

After building and copying the DLL, run HUDRA and:
1. Navigate to Scaling page
2. Expand "AMD Features"
3. Test RSR toggle
4. Test AFMF toggle
5. Test Anti-Lag toggle

Check the debug output for ADLX messages.
