// ADLX_3DSettings.cpp - C++ wrapper for ADLX 3D Graphics features
// Exports functions for RSR, AFMF, and Anti-Lag
//
// Based on AMD ADLX SDK: https://github.com/GPUOpen-LibrariesAndSDKs/ADLX

#include "SDK/Include/ADLXHelper/Windows/Cpp/ADLXHelper.h"
#include "SDK/Include/I3DSettings.h"
#include <iostream>

// ADLX Helper instance
static ADLXHelper g_ADLXHelper;
static bool g_bInitialized = false;

// Initialize ADLX on first use
bool InitializeADLX()
{
    if (g_bInitialized)
        return true;

    ADLX_RESULT res = g_ADLXHelper.Initialize();
    if (ADLX_SUCCEEDED(res))
    {
        g_bInitialized = true;
        return true;
    }
    return false;
}

// Get the 3D Graphics Services interface
adlx::IADLXInterface* Get3DGraphicsServices()
{
    if (!InitializeADLX())
        return nullptr;

    adlx::IADLXSystem* sys = g_ADLXHelper.GetSystemServices();
    if (sys == nullptr)
        return nullptr;

    adlx::IADLXGPU* gpu = nullptr;
    adlx::IADLXGPUList* gpus = nullptr;

    // Get GPU list
    ADLX_RESULT res = sys->GetGPUs(&gpus);
    if (ADLX_FAILED(res) || gpus == nullptr)
        return nullptr;

    // Get first GPU
    res = gpus->At(0, &gpu);
    if (ADLX_FAILED(res) || gpu == nullptr)
    {
        if (gpus != nullptr)
            gpus->Release();
        return nullptr;
    }

    // Get 3D Graphics Services
    adlx::IADLX3DSettingsServices* d3dSettingSrv = nullptr;
    res = sys->Get3DSettingsServices(&d3dSettingSrv);

    if (gpus != nullptr)
        gpus->Release();
    if (gpu != nullptr)
        gpu->Release();

    return d3dSettingSrv;
}

// ============================================================================
// RSR (Radeon Super Resolution) Functions
// ============================================================================

extern "C" __declspec(dllexport) bool HasRSRSupport()
{
    adlx::IADLX3DSettingsServices* d3dSettingSrv = (adlx::IADLX3DSettingsServices*)Get3DGraphicsServices();
    if (d3dSettingSrv == nullptr)
        return false;

    adlx::IADLX3DRadeonSuperResolution* rsr = nullptr;
    ADLX_RESULT res = d3dSettingSrv->GetRadeonSuperResolution(&rsr);

    bool supported = ADLX_SUCCEEDED(res) && rsr != nullptr;

    if (rsr != nullptr)
        rsr->Release();
    if (d3dSettingSrv != nullptr)
        d3dSettingSrv->Release();

    return supported;
}

extern "C" __declspec(dllexport) bool GetRSRState()
{
    adlx::IADLX3DSettingsServices* d3dSettingSrv = (adlx::IADLX3DSettingsServices*)Get3DGraphicsServices();
    if (d3dSettingSrv == nullptr)
        return false;

    adlx::IADLX3DRadeonSuperResolution* rsr = nullptr;
    ADLX_RESULT res = d3dSettingSrv->GetRadeonSuperResolution(&rsr);

    bool enabled = false;
    if (ADLX_SUCCEEDED(res) && rsr != nullptr)
    {
        rsr->IsEnabled(&enabled);
    }

    if (rsr != nullptr)
        rsr->Release();
    if (d3dSettingSrv != nullptr)
        d3dSettingSrv->Release();

    return enabled;
}

extern "C" __declspec(dllexport) bool SetRSR(bool isEnabled)
{
    adlx::IADLX3DSettingsServices* d3dSettingSrv = (adlx::IADLX3DSettingsServices*)Get3DGraphicsServices();
    if (d3dSettingSrv == nullptr)
        return false;

    adlx::IADLX3DRadeonSuperResolution* rsr = nullptr;
    ADLX_RESULT res = d3dSettingSrv->GetRadeonSuperResolution(&rsr);

    bool success = false;
    if (ADLX_SUCCEEDED(res) && rsr != nullptr)
    {
        res = rsr->SetEnabled(isEnabled);
        success = ADLX_SUCCEEDED(res);
    }

    if (rsr != nullptr)
        rsr->Release();
    if (d3dSettingSrv != nullptr)
        d3dSettingSrv->Release();

    return success;
}

extern "C" __declspec(dllexport) int GetRSRSharpness()
{
    adlx::IADLX3DSettingsServices* d3dSettingSrv = (adlx::IADLX3DSettingsServices*)Get3DGraphicsServices();
    if (d3dSettingSrv == nullptr)
        return -1;

    adlx::IADLX3DRadeonSuperResolution* rsr = nullptr;
    ADLX_RESULT res = d3dSettingSrv->GetRadeonSuperResolution(&rsr);

    int sharpness = -1;
    if (ADLX_SUCCEEDED(res) && rsr != nullptr)
    {
        adlx_int value = 0;
        res = rsr->GetSharpness(&value);
        if (ADLX_SUCCEEDED(res))
            sharpness = (int)value;
    }

    if (rsr != nullptr)
        rsr->Release();
    if (d3dSettingSrv != nullptr)
        d3dSettingSrv->Release();

    return sharpness;
}

extern "C" __declspec(dllexport) bool SetRSRSharpness(int sharpness)
{
    if (sharpness < 0 || sharpness > 100)
        return false;

    adlx::IADLX3DSettingsServices* d3dSettingSrv = (adlx::IADLX3DSettingsServices*)Get3DGraphicsServices();
    if (d3dSettingSrv == nullptr)
        return false;

    adlx::IADLX3DRadeonSuperResolution* rsr = nullptr;
    ADLX_RESULT res = d3dSettingSrv->GetRadeonSuperResolution(&rsr);

    bool success = false;
    if (ADLX_SUCCEEDED(res) && rsr != nullptr)
    {
        res = rsr->SetSharpness(sharpness);
        success = ADLX_SUCCEEDED(res);
    }

    if (rsr != nullptr)
        rsr->Release();
    if (d3dSettingSrv != nullptr)
        d3dSettingSrv->Release();

    return success;
}

// ============================================================================
// AFMF (AMD Fluid Motion Frames) Functions
// ============================================================================

extern "C" __declspec(dllexport) bool HasAFMFSupport()
{
    adlx::IADLX3DSettingsServices* d3dSettingSrv = (adlx::IADLX3DSettingsServices*)Get3DGraphicsServices();
    if (d3dSettingSrv == nullptr)
        return false;

    adlx::IADLX3DFrameRateTargetControl* afmf = nullptr;
    ADLX_RESULT res = d3dSettingSrv->GetFrameRateTargetControl(&afmf);

    bool supported = ADLX_SUCCEEDED(res) && afmf != nullptr;

    if (afmf != nullptr)
        afmf->Release();
    if (d3dSettingSrv != nullptr)
        d3dSettingSrv->Release();

    return supported;
}

extern "C" __declspec(dllexport) bool GetAFMFState()
{
    adlx::IADLX3DSettingsServices* d3dSettingSrv = (adlx::IADLX3DSettingsServices*)Get3DGraphicsServices();
    if (d3dSettingSrv == nullptr)
        return false;

    adlx::IADLX3DFrameRateTargetControl* afmf = nullptr;
    ADLX_RESULT res = d3dSettingSrv->GetFrameRateTargetControl(&afmf);

    bool enabled = false;
    if (ADLX_SUCCEEDED(res) && afmf != nullptr)
    {
        afmf->IsEnabled(&enabled);
    }

    if (afmf != nullptr)
        afmf->Release();
    if (d3dSettingSrv != nullptr)
        d3dSettingSrv->Release();

    return enabled;
}

extern "C" __declspec(dllexport) bool SetAFMFState(bool isEnabled)
{
    adlx::IADLX3DSettingsServices* d3dSettingSrv = (adlx::IADLX3DSettingsServices*)Get3DGraphicsServices();
    if (d3dSettingSrv == nullptr)
        return false;

    adlx::IADLX3DFrameRateTargetControl* afmf = nullptr;
    ADLX_RESULT res = d3dSettingSrv->GetFrameRateTargetControl(&afmf);

    bool success = false;
    if (ADLX_SUCCEEDED(res) && afmf != nullptr)
    {
        res = afmf->SetEnabled(isEnabled);
        success = ADLX_SUCCEEDED(res);
    }

    if (afmf != nullptr)
        afmf->Release();
    if (d3dSettingSrv != nullptr)
        d3dSettingSrv->Release();

    return success;
}

// ============================================================================
// Anti-Lag Functions
// ============================================================================

extern "C" __declspec(dllexport) bool HasAntiLagSupport()
{
    adlx::IADLX3DSettingsServices* d3dSettingSrv = (adlx::IADLX3DSettingsServices*)Get3DGraphicsServices();
    if (d3dSettingSrv == nullptr)
        return false;

    adlx::IADLX3DAntiLag* antiLag = nullptr;
    ADLX_RESULT res = d3dSettingSrv->GetAntiLag(&antiLag);

    bool supported = ADLX_SUCCEEDED(res) && antiLag != nullptr;

    if (antiLag != nullptr)
        antiLag->Release();
    if (d3dSettingSrv != nullptr)
        d3dSettingSrv->Release();

    return supported;
}

extern "C" __declspec(dllexport) bool GetAntiLagState()
{
    adlx::IADLX3DSettingsServices* d3dSettingSrv = (adlx::IADLX3DSettingsServices*)Get3DGraphicsServices();
    if (d3dSettingSrv == nullptr)
        return false;

    adlx::IADLX3DAntiLag* antiLag = nullptr;
    ADLX_RESULT res = d3dSettingSrv->GetAntiLag(&antiLag);

    bool enabled = false;
    if (ADLX_SUCCEEDED(res) && antiLag != nullptr)
    {
        antiLag->IsEnabled(&enabled);
    }

    if (antiLag != nullptr)
        antiLag->Release();
    if (d3dSettingSrv != nullptr)
        d3dSettingSrv->Release();

    return enabled;
}

extern "C" __declspec(dllexport) bool SetAntiLagState(bool isEnabled)
{
    adlx::IADLX3DSettingsServices* d3dSettingSrv = (adlx::IADLX3DSettingsServices*)Get3DGraphicsServices();
    if (d3dSettingSrv == nullptr)
        return false;

    adlx::IADLX3DAntiLag* antiLag = nullptr;
    ADLX_RESULT res = d3dSettingSrv->GetAntiLag(&antiLag);

    bool success = false;
    if (ADLX_SUCCEEDED(res) && antiLag != nullptr)
    {
        res = antiLag->SetEnabled(isEnabled);
        success = ADLX_SUCCEEDED(res);
    }

    if (antiLag != nullptr)
        antiLag->Release();
    if (d3dSettingSrv != nullptr)
        d3dSettingSrv->Release();

    return success;
}

// ============================================================================
// DLL Entry Point
// ============================================================================

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        break;
    case DLL_PROCESS_DETACH:
        if (g_bInitialized)
        {
            g_ADLXHelper.Terminate();
            g_bInitialized = false;
        }
        break;
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
        break;
    }
    return TRUE;
}
